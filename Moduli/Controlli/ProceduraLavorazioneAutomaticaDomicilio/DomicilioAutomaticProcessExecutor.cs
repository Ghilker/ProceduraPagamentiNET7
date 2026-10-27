using DomicilioAutomation;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text.Json;

namespace ProcedureNet7
{
    internal sealed class DomicilioAutomaticProcessExecutor : IDomicilioAutomaticExecutor
    {
        private readonly SqlConnection _connection;
        private readonly string _workerPath;
        private readonly string? _originalConnectionString;

        public DomicilioAutomaticProcessExecutor(SqlConnection connection, string? originalConnectionString)
        {
            _connection = connection;
            _originalConnectionString = originalConnectionString;
            _workerPath = Path.Combine(AppContext.BaseDirectory, "DomicilioAutomation", "DomicilioAutomationWorker.dll");
        }

        public IEnumerable<DomicilioAutomationResult> Execute(IReadOnlyList<DomicilioAutomationRequest> requests)
        {
            var results = new Dictionary<int, DomicilioAutomationResult>();
            if (requests.Count == 0) return results.Values;
            if (!File.Exists(_workerPath))
                return requests.Select(x => new DomicilioAutomationResult(x.NumIstanza, "ERRORE",
                    "Componente di accoglimento non installato: compilare Tools/DomicilioAutomationWorker/Build.ps1 e ridistribuire l'applicazione."));

            string workerConnectionString;
            try { workerConnectionString = DomicilioAutomaticConnectionSettings.FromOriginal(_originalConnectionString); }
            catch (InvalidOperationException)
            {
                return requests.Select(x => new DomicilioAutomationResult(x.NumIstanza, "ERRORE",
                    "Connessione originale non disponibile per il componente di accoglimento. Riconnettersi a Procedure prima di riprovare; nessuna istanza lavorata."));
            }

            var settings = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            settings.ArgumentList.Add(_workerPath);
            using var process = new Process { StartInfo = settings };
            try
            {
                process.Start();
                // Drain diagnostics without copying provider messages or connection details to the report.
                var diagnostics = process.StandardError.ReadToEndAsync();
                process.StandardInput.Write(JsonSerializer.Serialize(new DomicilioAutomationJob
                {
                    ConnectionString = workerConnectionString, Requests = requests.ToList()
                }));
                process.StandardInput.Close();

                var expectedIds = requests.Select(x => x.NumIstanza).ToHashSet();
                var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                while (true)
                {
                    var read = process.StandardOutput.ReadLineAsync();
                    if (!read.Wait(TimeSpan.FromMinutes(5))) throw new TimeoutException();
                    var line = read.Result;
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var result = JsonSerializer.Deserialize<DomicilioAutomationResult>(line, jsonOptions);
                    if (result == null || !expectedIds.Remove(result.NumIstanza) ||
                        result.Outcome is not ("ACCOLTA" or "IGNORATA" or "ERRORE"))
                        throw new InvalidDataException("Risposta del componente di accoglimento non valida.");
                    results.Add(result.NumIstanza, result);
                    Logger.LogInfo(80 + 19 * results.Count / requests.Count,
                        $"Lavorazione istanze: {results.Count}/{requests.Count}; ultima: {result.NumIstanza}, {result.Outcome}.");
                }
                if (!process.WaitForExit(10000)) throw new TimeoutException();
                _ = diagnostics.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // The worker may have committed before a communication failure.
                // Do not resend; recover the confirmed state below.
            }
            finally
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); } }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }

            var unconfirmed = requests.Where(x => !results.ContainsKey(x.NumIstanza) || results[x.NumIstanza].Outcome == "ERRORE").ToList();
            ConfirmCommitted(unconfirmed, results);
            foreach (var request in unconfirmed.Where(x => !results.ContainsKey(x.NumIstanza)))
                results[request.NumIstanza] = new(request.NumIstanza, "ERRORE", "Esito non confermato: verificare lo stato prima di riprovare.");
            return results.Values.OrderBy(x => x.NumIstanza).ToList();
        }

        private void ConfirmCommitted(IReadOnlyList<DomicilioAutomationRequest> requests,
            IDictionary<int, DomicilioAutomationResult> results)
        {
            if (requests.Count == 0) return;
            try
            {
                using var connection = (SqlConnection)((ICloneable)_connection).Clone();
                connection.Open();
                foreach (var batch in requests.Chunk(500))
                {
                    using var command = connection.CreateCommand();
                    command.CommandTimeout = 15;
                    var names = batch.Select((x, i) =>
                    {
                        var name = "@id" + i;
                        command.Parameters.Add(name, SqlDbType.Int).Value = x.NumIstanza;
                        return name;
                    });
                    command.CommandText = $@"SELECT Num_istanza,Esito_istanza,Utente FROM dbo.Istanza_dati_generali
WHERE Num_istanza IN ({string.Join(",", names)}) AND Data_fine_validita IS NOT NULL;";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var id = reader.GetInt32(0);
                        var accepted = !reader.IsDBNull(1) && reader.GetInt32(1) == 1;
                        var actor = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                        results[id] = accepted && actor == DomicilioAutomaticEngine.AuditUser
                            ? new(id, "ACCOLTA", "Accoglimento confermato rileggendo lo stato dopo un errore di comunicazione.")
                            : new(id, "IGNORATA", "Istanza già conclusa da un'altra lavorazione.");
                    }
                }
            }
            catch (SqlException) { /* Preserve uncertainty; no automatic retry. */ }
        }
    }
}
