using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ProcedureNet7
{
    internal static class DrosoDocumentRepository
    {
        private const long MaxBytes = 30 * 1024 * 1024;
        internal const int DownloadWorkers = 4;

        public static async Task ExtractAsync(SqlConnection connection, string filesDatabase, int maximum,
            DrosoSession session, IProgress<string> progress, CancellationToken cancellation,
            DateTime? startDate = null, DateTime? endDate = null)
        {
            using var selectionCommand = CreateSelectionCommand(connection, filesDatabase, maximum, startDate, endDate);
            if (connection.State != ConnectionState.Open) throw new InvalidOperationException("Collegare prima ProcedureNet7 al database.");
            string archive = ArchiveTableName(filesDatabase);
            session.SourceDatabase = connection.Database;
            session.SourceServer = connection.DataSource;
            session.FilesDatabase = filesDatabase;
            var selectionTime = Stopwatch.StartNew();
            progress.Report("Selezione degli allegati pendenti nelle coorti dell'estrazione mail…");
            using (var reader = await selectionCommand.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    var document = new DrosoDocument
                    {
                        IdAllegato = Convert.ToInt64(reader.GetValue(0)), CodiceFiscale = Convert.ToString(reader.GetValue(1))!.Trim(),
                        DeclaredLabel = DrosoSession.Category(Convert.ToString(reader.GetValue(2))!.Trim()),
                        Coorte = reader.GetString(3), SubmittedAt = reader.GetDateTime(4),
                        FileId = reader.IsDBNull(6) ? null : Convert.ToInt64(reader.GetValue(6)),
                        Bytes = reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                        SelectedStatusId = reader.GetInt32(9)
                    };
                    if (reader.GetInt64(5) != 1) document.Error = "Collegamento al file assente o non univoco.";
                    else if (reader.GetInt32(8) != 1 || document.DeclaredLabel.Length == 0) document.Error = "Studente o categoria non coerenti negli archivi.";
                    else if (document.Bytes < 1) document.RejectUnreadablePdf("PDF non leggibile: allegato vuoto.");
                    else if (document.Bytes > MaxBytes) document.Error = "File oltre 30 MB.";
                    session.Documents.Add(document);
                }
            }
            progress.Report($"Selezionati {session.Documents.Count} allegati in {selectionTime.Elapsed.TotalSeconds:F1} s.");
            await DownloadAsync(session, async (document, temporaryPath, token) =>
            {
                // Clone preserves authentication even when the opened connection hides its password.
                using var worker = (SqlConnection)((ICloneable)connection).Clone();
                await worker.OpenAsync(token);
                using var command = worker.CreateCommand();
                command.CommandTimeout = 90;
                command.CommandText = "SELECT [file] FROM " + archive + @"
WHERE id_allegato=@file AND Id_Allegato_Adisu=@id AND cod_fiscale=@cf
    AND DATALENGTH([file])=@size AND DATALENGTH([file]) BETWEEN 1 AND @max";
                command.Parameters.Add("@file", SqlDbType.Decimal).Value = document.FileId!.Value;
                command.Parameters.Add("@id", SqlDbType.Decimal).Value = document.IdAllegato;
                command.Parameters.Add("@cf", SqlDbType.VarChar, 16).Value = document.CodiceFiscale;
                command.Parameters.Add("@size", SqlDbType.BigInt).Value = document.Bytes;
                command.Parameters.Add("@max", SqlDbType.BigInt).Value = MaxBytes;
                using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow, token);
                if (!await reader.ReadAsync(token) || reader.IsDBNull(0))
                    throw new InvalidDataException("File non disponibile o modificato dopo la selezione.");
                await using var source = reader.GetStream(0);
                await using var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > document.Bytes || total > MaxBytes)
                        throw new InvalidDataException("File oltre la dimensione dichiarata dopo la selezione.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
            }, progress, cancellation);
        }

        private static string ArchiveTableName(string filesDatabase)
        {
            if (string.IsNullOrWhiteSpace(filesDatabase) || filesDatabase.Length > 128)
                throw new ArgumentException("Indicare il database degli allegati (massimo 128 caratteri).", nameof(filesDatabase));
            return "[" + filesDatabase.Replace("]", "]]") + "].DIRSTUDIO.ALLEGATI";
        }

        internal static SqlCommand CreateSelectionCommand(SqlConnection connection, string filesDatabase, int maximum,
            DateTime? startDate = null, DateTime? endDate = null)
        {
            string archive = ArchiveTableName(filesDatabase);
            if (maximum < 0)
                throw new ArgumentOutOfRangeException(nameof(maximum), "Il massimo deve essere zero (tutti) o un numero positivo.");
            DateTime? start = startDate?.Date, end = endDate?.Date;
            if (start < SqlDateTime.MinValue.Value)
                throw new ArgumentOutOfRangeException(nameof(startDate), "La data iniziale deve essere dal 01/01/1753 in poi.");
            if (end < SqlDateTime.MinValue.Value || end == DateTime.MaxValue.Date)
                throw new ArgumentOutOfRangeException(nameof(endDate), "La data finale deve essere tra il 01/01/1753 e il 30/12/9999.");
            if (start > end)
                throw new ArgumentException("La data iniziale non può essere successiva alla data finale.", nameof(endDate));
            DateTime? endExclusive = end?.AddDays(1);
            var command = connection.CreateCommand();
            command.CommandTimeout = 120;
            command.CommandText = @"
;WITH candidates AS (
    SELECT s.Id_allegato, s.Cod_fiscale, s.Tipo_documento, s.Tipo_permesso, 'OLD' coorte
    FROM Specifiche_permesso_soggiorno s
    WHERE EXISTS (SELECT 1 FROM Domanda d INNER JOIN Studente st ON st.Cod_fiscale=d.Cod_fiscale
        WHERE d.Cod_fiscale=s.Cod_fiscale AND d.Anno_accademico IN (20252026,20242025,20232024)
        AND d.Tipo_bando LIKE 'l%' AND EXISTS (SELECT 1 FROM vEsiti_concorsi e
            WHERE e.Num_domanda=d.Num_domanda AND e.Cod_beneficio='bs' AND e.Cod_tipo_esito<>0))
    UNION
    SELECT s.Id_allegato, s.Cod_fiscale, s.Tipo_documento, s.Tipo_permesso, 'NEW' coorte
    FROM vSpecifiche_permesso_soggiorno s
    WHERE EXISTS (SELECT 1 FROM Domanda d INNER JOIN Studente st ON st.Cod_fiscale=d.Cod_fiscale
        WHERE d.Cod_fiscale=s.Cod_fiscale AND d.Anno_accademico=20262027 AND d.Tipo_bando LIKE 'l%'
        AND EXISTS (SELECT 1 FROM vStatus_compilazione sc WHERE sc.anno_accademico=d.Anno_accademico
            AND sc.num_domanda=d.Num_domanda AND sc.status_compilazione>=90))
), ranked AS (
    SELECT c.*, ROW_NUMBER() OVER (PARTITION BY coorte,c.Cod_fiscale,Tipo_documento,Tipo_permesso
        ORDER BY c.Id_allegato DESC) rn
    FROM candidates c INNER JOIN ALLEGATI a ON a.id_allegato=c.Id_allegato AND a.data_fine_validita IS NULL
), selected AS (
    SELECT Id_allegato, MIN(Cod_fiscale) cf, MAX(Cod_fiscale) cf_max,
        MIN(Tipo_documento) tipo, MAX(Tipo_documento) tipo_max,
        CASE WHEN MIN(coorte)=MAX(coorte) THEN MIN(coorte) ELSE 'OLD/NEW' END coorte
    FROM ranked WHERE rn=1 GROUP BY Id_allegato
)
SELECT " + (maximum == 0 ? "" : "TOP (@maximum) ") + @"s.Id_allegato,s.cf,s.tipo,s.coorte,sa.data_validita,
    f.file_count,f.file_id,f.byte_count,
    CASE WHEN s.cf=s.cf_max AND s.tipo=s.tipo_max AND f.cf=s.cf AND f.cf_max=s.cf THEN 1 ELSE 0 END consistent,
    sa.Id_STATUS_ALLEGATI
FROM selected s
CROSS APPLY (SELECT TOP (1) cod_status,data_validita,Id_STATUS_ALLEGATI FROM STATUS_ALLEGATI
    WHERE id_allegato=s.Id_allegato AND data_fine_validita IS NULL
    ORDER BY data_validita DESC,Id_STATUS_ALLEGATI DESC) sa
OUTER APPLY (SELECT COUNT_BIG(*) file_count,MIN(id_allegato) file_id,
    MIN(CONVERT(bigint,DATALENGTH([file]))) byte_count,MIN(cod_fiscale) cf,MAX(cod_fiscale) cf_max
    FROM " + archive + @" WHERE Id_Allegato_Adisu=s.Id_allegato) f
WHERE sa.cod_status='01'
    AND (SELECT COUNT_BIG(*) FROM STATUS_ALLEGATI t
        WHERE t.id_allegato=s.Id_allegato AND t.data_validita=sa.data_validita)=1";
            // Filter only after selecting the latest attachment and status; older rows must not resurface.
            if (start.HasValue) command.CommandText += "\n    AND sa.data_validita>=@startDate";
            if (endExclusive.HasValue) command.CommandText += "\n    AND sa.data_validita<@endDateExclusive";
            command.CommandText += "\nORDER BY sa.data_validita,s.Id_allegato";
            if (maximum > 0) command.Parameters.Add("@maximum", SqlDbType.Int).Value = maximum;
            if (start.HasValue) command.Parameters.Add("@startDate", SqlDbType.DateTime).Value = start.Value;
            if (endExclusive.HasValue) command.Parameters.Add("@endDateExclusive", SqlDbType.DateTime).Value = endExclusive.Value;
            return command;
        }

        internal static async Task DownloadAsync(DrosoSession session,
            Func<DrosoDocument, string, CancellationToken, Task> transfer,
            IProgress<string> progress, CancellationToken cancellation)
        {
            session.Save();
            string pdfDirectory = Path.Combine(session.Directory, "pdf");
            Directory.CreateDirectory(pdfDirectory);
            int completed = 0, downloaded = 0;
            long downloadedBytes = 0;
            var progressGate = new object();
            var elapsed = Stopwatch.StartNew();
            progress.Report($"Download parallelo: fino a {DownloadWorkers} allegati contemporaneamente.");
            try
            {
                await Parallel.ForEachAsync(session.Documents, new ParallelOptions
                {
                    MaxDegreeOfParallelism = DownloadWorkers, CancellationToken = cancellation
                }, async (document, token) =>
                {
                    string path = Path.Combine(pdfDirectory, document.IdAllegato + ".pdf");
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        if (document.Error.Length > 0) return;
                        await transfer(document, path + ".tmp", token);
                        string hash;
                        await using (var input = new FileStream(path + ".tmp", FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                        {
                            if (input.Length != document.Bytes || input.Length < 1 || input.Length > MaxBytes)
                                throw new InvalidDataException("File non disponibile o modificato dopo la selezione.");
                            byte[] header = new byte[(int)Math.Min(1024, input.Length)];
                            await input.ReadExactlyAsync(header, token);
                            if (!Encoding.ASCII.GetString(header).Contains("%PDF-"))
                            {
                                document.RejectUnreadablePdf("PDF non leggibile: il file non contiene un PDF riconoscibile.");
                                throw new InvalidDataException("Il file non contiene un PDF riconoscibile.");
                            }
                            input.Position = 0;
                            hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
                        }
                        token.ThrowIfCancellationRequested();
                        File.Move(path + ".tmp", path, true);
                        document.Path = path;
                        document.Sha256 = hash;
                        Interlocked.Increment(ref downloaded);
                        Interlocked.Add(ref downloadedBytes, document.Bytes);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or SqlException)
                    {
                        if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                        if (!document.PdfUnreadable) document.Error = ex.Message;
                        Logger.LogError(null, $"[Droso] Download allegato {document.IdAllegato} non completato.\n{ex}");
                    }
                    finally
                    {
                        try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { Logger.LogWarning(null, $"[Droso] File temporaneo non eliminato: {path}.tmp\n{ex}"); }
                        if (!token.IsCancellationRequested)
                            lock (progressGate)
                                progress.Report($"Download completati {++completed}/{session.Documents.Count} — allegato {document.IdAllegato}");
                    }
                });
            }
            finally
            {
                foreach (var document in session.Documents.Where(d => d.Path.Length == 0 && d.Error.Length == 0))
                    document.Error = "Download non completato: eseguire una nuova estrazione.";
                session.Save();
            }
            progress.Report($"Download terminato: {downloaded} PDF, {downloadedBytes / 1048576d:F1} MB in {elapsed.Elapsed.TotalSeconds:F1} s; "
                + $"{session.Documents.Count(d => d.Error.Length > 0)} allegati con errori.");
        }
    }
}
