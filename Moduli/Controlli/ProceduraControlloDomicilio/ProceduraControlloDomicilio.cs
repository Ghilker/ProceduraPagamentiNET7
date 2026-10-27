using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DomicilioAutomation;

namespace ProcedureNet7
{
    /// <summary>
    /// Controllo in sola lettura delle nuove istanze domicilio inviate.
    /// Produce proposte di lavorazione per istanza e dettaglio per operazione.
    /// </summary>
    internal sealed class ControlloDomicilio : BaseProcedure<ArgsControlloDomicilio>
    {
        private const int MinAcademicYear = 20262027;
        private const int PdfProgressInterval = 160;
        private const int PdfMaxParallelism = 16;
        private const int PdfQueueCapacityPerWorker = 1;
        private const int PdfSqlDownloadMaxParallelism = 4;
        private const string AllegatiDatabase = "ADISU_ALLEGATI";

        public string selectedAA = string.Empty;

        private sealed record PdfWorkItem(
            DomicilioAttachmentRecord Attachment,
            DomicilioPdfExpectation Expectation,
            DomicilioControlState DestinationState);

        private sealed record LoadedPdf(
            decimal IdAllegato,
            byte[]? Content,
            string FileName,
            string ContentType,
            string? SkipReason = null);

        private sealed record PdfDescriptor(
            decimal IdAllegato,
            decimal? PhysicalFileId,
            long ContentLength,
            string FileName,
            string ContentType);

        private sealed record PdfStreamMetrics(
            int Documents,
            long Bytes,
            long QueueWaitTicks);

        private sealed record CompletedPdfAnalysis(
            PdfWorkItem WorkItem,
            DomicilioPdfAnalysis Analysis,
            string FileName,
            string ContentType);

        public ControlloDomicilio(MasterForm? masterForm, SqlConnection? connection)
            : base(masterForm, connection)
        {
        }

        public override void RunProcedure(ArgsControlloDomicilio args)
        {
            var requests = Analyze(args);
            var outputPath = IstanzaDomicilioReport.Export(requests, args._folderPath, selectedAA);
            var decisions = requests.Select(IstanzaDomicilioReport.Decide).ToList();
            Logger.LogInfo(100,
                $"Controllo completato. CSV: {outputPath}. " +
                $"Istanze: {requests.Count}; accettabili: {decisions.Count(x => x.Azione == IstanzaDomicilioReport.Accettabile)}; " +
                $"da validare: {decisions.Count(x => x.Azione == IstanzaDomicilioReport.DaValidare)}; " +
                $"proposte di rifiuto: {decisions.Count(x => x.Azione == IstanzaDomicilioReport.Rifiutare)}.");
        }

        internal List<IstanzaDomicilioRecord> Analyze(ArgsControlloDomicilio args, bool forAutomation = false)
        {
            ValidateArguments(args);
            selectedAA = args._selectedAA.Trim();

            EnsureConnectionOpen();
            Directory.CreateDirectory(args._folderPath);

            var allowedFiscalCodes = args._codiciFiscali == null
                ? null
                : new HashSet<string>(
                    args._codiciFiscali.Select(NormalizeFiscalCode).Where(x => x.Length > 0),
                    StringComparer.OrdinalIgnoreCase);

            Logger.LogInfo(2, $"Avvio controllo istanze domicilio inviate per l'anno accademico {selectedAA}.");
            // Capture BEFORE loading the analyzed graph. Acceptance checks this
            // fingerprint again under the backend transaction's write gate.
            var expectedStates = forAutomation
                ? DomicilioAutomationSnapshot.CaptureFree(CONNECTION!, selectedAA)
                : null;
            var data = new IstanzaDomicilioData();
            var requests = data.Load(CONNECTION!, selectedAA, allowedFiscalCodes);
            Logger.LogInfo(20, $"Caricate {requests.Count} istanze inviate e {requests.Sum(x => x.Operazioni.Count)} operazioni.");

            var today = DateTime.Today;
            var candidates = requests.Where(x => !forAutomation || !x.PresaCarico && expectedStates!.ContainsKey(x.NumIstanza)).ToList();
            foreach (var request in candidates)
            {
                if (forAutomation) request.ExpectedFingerprint = expectedStates![request.NumIstanza];
                IstanzaDomicilioValidator.Validate(request, data.Comuni, today);
            }
            data.BindAttachments(candidates);

            var pdfWork = BuildInstancePdfWork(candidates, forAutomation);
            Logger.LogInfo(35, $"Individuati {pdfWork.Count} documenti PDF da controllare.");
            AnalyzePdfDocuments(pdfWork);

            return requests;
        }

