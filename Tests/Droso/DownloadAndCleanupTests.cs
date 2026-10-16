using System.Text;
using ProcedureNet7;

internal static class DownloadAndCleanupTests
{
    public static async Task RunAsync(string directory, Action<bool, string> check)
    {
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4 synthetic download");
        var session = new DrosoSession { Directory = Path.Combine(directory, "parallel"), Documents = Enumerable.Range(1, 9)
            .Select(id => new DrosoDocument { IdAllegato = id, FileId = id, Bytes = pdf.Length }).ToList() };
        session.Documents[8].Error = "metadata error";
        int active = 0, peak = 0, calls = 0;
        var gate = new object();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new List<string>();
        await DrosoDocumentRepository.DownloadAsync(session, async (document, path, token) =>
        {
            lock (gate)
            {
                calls++; active++; peak = Math.Max(peak, active);
                if (active == 4) ready.TrySetResult();
            }
            try
            {
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                await Task.Delay(document.IdAllegato == 1 ? 80 : 10, token);
                if (document.IdAllegato == 6) throw new IOException("synthetic transfer failure");
                byte[] data = document.IdAllegato == 7 ? pdf[..^1] : document.IdAllegato == 5 ? new byte[pdf.Length] : pdf;
                await File.WriteAllBytesAsync(path, data, token);
            }
            finally { lock (gate) active--; }
        }, new InlineProgress(messages.Add), CancellationToken.None);
        check(peak == 4 && active == 0 && calls == 8, "Quattro download contemporanei, limite rispettato e metadati esclusi");
        check(session.Documents[4].PdfUnreadable && session.Documents[4].AnalysisCompleted && session.Documents[4].ProposedStatus == "04",
            "Download di contenuto non PDF rifiutato direttamente senza pubblicare il file");
        check(!session.Documents[5].PdfUnreadable && !session.Documents[5].AnalysisCompleted
            && !session.Documents[6].PdfUnreadable && !session.Documents[6].AnalysisCompleted,
            "Guasto di trasferimento e download incompleto restano errori operativi");
        check(session.Documents.Where(d => d.Error.Length == 0).All(d => File.Exists(d.Path) && DrosoSession.Hash(d.Path) == d.Sha256)
            && session.Documents.Count(d => d.Path.Length > 0) == 5, "PDF validi pubblicati con hash, errori isolati");
        check(!Directory.EnumerateFiles(session.Directory, "*.tmp", SearchOption.AllDirectories).Any(), "File temporanei rimossi anche dopo errori");
        check(session.Documents.Select(d => d.IdAllegato).SequenceEqual(Enumerable.Range(1, 9).Select(i => (long)i)), "Ordine della selezione conservato con download fuori ordine");
        check(messages.Where(m => m.StartsWith("Download completati ")).Select(m => int.Parse(m.Split(' ')[2].Split('/')[0]))
            .SequenceEqual(Enumerable.Range(1, 9)), "Avanzamento monotono dei download completati");
        check(DrosoSession.Load(Path.Combine(session.Directory, "session.json")).Documents.Count(d => d.Path.Length > 0) == 5,
            "Sessione finale conserva download ed errori");

        using var cancel = new CancellationTokenSource();
        var cancelled = new DrosoSession { Directory = Path.Combine(directory, "cancelled-download"), Documents = Enumerable.Range(1, 12)
            .Select(id => new DrosoDocument { IdAllegato = id, FileId = id, Bytes = pdf.Length }).ToList() };
        int started = 0;
        try
        {
            await DrosoDocumentRepository.DownloadAsync(cancelled, async (document, path, token) =>
            {
                int current = Interlocked.Increment(ref started);
                await File.WriteAllBytesAsync(path, pdf, token);
                if (document.IdAllegato == 1) return;
                if (current == 5) cancel.Cancel();
                await Task.Delay(Timeout.Infinite, token);
            }, new InlineProgress(_ => { }), cancel.Token);
            throw new Exception("Download non annullato");
        }
        catch (OperationCanceledException) { }
        check(started == 5 && cancelled.Documents.Count(d => File.Exists(d.Path)) == 1, "Annullamento ferma i nuovi download e conserva quello completato");
        check(!Directory.EnumerateFiles(cancelled.Directory, "*.tmp", SearchOption.AllDirectories).Any()
            && DrosoSession.Load(Path.Combine(cancelled.Directory, "session.json")).Documents.All(d => d.Path.Length > 0 || d.Error.Length > 0),
            "Annullamento salva gli incompleti e pulisce i trasferimenti parziali");

        string root = Path.Combine(directory, "cleanup");
        var clean = new DrosoSession { Directory = root, Documents = new() { new DrosoDocument { Path = Path.Combine(directory, "external.pdf"), Predicted = "PASSAPORTO" } } };
        clean.Save();
        File.WriteAllBytes(clean.Documents[0].Path, pdf);
        string oldRun = Path.Combine(root, "analysis-" + Guid.NewGuid().ToString("N"));
        string[] downloads = { Path.Combine(root, "pdf", "document.pdf"), Path.Combine(root, "pdf", "partial.pdf.tmp"),
            Path.Combine(root, "prepared", "document.pdf.pages", "page-1.png"), Path.Combine(oldRun, "prepared", "document.pdf") };
        foreach (string path in downloads) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, pdf); }
        string[] retained = { Path.Combine(root, "session.json"), Path.Combine(root, "results.csv"), Path.Combine(oldRun, "result.json"), Path.Combine(oldRun, "events.jsonl") };
        foreach (string path in retained.Skip(1)) File.WriteAllText(path, "synthetic retained report");
        var hashes = retained.ToDictionary(path => path, DrosoSession.Hash);
        var removed = DrosoSessionFiles.DeleteDownloads(clean, CancellationToken.None);
        check(removed.Files == 4 && removed.Bytes == pdf.Length * 4 && downloads.All(path => !File.Exists(path)), "Pulizia elimina PDF, immagini e trasferimenti parziali della sola sessione");
        check(hashes.All(pair => DrosoSession.Hash(pair.Key) == pair.Value) && File.Exists(clean.Documents[0].Path), "Pulizia conserva risultati, log e file originali esterni");
        check(DrosoSessionFiles.DeleteDownloads(clean, CancellationToken.None).Files == 0, "Pulizia ripetuta senza errori");
        using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
        Directory.CreateDirectory(Path.Combine(root, "pdf")); File.WriteAllBytes(downloads[0], pdf);
        try { DrosoSessionFiles.DeleteDownloads(clean, alreadyCancelled.Token); throw new Exception("Pulizia non annullata"); }
        catch (OperationCanceledException) { check(File.Exists(downloads[0]), "Pulizia annullata non elimina file"); }
        string foreign = Path.Combine(directory, "not-a-session"); Directory.CreateDirectory(Path.Combine(foreign, "pdf"));
        File.WriteAllBytes(Path.Combine(foreign, "pdf", "keep.pdf"), pdf);
        try { DrosoSessionFiles.DeleteDownloads(new DrosoSession { Directory = foreign }, CancellationToken.None); throw new Exception("Cartella estranea accettata"); }
        catch (InvalidOperationException) { check(File.Exists(Path.Combine(foreign, "pdf", "keep.pdf")), "Cartelle prive di sessione non cancellabili"); }
        using var locked = new FileStream(downloads[0], FileMode.Open, FileAccess.Read, FileShare.None);
        try { DrosoSessionFiles.DeleteDownloads(clean, CancellationToken.None); throw new Exception("File bloccato ignorato"); }
        catch (IOException) { check(hashes.All(pair => DrosoSession.Hash(pair.Key) == pair.Value), "File bloccato segnala errore e conserva i risultati"); }
    }

    private sealed class InlineProgress : IProgress<string>
    {
        private readonly Action<string> report;
        public InlineProgress(Action<string> report) => this.report = report;
        public void Report(string value) => report(value);
    }
}
