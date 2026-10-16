using System.Data;
using System.Data.SqlClient;
using ProcedureNet7;

internal static class DrosoMessageTests
{
    public static async Task RunAsync(string folder, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        var progress = new Progress<string>();
        DrosoSession Session(string name, params DrosoDocument[] documents) => new()
        {
            Directory = Path.Combine(folder, name), SourceServer = "SyntheticServer", SourceDatabase = "SyntheticDatabase",
            CheckpointSha256 = "SYNTHETIC-CHECKPOINT", Documents = documents.ToList()
        };
        check(DrosoMessageRepository.MessageText == "#DA SCRIVERE#", "Messaggio: testo provvisorio definito nel codice");

        var local = Session("messages-local", Document(1));
        local.SourceDatabase = "";
        using (var connection = new SqlConnection())
            await DrosoMessageRepository.SendRejectedAsync(connection, local, progress, CancellationToken.None);
        check(local.Documents[0].MessageSentAt is null && local.Documents[0].MessageUpdateNote.Contains("locale"),
            "Messaggi: sessione locale non apre una connessione SQL");

        var invalid = new List<DrosoDocument>();
        void Invalid(Action<DrosoDocument> change) { var document = Document(invalid.Count + 10); change(document); invalid.Add(document); }
        Invalid(document => document.AppliedStatus = "");
        Invalid(document => document.AppliedStatus = "02");
        Invalid(document => document.AppliedStatusId = null);
        Invalid(document => document.AppliedAt = null);
        Invalid(document => document.SelectedStatusId = null);
        Invalid(document => document.SubmittedAt = null);
        Invalid(document => document.AnalysisCompleted = false);
        Invalid(document => document.Coorte = "Locale");
        Invalid(document => document.IdAllegato = 0);
        Invalid(document => document.CodiceFiscale = "INVALID");
        Invalid(document => document.AppliedStatusId = (long)int.MaxValue + 1);
        Invalid(document => document.AppliedStatusId = document.SelectedStatusId);
        Invalid(document => document.AppliedAt = new DateTime(1700, 1, 1));
        Invalid(document => document.AppliedAt = document.SubmittedAt);
        var invalidSession = Session("messages-invalid", invalid.ToArray());
        int calls = 0;
        await DrosoMessageRepository.SendOutcomesAsync(invalidSession, () =>
        {
            calls++;
            throw new InvalidOperationException("Nessuna transazione per allegati non eleggibili.");
        }, progress, CancellationToken.None);
        check(calls == 0 && invalid.All(document => document.MessageSentAt is null && document.MessageUpdateNote.Length > 0),
            "Messaggi: proposte, 02, analisi locali/incomplete e versioni mancanti non inviano");

        var missingServer = Session("messages-missing-origin", Document(30)); missingServer.SourceServer = "";
        var missingModel = Session("messages-missing-model", Document(31)); missingModel.CheckpointSha256 = "";
        foreach (var session in new[] { missingServer, missingModel })
            await DrosoMessageRepository.SendOutcomesAsync(session, () => throw new Exception("Origine incompleta"), progress, CancellationToken.None);
        check(missingServer.Documents[0].MessageSentAt is null && missingModel.Documents[0].MessageSentAt is null,
            "Messaggi: origine o checkpoint mancanti escludono le vecchie sessioni");

        var first = Document(40);
        first.Predicted = "PASSAPORTO"; first.Confidence = .99; // Current prediction proposes 02; saved SQL refusal remains authoritative.
        var second = Document(41);
        var duplicate = Document(40);
        var other = Document(42, "OTHER00000000001");
        var accepted = Document(43, "THIRD00000000001"); accepted.AppliedStatus = "02";
        var changed = Document(44, "FOURTH0000000001");
        var batch = Session("messages-deduplicated", first, second, duplicate, other, accepted, changed);
        var ledger = new Ledger(first, second, other, accepted, changed);
        ledger.Rows[44].Status = "05";
        await DrosoMessageRepository.SendOutcomesAsync(batch, ledger.Begin, progress, CancellationToken.None);
        check(ledger.Messages.Count == 2 && ledger.Messages.All(message => message.Text == DrosoMessageRepository.MessageText),
            "Messaggi: uno per CF, testo costante, nessun invio per 02 o stato cambiato");
        check(ledger.Reads == 4 && ledger.Rows[40].Sent == "1" && ledger.Rows[41].Sent == "1" && ledger.Rows[42].Sent == "1"
            && ledger.Rows[43].Sent is null && ledger.Rows[44].Sent is null,
            "Messaggi: allegati duplicati non raddoppiano i flag; si marcano soltanto i rifiuti notificati");
        check(first.MessageSentAt == Ledger.ServerTime && second.MessageSentAt == Ledger.ServerTime && duplicate.MessageSentAt == Ledger.ServerTime
            && accepted.MessageSentAt is null && changed.MessageSentAt is null && first.ProposedStatus == "02" && first.AppliedStatus == "04",
            "Messaggi: audit dopo commit, rifiuto salvato prevale sulla proposta corrente");
        var reloaded = DrosoSession.Load(Path.Combine(batch.Directory, "session.json"));
        await DrosoMessageRepository.SendOutcomesAsync(reloaded, ledger.Begin, progress, CancellationToken.None);
        check(ledger.Messages.Count == 2 && reloaded.Documents[0].MessageSentAt == Ledger.ServerTime
            && reloaded.Documents[0].MessageUpdateNote.Contains("già inviato"),
            "Messaggi: sessione riletta e flag SQL impediscono il reinvio, mantenendo l'audit");

        // A case-sensitive database can reject the first alias in an otherwise
        // case-insensitive local CF group. The recipient must come from SQL's accepted subset.
        var excludedAlias = Document(45, "synthetic0000000");
        var matchedAlias = Document(46);
        var aliasSession = Session("messages-recipient-alias", excludedAlias, matchedAlias);
        var aliasLedger = new Ledger(excludedAlias, matchedAlias);
        aliasLedger.Rows[45].Cf = "SYNTHETIC0000000";
        await DrosoMessageRepository.SendOutcomesAsync(aliasSession, aliasLedger.Begin, progress, CancellationToken.None);
        check(aliasLedger.Messages.Count == 1 && aliasLedger.Messages[0].Cf == matchedAlias.CodiceFiscale
            && aliasLedger.Rows[45].Sent is null && aliasLedger.Rows[46].Sent == "1"
            && excludedAlias.MessageSentAt is null && matchedAlias.MessageSentAt == Ledger.ServerTime,
            "Messaggi: il destinatario proviene da un allegato confermato SQL, non da un alias escluso");

        var staleDocs = Enumerable.Range(50, 6).Select(index => Document(index)).ToArray();
        var stale = Session("messages-stale", staleDocs);
        var staleLedger = new Ledger(staleDocs);
        staleLedger.Rows[50].Active = false;
        staleLedger.Rows[51].Cf = "OTHER00000000001";
        staleLedger.Rows[52].Status = "02";
        staleLedger.Rows[53].StatusId++;
        staleLedger.Rows[54].Date = staleLedger.Rows[54].Date.AddSeconds(1);
        staleLedger.Rows[55].Sent = "2";
        var identities = staleDocs.Select(document => (document.SelectedStatusId, document.AppliedStatusId, document.AppliedAt)).ToArray();
        await DrosoMessageRepository.SendOutcomesAsync(stale, staleLedger.Begin, progress, CancellationToken.None);
        check(staleLedger.Messages.Count == 0 && staleDocs.All(document => document.MessageSentAt is null && document.MessageUpdateNote.Contains("cambiato"))
            && staleDocs.Select(document => (document.SelectedStatusId, document.AppliedStatusId, document.AppliedAt)).SequenceEqual(identities),
            "Messaggi: allegato cessato, CF/stato/id/data/flag cambiati sono esclusi senza alterare l'identità locale");

        var already = Session("messages-already-sent", Document(60));
        var alreadyLedger = new Ledger(already.Documents.ToArray()); alreadyLedger.Rows[60].Sent = "1";
        await DrosoMessageRepository.SendOutcomesAsync(already, alreadyLedger.Begin, progress, CancellationToken.None);
        check(alreadyLedger.Messages.Count == 0 && already.Documents[0].MessageSentAt is null && already.Documents[0].MessageUpdateNote.Contains("già inviato"),
            "Messaggi: flag SQL già presente blocca l'invio anche senza un audit locale precedente");

        var failure = Session("messages-rollback", Document(70), Document(71));
        var failureLedger = new Ledger(failure.Documents.ToArray()) { FailMarks = true };
        try
        {
            await DrosoMessageRepository.SendOutcomesAsync(failure, failureLedger.Begin, progress, CancellationToken.None);
            check(false, "Errore della marcatura atteso");
        }
        catch (IOException)
        {
            var saved = DrosoSession.Load(Path.Combine(failure.Directory, "session.json"));
            check(failureLedger.Messages.Count == 0 && failureLedger.Rows.Values.All(status => status.Sent is null)
                && failureLedger.Rollbacks == 1 && saved.Documents.All(document => document.MessageSentAt is null && document.MessageUpdateNote.Contains("non confermato")),
                "Messaggi: errore tra inserimento e flag annulla entrambi; audit non dichiara successo");
        }

        using (var cancellation = new CancellationTokenSource())
        {
            var cancelled = Session("messages-cancel-before-commit", Document(80));
            var cancelledLedger = new Ledger(cancelled.Documents.ToArray()) { AfterInsert = cancellation.Cancel };
            try
            {
                await DrosoMessageRepository.SendOutcomesAsync(cancelled, cancelledLedger.Begin, progress, cancellation.Token);
                check(false, "Annullamento dell'invio atteso");
            }
            catch (OperationCanceledException)
            {
                check(cancelledLedger.Messages.Count == 0 && cancelledLedger.Rows[80].Sent is null && cancelledLedger.Rollbacks == 1
                    && DrosoSession.Load(Path.Combine(cancelled.Directory, "session.json")).Documents[0].MessageSentAt is null,
                    "Messaggi: annullamento dopo inserimento ma prima del commit non invia né marca");
            }
        }

        using (var cancellation = new CancellationTokenSource())
        {
            var partial = Session("messages-partial", Document(90), Document(91, "OTHER00000000001"));
            var partialLedger = new Ledger(partial.Documents.ToArray()) { AfterCommit = cancellation.Cancel };
            try
            {
                await DrosoMessageRepository.SendOutcomesAsync(partial, partialLedger.Begin, progress, cancellation.Token);
                check(false, "Annullamento tra studenti atteso");
            }
            catch (OperationCanceledException)
            {
                var saved = DrosoSession.Load(Path.Combine(partial.Directory, "session.json"));
                check(partialLedger.Messages.Count == 1 && saved.Documents[0].MessageSentAt == Ledger.ServerTime
                    && saved.Documents[1].MessageSentAt is null && partialLedger.Rows[90].Sent == "1" && partialLedger.Rows[91].Sent is null,
                    "Messaggi: commit precedente conservato, studente successivo non tentato dopo annullamento");
                partialLedger.AfterCommit = null;
                await DrosoMessageRepository.SendOutcomesAsync(saved, partialLedger.Begin, progress, CancellationToken.None);
                check(partialLedger.Messages.Count == 2 && partialLedger.Messages.Select(message => message.Cf).Distinct().Count() == 2,
                    "Messaggi: retry della lavorazione parziale notifica solo lo studente rimasto");
            }
        }

        var lost = Session("messages-lost-acknowledgement", Document(100));
        var lostLedger = new Ledger(lost.Documents.ToArray()) { LoseAcknowledgement = true };
        try
        {
            await DrosoMessageRepository.SendOutcomesAsync(lost, lostLedger.Begin, progress, CancellationToken.None);
            check(false, "Conferma del commit persa attesa");
        }
        catch (IOException)
        {
            var saved = DrosoSession.Load(Path.Combine(lost.Directory, "session.json"));
            check(lostLedger.Messages.Count == 1 && lostLedger.Rows[100].Sent == "1" && lostLedger.Rollbacks == 0
                && saved.Documents[0].MessageSentAt is null && saved.Documents[0].MessageUpdateNote.Contains("non confermato"),
                "Messaggi: conferma persa dopo commit non inventa rollback o audit positivo");
            await DrosoMessageRepository.SendOutcomesAsync(saved, lostLedger.Begin, progress, CancellationToken.None);
            check(lostLedger.Messages.Count == 1 && saved.Documents[0].MessageUpdateNote.Contains("già inviato"),
                "Messaggi: SQL flag impedisce il reinvio dopo conferma persa e ricaricamento");
        }

        var wrongOrigin = Session("messages-wrong-origin", Document(110));
        using (var connection = new SqlConnection("Data Source=OtherServer;Initial Catalog=OtherDatabase;Integrated Security=True"))
            await DrosoMessageRepository.SendRejectedAsync(connection, wrongOrigin, progress, CancellationToken.None);
        check(wrongOrigin.Documents[0].MessageSentAt is null && wrongOrigin.Documents[0].MessageUpdateNote.Contains("diversi"),
            "Messaggi: origine diversa viene rifiutata senza aprire la connessione");

        using (var connection = new SqlConnection())
        using (var guard = DrosoMessageRepository.CreateGuardCommand(connection, null!, first))
        using (var mark = DrosoMessageRepository.CreateMarkCommand(connection, null!, first))
        {
            check(guard.Parameters["@id"].SqlDbType == SqlDbType.Decimal && guard.Parameters["@id"].Precision == 18
                && guard.Parameters["@cf"].SqlDbType == SqlDbType.Char && guard.Parameters["@cf"].Size == 16
                && guard.Parameters["@expectedId"].SqlDbType == SqlDbType.Int
                && (int)guard.Parameters["@expectedId"].Value == first.AppliedStatusId
                && guard.Parameters["@expectedDate"].SqlDbType == SqlDbType.DateTime,
                "Messaggi: guardia usa parametri tipizzati e versione del rifiuto applicato, non della selezione 01");
            check(guard.CommandText.Contains("UPDLOCK,HOLDLOCK") && !guard.CommandText.Contains("SET NOCOUNT ON")
                && guard.CommandText.Contains("ORDER BY data_validita DESC,Id_STATUS_ALLEGATI DESC")
                && mark.CommandText.Contains("SET Messaggio_inviato='1'") && mark.CommandText.Contains("NOT EXISTS")
                && mark.CommandText.Contains("SELECT @@ROWCOUNT;")
                && !mark.CommandText.Contains("SET cod_status") && !mark.CommandText.Contains("Motivazione_Esclusione"),
                "Messaggi: SQL protegge l'ultimo stato attivo e aggiorna soltanto il flag del rifiuto");
        }
    }