        private static List<PdfWorkItem> BuildInstancePdfWork(IEnumerable<IstanzaDomicilioRecord> requests, bool readAllPages)
        {
            var work = new List<PdfWorkItem>();
            foreach (var operation in requests.SelectMany(x => x.Operazioni))
            foreach (var document in operation.Documenti)
            {
                if (document.Expectation == null || document.Attachment == null)
                    continue;
                // Uno stesso file può essere confrontato con aspettative diverse:
                // ogni documento conserva la propria analisi, senza sovrascritture.
                var source = document.Attachment;
                var attachment = new DomicilioAttachmentRecord
                {
                    IdAllegato = source.IdAllegato, CodTipoAllegato = source.CodTipoAllegato,
                    CodStatus = source.CodStatus, StatusDescription = source.StatusDescription,
                    DataValidita = source.DataValidita, IdContratto = source.IdContratto,
                    IdProroga = source.IdProroga, CodFiscale = source.CodFiscale,
                    NumDomanda = source.NumDomanda
                };
                document.Attachment = attachment;
                work.Add(new PdfWorkItem(attachment,
                    readAllPages ? document.Expectation with { ReadAllPages = true } : document.Expectation,
                    operation.State));
            }
            return work;
        }

        private static void ValidateArguments(ArgsControlloDomicilio args)
        {
            ArgumentNullException.ThrowIfNull(args);

            if (!Regex.IsMatch(args._selectedAA?.Trim() ?? string.Empty, @"^\d{8}$") ||
                !int.TryParse(args._selectedAA, NumberStyles.None, CultureInfo.InvariantCulture, out var aa))
                throw new ArgumentException("L'anno accademico deve contenere otto cifre nel formato xxxxyyyy.");

            var firstYear = aa / 10000;
            var secondYear = aa % 10000;
            if (secondYear != firstYear + 1)
                throw new ArgumentException("L'anno accademico deve indicare due anni consecutivi (es. 20262027).");
            if (aa < MinAcademicYear)
                throw new ArgumentException("La nuova gestione dei domicili è supportata a partire dall'anno accademico 20262027.");
            if (string.IsNullOrWhiteSpace(args._folderPath))
                throw new ArgumentException("Selezionare la cartella in cui generare il CSV.");
        }

        private void EnsureConnectionOpen()
        {
            if (CONNECTION == null)
                throw new InvalidOperationException("La connessione al database principale non è disponibile.");
            if (CONNECTION.State != ConnectionState.Open)
                CONNECTION.Open();
        }

        private void AnalyzePdfDocuments(List<PdfWorkItem> work)
        {
            if (work.Count == 0)
                return;

            var byAttachment = work
                .GroupBy(x => x.Attachment.IdAllegato)
                .ToDictionary(x => x.Key, x => x.ToList());
            var ids = byAttachment.Keys.OrderBy(x => x).ToList();
            var parallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, PdfMaxParallelism));
            var queueCapacity = Math.Max(parallelism, parallelism * PdfQueueCapacityPerWorker);
            Logger.LogInfo(35,
                $"Letture SQL per gruppi di allegati; download BLOB fino a {PdfSqlDownloadMaxParallelism} connessioni e " +
                $"analisi iText con {parallelism} processi paralleli (coda massima {queueCapacity} PDF).");


            using var queue = new BlockingCollection<LoadedPdf>(queueCapacity);
            var completed = new ConcurrentBag<CompletedPdfAnalysis>();
            var elapsed = Stopwatch.StartNew();
            var progressLock = new object();
            var processedAttachments = 0;
            var lastLogged = 0;
            long analyzedDocuments = 0;
            long totalPagesRead = 0;
            long totalPages = 0;

