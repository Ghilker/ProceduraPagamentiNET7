using DomicilioAutomation;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal interface IDomicilioAutomaticExecutor
    {
        IEnumerable<DomicilioAutomationResult> Execute(IReadOnlyList<DomicilioAutomationRequest> requests);
    }

    internal static class DomicilioAutomaticEngine
    {
        public const string AuditUser = "AUTO_DOMICILIO";

        public static IReadOnlyList<DomicilioAutomationResult> Run(IReadOnlyList<IstanzaDomicilioRecord> requests,
            bool simulation, IDomicilioAutomaticExecutor? executor = null)
        {
            var results = new Dictionary<int, DomicilioAutomationResult>();
            var eligible = new List<DomicilioAutomationRequest>();
            var duplicates = requests.GroupBy(x => x.NumIstanza).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
            foreach (var request in requests)
            {
                string? ignored = null;
                if (duplicates.Contains(request.NumIstanza)) ignored = "Istanza ripetuta nel lotto.";
                else if (request.PresaCarico) ignored = "Istanza già presa in carico: lasciata invariata.";
                else if (request.ExpectedFingerprint?.Length != 64 ||
                    !Regex.IsMatch(request.ExpectedFingerprint, "\\A[A-Fa-f0-9]{64}\\z"))
                    ignored = "Istanza non libera all'inizio del controllo o dati iniziali non verificabili.";
                else
                {
                    var decision = IstanzaDomicilioReport.Decide(request);
                    if (decision.Azione != IstanzaDomicilioReport.Accettabile) ignored = decision.Motivazione;
                    else if (request.Operazioni.SelectMany(x => x.Documenti).Any(x => !DocumentComplete(x)))
                        ignored = "Non tutti i documenti necessari sono stati letti integralmente e verificati in ogni campo richiesto.";
                }

                if (ignored != null) results[request.NumIstanza] = new(request.NumIstanza, "IGNORATA", ignored);
                else if (simulation) results[request.NumIstanza] = new(request.NumIstanza, "IDONEA", "Controlli superati. Simulazione: nessun dato modificato.");
                else eligible.Add(new(request.NumIstanza, request.ExpectedFingerprint!));
            }

            if (eligible.Count > 0)
            {
                var pending = eligible.Select(x => x.NumIstanza).ToHashSet();
                try
                {
                    if (executor == null) throw new InvalidOperationException("Motore di accoglimento non disponibile.");
                    foreach (var result in executor.Execute(eligible))
                    {
                        if (!pending.Remove(result.NumIstanza) || result.Outcome is not ("ACCOLTA" or "IGNORATA" or "ERRORE"))
                            throw new InvalidOperationException("Risposta del motore di accoglimento non valida.");
                        results[result.NumIstanza] = result;
                    }
                }
                catch
                {
                    // Keep confirmed successes; never retry an unknown outcome.
                }
                foreach (var id in eligible.Select(x => x.NumIstanza).Where(x => !results.ContainsKey(x)))
                    results[id] = new(id, "ERRORE", "Accoglimento non confermato: verificare lo stato prima di riprovare.");
            }
            return results.Values.OrderBy(x => x.NumIstanza).ToList();
        }

        private static bool DocumentComplete(IstanzaDomicilioDocument document)
        {
            var analysis = document.Attachment?.Analysis;
            return document.Expectation != null && analysis is
                { PdfValid: true, Readable: true, TypeRecognized: true, DataCoherent: true, PageCount: > 0 } &&
                analysis.PagesRead == analysis.PageCount &&
                (!document.Expectation.RequireFiscalCode || analysis.FiscalCodePresent == true);
        }
    }
}