    private static DrosoDocument Document(long id, string cf = "SYNTHETIC0000000") => new()
    {
        IdAllegato = id, CodiceFiscale = cf, Coorte = "NEW", SelectedStatusId = id + 100,
        SubmittedAt = new DateTime(2026, 10, 6, 12, 0, 0), AnalysisCompleted = true,
        Predicted = "ERRATO", Confidence = .80, AppliedStatus = "04", AppliedStatusId = id + 1000,
        AppliedAt = new DateTime(2026, 10, 6, 12, 0, 1)
    };

    private sealed class State
    {
        public string Cf = "";
        public int StatusId;
        public DateTime Date;
        public string Status = "04";
        public bool Active = true;
        public string? Sent;
    }

    private sealed class Ledger
    {
        public static readonly DateTime ServerTime = new(2026, 10, 6, 12, 0, 2);
        public readonly Dictionary<long, State> Rows = new();
        public readonly List<(string Cf, string Text)> Messages = new();
        public int Reads, Rollbacks;
        public bool FailMarks, LoseAcknowledgement;
        public Action? AfterInsert, AfterCommit;
        public Ledger(params DrosoDocument[] documents)
        {
            foreach (var document in documents)
                Rows[document.IdAllegato] = new State { Cf = document.CodiceFiscale.Trim(), StatusId = (int)document.AppliedStatusId!.Value,
                    Date = document.AppliedAt!.Value, Status = document.AppliedStatus };
        }
        public IDrosoMessageTransaction Begin() => new Transaction(this);
        private sealed class Transaction : IDrosoMessageTransaction
        {
            private readonly Ledger ledger;
            private readonly List<(string Cf, string Text)> pendingMessages = new();
            private readonly HashSet<long> pendingFlags = new();
            private bool committed;
            public Transaction(Ledger ledger) => this.ledger = ledger;
            public Task<DrosoMessageGuard> ReadAsync(DrosoDocument document, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested(); ledger.Reads++;
                var state = ledger.Rows[document.IdAllegato];
                bool matches = state.Active && state.Cf == document.CodiceFiscale.Trim() && state.Status == "04"
                    && state.StatusId == document.AppliedStatusId && state.Date == document.AppliedAt && state.Sent is null or "0" or "1";
                return Task.FromResult(new DrosoMessageGuard(matches, matches && state.Sent == "1"));
            }
            public void InsertMessage(string codiceFiscale, string text)
            {
                pendingMessages.Add((codiceFiscale, text)); ledger.AfterInsert?.Invoke();
            }
            public Task<DateTime> MarkSentAsync(IReadOnlyList<DrosoDocument> documents, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (ledger.FailMarks) throw new IOException("Synthetic marker failure");
                foreach (var document in documents)
                {
                    if (ledger.Rows[document.IdAllegato].Sent == "1") throw new IOException("Synthetic concurrent mark");
                    pendingFlags.Add(document.IdAllegato);
                }
                return Task.FromResult(ServerTime);
            }
            public void Commit()
            {
                ledger.Messages.AddRange(pendingMessages);
                foreach (long id in pendingFlags) ledger.Rows[id].Sent = "1";
                committed = true;
                ledger.AfterCommit?.Invoke();
                if (ledger.LoseAcknowledgement)
                {
                    ledger.LoseAcknowledgement = false;
                    throw new IOException("Synthetic lost commit acknowledgement");
                }
            }
            public void Dispose() { if (!committed) ledger.Rollbacks++; }
        }
    }
}
