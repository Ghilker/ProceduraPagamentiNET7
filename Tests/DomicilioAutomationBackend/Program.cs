using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DirStudio.Data.Files.Models;
using DirStudio.Data.Models;
using DomicilioAutomation;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using DomicilioAutomationWorker;
using Serbenco.Helpers;

if (args.Length == 0)
{
    Console.WriteLine("Suite opzionale: --rollback oppure --accept-rollback configPath requestId. Non eseguita alcuna connessione.");
    return;
}
if (args.Length != 3 || args[0] is not ("--rollback" or "--accept-rollback") || !int.TryParse(args[2], out var requestId) || requestId <= 0)
    throw new ArgumentException("Usare --rollback oppure --accept-rollback configPath requestId con un'istanza di test inviata, libera e validabile.");

using var config = JsonDocument.Parse(File.ReadAllText(args[1]), new JsonDocumentOptions
{
    CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
});
var connections = FindConnectionMain(config.RootElement).Distinct(StringComparer.Ordinal).ToArray();
if (connections.Length != 1) throw new InvalidOperationException("La configurazione deve contenere una sola ConnectionMain esterna.");
var connection = new SqlConnectionStringBuilder(connections[0]);
if (!string.Equals(connection.DataSource, "database-disco.ctayekcu0lnn.eu-south-1.rds.amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
    !string.Equals(connection.InitialCatalog, "ADISU", StringComparison.OrdinalIgnoreCase) ||
    !string.Equals(connection.UserID, "Verifica", StringComparison.Ordinal) || connection.IntegratedSecurity || connection.AttachDBFilename.Length > 0)
    throw new InvalidOperationException("Il test di rollback consente soltanto il server, database e utente di test autorizzati.");

// External contexts may log SQL through Console.WriteLine in OnConfiguring.
// Keep that output private and emit only the final synthetic test outcome.
var testOutput = Console.Out;
Console.SetOut(TextWriter.Null);
if (args[0] == "--accept-rollback")
{
    await AcceptanceRollback(connection, requestId, testOutput);
    return;
}

// The fake applier never invokes a real manager. A separate transaction
// interceptor also forbids every commit, even if the service regresses.
var commitGuard = new ForbidCommitInterceptor();
await using var db = new ADISU_SVILUPPOContext(new DbContextOptionsBuilder<ADISU_SVILUPPOContext>()
    .UseSqlServer(connection.ConnectionString).AddInterceptors(commitGuard).Options);
connection.InitialCatalog = "ADISU_ALLEGATI";
await using var files = new ADISU_SVILUPPO_FILESContext(new DbContextOptionsBuilder<ADISU_SVILUPPO_FILESContext>()
    .UseSqlServer(connection.ConnectionString).Options);
await db.Database.OpenConnectionAsync();
var dbConnection = db.Database.GetDbConnection();
var expectedFingerprint = DomicilioAutomationSnapshot.Capture(dbConnection, null, requestId);
var beforeEffects = CaptureEffects(dbConnection, requestId);
var applier = new FailingApplier(db);
var service = new DomicilioLavorazioneService(db, files, applier);
var staleRejected = false;
var staleFingerprint = (expectedFingerprint[0] == 'A' ? "B" : "A") + expectedFingerprint[1..];
try
{
    await service.AcceptAutomaticallyAsync(requestId, staleFingerprint, DomicilioAutomationSnapshot.Capture);
}
catch (IstanzaLavorazioneException)
{
    staleRejected = true;
}
if (!staleRejected || applier.Calls != 0 || expectedFingerprint != DomicilioAutomationSnapshot.Capture(dbConnection, null, requestId) ||
    beforeEffects != CaptureEffects(dbConnection, requestId))
    throw new Exception("Uno snapshot diverso deve fermare l'accoglimento prima dell'applier senza mantenere la presa in carico.");
var failedAsExpected = false;
try
{
    await service.AcceptAutomaticallyAsync(requestId, expectedFingerprint, DomicilioAutomationSnapshot.Capture);
}
catch (ForcedApplierFailure)
{
    failedAsExpected = true;
}
catch (ForbiddenCommitFailure)
{
    throw new Exception("Il servizio ha tentato un commit senza attraversare l'applier di test; il commit è stato impedito.");
}
catch
{
    throw new Exception("L'istanza è stata fermata da una precondizione prima del punto di rollback previsto; nessun test di rollback dimostrato.");
}
if (!failedAsExpected || applier.Calls != 1 || !applier.ObservedClaim || !applier.ObservedTransaction || applier.OperationCount <= 0)
    throw new Exception("Il test deve raggiungere l'applier una sola volta, nella transazione e dopo la presa in carico AUTO_DOMICILIO.");
if (commitGuard.Attempts != 0)
    throw new Exception("Il percorso di errore non deve tentare alcun commit.");
if (db.Database.CurrentTransaction != null || db.ChangeTracker.Entries().Any())
    throw new Exception("La transazione e il tracker devono essere liberati dopo il rollback.");
var afterFingerprint = DomicilioAutomationSnapshot.Capture(dbConnection, null, requestId);
var afterEffects = CaptureEffects(dbConnection, requestId);
if (expectedFingerprint != afterFingerprint || beforeEffects != afterEffects)
    throw new Exception("Il rollback non ha ripristinato integralmente istanza, presa in carico, dati, storico, messaggi e blocchi.");
testOutput.WriteLine("PASS: errore applicativo forzato dopo presa in carico; rollback integrale verificato, nessun accoglimento eseguito.");

static async Task AcceptanceRollback(SqlConnectionStringBuilder connection, int requestId, TextWriter output)
{
    var guard = new ForbidCommitInterceptor();
    var main = connection.ConnectionString;
    var files = new SqlConnectionStringBuilder(main) { InitialCatalog = "ADISU_ALLEGATI" }.ConnectionString;
    using var services = BackendServices.Create(main, files, guard);
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ADISU_SVILUPPOContext>();
    var realApplier = scope.ServiceProvider.GetRequiredService<IDomicilioIstanzaApplier>();
    if (realApplier is not DomicilioIstanzaApplier)
        throw new Exception("La verifica integrale richiede l'applier reale registrato dal worker.");
    var service = scope.ServiceProvider.GetRequiredService<DomicilioLavorazioneService>();
    await db.Database.OpenConnectionAsync();
    var sql = db.Database.GetDbConnection();
    var beforeFingerprint = DomicilioAutomationSnapshot.Capture(sql, null, requestId);
    var beforeEffects = CaptureEffects(sql, requestId);
    var before = ObserveAcceptance(sql, null, requestId);
    var observedAcceptance = false;
    guard.BeforeCommit = tx =>
    {
        var after = ObserveAcceptance(tx.Connection!, tx, requestId);
        observedAcceptance = after.Accepted && before.ActiveOperations > 0 && after.ActiveOperations == 0 &&
            after.ActiveStatuses == 0 && after.AutoClosedStatuses > before.AutoClosedStatuses &&
            after.AutoMessages == before.AutoMessages + 1 && after.AutoNotes > before.AutoNotes &&
            after.ActiveVvc == 0 && after.OtherActiveBlocks == before.OtherActiveBlocks &&
            (before.ActiveVvc == 0 || after.GeneralHistory > before.GeneralHistory && after.AutoGeneralHistory > before.AutoGeneralHistory);
    };
    var commitPrevented = false;
    try
    {
        await service.AcceptAutomaticallyAsync(requestId, beforeFingerprint, DomicilioAutomationSnapshot.Capture);
    }
    catch (ForbiddenCommitFailure)
    {
        commitPrevented = true;
    }
    catch
    {
        throw new Exception("Il percorso reale è stato fermato prima del commit di prova: nessun accoglimento integrale dimostrato e nessun commit consentito.");
    }
    if (!commitPrevented || guard.Attempts != 1 || !observedAcceptance)
        throw new Exception("Gli effetti standard dell'accoglimento devono essere visibili nella transazione prima del commit impedito.");
    if (db.Database.CurrentTransaction != null || db.ChangeTracker.Entries().Any() ||
        beforeFingerprint != DomicilioAutomationSnapshot.Capture(sql, null, requestId) || beforeEffects != CaptureEffects(sql, requestId))
        throw new Exception("Dopo il commit impedito il rollback deve ripristinare integralmente l'istanza e tutti gli effetti osservati.");
    output.WriteLine("PASS: DI e applier reali; accoglimento, chiusure, messaggio, storico e VVC osservati nella transazione. Commit impedito e rollback integrale verificato.");
}

static AcceptanceObservation ObserveAcceptance(DbConnection connection, DbTransaction? transaction, int requestId)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandTimeout = 180;
    command.CommandText = @"
SELECT CASE WHEN g.Esito_istanza=1 AND g.Data_fine_validita IS NOT NULL AND g.Utente='AUTO_DOMICILIO' THEN 1 ELSE 0 END,
 (SELECT COUNT(*) FROM dbo.Istanza_Domicilio_Operazioni o WHERE o.NUM_ISTANZA=g.Num_istanza AND o.DATA_FINE_VALIDITA IS NULL),
 (SELECT COUNT(*) FROM dbo.Istanza_status s WHERE s.Num_istanza=g.Num_istanza AND s.data_fine_validita IS NULL),
 (SELECT COUNT(*) FROM dbo.Istanza_status s WHERE s.Num_istanza=g.Num_istanza AND s.data_fine_validita IS NOT NULL AND s.presa_carico=1 AND s.utente_presa_carico='AUTO_DOMICILIO'),
 (SELECT COUNT(*) FROM dbo.Messaggi_studente m WHERE m.Cod_fiscale=g.Cod_fiscale AND m.Utente='AUTO_DOMICILIO'),
 (SELECT COUNT(*) FROM dbo.Documentazione n WHERE n.Anno_accademico=g.Anno_accademico AND n.Num_domanda=g.Num_domanda AND n.Utente='AUTO_DOMICILIO'),
 (SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti b WHERE b.Anno_accademico=g.Anno_accademico AND b.Num_domanda=g.Num_domanda AND b.Cod_tipologia_blocco='VVC' AND b.Data_fine_validita IS NULL),
 (SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti b WHERE b.Anno_accademico=g.Anno_accademico AND b.Num_domanda=g.Num_domanda AND b.Cod_tipologia_blocco<>'VVC' AND b.Data_fine_validita IS NULL),
 (SELECT COUNT(*) FROM dbo.DatiGenerali_dom d WHERE d.Anno_accademico=g.Anno_accademico AND d.Num_domanda=g.Num_domanda),
 (SELECT COUNT(*) FROM dbo.DatiGenerali_dom d WHERE d.Anno_accademico=g.Anno_accademico AND d.Num_domanda=g.Num_domanda AND d.Utente='AUTO_DOMICILIO')
FROM dbo.Istanza_dati_generali g WHERE g.Num_istanza=@id;";
    var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.DbType = DbType.Int32; parameter.Value = requestId;
    command.Parameters.Add(parameter);
    using var reader = command.ExecuteReader();
    if (!reader.Read()) throw new InvalidOperationException("Istanza non disponibile per osservare gli effetti standard.");
    return new AcceptanceObservation(reader.GetInt32(0) == 1, reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
        reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9));
}