            void AnalyzeLoadedPdf(LoadedPdf file)
            {
                foreach (var workItem in byAttachment[file.IdAllegato])
                {
                    DomicilioPdfAnalysis analysis;
                    try
                    {
                        if (file.SkipReason != null)
                        {
                            analysis = new DomicilioPdfAnalysis { FileAvailable = true };
                            analysis.State.Add(DomicilioControlSeverity.VerificaManuale, "PDF_002",
                                file.SkipReason, "Aprire e verificare manualmente il documento.");
                        }
                        else
                            analysis = ControlloDomicilioPdfAnalyzer.Analyze(file.Content, workItem.Expectation);
                    }
                    catch (Exception)
                    {
                        analysis = new DomicilioPdfAnalysis();
                        analysis.State.Add(
                            DomicilioControlSeverity.VerificaManuale,
                            "PDF_099",
                            "Si è verificato un errore imprevisto durante l'analisi automatica del PDF.",
                            "Aprire e verificare manualmente il documento.");
                    }

                    completed.Add(new CompletedPdfAnalysis(
                        workItem,
                        analysis,
                        file.FileName,
                        file.ContentType));
                    Interlocked.Increment(ref analyzedDocuments);
                    Interlocked.Add(ref totalPagesRead, analysis.PagesRead);
                    Interlocked.Add(ref totalPages, analysis.PageCount);
                }

                var processed = Interlocked.Increment(ref processedAttachments);
                if (processed % PdfProgressInterval != 0 && processed != ids.Count)
                    return;

                lock (progressLock)
                {
                    if (processed <= lastLogged)
                        return;
                    lastLogged = processed;
                    var rate = processed / Math.Max(0.001d, elapsed.Elapsed.TotalSeconds);
                    var remainingSeconds = rate <= 0d ? 0d : (ids.Count - processed) / rate;
                    var progress = 35 + (int)(45d * processed / ids.Count);
                    var documentCount = Math.Max(1L, Interlocked.Read(ref analyzedDocuments));
                    var averagePages = (double)Interlocked.Read(ref totalPagesRead) / documentCount;
                    Logger.LogInfo(progress,
                        $"Analizzati {processed} di {ids.Count} allegati ({rate:F1} PDF/s; " +
                        $"{averagePages:F1} pagine lette/PDF; coda {queue.Count}/{queueCapacity}; " +
                        $"tempo residuo stimato {FormatDuration(remainingSeconds)}).");
                }
            }

