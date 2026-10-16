using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;

namespace ProcedureNet7
{
    internal sealed record DrosoStatusResult(bool Applied, DateTime? StatusDate, int? CurrentStatusId, string CurrentStatus);

    internal static class DrosoStatusRepository
    {
        public static async Task ApplyAsync(SqlConnection connection, DrosoSession session,
            IProgress<string> progress, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            // Local and older sessions need no database connection, even when they
            // contain a completed classification or a manually entered identifier.
            bool hasPending = session.Documents.Any(document => string.IsNullOrEmpty(document.AppliedStatus)
                && UnavailableReason(session, document) is null);
            if (!hasPending)
            {
                await ApplyOutcomesAsync(session, (_, _) => throw new InvalidOperationException("Nessun allegato aggiornabile."),
                    progress, cancellation);
                return;
            }
            if (string.IsNullOrWhiteSpace(session.SourceServer)
                || !string.Equals(connection.DataSource, session.SourceServer, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(connection.Database, session.SourceDatabase, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var document in session.Documents.Where(document => string.IsNullOrEmpty(document.AppliedStatus)))
                    document.StatusUpdateNote = "Server o database diversi da quelli di estrazione, oppure origine incompleta: stato non aggiornato.";
                session.Save();
                progress.Report("Server o database diversi da quelli di estrazione, oppure origine incompleta: nessuno stato aggiornato.");
                return;
            }
            if (connection.State != ConnectionState.Open)
                throw new InvalidOperationException("Collegare prima ProcedureNet7 al database di origine della sessione.");

            // Clone the already authenticated connection once, without reading or
            // persisting its credentials. Classification never runs in a transaction.
            using var worker = (SqlConnection)((ICloneable)connection).Clone();
            await worker.OpenAsync(cancellation);
            if (!string.Equals(worker.DataSource, session.SourceServer, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(worker.Database, session.SourceDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La connessione non corrisponde al server e database della sessione.");
            await ApplyOutcomesAsync(session, async (document, token) =>
            {
                using var transaction = worker.BeginTransaction(IsolationLevel.Serializable);
                using var command = CreateInsertCommand(worker, transaction, document);
                DrosoStatusResult result;
                using (var reader = await command.ExecuteReaderAsync(token))
                {
                    if (!await reader.ReadAsync(token))
                        throw new InvalidDataException("Esito del salvataggio dello stato assente.");
                    result = new(reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                        reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? "" : reader.GetString(3));
                }
                token.ThrowIfCancellationRequested();
                transaction.Commit();
                return result;
            }, progress, cancellation);
        }

        internal static async Task ApplyOutcomesAsync(DrosoSession session,
            Func<DrosoDocument, CancellationToken, Task<DrosoStatusResult>> write,
            IProgress<string> progress, CancellationToken cancellation)
        {
            int applied = 0, skipped = 0;
            try
            {
                foreach (var document in session.Documents)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(document.AppliedStatus))
                    {
                        skipped++;
                        continue;
                    }
                    string? reason = UnavailableReason(session, document);
                    if (reason is not null)
                    {
                        document.StatusUpdateNote = reason;
                        skipped++;
                        continue;
                    }
                    try
                    {
                        DrosoStatusResult result = await write(document, cancellation);
                        if (result.Applied)
                        {
                            if (result.StatusDate is null || result.CurrentStatus != document.ProposedStatus)
                                throw new InvalidDataException("Conferma SQL dello stato non coerente con l'esito richiesto.");
                            document.AppliedStatus = result.CurrentStatus;
                            document.AppliedStatusId = result.CurrentStatusId;
                            document.AppliedAt = result.StatusDate;
                            document.StatusUpdateNote = result.CurrentStatus == "04"
                                ? "Stato 04 salvato; " + document.StatusReason + ". Motivazione registrata soltanto nella sessione."
                                : "Stato 02 salvato: in lavorazione.";
                            applied++;
                        }
                        else
                        {
                            document.StatusUpdateNote = "Allegato o stato cambiato dopo l'estrazione: aggiornamento non applicato.";
                            skipped++;
                        }
                    }
                    catch
                    {
                        // A lost acknowledgement can occur after commit: do not
                        // claim rollback or reapply without the SQL version guard.
                        document.StatusUpdateNote = "Salvataggio SQL non confermato: verificare lo stato corrente prima di riprovare.";
                        throw;
                    }
                    progress.Report($"Stati: {applied} salvati, {skipped} non applicati, {applied + skipped}/{session.Documents.Count} esaminati.");
                }
                progress.Report($"Aggiornamento stati terminato: {applied} salvati, {skipped} non applicati.");
            }
            finally
            {
                // Preserve completed rows on cancellation/error together with the
                // original classification, confidence and checkpoint identity.
                session.Save();
            }
        }

        private static string? UnavailableReason(DrosoSession session, DrosoDocument document)
        {
            if (string.IsNullOrWhiteSpace(session.SourceDatabase))
                return "Sessione locale o senza origine database: stato non aggiornato.";
            if (string.IsNullOrWhiteSpace(session.CheckpointSha256) && !document.PdfUnreadable)
                return "Modello dell'analisi non identificato: stato non aggiornato.";
            return DocumentUnavailableReason(document);
        }

        private static string? DocumentUnavailableReason(DrosoDocument document)
        {
            if (!document.AnalysisCompleted || document.ProposedStatus is not ("02" or "04"))
                return "Analisi non completata: stato non aggiornato.";
            if (document.IdAllegato <= 0 || document.IdAllegato > 999999999999999999
                || string.IsNullOrWhiteSpace(document.CodiceFiscale) || document.CodiceFiscale.Trim().Length != 16)
                return "Identità database dell'allegato assente o non valida: stato non aggiornato.";
            if (document.SelectedStatusId is not > 0 or > int.MaxValue || document.SubmittedAt is not DateTime selected
                || selected < SqlDateTime.MinValue.Value || selected > SqlDateTime.MaxValue.Value)
                return "Versione dello stato estratto assente o non valida: eseguire una nuova estrazione.";
            return null;
        }

        internal static SqlCommand CreateInsertCommand(SqlConnection connection, SqlTransaction transaction, DrosoDocument document)
        {
            string? reason = DocumentUnavailableReason(document);
            if (reason is not null) throw new ArgumentException(reason, nameof(document));
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 30;
            command.CommandText = @"
SET NOCOUNT ON;
DECLARE @currentId int, @currentStatus varchar(2), @currentDate datetime;
DECLARE @applied bit=0, @statusDate datetime=NULL, @attachmentMatches bit=0;
SELECT @attachmentMatches=1
FROM dbo.ALLEGATI WITH (UPDLOCK,HOLDLOCK)
WHERE id_allegato=@id AND cod_fiscale=@cf AND data_fine_validita IS NULL;
SELECT TOP (1) @currentId=Id_STATUS_ALLEGATI, @currentStatus=cod_status, @currentDate=data_validita
FROM dbo.STATUS_ALLEGATI WITH (UPDLOCK,HOLDLOCK)
WHERE id_allegato=@id AND data_fine_validita IS NULL
ORDER BY data_validita DESC,Id_STATUS_ALLEGATI DESC;
IF @attachmentMatches=1 AND @currentStatus='01'
    AND @currentId=@expectedId AND @currentDate=@expectedDate
BEGIN
    SET @statusDate=GETDATE();
    IF @statusDate<=@currentDate SET @statusDate=DATEADD(millisecond,4,@currentDate);
    INSERT INTO dbo.STATUS_ALLEGATI
        (id_allegato,cod_status,data_validita,utente,data_fine_validita,Motivazione_Esclusione,Messaggio_inviato)
    VALUES (@id,@target,@statusDate,SUSER_SNAME(),NULL,NULL,NULL);
    SET @applied=1;
    SET @currentId=CONVERT(int,SCOPE_IDENTITY());
    SET @currentStatus=@target;
END;
SELECT @applied AS Applied,@statusDate AS StatusDate,@currentId AS CurrentStatusId,@currentStatus AS CurrentStatus;";
            var id = command.Parameters.Add("@id", SqlDbType.Decimal);
            id.Precision = 18;
            id.Scale = 0;
            id.Value = (decimal)document.IdAllegato;
            command.Parameters.Add("@cf", SqlDbType.Char, 16).Value = document.CodiceFiscale.Trim();
            command.Parameters.Add("@expectedId", SqlDbType.Int).Value = checked((int)document.SelectedStatusId!.Value);
            command.Parameters.Add("@expectedDate", SqlDbType.DateTime).Value = document.SubmittedAt!.Value;
            command.Parameters.Add("@target", SqlDbType.VarChar, 2).Value = document.ProposedStatus;
            return command;
        }
    }
}
