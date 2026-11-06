using DomicilioAutomation;
using DomicilioAutomationWorker;
using DirStudio.Data.Files.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serbenco.Helpers;
using System.Text.Json;

var protocolWriter = Console.Out;
Console.SetOut(TextWriter.Null);
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
DomicilioAutomationJob? job;
try
{
    job = JsonSerializer.Deserialize<DomicilioAutomationJob>(await Console.In.ReadToEndAsync(), options);
    if (job == null || job.Requests == null || job.Requests.Any(x => x is null) || string.IsNullOrWhiteSpace(job.ConnectionString))
        throw new JsonException();
}
catch
{
    Console.Error.WriteLine("Richiesta del worker non valida.");
    return 2;
}

void WriteResult(int requestId, string outcome, string reason)
{
    protocolWriter.WriteLine(JsonSerializer.Serialize(new DomicilioAutomationResult(requestId, outcome, reason)));
    protocolWriter.Flush();
}

if (job.Requests.Count == 0)
    return 0;

var duplicates = job.Requests.GroupBy(x => x.NumIstanza).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
var requests = new List<DomicilioAutomationRequest>();
foreach (var request in job.Requests)
{
    if (request.NumIstanza <= 0 || string.IsNullOrWhiteSpace(request.Fingerprint) ||
        request.Fingerprint.Length != 64 || !request.Fingerprint.All(Uri.IsHexDigit) || duplicates.Contains(request.NumIstanza) ||
        request.Corrections == null || request.Corrections.Any(c => c is null || c.OperationId <= 0 ||
            string.IsNullOrWhiteSpace(c.BeforePayload) || string.IsNullOrWhiteSpace(c.AfterPayload) || c.Fields == null || c.Fields.Count == 0) ||
        request.Corrections.Select(c => c.OperationId).Distinct().Count() != request.Corrections.Count)
        WriteResult(request.NumIstanza, "IGNORATA", "Identificativo, impronta o correzioni non validi, oppure istanza ripetuta nel lotto.");
    else
        requests.Add(request);
}
if (requests.Count == 0)
    return 0;

string filesConnectionString;
try
{
    var builder = new SqlConnectionStringBuilder(job.ConnectionString);
    if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        throw new ArgumentException();
    builder.InitialCatalog = "ADISU_ALLEGATI";
    filesConnectionString = builder.ConnectionString;
}
catch
{
    foreach (var request in requests)
        WriteResult(request.NumIstanza, "ERRORE", "La connessione SQL fornita non identifica un database valido.");
    return 2;
}

ServiceProvider services;
try
{
    services = BackendServices.Create(job.ConnectionString, filesConnectionString);
}
catch
{
    foreach (var request in requests)
        WriteResult(request.NumIstanza, "ERRORE", "Servizi backend non disponibili. Nessuna istanza è stata lavorata.");
    return 2;
}
using var disposeServices = services;
try
{
    using var checkScope = services.CreateScope();
    var files = checkScope.ServiceProvider.GetRequiredService<ADISU_SVILUPPO_FILESContext>();
    await files.Database.OpenConnectionAsync();
    await files.Database.CloseConnectionAsync();
}
catch (Exception ex)
{
    var detail = ex is SqlException sql ? $" (SQL {sql.Number})" : "";
    foreach (var request in requests)
        WriteResult(request.NumIstanza, "ERRORE", $"Archivio allegati non disponibile{detail}. Nessuna istanza è stata lavorata.");
    return 2;
}

foreach (var request in requests)
{
    try
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DomicilioLavorazioneService>();
        await service.AcceptAutomaticallyAsync(request.NumIstanza, request.Fingerprint, DomicilioAutomationSnapshot.Capture, request.Corrections);
        WriteResult(request.NumIstanza, "ACCOLTA", request.Corrections.Count == 0
            ? "Istanza accolta con i servizi backend e registrata come AUTO_DOMICILIO."
            : "Dati proposti corretti dalle fonti PDF e istanza accolta nella stessa transazione come AUTO_DOMICILIO.");
    }
    catch (IstanzaLavorazioneException ex)
    {
        WriteResult(request.NumIstanza, "IGNORATA", ex.Message);
    }
    catch
    {
        // Database/provider exceptions can contain connection details. Keep them out of the protocol.
        WriteResult(request.NumIstanza, "ERRORE", "Accoglimento non confermato. Verificare lo stato dell'istanza prima di riprovare.");
    }
}
return 0;
