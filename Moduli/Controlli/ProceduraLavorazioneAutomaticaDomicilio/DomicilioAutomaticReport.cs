using DomicilioAutomation;
using System.Text;

namespace ProcedureNet7
{
    internal static class DomicilioAutomaticReport
    {
        public static string Export(IReadOnlyList<IstanzaDomicilioRecord> requests,
            IReadOnlyList<DomicilioAutomationResult> results, bool simulation, string folderPath, string academicYear)
        {
            Directory.CreateDirectory(folderPath);
            var mode = simulation ? "SIMULAZIONE" : "LAVORAZIONE";
            var path = Path.Combine(folderPath, $"Lavorazione_Automatica_Domicilio_{academicYear}_{mode}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var byId = results.ToDictionary(x => x.NumIstanza);
            var actionIndex = Array.IndexOf(IstanzaDomicilioReport.Headers, "AZIONE_ISTANZA");
            var reasonIndex = Array.IndexOf(IstanzaDomicilioReport.Headers, "MOTIVO_AZIONE");
            var officeIndex = Array.IndexOf(IstanzaDomicilioReport.Headers, "AZIONE_UFFICIO");
            using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
            writer.WriteLine(string.Join(";", IstanzaDomicilioReport.Headers.Concat(new[] { "MODALITA", "UTENTE_LAVORAZIONE" })));
            foreach (var request in requests.OrderBy(x => x.NumIstanza))
            {
                var result = byId[request.NumIstanza];
                foreach (var row in IstanzaDomicilioReport.BuildRows(new[] { request }))
                {
                    row[actionIndex] = result.Outcome;
                    row[reasonIndex] = result.Reason;
                    row[officeIndex] = result.Outcome switch
                    {
                        "ACCOLTA" => "Accolta automaticamente con gli effetti standard del backoffice.",
                        "IDONEA" => "Nessuna modifica: istanza idonea nella simulazione.",
                        "IGNORATA" => "Lasciata alla lavorazione manuale: " + result.Reason,
                        _ => "Verificare lo stato dell'istanza prima di riprovare."
                    };
                    writer.WriteLine(string.Join(";", row.Concat(new[] { mode, DomicilioAutomaticEngine.AuditUser }).Select(Escape)));
                }
            }
            return path;
        }

        private static string Escape(string value) => value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
