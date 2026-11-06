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
            writer.WriteLine(string.Join(";", IstanzaDomicilioReport.Headers.Concat(new[]
            {
                "MODALITA", "UTENTE_LAVORAZIONE", "CORREZIONI_PROPOSTE", "VALORI_ORIGINALI", "VALORI_DOCUMENTATI",
                "FONTI_CORREZIONI", "ESITO_DOPO_CORREZIONI", "MOTIVO_DOPO_CORREZIONI"
            })));
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
                        "IDONEA_CON_CORREZIONI" => "Nessuna modifica: proposta documentata idonea dopo la rivalidazione completa.",
                        "IGNORATA" => "Lasciata alla lavorazione manuale: " + result.Reason,
                        _ => "Verificare lo stato dell'istanza prima di riprovare."
                    };
                    var operationId = Array.IndexOf(IstanzaDomicilioReport.Headers, "ID_OPERAZIONE");
                    var corrections = request.ProposedCorrections
                        .Where(x => string.IsNullOrWhiteSpace(row[operationId]) || x.OperationId.ToString() == row[operationId])
                        .SelectMany(op => op.Fields.Select(field => (op.OperationId, Field: field))).ToList();
                    var candidate = request.CorrectionCandidate;
                    var candidateDecision = candidate == null ? null : IstanzaDomicilioReport.Decide(candidate);
                    var extra = new[]
                    {
                        mode, DomicilioAutomaticEngine.AuditUser,
                        string.Join(" | ", corrections.Select(x => $"Op. {x.OperationId}: {FieldLabel(x.Field.Field)}")),
                        string.Join(" | ", corrections.Select(x => $"{FieldLabel(x.Field.Field)}={DisplayValue(x.Field.Before)}")),
                        string.Join(" | ", corrections.Select(x => $"{FieldLabel(x.Field.Field)}={DisplayValue(x.Field.After)}")),
                        string.Join(" | ", corrections.Select(x => $"Allegato {x.Field.AttachmentId:0}, pagina {x.Field.Page}: {x.Field.Label}")),
                        candidateDecision?.Azione ?? "", candidateDecision?.Motivazione ?? ""
                    };
                    writer.WriteLine(string.Join(";", row.Concat(extra).Select(Escape)));
                }
            }
            return path;
        }

        private static string DisplayValue(string value)
            => DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date) && value.Length >= 10 && value[4] == '-'
                ? date.ToString("dd/MM/yyyy") : value;

        private static string FieldLabel(string field) => field.ToLowerInvariant() switch
        {
            "numeroseriecontratto" => "Estremi contratto", "dataregistrazionecontratto" => "Registrazione contratto",
            "datainiziocontratto" => "Decorrenza contratto", "datafinecontratto" => "Scadenza contratto",
            "numeroseriesubentro" => "Estremi subentro", "datainiziosubentro" => "Decorrenza subentro",
            "numeroserieproroga" => "Estremi proroga", "datadecorrenza" => "Decorrenza proroga",
            "datascadenza" => "Scadenza proroga", _ => field
        };

        private static string Escape(string value) => value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
