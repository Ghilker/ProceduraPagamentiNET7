using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ProcedureNet7
{
    internal static class DomicilioUnlimitedOcrRuntime
    {
        private const string LocalUrl = "http://127.0.0.1:10000/v1/chat/completions";
        private const string LinuxLogs = "~/.local/share/procedurenet7/unlimited-ocr2/ (setup.log e server.log)";
        private const string SetupMarker = "PROCEDURENET7_OCR_SETUP";
        private const string StartMarker = "PROCEDURENET7_OCR_START";
        private static readonly object Gate = new();
        private static Task<(string? Url, string Message)>? _check;

        public static Task<(string? Url, string Message)> CheckAsync(IProgress<string> progress)
        {
            lock (Gate)
            {
                if (_check is { IsCompleted: false }) return _check;
                return _check = CheckServiceAsync(progress);
            }
        }

        private static async Task<(string? Url, string Message)> CheckServiceAsync(IProgress<string> progress)
        {
            var configuredUrl = Environment.GetEnvironmentVariable("DOMICILIO_UNLIMITED_OCR_URL")?.Trim();
            var url = string.IsNullOrEmpty(configuredUrl) ? LocalUrl : configuredUrl;
            var endpoint = DomicilioUnlimitedOcr.GetEndpoint(url);
            if (endpoint == null)
                return ReportUnavailable("L'URL Unlimited-OCR 2 deve terminare con /v1/chat/completions e usare HTTPS o HTTP locale.");

            Logger.LogInfo(null, "Unlimited-OCR 2: verifica del servizio " + (url == LocalUrl ? "locale." : "configurato."));

            if (await DomicilioUnlimitedOcr.IsModelAvailableAsync(endpoint).ConfigureAwait(false))
                return ReportReady(url);

            if (url != LocalUrl)
                return ReportUnavailable("Il servizio configurato non risponde con il modello Unlimited-OCR-2.");

            try
            {
                Logger.LogInfo(null, "Unlimited-OCR 2: modello non raggiungibile. Avvio o preparazione in Ubuntu WSL.");
                var setupPath = Path.Combine(AppContext.BaseDirectory, "DomicilioOcr", "setup-unlimited-ocr.sh");
                var setup = Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(setupPath).ConfigureAwait(false))
                    .Replace("\r\n", "\n", StringComparison.Ordinal));
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsl.exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                start.Environment["WSL_UTF8"] = "1";
                foreach (var argument in new[] { "--distribution", "Ubuntu", "--exec", "bash", "-c" })
                    start.ArgumentList.Add(argument);
                start.ArgumentList.Add($$"""
                    umask 077
                    folder="$HOME/.local/share/procedurenet7/unlimited-ocr2"
                    launcher="$folder/start.sh"
                    report_failure() {
                        local code="$1" log="$2"
                        if [ "$code" -ne 75 ] && [ -f "$log" ]; then
                            tail -n 16 "$log" | sed -E 's#https?://[^[:space:]]+#[URL]#g' >&2
                        fi
                        exit "$code"
                    }
                    if ! command -v cc >/dev/null || ! command -v c++ >/dev/null; then
                        echo 'Compilatori C/C++ mancanti in Ubuntu. Installare build-essential.' >&2
                        exit 43
                    fi
                    if ! command -v ninja >/dev/null; then
                        echo 'Ninja mancante in Ubuntu. Installare ninja-build.' >&2
                        exit 44
                    fi
                    if [ ! -x "$launcher" ]; then
                        echo '{{SetupMarker}}'
                        mkdir -p "$folder" || exit $?
                        bash -s >> "$folder/setup.log" 2>&1 || report_failure "$?" "$folder/setup.log"
                    fi
                    for toolkit in "$folder"/.venv/lib/python*/site-packages/nvidia/cu13; do
                        if [ -x "$toolkit/bin/nvcc" ]; then
                            export CUDA_HOME="$toolkit"
                            export PATH="$toolkit/bin:$PATH"
                            export LIBRARY_PATH="$toolkit/lib${LIBRARY_PATH:+:$LIBRARY_PATH}"
                            export LD_LIBRARY_PATH="$toolkit/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
                            if [ ! -e "$toolkit/lib/libcudart.so" ] && [ -e "$toolkit/lib/libcudart.so.13" ]; then
                                linker="$folder/cuda-link"
                                mkdir -p "$linker" && ln -sfn "$toolkit/lib/libcudart.so.13" "$linker/libcudart.so" || exit $?
                                export LIBRARY_PATH="$linker:$LIBRARY_PATH"
                            fi
                            echo 'Toolkit CUDA incluso nel motore selezionato.'
                            break
                        fi
                    done
                    proxy_exclusions="${no_proxy:+$no_proxy,}${NO_PROXY:+$NO_PROXY,}127.0.0.1,localhost,::1"
                    export no_proxy="$proxy_exclusions" NO_PROXY="$proxy_exclusions"
                    echo '{{StartMarker}}'
                    "$launcher" </dev/null || report_failure "$?" "$folder/server.log"
                    """.Replace("\r\n", "\n", StringComparison.Ordinal));
                using var service = Process.Start(start);
                if (service == null) return ReportUnavailable("Impossibile avviare il servizio Unlimited-OCR 2 locale.");

                var diagnostics = new ConcurrentQueue<string>();
                string? phase = null;
                void RecordDiagnostic(object sender, DataReceivedEventArgs args)
                {
                    var line = args.Data?.Replace("\0", string.Empty).Trim();
                    if (line is SetupMarker or StartMarker)
                        Volatile.Write(ref phase, line);
                    else if (!string.IsNullOrEmpty(line))
                    {
                        diagnostics.Enqueue(line[..Math.Min(line.Length, 400)]);
                        while (diagnostics.Count > 18 && diagnostics.TryDequeue(out _)) { }
                    }
                }
                service.OutputDataReceived += RecordDiagnostic;
                service.ErrorDataReceived += RecordDiagnostic;
                service.BeginOutputReadLine();
                service.BeginErrorReadLine();
                try
                {
                    await service.StandardInput.BaseStream.WriteAsync(setup).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // WSL may close its input before reading it when Ubuntu is unavailable or the launcher already exists.
                }
                finally
                {
                    service.StandardInput.Close();
                }

                var elapsed = Stopwatch.StartNew();
                var limit = TimeSpan.FromSeconds(75);
                var nextProgress = TimeSpan.FromSeconds(30);
                string? observedPhase = null;
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    // The launcher returns 75 when another instance holds the service lock.
                    if (service.HasExited && service.ExitCode is not (0 or 75))
                    {
                        await service.WaitForExitAsync().ConfigureAwait(false);
                        var details = diagnostics.ToArray();
                        Logger.LogError(null, $"Unlimited-OCR 2: WSL terminato con codice {service.ExitCode}.");
                        foreach (var line in details)
                            Logger.LogError(null, $"Unlimited-OCR 2 [WSL]: {line}");
                        if (details.Any(line => line.Contains("WSL_E_DISTRO_NOT_FOUND", StringComparison.Ordinal)))
                            return ReportUnavailable("Ubuntu non risulta registrato in WSL per questo utente Windows. Aprire Ubuntu dal menu Start e verificare con wsl --list --verbose.");
                        if (details.Any(line => line.Contains("E_ACCESSDENIED", StringComparison.Ordinal)))
                            return ReportUnavailable("WSL ha negato l'accesso a Ubuntu. Verificare che Ubuntu si apra con lo stesso utente Windows.");
                        if (service.ExitCode == 43)
                            return ReportUnavailable("In Ubuntu mancano i compilatori C/C++ necessari al motore OCR. Installare il pacchetto build-essential.");
                        if (service.ExitCode == 44)
                            return ReportUnavailable("In Ubuntu manca Ninja, necessario al motore OCR. Installare il pacchetto ninja-build.");
                        return ReportUnavailable($"Unlimited-OCR 2 non disponibile. Consultare il logger e i log in {LinuxLogs}.");
                    }

                    var currentPhase = Volatile.Read(ref phase);
                    if (currentPhase != observedPhase)
                    {
                        observedPhase = currentPhase;
                        var settingUp = observedPhase == SetupMarker;
                        var message = settingUp
                            ? "Preparazione di Unlimited-OCR 2 in corso. Il primo download può richiedere diversi minuti."
                            : "Avvio del motore Unlimited-OCR 2 in Ubuntu.";
                        Logger.LogInfo(null, message);
                        if (currentPhase == StartMarker && diagnostics.Contains("Toolkit CUDA incluso nel motore selezionato."))
                            Logger.LogInfo(null, "Unlimited-OCR 2: uso del toolkit CUDA incluso nell'ambiente Python.");
                        progress.Report(message);
                        limit = settingUp ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(75);
                        nextProgress = TimeSpan.FromSeconds(30);
                        elapsed.Restart();
                    }
                    if (await DomicilioUnlimitedOcr.IsModelAvailableAsync(endpoint).ConfigureAwait(false))
                        return ReportReady(url);
                    if (elapsed.Elapsed >= limit)
                        return ReportUnavailable(observedPhase == SetupMarker
                            ? $"Unlimited-OCR 2 non è ancora pronto. La preparazione può proseguire. Controllare i log in {LinuxLogs} prima di riprovare."
                            : $"Il servizio Unlimited-OCR 2 non risponde dopo l'avvio. Consultare i log in {LinuxLogs}.");
                    if (elapsed.Elapsed >= nextProgress)
                    {
                        var stage = observedPhase == SetupMarker ? "preparazione" : "avvio";
                        var duration = elapsed.Elapsed.ToString(@"mm\:ss");
                        var message = $"Unlimited-OCR 2: {stage} in corso da {duration}.";
                        Logger.LogInfo(null, message);
                        progress.Report(message);
                        nextProgress += TimeSpan.FromSeconds(30);
                    }
                }
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                Logger.LogError(null, $"Unlimited-OCR 2: errore avvio locale ({ex.GetType().Name}, 0x{ex.HResult:X8}). {ex.Message}");
                return ReportUnavailable("Servizio locale Unlimited-OCR 2 non disponibile. Consultare il logger e verificare Ubuntu WSL.");
            }
        }

        private static (string? Url, string Message) ReportReady(string url)
        {
            DomicilioUnlimitedOcrCache.BeginRun();
            const string message = "Unlimited-OCR 2 pronto.";
            Logger.LogInfo(null, message);
            return (url, message);
        }

        private static (string? Url, string Message) ReportUnavailable(string message)
        {
            Logger.LogWarning(null, message);
            return (null, message);
        }
    }
}