static IEnumerable<string> FindConnectionMain(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("ConnectionMain") && property.Value.ValueKind == JsonValueKind.String)
                yield return property.Value.GetString()!;
            else foreach (var value in FindConnectionMain(property.Value)) yield return value;
        }
    else if (element.ValueKind == JsonValueKind.Array)
        foreach (var item in element.EnumerateArray())
            foreach (var value in FindConnectionMain(item)) yield return value;
}

static string CaptureEffects(DbConnection connection, int requestId)
{
    using var command = connection.CreateCommand();
    command.CommandTimeout = 180;
    command.CommandText = @"
SELECT
 (SELECT m.* FROM dbo.Messaggi_studente m WHERE m.Cod_fiscale=g.Cod_fiscale
  ORDER BY m.Data_inserimento_messaggio,CONVERT(nvarchar(max),m.Messaggio),m.Utente FOR JSON PATH, INCLUDE_NULL_VALUES),
 (SELECT n.* FROM dbo.Documentazione n WHERE n.Anno_accademico=g.Anno_accademico AND n.Num_domanda=g.Num_domanda
  ORDER BY n.Data_validita,CONVERT(nvarchar(max),n.Nota),n.Utente FOR JSON PATH, INCLUDE_NULL_VALUES),
 (SELECT b.* FROM dbo.Motivazioni_blocco_pagamenti b WHERE b.Anno_accademico=g.Anno_accademico AND b.Num_domanda=g.Num_domanda
  ORDER BY b.Data_validita,b.Cod_tipologia_blocco FOR JSON PATH, INCLUDE_NULL_VALUES),
 (SELECT d.* FROM dbo.DatiGenerali_dom d WHERE d.Anno_accademico=g.Anno_accademico AND d.Num_domanda=g.Num_domanda
  ORDER BY d.Data_validita FOR JSON PATH, INCLUDE_NULL_VALUES)
FROM dbo.Istanza_dati_generali g WHERE g.Num_istanza=@id;";
    var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.DbType = DbType.Int32; parameter.Value = requestId;
    command.Parameters.Add(parameter);
    using var reader = command.ExecuteReader();
    if (!reader.Read()) throw new InvalidOperationException("Istanza non disponibile per confrontare gli effetti del rollback.");
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    for (var i = 0; i < reader.FieldCount; i++)
    {
        var text = reader.IsDBNull(i) ? "" : reader.GetString(i);
        var bytes = Encoding.UTF8.GetBytes(text);
        hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
    }
    return Convert.ToHexString(hash.GetHashAndReset());
}

