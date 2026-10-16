using System.Data;
using System.Data.SqlClient;
using ProcedureNet7;

internal static class DrosoStatusTests
{
    public static async Task RunAsync(string folder, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        var progress = new Progress<string>();
        DrosoSession Session(string name, params DrosoDocument[] documents) => new()
        {
            Directory = Path.Combine(folder, name), SourceDatabase = "SyntheticSource",
            CheckpointSha256 = "SYNTHETIC-CHECKPOINT", Documents = documents.ToList()
        };

        var local = Session("local", Document(1));
        local.SourceDatabase = "";
        using (var unopened = new SqlConnection())
            await DrosoStatusRepository.ApplyAsync(unopened, local, progress, CancellationToken.None);
        check(local.Documents[0].AppliedStatus == "" && local.Documents[0].StatusUpdateNote.Contains("locale"),
            "Sessione locale: nessun collegamento SQL e motivazione locale salvata");

        var missingServer = Session("missing-server", Document(1));
        using (var unopened = new SqlConnection())
            await DrosoStatusRepository.ApplyAsync(unopened, missingServer, progress, CancellationToken.None);
        check(missingServer.Documents[0].AppliedStatus == "" && missingServer.Documents[0].StatusUpdateNote.Contains("origine incompleta"),
            "Sessione senza server di origine: stato non scritto e nessuna apertura SQL");

        var wrongServer = Session("wrong-server", Document(1));
        wrongServer.SourceServer = "ServerOriginale";
        using (var unopened = new SqlConnection("Data Source=ServerDiverso;Initial Catalog=SyntheticSource;Integrated Security=True"))
            await DrosoStatusRepository.ApplyAsync(unopened, wrongServer, progress, CancellationToken.None);
        check(wrongServer.Documents[0].AppliedStatus == "" && wrongServer.Documents[0].StatusUpdateNote.Contains("Server o database diversi"),
            "Stesso nome database su un altro server: nessun collegamento né aggiornamento");

        var missingModel = Session("missing-model", Document(1));
        missingModel.CheckpointSha256 = "";
        int calls = 0;
        await DrosoStatusRepository.ApplyOutcomesAsync(missingModel, (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("Il writer non deve essere invocato");
        }, progress, CancellationToken.None);
        check(calls == 0 && missingModel.Documents[0].AppliedStatus == "", "Checkpoint assente: nessuna scrittura");

        var incomplete = Document(1); incomplete.AnalysisCompleted = false;
        var oldSession = Document(2); oldSession.SelectedStatusId = null;
        var missingDate = Document(3); missingDate.SubmittedAt = null;
        var localId = Document(0);
        var invalidCf = Document(5); invalidCf.CodiceFiscale = "INVALID";
        var invalidStateId = Document(6); invalidStateId.SelectedStatusId = (long)int.MaxValue + 1;
        var invalid = Session("invalid", incomplete, oldSession, missingDate, localId, invalidCf, invalidStateId);
        await DrosoStatusRepository.ApplyOutcomesAsync(invalid, (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("Il writer non deve essere invocato");
        }, progress, CancellationToken.None);
        check(calls == 0 && invalid.Documents.All(document => document.AppliedStatus == "" && document.StatusUpdateNote.Length > 0),
            "Analisi e identità incomplete: nessuna scrittura per sessioni vecchie o locali");

        var accepted = Document(10);
        accepted.DeclaredLabel = "RICEVUTA";
        var rejected = Document(11); rejected.Confidence = .9;
        var applied = Session("applied", accepted, rejected);
        DateTime serverTime = new(2026, 10, 6, 12, 0, 1);
        calls = 0;
        Task<DrosoStatusResult> Success(DrosoDocument document, CancellationToken _)
        {
            calls++;
            return Task.FromResult(new DrosoStatusResult(true, serverTime, checked((int)document.SelectedStatusId!.Value + 1), document.ProposedStatus));
        }
        await DrosoStatusRepository.ApplyOutcomesAsync(applied, Success, progress, CancellationToken.None);
        check(calls == 2 && accepted.AppliedStatus == "02" && rejected.AppliedStatus == "04",
            "Applicati 02 e 04 secondo il punteggio, senza dipendere dalla sezione dichiarata");
        check(accepted.AppliedStatusId == accepted.SelectedStatusId + 1 && rejected.AppliedStatusId == rejected.SelectedStatusId + 1,
            "Identità degli stati salvati conservata per verificare i messaggi ai respinti");
        check(accepted.AppliedAt == serverTime && rejected.AppliedAt == serverTime && rejected.StatusUpdateNote.Contains("non riconosciuto"),
            "Data SQL e motivazione locale conservate nell'audit");
        await DrosoStatusRepository.ApplyOutcomesAsync(applied, Success, progress, CancellationToken.None);
        check(calls == 2, "Ripetere il salvataggio non invoca SQL per risultati già applicati");
        var roundTrip = DrosoSession.Load(Path.Combine(applied.Directory, "session.json"));
        check(roundTrip.SourceDatabase == applied.SourceDatabase && roundTrip.CheckpointSha256 == applied.CheckpointSha256
            && roundTrip.Documents[1].AppliedStatus == "04" && roundTrip.Documents[1].AppliedAt == serverTime
            && roundTrip.Documents[1].AppliedStatusId == rejected.AppliedStatusId,
            "Sessione riletta: origine, checkpoint e audit degli stati preservati");

        var stale = Session("stale", Document(20));
        await DrosoStatusRepository.ApplyOutcomesAsync(stale, (_, _) =>
            Task.FromResult(new DrosoStatusResult(false, null, 999, "05")), progress, CancellationToken.None);
        check(stale.Documents[0].AppliedStatus == "" && stale.Documents[0].AppliedAt == null
            && stale.Documents[0].StatusUpdateNote.Contains("cambiato"), "Stato modificato dall'operatore: aggiornamento non attribuito a Droso");

        var partial = Session("partial", Document(30), Document(31));
        using (var cancellation = new CancellationTokenSource())
        {
            calls = 0;
            try
            {
                await DrosoStatusRepository.ApplyOutcomesAsync(partial, (document, _) =>
                {
                    calls++;
                    cancellation.Cancel();
                    return Task.FromResult(new DrosoStatusResult(true, serverTime, 301, document.ProposedStatus));
                }, progress, cancellation.Token);
                check(false, "Annullamento tra due aggiornamenti atteso");
            }
            catch (OperationCanceledException)
            {
                var saved = DrosoSession.Load(Path.Combine(partial.Directory, "session.json"));
                check(calls == 1 && saved.Documents[0].AppliedStatus == "02" && saved.Documents[1].AppliedStatus == "",
                    "Annullamento: il primo commit confermato resta salvato, il secondo non viene tentato");
            }
        }

        var failure = Session("failure", Document(40));
        try
        {
            await DrosoStatusRepository.ApplyOutcomesAsync(failure, (_, _) =>
                throw new IOException("Synthetic acknowledgement failure"), progress, CancellationToken.None);
            check(false, "Errore del writer atteso");
        }
        catch (IOException)
        {
            var saved = DrosoSession.Load(Path.Combine(failure.Directory, "session.json"));
            check(saved.Documents[0].AppliedStatus == "" && saved.Documents[0].StatusUpdateNote.Contains("non confermato"),
                "Errore di conferma: audit preservato senza dichiarare un rollback non verificato");
        }

        using (var connection = new SqlConnection())
        using (var command = DrosoStatusRepository.CreateInsertCommand(connection, null!, rejected))
        {
            check(command.Parameters["@id"].SqlDbType == SqlDbType.Decimal && command.Parameters["@id"].Precision == 18
                && command.Parameters["@expectedId"].SqlDbType == SqlDbType.Int
                && command.Parameters["@expectedDate"].SqlDbType == SqlDbType.DateTime
                && command.Parameters["@cf"].Size == 16 && (string)command.Parameters["@target"].Value == "04",
                "Parametri SQL tipizzati secondo lo schema, senza interpolare identità o categoria");
            check(command.CommandText.Contains("INSERT INTO dbo.STATUS_ALLEGATI")
                && !command.CommandText.Contains("UPDATE dbo.") && !command.CommandText.Contains("DELETE ")
                && command.CommandText.Contains("SUSER_SNAME(),NULL,NULL,NULL"),
                "Storico append-only; autore SQL e campi motivo/flag/fine validità NULL");
        }
    }

    private static DrosoDocument Document(long id) => new()
    {
        IdAllegato = id, CodiceFiscale = "SYNTHETIC0000000", SelectedStatusId = id + 100,
        SubmittedAt = new DateTime(2026, 10, 6, 12, 0, 0), AnalysisCompleted = true,
        Predicted = "PASSAPORTO", Confidence = .95, Sha256 = "SYNTHETIC", Path = "synthetic.pdf"
    };
}
