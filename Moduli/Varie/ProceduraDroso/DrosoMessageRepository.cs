using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;

namespace ProcedureNet7
{
    internal readonly record struct DrosoMessageGuard(bool Matches, bool AlreadySent);

    internal interface IDrosoMessageTransaction : IDisposable
    {
        Task<DrosoMessageGuard> ReadAsync(DrosoDocument document, CancellationToken cancellation);
        void InsertMessage(string codiceFiscale, string text);
        Task<DateTime> MarkSentAsync(IReadOnlyList<DrosoDocument> documents, CancellationToken cancellation);
        void Commit();
    }

    internal static class DrosoMessageRepository
    {
        public const string MessageText = "#DA SCRIVERE#";

        public static async Task SendRejectedAsync(SqlConnection connection, DrosoSession session,
            IProgress<string> progress, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!session.Documents.Any(document => UnavailableReason(session, document) is null))
            {
                await SendOutcomesAsync(session, () => throw new InvalidOperationException("Nessun rifiuto notificabile."),
                    progress, cancellation);
                return;
            }
            if (!string.Equals(connection.DataSource, session.SourceServer, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(connection.Database, session.SourceDatabase, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var document in session.Documents.Where(document => UnavailableReason(session, document) is null))
                    document.MessageUpdateNote = "Server o database diversi da quelli di estrazione: messaggio non inviato.";
                session.Save();
                progress.Report("Server o database diversi da quelli di estrazione: nessun messaggio inviato.");
                return;
            }
            if (connection.State != ConnectionState.Open)
                throw new InvalidOperationException("Collegare prima ProcedureNet7 al database di origine della sessione.");

            // MessageUtils is synchronous. Its connection and transactions belong
            // entirely to this worker; the UI's authenticated connection is cloned.
            await Task.Run(async () =>
            {
                bool started = false;
                try
                {
                    using var worker = (SqlConnection)((ICloneable)connection).Clone();
                    await worker.OpenAsync(cancellation);
                    if (!string.Equals(worker.DataSource, session.SourceServer, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(worker.Database, session.SourceDatabase, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("La connessione non corrisponde all'origine della sessione.");
                    started = true;
                    await SendOutcomesAsync(session, () => new SqlMessageTransaction(worker), progress, cancellation);
                }
                catch when (!started)
                {
                    foreach (var document in session.Documents.Where(document => UnavailableReason(session, document) is null))
                        document.MessageUpdateNote = "Invio non avviato: connessione di origine non disponibile o operazione annullata.";
                    session.Save();
                    throw;
                }
            });
        }

        internal static async Task SendOutcomesAsync(DrosoSession session, Func<IDrosoMessageTransaction> begin,
            IProgress<string> progress, CancellationToken cancellation)
        {
            int sent = 0, alreadySent = 0, skipped = 0;
            try
            {
                var eligible = new List<DrosoDocument>();
                foreach (var document in session.Documents)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string? reason = UnavailableReason(session, document);
                    if (reason is null) eligible.Add(document);
                    else { document.MessageUpdateNote = reason; skipped++; }
                }
                foreach (var group in eligible.GroupBy(document => document.CodiceFiscale.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var documents = group.ToArray();
                    try
                    {
                        using var transaction = begin();
                        var guards = new Dictionary<(long Id, long? Status, DateTime? Date), DrosoMessageGuard>();
                        var pending = new List<DrosoDocument>();
                        foreach (var document in documents.DistinctBy(Identity))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var guard = await transaction.ReadAsync(document, cancellation);
                            guards.Add(Identity(document), guard);
                            if (guard.Matches && !guard.AlreadySent) pending.Add(document);
                        }
                        DateTime? sentAt = null;
                        if (pending.Count > 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            transaction.InsertMessage(pending[0].CodiceFiscale.Trim(), MessageText);
                            cancellation.ThrowIfCancellationRequested();
                            sentAt = await transaction.MarkSentAsync(pending, cancellation);
                        }
                        cancellation.ThrowIfCancellationRequested();
                        transaction.Commit();
                        // Acknowledged commits remain audited even if cancellation
                        // arrives immediately afterwards, before the next student.
                        foreach (var document in documents)
                        {
                            var guard = guards[Identity(document)];
                            if (!guard.Matches)
                            {
                                document.MessageUpdateNote = "Allegato, stato o flag cambiato dopo il rifiuto: messaggio non inviato.";
                                skipped++;
                            }
                            else if (guard.AlreadySent)
                            {
                                document.MessageUpdateNote = "Messaggio già inviato per questo rifiuto: nessun reinvio.";
                                alreadySent++;
                            }
                            else
                            {
                                document.MessageSentAt = sentAt;
                                document.MessageUpdateNote = "Messaggio inviato e flag SQL registrato per il rifiuto 04.";
                            }
                        }
                        if (pending.Count > 0) sent++;
                    }
                    catch
                    {
                        // Commit acknowledgement can be lost after SQL succeeds.
                        // Retrying rechecks the SQL flag instead of assuming rollback.
                        foreach (var document in documents)
                            document.MessageUpdateNote = "Invio SQL non confermato: verificare l'esito; il flag SQL protegge dai reinvii.";
                        throw;
                    }
                    progress.Report($"Messaggi: {sent} studenti notificati, {alreadySent} allegati già notificati, {skipped} non notificabili.");
                }
                progress.Report($"Invio messaggi terminato: {sent} studenti notificati, {alreadySent} allegati già notificati, {skipped} non notificabili.");
            }
            finally { session.Save(); }
        }

        private static (long Id, long? Status, DateTime? Date) Identity(DrosoDocument document) =>
            (document.IdAllegato, document.AppliedStatusId, document.AppliedAt);

        internal static string? UnavailableReason(DrosoSession session, DrosoDocument document)
        {
            if (session.Version != 1 || string.IsNullOrWhiteSpace(session.SourceServer)
                || string.IsNullOrWhiteSpace(session.SourceDatabase)
                || string.IsNullOrWhiteSpace(session.CheckpointSha256) && !document.PdfUnreadable)
                return "Sessione locale o senza provenienza completa dell'analisi: messaggio non inviato.";
            if (document.Coorte is not ("OLD" or "NEW" or "OLD/NEW") || !document.AnalysisCompleted)
                return "Allegato non estratto dal database o analisi non completata: messaggio non inviato.";
            if (document.AppliedStatus != "04")
                return "Nessun rifiuto 04 salvato per questo allegato: messaggio non inviato.";
            if (document.IdAllegato <= 0 || document.IdAllegato > 999999999999999999
                || string.IsNullOrWhiteSpace(document.CodiceFiscale) || document.CodiceFiscale.Trim().Length != 16)
                return "Identità dell'allegato non valida: messaggio non inviato.";
            if (document.SelectedStatusId is not > 0 or > int.MaxValue || !SqlDate(document.SubmittedAt)
                || document.AppliedStatusId is not > 0 or > int.MaxValue || !SqlDate(document.AppliedAt)
                || document.AppliedStatusId == document.SelectedStatusId || document.AppliedAt <= document.SubmittedAt)
                return "Versione del rifiuto salvato assente o non valida: messaggio non inviato.";
            return null;
        }

        private static bool SqlDate(DateTime? value) => value is DateTime date
            && date >= SqlDateTime.MinValue.Value && date <= SqlDateTime.MaxValue.Value;

        internal static SqlCommand CreateGuardCommand(SqlConnection connection, SqlTransaction transaction, DrosoDocument document)
        {
            var command = CreateCommand(connection, transaction, document);
            command.CommandText = @"
DECLARE @attachment bit=0,@currentId int,@currentDate datetime,@status varchar(2),@sent char(1);
SELECT @attachment=1 FROM dbo.ALLEGATI WITH (UPDLOCK,HOLDLOCK)
WHERE id_allegato=@id AND cod_fiscale=@cf AND data_fine_validita IS NULL;
SELECT TOP (1) @currentId=Id_STATUS_ALLEGATI,@currentDate=data_validita,@status=cod_status,@sent=Messaggio_inviato
FROM dbo.STATUS_ALLEGATI WITH (UPDLOCK,HOLDLOCK)
WHERE id_allegato=@id AND data_fine_validita IS NULL
ORDER BY data_validita DESC,Id_STATUS_ALLEGATI DESC;
DECLARE @matches bit=CASE WHEN @attachment=1 AND @currentId=@expectedId AND @currentDate=@expectedDate
    AND @status='04' AND (@sent IS NULL OR @sent IN ('0','1')) THEN 1 ELSE 0 END;
SELECT @matches,CONVERT(bit,CASE WHEN @matches=1 AND @sent='1' THEN 1 ELSE 0 END);";
            return command;
        }

        internal static SqlCommand CreateMarkCommand(SqlConnection connection, SqlTransaction transaction, DrosoDocument document)
        {
            var command = CreateCommand(connection, transaction, document);
            command.CommandText = @"
UPDATE s SET Messaggio_inviato='1'
FROM dbo.STATUS_ALLEGATI s WITH (UPDLOCK,HOLDLOCK)
WHERE s.id_allegato=@id AND s.Id_STATUS_ALLEGATI=@expectedId AND s.cod_status='04'
    AND s.data_validita=@expectedDate AND s.data_fine_validita IS NULL
    AND (s.Messaggio_inviato IS NULL OR s.Messaggio_inviato='0')
    AND EXISTS (SELECT 1 FROM dbo.ALLEGATI a WITH (UPDLOCK,HOLDLOCK)
        WHERE a.id_allegato=s.id_allegato AND a.cod_fiscale=@cf AND a.data_fine_validita IS NULL)
    AND NOT EXISTS (SELECT 1 FROM dbo.STATUS_ALLEGATI newer WITH (UPDLOCK,HOLDLOCK)
        WHERE newer.id_allegato=s.id_allegato AND newer.data_fine_validita IS NULL
            AND (newer.data_validita>s.data_validita
                OR newer.data_validita=s.data_validita AND newer.Id_STATUS_ALLEGATI>s.Id_STATUS_ALLEGATI));
SELECT @@ROWCOUNT;";
            return command;
        }

        private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, DrosoDocument document)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 30;
            var id = command.Parameters.Add("@id", SqlDbType.Decimal);
            id.Precision = 18; id.Scale = 0; id.Value = (decimal)document.IdAllegato;
            command.Parameters.Add("@cf", SqlDbType.Char, 16).Value = document.CodiceFiscale.Trim();
            command.Parameters.Add("@expectedId", SqlDbType.Int).Value = checked((int)document.AppliedStatusId!.Value);
            command.Parameters.Add("@expectedDate", SqlDbType.DateTime).Value = document.AppliedAt!.Value;
            return command;
        }

