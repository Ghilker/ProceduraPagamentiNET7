using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal sealed record IstanzaDomicilioDecision(string Azione, string Motivazione);

    internal static class IstanzaDomicilioReport
    {
        public const string Accettabile = "ACCETTABILE_DIRETTAMENTE";
        public const string DaValidare = "DA_VALIDARE";
        public const string Rifiutare = "RIFIUTARE";

        public static readonly string[] Headers =
        {
            "TIPO_RIGA", "NUM_ISTANZA", "AZIONE_ISTANZA", "MOTIVO_AZIONE", "ANNO_ACCADEMICO",
            "NUM_DOMANDA", "COD_FISCALE", "STUDENTE", "DATA_INVIO", "PRESA_CARICO", "OPERATORE",
            "NUM_OPERAZIONI", "ID_OPERAZIONE", "ORDINE", "ENTITA", "OPERAZIONE", "ID_OPERAZIONE_PADRE",
            "ID_DOMICILIO", "ID_CONTRATTO", "ID_PROROGA", "ESITO_TECNICO",
            "CODICI_ANOMALIA", "ANOMALIE", "AZIONE_UFFICIO", "DATI_PROPOSTI", "TIPI_DOCUMENTI",
            "ID_ALLEGATI", "PDF_LEGGIBILI", "TIPI_DOCUMENTO_RICONOSCIUTI", "DATI_PDF_CONGRUENTI",
            "DATI_ESTRATTI_DAL_PDF", "NOMI_FILE"
        };

        public static IstanzaDomicilioDecision Decide(IstanzaDomicilioRecord request)
        {
            var state = RequestState(request);
            return Decide(state, request.Operazioni.SelectMany(x => x.Documenti).ToList(), request.CodFiscale);
        }

        private static DomicilioControlState RequestState(IstanzaDomicilioRecord request)
        {
            var state = request.State.Copy();
            if (!request.ValidazioneCompletata || request.Operazioni.Count == 0)
                state.Add(DomicilioControlSeverity.VerificaManuale, "IST_NON_VALIDATA",
                    "La validazione dell'intera istanza non è completa o non sono presenti operazioni.",
                    "Verificare l'istanza nel backoffice.");
            foreach (var operation in request.Operazioni)
                state.Merge(OperationState(operation));
            return state;
        }

        private static DomicilioControlState OperationState(IstanzaDomicilioOperation operation)
        {
            var state = operation.State.Copy();
            foreach (var document in operation.Documenti)
            {
                var analysis = document.Attachment?.Analysis;
                if (analysis != null)
                    state.Merge(analysis.State);
                if (document.Expectation == null || analysis == null || !analysis.PdfValid ||
                    !analysis.Readable || !analysis.TypeRecognized || !analysis.DataCoherent.HasValue)
                    state.Add(DomicilioControlSeverity.VerificaManuale, "DOC_DA_VALIDARE",
                        $"Il controllo automatico del documento {document.Tipo} non è completo.",
                        "Aprire il documento e completare la validazione.");
            }
            return state;
        }

        private static IstanzaDomicilioDecision Decide(
            DomicilioControlState state, IReadOnlyCollection<IstanzaDomicilioDocument> documents, string fiscalCode)
        {
            if (state.Severity == DomicilioControlSeverity.Conforme)
                return new(Accettabile, "Controlli superati: operazioni coerenti e documenti richiesti leggibili e congruenti.");

            // Un errore correggibile non equivale a un motivo di rifiuto. Anche
            // l'identità richiede tutti i documenti leggibili e un contesto certo.
            var contextCertain = documents.Count > 0 &&
                state.Findings.All(x => x.Code.StartsWith("PDF_", StringComparison.Ordinal) &&
                    x.Severity != DomicilioControlSeverity.VerificaManuale) &&
                documents.All(x => x.Expectation != null && x.Attachment?.Analysis is
                    { PdfValid: true, Readable: true, TypeRecognized: true, DataCoherent: not null } analysis &&
                    analysis.State.Findings.All(f => f.Severity != DomicilioControlSeverity.VerificaManuale));
            if (contextCertain)
            {
                foreach (var document in documents)
                {
                    var analysis = document.Attachment!.Analysis!;
                    var wrongRole = analysis.State.Findings.Any(x => x.Code == "PDF_020");
                    var missingExpectedIdentity = analysis.State.Findings.Any(x => x.Code is "PDF_009" or "PDF_022");
                    // Un CF diverso potrebbe appartenere al locatore. Serve
                    // identificare esplicitamente un altro conduttore/cessionario.
                    var otherTenant = Regex.Matches(analysis.ExtractedFiscalCodes,
                            @"\b(?<cf>[A-Z]{6}[0-9]{2}[A-Z][0-9]{2}[A-Z][0-9]{3}[A-Z])\s*\((?<ruolo>[^)]+)\)", RegexOptions.IgnoreCase)
                        .Cast<Match>().Any(x =>
                            !string.Equals(x.Groups["cf"].Value, fiscalCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                            (document.Expectation!.Type == DomicilioPdfDocumentType.Contratto &&
                                 x.Groups["ruolo"].Value == "locatore cessionario/conduttore" ||
                             document.Expectation.Type == DomicilioPdfDocumentType.Subentro &&
                                 x.Groups["ruolo"].Value == "conduttore cessionario"));
                    var differentIdentity = missingExpectedIdentity && otherTenant;
                    if (wrongRole || differentIdentity)
                        return new(Rifiutare, wrongRole
                            ? "Documento leggibile: lo studente risulta in un ruolo incompatibile con il subentro richiesto."
                            : "Documento leggibile: il codice fiscale dello studente è assente ed è identificato un altro conduttore.");
                }
            }

            return new(DaValidare, state.Descriptions);
        }

        public static IEnumerable<string[]> BuildRows(IEnumerable<IstanzaDomicilioRecord> requests)
        {
            foreach (var request in requests.OrderBy(x => x.NumIstanza))
            {
                var decision = Decide(request);
                yield return Row(request, null, decision, RequestState(request));
                foreach (var operation in request.Operazioni.OrderBy(x => x.Ordine).ThenBy(x => x.Id))
                    yield return Row(request, operation, decision, OperationState(operation));
            }
        }

        private static string[] Row(IstanzaDomicilioRecord request, IstanzaDomicilioOperation? operation,
            IstanzaDomicilioDecision decision, DomicilioControlState state)
        {
            var documents = operation?.Documenti ?? request.Operazioni.SelectMany(x => x.Documenti).ToList();
            string DocumentValues(Func<IstanzaDomicilioDocument, string?> value)
                => string.Join(" | ", documents.Select(x => $"{x.Tipo} [{Number(x.IdAllegato)}]: {value(x)}"));
            var officeAction = decision.Azione == Rifiutare
                ? "Proposta di rifiuto per identità/ruolo incompatibili. " + state.OfficeActions
                : decision.Azione == Accettabile
                    ? "Accettabile nel backoffice dopo verifica che l'istanza e i dati correnti non siano cambiati."
                    : state.OfficeActions;
            return new[]
            {
                operation == null ? "ISTANZA" : "OPERAZIONE", Number(request.NumIstanza), decision.Azione,
                decision.Motivazione, request.AnnoAccademico, Number(request.NumDomanda), request.CodFiscale,
                request.Studente, request.DataPresentazione.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                request.PresaCarico ? "SI" : "NO", request.Operatore, Number(request.Operazioni.Count),
                Number(operation?.Id), Number(operation?.Ordine), operation?.EntitaDescrizione ?? string.Empty,
                operation?.OperazioneDescrizione ?? string.Empty, Number(operation?.IdOperazionePadre),
                Number(operation?.IdDomicilio), Number(operation?.IdContratto), Number(operation?.IdProroga),
                state.Outcome, state.Codes, state.Descriptions, officeAction, operation?.DatiProposti ?? string.Empty,
                string.Join(" | ", documents.Select(x => x.Tipo)),
                string.Join(" | ", documents.Select(x => Number(x.IdAllegato))),
                DocumentValues(x => Bool(x.Attachment?.Analysis?.Readable)),
                DocumentValues(x => Bool(x.Attachment?.Analysis?.TypeRecognized)),
                DocumentValues(x => Bool(x.Attachment?.Analysis?.DataCoherent)),
                DocumentValues(x => x.Attachment?.Analysis?.ExtractedData),
                DocumentValues(x => x.Attachment?.FileName)
            };
        }

        public static string Export(IEnumerable<IstanzaDomicilioRecord> requests, string folderPath, string academicYear)
        {
            Directory.CreateDirectory(folderPath);
            var path = Path.Combine(folderPath, $"Controllo_Istanze_Domicilio_{academicYear}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
            writer.WriteLine(string.Join(";", Headers));
            foreach (var row in BuildRows(requests))
                writer.WriteLine(string.Join(";", row.Select(Escape)));
            return path;
        }

        private static string Number(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        private static string Bool(bool? value) => value.HasValue ? value.Value ? "SI" : "NO" : "NON_VERIFICABILE";
        private static string Escape(string value) => value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