            var workers = Enumerable.Range(0, parallelism)
                .Select(_ => Task.Factory.StartNew(
                    () =>
                    {
                        foreach (var file in queue.GetConsumingEnumerable())
                            AnalyzeLoadedPdf(file);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();

            try
            {
                StreamPdfDocuments(byAttachment, queue);
            }
            finally
            {
                queue.CompleteAdding();
                Task.WaitAll(workers);
            }

            // Il merge è sequenziale e ordinato: nessuna struttura di esito viene
            // modificata dai worker e il CSV rimane deterministico.
            foreach (var result in completed
                         .OrderBy(x => x.WorkItem.Attachment.IdAllegato)
                         .ThenBy(x => x.WorkItem.Expectation.Type))
            {
                result.WorkItem.Attachment.FileName = result.FileName;
                result.WorkItem.Attachment.ContentType = result.ContentType;
                result.WorkItem.Attachment.Analysis = result.Analysis;
                result.WorkItem.DestinationState.Merge(result.Analysis.State);
            }

            var finalDocumentCount = Math.Max(1L, analyzedDocuments);
            Logger.LogInfo(80,
                $"Analisi PDF completata in {FormatDuration(elapsed.Elapsed.TotalSeconds)}: {ids.Count} allegati, " +
                $"media {(ids.Count / Math.Max(0.001d, elapsed.Elapsed.TotalSeconds)):F1} PDF/s; " +
                $"lette {(double)totalPagesRead / finalDocumentCount:F1} pagine/PDF su " +
                $"{(double)totalPages / finalDocumentCount:F1} presenti.");
        }

        private void StreamPdfDocuments(
            IReadOnlyDictionary<decimal, List<PdfWorkItem>> byAttachment,
            BlockingCollection<LoadedPdf> destination)
        {
            var streamElapsed = Stopwatch.StartNew();
            var descriptors = LoadPdfDescriptors(byAttachment);
            var downloadable = descriptors.Where(x => x.PhysicalFileId.HasValue &&
                x.ContentLength <= ControlloDomicilioPdfAnalyzer.MaxPdfBytes).ToList();
            long missingQueueWaitTicks = 0;

            foreach (var descriptor in descriptors.Where(x => !x.PhysicalFileId.HasValue ||
                         x.ContentLength > ControlloDomicilioPdfAnalyzer.MaxPdfBytes))
            {
                var queueWaitStart = Stopwatch.GetTimestamp();
                destination.Add(new LoadedPdf(
                    descriptor.IdAllegato,
                    null,
                    descriptor.FileName,
                    descriptor.ContentType,
                    descriptor.ContentLength > ControlloDomicilioPdfAnalyzer.MaxPdfBytes
                        ? "Il documento supera il limite operativo di 20 MB e non è stato scaricato né analizzato automaticamente."
                        : null));
                missingQueueWaitTicks += Stopwatch.GetTimestamp() - queueWaitStart;
            }

            if (downloadable.Count == 0)
                return;

            var requestedConnections = Math.Max(
                1,
                Math.Min(PdfSqlDownloadMaxParallelism, downloadable.Count));
            var connections = new List<SqlConnection>(requestedConnections);
            var metrics = new List<PdfStreamMetrics>();

            try
            {
                for (var index = 0; index < requestedConnections; index++)
                {
                    var clonedConnection = (SqlConnection)((ICloneable)CONNECTION!).Clone();
                    clonedConnection.Open();
                    connections.Add(clonedConnection);
                }
            }
            catch (Exception ex)
            {
                foreach (var connection in connections)
                    connection.Dispose();
                connections.Clear();

                Logger.LogWarning(null,
                    $"Download SQL parallelo non disponibile ({ex.GetBaseException().Message}). " +
                    "La procedura prosegue con la connessione principale.");
            }

            if (connections.Count == 0)
            {
                metrics.Add(StreamPdfShard(CONNECTION!, downloadable, destination));
            }
            else
            {
                var shards = PartitionPdfDescriptors(downloadable, connections.Count);
                Logger.LogInfo(null,
                    $"Download di {downloadable.Count} BLOB distribuito su {connections.Count} connessioni SQL parallele.");

                try
                {
                    var downloadTasks = connections
                        .Select((connection, index) => Task.Run(() =>
                            StreamPdfShard(connection, shards[index], destination)))
                        .ToArray();
                    Task.WaitAll(downloadTasks);
                    metrics.AddRange(downloadTasks.Select(x => x.Result));
                }
                finally
                {
                    foreach (var connection in connections)
                        connection.Dispose();
                }
            }

            var streamedDocuments = metrics.Sum(x => x.Documents) + descriptors.Count(x => !x.PhysicalFileId.HasValue);
            var totalBytes = metrics.Sum(x => x.Bytes);
            var queueWaitTicks = metrics.Sum(x => x.QueueWaitTicks) + missingQueueWaitTicks;
            var queueWait = TimeSpan.FromSeconds((double)queueWaitTicks / Stopwatch.Frequency);
            var effectiveReadSeconds = Math.Max(0.001d, streamElapsed.Elapsed.TotalSeconds);
            var megabytes = totalBytes / 1024d / 1024d;
            Logger.LogInfo(null,
                $"Flusso SQL completato: {streamedDocuments} allegati, {megabytes:F1} MB; " +
                $"tempo totale {FormatDuration(effectiveReadSeconds)} ({megabytes / effectiveReadSeconds:F1} MB/s), " +
                $"attesa cumulativa della coda iText {FormatDuration(queueWait.TotalSeconds)}.");
        }

        private List<PdfDescriptor> LoadPdfDescriptors(
            IReadOnlyDictionary<decimal, List<PdfWorkItem>> byAttachment)
        {
            var descriptors = new List<PdfDescriptor>(byAttachment.Count);
            foreach (var batch in byAttachment.Keys.OrderBy(x => x).Chunk(500))
            {
                using var command = CONNECTION!.CreateCommand();
                command.CommandTimeout = 3600;
                var parameters = AddIdParameters(command, batch);
                command.CommandText = $@"
;WITH richiesti AS
(
    SELECT v.ID_ALLEGATO FROM (VALUES {string.Join(",", parameters.Select(x => $"({x})"))}) v(ID_ALLEGATO)
), RANKED_ALLEGATI AS
(
    SELECT a.id_allegato AS ID_FILE, a.Id_Allegato_Adisu AS ID_ALLEGATO,
        ROW_NUMBER() OVER (PARTITION BY a.Id_Allegato_Adisu ORDER BY a.data_validita DESC, a.id_allegato DESC) AS RN,
        COUNT_BIG(*) OVER (PARTITION BY a.Id_Allegato_Adisu) AS ACTIVE_COUNT
    FROM [{AllegatiDatabase}].DIRSTUDIO.ALLEGATI a
    INNER JOIN richiesti r ON r.ID_ALLEGATO = a.Id_Allegato_Adisu
    WHERE a.data_fine_validita IS NULL
)
SELECT r.ID_ALLEGATO AS Id_Allegato_Adisu, ISNULL(s.ACTIVE_COUNT, 0) AS ACTIVE_COUNT,
    s.ID_FILE, ISNULL(a.doc_name, '') AS doc_name, ISNULL(a.doc_type, '') AS doc_type,
    ISNULL(DATALENGTH(a.[file]), 0) AS CONTENT_LENGTH
FROM richiesti r
LEFT JOIN RANKED_ALLEGATI s ON s.ID_ALLEGATO = r.ID_ALLEGATO AND s.RN = 1
LEFT JOIN [{AllegatiDatabase}].DIRSTUDIO.ALLEGATI a ON a.id_allegato = s.ID_FILE
ORDER BY r.ID_ALLEGATO OPTION (RECOMPILE);";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = ReadDecimal(reader, "Id_Allegato_Adisu");
                    var activeCount = Convert.ToInt64(reader["ACTIVE_COUNT"], CultureInfo.InvariantCulture);
                    if (activeCount > 1)
                        foreach (var item in byAttachment[id])
                            AddManual(item.DestinationState, "DOC_007",
                                $"In ADISU_ALLEGATI esistono {activeCount} file attivi per l'allegato {id:0}.",
                                "Verificare quale file sia valido.");
                    descriptors.Add(new PdfDescriptor(id, ReadNullableDecimal(reader, "ID_FILE"),
                        Convert.ToInt64(reader["CONTENT_LENGTH"], CultureInfo.InvariantCulture),
                        ReadString(reader, "doc_name"), ReadString(reader, "doc_type")));
                }
            }
            return descriptors;
        }

        private static string[] AddIdParameters(SqlCommand command, IEnumerable<decimal> ids)
        {
            var names = new List<string>();
            foreach (var id in ids)
            {
                var name = "@id" + names.Count.ToString(CultureInfo.InvariantCulture);
                command.Parameters.Add(new SqlParameter(name, SqlDbType.Decimal) { Precision = 18, Scale = 0, Value = id });
                names.Add(name);
            }
            return names.ToArray();
        }

        private static List<List<PdfDescriptor>> PartitionPdfDescriptors(
            IReadOnlyCollection<PdfDescriptor> descriptors,
            int partitionsCount)
        {
            var partitions = Enumerable.Range(0, partitionsCount)
                .Select(_ => new List<PdfDescriptor>())
                .ToList();
            var partitionSizes = new long[partitionsCount];

            foreach (var descriptor in descriptors
                         .OrderByDescending(x => x.ContentLength)
                         .ThenBy(x => x.IdAllegato))
            {
                var target = 0;
                for (var index = 1; index < partitionSizes.Length; index++)
                {
                    if (partitionSizes[index] < partitionSizes[target])
                        target = index;
                }

                partitions[target].Add(descriptor);
                partitionSizes[target] += descriptor.ContentLength;
            }

            return partitions;
        }

        private static PdfStreamMetrics StreamPdfShard(
            SqlConnection connection,
            IReadOnlyCollection<PdfDescriptor> descriptors,
            BlockingCollection<LoadedPdf> destination)
        {
            long queueWaitTicks = 0;
            long totalBytes = 0;
            var documents = 0;
            foreach (var batch in descriptors.Chunk(500))
            {
                using var command = connection.CreateCommand();
                command.CommandTimeout = 3600;
                var parameters = AddIdParameters(command, batch.Select(x => x.PhysicalFileId!.Value));
                command.CommandText = $@"
SELECT a.id_allegato AS ID_FILE, ISNULL(DATALENGTH(a.[file]), 0) AS CONTENT_LENGTH,
    CASE WHEN DATALENGTH(a.[file]) <= {ControlloDomicilioPdfAnalyzer.MaxPdfBytes} THEN a.[file] ELSE NULL END AS [file]
FROM [{AllegatiDatabase}].DIRSTUDIO.ALLEGATI a
WHERE a.id_allegato IN ({string.Join(",", parameters)}) AND a.data_fine_validita IS NULL
ORDER BY a.id_allegato;";
                var byPhysicalId = batch.ToDictionary(x => x.PhysicalFileId!.Value);
                var received = new HashSet<decimal>();
                using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    while (reader.Read())
                    {
                        var physicalId = ReadDecimal(reader, "ID_FILE");
                        var descriptor = byPhysicalId[physicalId];
                        received.Add(physicalId);
                        var length = Convert.ToInt64(reader["CONTENT_LENGTH"], CultureInfo.InvariantCulture);
                        var tooLarge = length > ControlloDomicilioPdfAnalyzer.MaxPdfBytes;
                        var content = tooLarge ? null : ReadBytes(reader, "file");
                        totalBytes += content?.LongLength ?? 0L;
                        Enqueue(descriptor, content, tooLarge
                            ? "Il documento supera il limite operativo di 20 MB e non è stato analizzato automaticamente."
                            : null);
                    }
                }
                foreach (var missing in batch.Where(x => !received.Contains(x.PhysicalFileId!.Value)))
                    Enqueue(missing, null, null);
            }
            return new PdfStreamMetrics(documents, totalBytes, queueWaitTicks);

            void Enqueue(PdfDescriptor descriptor, byte[]? content, string? skipReason)
            {
                var queueWaitStart = Stopwatch.GetTimestamp();
                destination.Add(new LoadedPdf(descriptor.IdAllegato, content,
                    descriptor.FileName, descriptor.ContentType, skipReason));
                queueWaitTicks += Stopwatch.GetTimestamp() - queueWaitStart;
                documents++;
            }
        }

