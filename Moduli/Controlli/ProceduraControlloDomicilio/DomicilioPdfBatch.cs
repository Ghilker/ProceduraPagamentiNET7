using System.Diagnostics;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed class DomicilioPdfBatch : IDisposable
    {
        internal sealed record FileRecord(string? FilePath, string FileName, string ContentType, long ActiveFileCount, bool TooLarge);

        internal readonly record struct Progress(int Total, int Downloaded, int Missing, int TooLarge, long Bytes)
        {
            internal int Resolved => Downloaded + Missing + TooLarge;
        }

        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("domicilio-pdf-");
        private readonly Dictionary<decimal, FileRecord> _files = new();
        private readonly object _sync = new();
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly Action<Progress> _report;
        private Progress _progress;
        private TimeSpan _lastReport;
        private bool _downloadComplete;

        private DomicilioPdfBatch(int total, Action<Progress> report)
        {
            _progress = new Progress(total, 0, 0, 0, 0);
            _report = report;
        }

        internal static DomicilioPdfBatch Download(int total, Action<DomicilioPdfBatch> download, Action<Progress> report)
        {
            var batch = new DomicilioPdfBatch(total, report);
            try
            {
                download(batch);
                if (batch._progress.Resolved != total)
                    throw new InvalidOperationException("Il download non ha restituito un esito per ogni allegato richiesto.");
                batch._downloadComplete = true;
                return batch;
            }
            catch
            {
                try { batch.Dispose(); }
                catch (IOException) { /* Preserve the original download error. */ }
                catch (UnauthorizedAccessException) { /* Preserve the original download error. */ }
                throw;
            }
        }

        internal void Add(decimal id, byte[]? content, string fileName, string contentType, long activeFileCount, bool tooLarge = false)
        {
            string? path = null;
            if (!tooLarge && content is { Length: > 0 })
            {
                path = Path.Combine(_directory.FullName, id.ToString(CultureInfo.InvariantCulture) + ".pdf");
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                stream.Write(content);
            }

            lock (_sync)
            {
                _files.Add(id, new FileRecord(path, fileName, contentType, activeFileCount, tooLarge));
                _progress = _progress with
                {
                    Downloaded = _progress.Downloaded + (path != null ? 1 : 0),
                    Missing = _progress.Missing + (path == null && !tooLarge ? 1 : 0),
                    TooLarge = _progress.TooLarge + (tooLarge ? 1 : 0),
                    Bytes = _progress.Bytes + (path != null ? content!.LongLength : 0)
                };

                var resolved = _progress.Resolved;
                var elapsed = _elapsed.Elapsed;
                if (resolved == 1 || resolved == _progress.Total || resolved % 25 == 0 ||
                    elapsed - _lastReport >= TimeSpan.FromSeconds(10))
                {
                    _lastReport = elapsed;
                    _report(_progress);
                }
            }
        }

        internal FileRecord? Find(decimal id)
        {
            if (!_downloadComplete)
                throw new InvalidOperationException("I PDF sono disponibili solo al termine del download.");
            return _files.GetValueOrDefault(id);
        }

        internal Progress Summary => _progress;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
