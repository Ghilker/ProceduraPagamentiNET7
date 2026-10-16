using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProcedureNet7
{
    internal static class DrosoProcessClient
    {
        public static async Task AnalyzeAsync(string assets, DrosoSession session,
            IProgress<string> progress, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!session.Documents.Any(d => d.PdfUnreadable || File.Exists(d.Path)))
            {
                await SelectReadableInputsAsync(session, cancellation);
                throw new InvalidOperationException("Nessun PDF disponibile da analizzare.");
            }
            DrosoRuntime.CheckWritableDirectory(session.Directory);
            DrosoDocument[] inputs;
            try { inputs = await SelectReadableInputsAsync(session, cancellation); }
            finally { session.Save(); }
            if (inputs.Length == 0)
            {
                if (!session.Documents.Any(d => d.PdfUnreadable))
                    throw new InvalidOperationException("Nessun PDF disponibile da analizzare.");
                progress.Report("Allegati PDF non leggibili rifiutati direttamente: nessuna classificazione avviata.");
                return;
            }
            assets = Path.GetFullPath(assets);
            progress.Report("Verifica del modello integrato e dei PDF…");
            string checkpoint = await DrosoRuntime.VerifyAssetsAsync(assets, cancellation);
            string python = DrosoRuntime.Python(assets), poppler = DrosoRuntime.Poppler(assets);
            string script = Path.Combine(assets, "scripts", "infer_procedure.py");
            if (!File.Exists(script)) throw new FileNotFoundException("Motore di inferenza assente dal pacchetto ProcedureNet7.", script);
            string run = Path.Combine(session.Directory, "analysis-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            string request = Path.Combine(run, "request.json"), cancel = Path.Combine(run, "cancel.request");
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new
            {
                Version = 1, AssetsDirectory = assets, PopplerDirectory = poppler,
                Documents = inputs.Select(d => d.Path).ToArray()
            }, DrosoSession.JsonOptions), cancellation);
            Logger.LogInfo(null, $"[Droso] Modello: {Path.Combine(assets, "model")}; SHA256: {checkpoint}; diagnostica: {run}");
            var start = new ProcessStartInfo(python)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = run
            };
            start.Environment["PYTHONUTF8"] = "1";
            start.Environment["PYTHONNOUSERSITE"] = "1";
            start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add(request);
            using var events = new StreamWriter(Path.Combine(run, "events.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
            using var process = Process.Start(start) ?? throw new IOException("Avvio del motore di inferenza non riuscito.");
            var standardError = process.StandardError.ReadToEndAsync();
            var output = ReadProgressAsync(process.StandardOutput, events, progress);
            void StopProcess(object? sender, EventArgs e)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            Application.ApplicationExit += StopProcess;
            try
            {
                DateTime? cancelling = null;
                while (!process.HasExited)
                {
                    if (output.IsFaulted) await output;
                    if (cancellation.IsCancellationRequested)
                    {
                        if (cancelling == null)
                        {
                            cancelling = DateTime.UtcNow;
                            await File.WriteAllTextAsync(cancel, "cancel");
                            progress.Report("Annullamento di Droso…");
                        }
                        if ((DateTime.UtcNow - cancelling.Value).TotalSeconds >= 20) StopProcess(null, EventArgs.Empty);
                    }
                    await Task.Delay(100);
                }
                await process.WaitForExitAsync();
                await output;
                string errorOutput = await standardError;
                if (errorOutput.Length > 0) await File.WriteAllTextAsync(Path.Combine(run, "engine-stderr.log"), errorOutput);
                cancellation.ThrowIfCancellationRequested();
                if (process.ExitCode == 2) throw new OperationCanceledException("Analisi annullata dal motore.");
                if (process.ExitCode != 0)
                {
                    string file = Path.Combine(run, "error.txt");
                    string details = File.Exists(file) ? await File.ReadAllTextAsync(file) : errorOutput;
                    throw new InvalidOperationException($"Motore Droso terminato con codice {process.ExitCode}. Diagnostica: {run}\n{details}");
                }
                string result = Path.Combine(run, "result.json");
                if (!File.Exists(result)) throw new InvalidDataException("Il motore non ha prodotto il risultato: " + result);
                await VerifyInputsAsync(inputs, cancellation);
                session.ApplyResult(await File.ReadAllTextAsync(result, cancellation), checkpoint, inputs);
                session.ModelDirectory = Path.Combine(assets, "model");
                session.Save();
            }
            finally
            {
                Application.ApplicationExit -= StopProcess;
                StopProcess(null, EventArgs.Empty);
                // Drain both pipes before disposing the log writer, including cancellation/error paths.
                try { await output; await standardError; }
                catch (IOException) { }
            }
        }

        private static Task VerifyInputsAsync(IEnumerable<DrosoDocument> inputs, CancellationToken cancellation) => Task.Run(() =>
        {
            foreach (var input in inputs)
            {
                cancellation.ThrowIfCancellationRequested();
                if (DrosoSession.Hash(input.Path) != input.Sha256)
                    throw new InvalidDataException("PDF modificato rispetto alla sessione: " + input.Path);
            }
        }, cancellation);

        internal static async Task<DrosoDocument[]> SelectReadableInputsAsync(DrosoSession session, CancellationToken cancellation)
        {
            var ready = new List<DrosoDocument>();
            foreach (var input in session.Documents.Where(document => !document.PdfUnreadable))
            {
                cancellation.ThrowIfCancellationRequested();
                if (!File.Exists(input.Path))
                {
                    if (input.Path.Length > 0)
                    {
                        input.Error = "PDF non più disponibile nella sessione.";
                        input.AnalysisCompleted = false;
                    }
                    continue;
                }
                await using var stream = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation));
                if (digest != input.Sha256)
                    throw new InvalidDataException("PDF modificato rispetto alla sessione: " + input.Path);
                stream.Position = 0;
                byte[] header = new byte[(int)Math.Min(1024, stream.Length)];
                await stream.ReadExactlyAsync(header, cancellation);
                if (!Encoding.ASCII.GetString(header).Contains("%PDF-", StringComparison.Ordinal))
                    input.RejectUnreadablePdf("PDF non leggibile: file vuoto o privo dell'intestazione PDF.");
                else ready.Add(input);
            }
            return ready.ToArray();
        }

        private static async Task ReadProgressAsync(StreamReader reader, StreamWriter events, IProgress<string> progress)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                await events.WriteLineAsync(line);
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("stage", out var stage)) continue;
                    string message = stage.GetString() ?? "Elaborazione";
                    if (root.TryGetProperty("current", out var current) && root.TryGetProperty("total", out var total)) message += $" {current}/{total}";
                    if (root.TryGetProperty("message", out var detail)) message += ": " + detail.GetString();
                    progress.Report(message);
                }
                catch (JsonException) { Logger.LogDebug(null, "[Droso] " + line); }
            }
        }
    }
}