internal sealed class ForcedApplierFailure : Exception { }
internal sealed class ForbiddenCommitFailure : Exception { }
internal sealed record AcceptanceObservation(bool Accepted, int ActiveOperations, int ActiveStatuses, int AutoClosedStatuses,
    int AutoMessages, int AutoNotes, int ActiveVvc, int OtherActiveBlocks, int GeneralHistory, int AutoGeneralHistory);

internal sealed class FailingApplier : IDomicilioIstanzaApplier
{
    private readonly ADISU_SVILUPPOContext _db;
    public FailingApplier(ADISU_SVILUPPOContext db) => _db = db;
    public int Calls { get; private set; }
    public bool ObservedClaim { get; private set; }
    public bool ObservedTransaction { get; private set; }
    public int OperationCount { get; private set; }
    public async Task ApplyAsync(IstanzaDatiGenerali request, Domandum application, IReadOnlyList<IstanzaDomicilioOperazione> operations, string username)
    {
        Calls++; OperationCount = operations.Count;
        ObservedTransaction = _db.Database.CurrentTransaction != null;
        ObservedClaim = username == "AUTO_DOMICILIO" && await _db.IstanzaStatus.AsNoTracking().AnyAsync(s =>
            s.NumIstanza == request.NumIstanza && s.DataFineValidita == null && s.PresaCarico == 1 && s.UtentePresaCarico == "AUTO_DOMICILIO");
        throw new ForcedApplierFailure();
    }
}

internal sealed class ForbidCommitInterceptor : DbTransactionInterceptor
{
    public int Attempts { get; private set; }
    public Action<DbTransaction>? BeforeCommit { get; set; }
    public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        Attempts++; BeforeCommit?.Invoke(transaction); throw new ForbiddenCommitFailure();
    }
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Attempts++; BeforeCommit?.Invoke(transaction); return ValueTask.FromException<InterceptionResult>(new ForbiddenCommitFailure());
    }
}
