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
        private const int PdfProgressInterval = 25;
        private const int PdfMaxParallelism = 16;
        private const int PdfSqlDownloadMaxParallelism = 4;
        private const string AllegatiDatabase = "ADISU_ALLEGATI";

        public string selectedAA = string.Empty;

        private sealed record PdfWorkItem(
            int NumIstanza,
            DomicilioAttachmentRecord Attachment,
            DomicilioPdfExpectation Expectation,
            DomicilioControlState DestinationState);

        private sealed record PdfDescriptor(
            decimal IdAllegato,
            decimal? PhysicalFileId,
            long ContentLength,
            string FileName,
            string ContentType,
            long ActiveFileCount);

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

        internal List<IstanzaDomicilioRecord> Analyze(ArgsControlloDomicilio args, bool forAutomation = false, bool useOcr = true,
            string? unlimitedOcrUrl = null)
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
            if (pdfWork.Count == 0)
                return requests;

            var attachmentIds = pdfWork.Select(x => x.Attachment.IdAllegato).Distinct().OrderBy(x => x).ToList();
            using var pdfBatch = DownloadPdfDocuments(attachmentIds);
            AnalyzePdfDocuments(pdfWork, pdfBatch, useOcr, unlimitedOcrUrl, startProgress: 55, endProgress: 80);

            if (forAutomation)
            {
                // Work on detached proposed payloads. The original request remains the CSV evidence.
                var plans = candidates.Select(request => (Request: request,
                        Plan: DomicilioAutomaticCorrectionPlanner.Plan(request, data.Comuni, today)))
                    .Where(x => x.Plan != null).ToList();
                if (plans.Count > 0)
                {
                    var corrected = plans.Select(x => x.Plan!.Candidate).ToList();
                    data.BindAttachments(corrected);
                    Logger.LogInfo(80, $"Rivalutazione di {plans.Count} proposte di correzione documentate, senza modifiche al database.");
                    AnalyzePdfDocuments(BuildInstancePdfWork(corrected, readAllPages: true), pdfBatch, useOcr, unlimitedOcrUrl,
                        startProgress: 80, endProgress: 90);
                    foreach (var item in plans)
                    {
                        item.Request.CorrectionCandidate = item.Plan!.Candidate;
                        item.Request.ProposedCorrections.AddRange(item.Plan.Corrections);
                    }
                }
            }

            return requests;
        }

        private static List<PdfWorkItem> BuildInstancePdfWork(IEnumerable<IstanzaDomicilioRecord> requests, bool readAllPages)
        {
            var work = new List<PdfWorkItem>();
            foreach (var request in requests)
            foreach (var operation in request.Operazioni)
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
                work.Add(new PdfWorkItem(request.NumIstanza, attachment,
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

        private static void AnalyzePdfDocuments(List<PdfWorkItem> work, DomicilioPdfBatch pdfBatch,
            bool useOcr, string? unlimitedOcrUrl, int startProgress, int endProgress)
        {
            if (work.Count == 0)
                return;

            var byAttachment = work
                .GroupBy(x => x.Attachment.IdAllegato)
                .ToDictionary(x => x.Key, x => x.ToList());
            var files = byAttachment.Keys.OrderBy(x => x).Select(id => (Id: id, Pdf: pdfBatch.Find(id))).ToList();
            foreach (var (id, file) in files)
            {
                var activeCount = file?.ActiveFileCount ?? 0;
                if (activeCount > 1)
                    foreach (var item in byAttachment[id])
                        AddManual(item.DestinationState, "DOC_007",
                            $"In ADISU_ALLEGATI esistono {activeCount} file attivi per l'allegato {id:0}.",
                            "Verificare quale file sia valido.");
            }
            var pdfCount = files.Count(x => x.Pdf?.FilePath != null);
            var parallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, PdfMaxParallelism));
            var ocrStatus = useOcr
                ? $"OCR {(unlimitedOcrUrl == null ? "Tesseract" : "Unlimited-OCR 2")} su un massimo di " +
                    $"{Math.Min(parallelism, DomicilioPdfOcr.MaxParallelDocuments)} PDF contemporaneamente."
                : "OCR disattivato.";
            Logger.LogInfo(startProgress,
                $"Avvio analisi: {pdfCount} PDF scaricati su {files.Count} allegati da verificare, " +
                $"fino a {parallelism} analisi parallele. {ocrStatus}");
            var completed = new ConcurrentBag<CompletedPdfAnalysis>();
            var elapsed = Stopwatch.StartNew();
            var progressLock = new object();
            var processedAttachments = 0;
            var analyzedPdfs = 0;
            var lastReport = TimeSpan.Zero;
            long analyzedDocuments = 0;
            long totalPagesRead = 0;
            long totalPages = 0;

            void AnalyzeLoadedPdf(decimal id, DomicilioPdfBatch.FileRecord? file)
            {
                byte[]? content = null;
                var localReadFailed = false;
                if (file?.FilePath is string filePath)
                {
                    try { content = File.ReadAllBytes(filePath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        localReadFailed = true;
                    }
                }

                foreach (var workItem in byAttachment[id])
                {
                    DomicilioPdfAnalysis analysis;
                    try
                    {
                        if (file?.TooLarge == true)
                        {
                            analysis = new DomicilioPdfAnalysis { FileAvailable = true };
                            analysis.State.Add(DomicilioControlSeverity.VerificaManuale, "PDF_002",
                                "Il documento supera il limite operativo di 20 MB e non è stato scaricato né analizzato automaticamente.",
                                "Aprire e verificare manualmente il documento.");
                        }
                        else if (localReadFailed)
                        {
                            analysis = new DomicilioPdfAnalysis { FileAvailable = true };
                            AddManual(analysis.State, "PDF_099", "Impossibile rileggere il PDF scaricato dalla cartella temporanea.",
                                "Aprire e verificare manualmente il documento.");
                        }
                        else
                            analysis = ControlloDomicilioPdfAnalyzer.Analyze(content, workItem.Expectation, useOcr, unlimitedOcrUrl);
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
                        file?.FileName ?? string.Empty,
                        file?.ContentType ?? string.Empty));
                    Interlocked.Increment(ref analyzedDocuments);
                    Interlocked.Add(ref totalPagesRead, analysis.PagesRead);
                    Interlocked.Add(ref totalPages, analysis.PageCount);
                }

                lock (progressLock)
                {
                    var processed = ++processedAttachments;
                    if (content is { Length: > 0 }) analyzedPdfs++;
                    var now = elapsed.Elapsed;
                    if (processed != 1 && processed != files.Count && processed % PdfProgressInterval != 0 &&
                        now - lastReport < TimeSpan.FromSeconds(10))
                        return;
                    lastReport = now;
                    var rate = processed / Math.Max(0.001d, now.TotalSeconds);
                    var remainingSeconds = (files.Count - processed) / rate;
                    var progress = startProgress + (int)((endProgress - startProgress) * (double)processed / files.Count);
                    Logger.LogInfo(progress,
                        $"Analizzati {analyzedPdfs}/{pdfCount} PDF, verificati {processed}/{files.Count} allegati " +
                        $"({rate:F1} allegati/s; " +
                        $"tempo residuo stimato {FormatDuration(remainingSeconds)}).");
                }
            }

            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                file => AnalyzeLoadedPdf(file.Id, file.Pdf));

            // Il merge è sequenziale e ordinato: nessuna struttura di esito viene
            // modificata dai worker e il CSV rimane deterministico.
            ControlloDomicilioPdfAnalyzer.ApplyLinkedMunicipalities(completed
                .Where(r => r.WorkItem.Expectation.ReadAllPages &&
                    !r.WorkItem.DestinationState.Findings.Any(f => f.Code is "DOC_007" or "IST_DOC_06"))
                .Select(r => (r.WorkItem.NumIstanza, r.WorkItem.Attachment.IdAllegato, r.WorkItem.Expectation, r.Analysis)));
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
            var cacheSummary = useOcr && unlimitedOcrUrl != null
                ? $" Verifiche con trascrizione OCR riutilizzata: {completed.Count(r => r.Analysis.UsedCachedTranscription)}."
                : string.Empty;
            Logger.LogInfo(endProgress,
                $"Analisi PDF completata in {FormatDuration(elapsed.Elapsed.TotalSeconds)}: {analyzedPdfs}/{pdfCount} PDF analizzati, " +
                $"{files.Count} allegati verificati; " +
                $"lette {(double)totalPagesRead / finalDocumentCount:F1} pagine/PDF su " +
                $"{(double)totalPages / finalDocumentCount:F1} presenti.{cacheSummary}");
        }

        private DomicilioPdfBatch DownloadPdfDocuments(IReadOnlyList<decimal> ids)
        {
            var elapsed = Stopwatch.StartNew();
            Logger.LogInfo(35, $"Avvio download PDF: {ids.Count} allegati da verificare, fino a {PdfSqlDownloadMaxParallelism} connessioni SQL parallele.");
            var descriptors = LoadPdfDescriptors(ids);
            var batch = DomicilioPdfBatch.Download(ids.Count, staged =>
            {
                var downloadable = new List<PdfDescriptor>();
                foreach (var descriptor in descriptors)
                {
                    var tooLarge = descriptor.ContentLength > ControlloDomicilioPdfAnalyzer.MaxPdfBytes;
                    if (!descriptor.PhysicalFileId.HasValue || tooLarge)
                        staged.Add(descriptor.IdAllegato, null, descriptor.FileName, descriptor.ContentType, descriptor.ActiveFileCount, tooLarge);
                    else
                        downloadable.Add(descriptor);
                }
                DownloadPdfFiles(downloadable, staged);
            }, progress => Logger.LogInfo(35 + (int)(20d * progress.Resolved / progress.Total),
                $"Scaricati {progress.Downloaded}/{progress.Total} PDF ({progress.Bytes / (double)(1024 * 1024):F1} MB), " +
                $"mancanti o vuoti {progress.Missing}, oltre il limite {progress.TooLarge}."));

            var summary = batch.Summary;
            Logger.LogInfo(55, $"Download PDF completato in {FormatDuration(elapsed.Elapsed.TotalSeconds)}: " +
                $"{summary.Downloaded}/{summary.Total} PDF salvati, mancanti o vuoti {summary.Missing}, oltre il limite {summary.TooLarge}.");
            return batch;
        }

        private void DownloadPdfFiles(List<PdfDescriptor> downloadable, DomicilioPdfBatch destination)
        {
            if (downloadable.Count == 0)
                return;

            var requestedConnections = Math.Max(
                1,
                Math.Min(PdfSqlDownloadMaxParallelism, downloadable.Count));
            var connections = new List<SqlConnection>(requestedConnections);
            try
            {
                for (var index = 0; index < requestedConnections; index++)
                {
                    var clonedConnection = (SqlConnection)((ICloneable)CONNECTION!).Clone();
                    connections.Add(clonedConnection);
                    clonedConnection.Open();
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
                DownloadPdfShard(CONNECTION!, downloadable, destination);
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
                            DownloadPdfShard(connection, shards[index], destination)))
                        .ToArray();
                    Task.WaitAll(downloadTasks);
                }
                finally
                {
                    foreach (var connection in connections)
                        connection.Dispose();
                }
            }
        }

        private List<PdfDescriptor> LoadPdfDescriptors(IReadOnlyList<decimal> ids)
        {
            var descriptors = new List<PdfDescriptor>(ids.Count);
            foreach (var batch in ids.Chunk(500))
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
                    descriptors.Add(new PdfDescriptor(id, ReadNullableDecimal(reader, "ID_FILE"),
                        Convert.ToInt64(reader["CONTENT_LENGTH"], CultureInfo.InvariantCulture),
                        ReadString(reader, "doc_name"), ReadString(reader, "doc_type"),
                        Convert.ToInt64(reader["ACTIVE_COUNT"], CultureInfo.InvariantCulture)));
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

        private static void DownloadPdfShard(
            SqlConnection connection,
            IReadOnlyCollection<PdfDescriptor> descriptors,
            DomicilioPdfBatch destination)
        {
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
                        destination.Add(descriptor.IdAllegato, content, descriptor.FileName, descriptor.ContentType,
                            descriptor.ActiveFileCount, tooLarge);
                    }
                }
                foreach (var missing in batch.Where(x => !received.Contains(x.PhysicalFileId!.Value)))
                    destination.Add(missing.IdAllegato, null, missing.FileName, missing.ContentType, missing.ActiveFileCount);
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
