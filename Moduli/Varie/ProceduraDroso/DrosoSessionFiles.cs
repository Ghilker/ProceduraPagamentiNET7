namespace ProcedureNet7
{
    internal static class DrosoSessionFiles
    {
        internal static (int Files, long Bytes) DeleteDownloads(DrosoSession session, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(session.Directory)) throw new InvalidOperationException("Aprire prima una sessione.");
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(session.Directory));
            CheckLinks(root);
            if (!File.Exists(Path.Combine(root, "session.json")))
                throw new InvalidOperationException("La cartella non contiene una sessione Droso salvata.");
            var targets = new List<string> { Path.Combine(root, "pdf"), Path.Combine(root, "prepared") };
            // Older runs kept rendered pages inside their own analysis directory.
            foreach (string run in Directory.EnumerateDirectories(root, "analysis-*"))
                if (Guid.TryParseExact(Path.GetFileName(run)[9..], "N", out _))
                {
                    CheckLinks(run);
                    targets.Add(Path.Combine(run, "prepared"));
                }
            var files = new List<string>();
            var directories = new List<string>();
            void Inspect(string path)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Percorso esterno alla sessione: " + path);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Pulizia interrotta: collegamento a un altro percorso: " + path);
                if (Directory.Exists(path))
                {
                    directories.Add(path);
                    foreach (string child in Directory.EnumerateFileSystemEntries(path)) Inspect(child);
                }
                else files.Add(path);
            }
            // Validate every target before deleting anything. Never follow paths from document rows.
            foreach (string target in targets)
            {
                if (File.Exists(target) || Directory.Exists(target)) Inspect(target);
            }
            int count = 0;
            long bytes = 0;
            foreach (string path in files)
            {
                cancellation.ThrowIfCancellationRequested();
                CheckLinks(path);
                long length = new FileInfo(path).Length;
                File.Delete(path);
                bytes += length;
                count++;
            }
            foreach (string path in directories.AsEnumerable().Reverse())
            {
                cancellation.ThrowIfCancellationRequested();
                CheckLinks(path);
                Directory.Delete(path, false);
            }
            return (count, bytes);
        }

        private static void CheckLinks(string path)
        {
            for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Pulizia interrotta: percorso con collegamenti: " + current);
        }
    }
}