        private sealed class SqlMessageTransaction : IDrosoMessageTransaction
        {
            private readonly SqlConnection connection;
            private readonly SqlTransaction transaction;
            public SqlMessageTransaction(SqlConnection connection)
            {
                this.connection = connection;
                transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            }
            public async Task<DrosoMessageGuard> ReadAsync(DrosoDocument document, CancellationToken cancellation)
            {
                using var command = CreateGuardCommand(connection, transaction, document);
                using var reader = await command.ExecuteReaderAsync(cancellation);
                if (!await reader.ReadAsync(cancellation)) throw new InvalidDataException("Conferma dello stato da notificare assente.");
                return new(reader.GetBoolean(0), reader.GetBoolean(1));
            }
            public void InsertMessage(string codiceFiscale, string text) =>
                MessageUtils.InsertMessages(connection, transaction, new Dictionary<string, string> { [codiceFiscale] = text });
            public async Task<DateTime> MarkSentAsync(IReadOnlyList<DrosoDocument> documents, CancellationToken cancellation)
            {
                foreach (var document in documents)
                {
                    using var command = CreateMarkCommand(connection, transaction, document);
                    // Guard reads and MessageUtils can leave NOCOUNT enabled.
                    // An explicit scalar count is independent of TDS row counts.
                    if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellation)) != 1)
                        throw new InvalidDataException("Stato cambiato durante l'invio: messaggio e flag non confermati.");
                }
                using var clock = new SqlCommand("SELECT GETDATE();", connection, transaction);
                return (DateTime)(await clock.ExecuteScalarAsync(cancellation)
                    ?? throw new InvalidDataException("Data SQL di conferma assente."));
            }
            public void Commit() => transaction.Commit();
            public void Dispose() => transaction.Dispose();
        }
    }
}