        private static string NormalizeFiscalCode(string? value)
            => Regex.Replace(value?.Trim().ToUpperInvariant() ?? string.Empty, @"\s+", string.Empty);

        private static void AddManual(DomicilioControlState state, string code, string description, string action)
            => state.Add(DomicilioControlSeverity.VerificaManuale, code, description, action);

        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d)
                return "meno di 1 secondo";

            var duration = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds));
            if (duration.TotalHours >= 1d)
                return $"{(int)duration.TotalHours}h {duration.Minutes:D2}m";
            if (duration.TotalMinutes >= 1d)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s";
            return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))}s";
        }

        private static byte[]? ReadBytes(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
                return null;

            var length = reader.GetBytes(ordinal, 0, null, 0, 0);
            if (length > int.MaxValue)
                throw new InvalidDataException($"L'allegato supera la dimensione massima gestibile ({length} byte).");
            if (length == 0)
                return Array.Empty<byte>();

            var content = GC.AllocateUninitializedArray<byte>((int)length);
            long offset = 0;
            while (offset < length)
            {
                var count = (int)Math.Min(81920L, length - offset);
                var read = reader.GetBytes(ordinal, offset, content, (int)offset, count);
                if (read <= 0)
                    throw new EndOfStreamException("Il contenuto del PDF è terminato prima della dimensione dichiarata.");
                offset += read;
            }

            return content;
        }

        private static string ReadString(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        private static decimal ReadDecimal(SqlDataReader reader, string column)
            => Convert.ToDecimal(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

        private static decimal? ReadNullableDecimal(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

    }
}
