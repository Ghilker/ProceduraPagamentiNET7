using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal sealed class DomicilioUnlimitedOcrCache
    {
        // Bump when the prompt, inference parameters, raster, batch policy or response parser changes.
        private const string TranscriptionRevision = "unlimited-ocr-2/transcription-v1/batch-3";
        private const int MaxEntryBytes = 16 * 1024 * 1024;
        private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
        private static readonly HttpClient MetadataClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseProxy = false
        }) { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 256 * 1024 };
        private static Lazy<DomicilioUnlimitedOcrCache?> _local = new(ResolveLocal);
        private static long _lastCleanupTicks;
        private readonly string _directory;
        private readonly byte[] _configuration;

        internal DomicilioUnlimitedOcrCache(string directory, string configuration)
        {
            _directory = directory;
            _configuration = SHA256.HashData(Encoding.UTF8.GetBytes(TranscriptionRevision + "/" + configuration));
        }

        internal static DomicilioUnlimitedOcrCache? ForEndpoint(string endpoint, HttpClient? client) =>
            client == null && endpoint == "http://127.0.0.1:10000/v1/chat/completions" &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOMICILIO_UNLIMITED_OCR_URL"))
                ? Volatile.Read(ref _local).Value : null;

        internal static void BeginRun() =>
            Volatile.Write(ref _local, new Lazy<DomicilioUnlimitedOcrCache?>(ResolveLocal));

        private static DomicilioUnlimitedOcrCache? ResolveLocal()
        {
            // A slow WSL filesystem or unavailable metadata may never hold up processing.
            try { return Task.Run(CreateLocal).WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult(); }
            catch (TimeoutException) { return null; }
        }

        private static DomicilioUnlimitedOcrCache? CreateLocal()
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (local.Length == 0) return null;
                // Identify the running service, including installations predating this executable.
                var response = MetadataClient.GetStringAsync("http://127.0.0.1:10000/server_info").GetAwaiter().GetResult();
                using var json = JsonDocument.Parse(response);
                var info = json.RootElement;
                if (!info.TryGetProperty("served_model_name", out var model) || model.GetString() != "Unlimited-OCR-2" ||
                    !info.TryGetProperty("model_path", out var path) || path.GetString() is not string linuxPath ||
                    !Regex.IsMatch(linuxPath, @"^/(?:home/[a-zA-Z0-9_][a-zA-Z0-9_-]*|root)/\.local/share/procedurenet7/unlimited-ocr2/model\z") ||
                    !info.TryGetProperty("version", out var version) || string.IsNullOrWhiteSpace(version.GetString())) return null;
                var modelDirectory = @"\\wsl.localhost\Ubuntu" + linuxPath.Replace('/', '\\');
                // The managed installer holds server.lock both while downloading and while serving.
                var metadata = Path.Combine(modelDirectory, ".cache", "huggingface", "download", "config.json.metadata");
                var revision = File.ReadLines(metadata).FirstOrDefault();
                if (revision == null || !Regex.IsMatch(revision, @"^[a-f0-9]{40}\z")) return null;
                var launcher = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(modelDirectory)!, "start.sh"));
                var profile = new StringBuilder(revision).Append('/').Append(version.GetString());
                foreach (var option in new[] { "revision", "weight_version", "dtype", "quantization", "chat_template",
                    "tokenizer_backend", "context_length", "attention_backend", "sampling_backend" })
                {
                    profile.Append('/').Append(option).Append('=');
                    if (info.TryGetProperty(option, out var value)) profile.Append(value.GetRawText());
                }
                var configuration = profile + "/" + Convert.ToHexString(SHA256.HashData(launcher)) + "/" +
                    typeof(Docnet.Core.DocLib).Module.ModuleVersionId + "/" +
                    typeof(SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder).Module.ModuleVersionId;
                return new(Path.Combine(local, "ProcedureNet7", "Domicilio", "UnlimitedOcrCache"), configuration);
            }
            catch (Exception ex) when (IsCacheError(ex)) { return null; }
        }

        internal (string Text, string? Warning)[] Read(byte[] pdf, int pageCount,
            Func<(string Text, string? Warning)[]> recognize, out bool reused)
        {
            reused = false;
            var hashes = new byte[64];
            _configuration.CopyTo(hashes, 0);
            SHA256.HashData(pdf, hashes.AsSpan(32));
            var key = SHA256.HashData(hashes);
            var token = Convert.ToHexString(key);
            var path = Path.Combine(_directory, token + ".ocr");
            Cleanup();
            var pages = TryRead(path, key, pageCount);
            if (pages != null)
            {
                reused = true;
                return pages;
            }

            // A fresh process can join an in-flight transcription of these exact bytes.
            // Other PDFs proceed independently and acquire their normal rendering slots.
            using var mutex = TryLock(token);
            if (mutex == null) return recognize();
            try
            {
                pages = TryRead(path, key, pageCount);
                if (pages != null)
                {
                    reused = true;
                    return pages;
                }
                pages = recognize();
                if (pages.Length == pageCount && pages.All(p => p.Warning == null && !string.IsNullOrWhiteSpace(p.Text)))
                    TryWrite(path, key, pages);
                return pages;
            }
            finally { mutex.ReleaseMutex(); }
        }

        private static Mutex? TryLock(string token)
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(false, @"Local\ProcedureNet7-DomicilioOCR-" + token);
                if (mutex.WaitOne(TimeSpan.FromMinutes(5))) return mutex;
            }
            catch (AbandonedMutexException) { return mutex; }
            catch (Exception ex) when (IsCacheError(ex)) { }
            mutex?.Dispose();
            return null;
        }

        private static (string Text, string? Warning)[]? TryRead(string path, byte[] key, int pageCount)
        {
            try
            {
                var entry = new FileInfo(path);
                if (!entry.Exists || entry.LastWriteTimeUtc < DateTime.UtcNow - Lifetime) return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (stream.Length is <= 0 or > MaxEntryBytes) return null;
                var ciphertext = new byte[(int)stream.Length];
                stream.ReadExactly(ciphertext);
                var plaintext = ProtectedData.Unprotect(ciphertext, key, DataProtectionScope.CurrentUser);
                try
                {
                    var pages = JsonSerializer.Deserialize<string[]>(plaintext);
                    if (pages == null || pages.Length != pageCount || pages.Any(string.IsNullOrWhiteSpace)) return null;
                    return pages.Select(t => (t, (string?)null)).ToArray();
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            catch (Exception ex) when (IsCacheError(ex)) { return null; }
        }

        private void TryWrite(string path, byte[] key, (string Text, string? Warning)[] pages)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var plaintext = JsonSerializer.SerializeToUtf8Bytes(pages.Select(p => p.Text));
                byte[] ciphertext;
                try
                {
                    if (plaintext.Length > MaxEntryBytes) return;
                    ciphertext = ProtectedData.Protect(plaintext, key, DataProtectionScope.CurrentUser);
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
                if (ciphertext.Length > MaxEntryBytes) return;
                Directory.CreateDirectory(_directory);
                File.WriteAllBytes(temp, ciphertext);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (IsCacheError(ex)) { }
            finally
            {
                try { File.Delete(temp); }
                catch (Exception ex) when (IsCacheError(ex)) { }
            }
        }

        private void Cleanup()
        {
            var now = DateTime.UtcNow;
            var previous = Interlocked.Read(ref _lastCleanupTicks);
            if (now.Ticks - previous < TimeSpan.TicksPerDay ||
                Interlocked.CompareExchange(ref _lastCleanupTicks, now.Ticks, previous) != previous) return;
            try
            {
                if (!Directory.Exists(_directory)) return;
                foreach (var entry in new DirectoryInfo(_directory).EnumerateFiles("*.ocr*"))
                {
                    if ((entry.Extension == ".ocr" || entry.Extension == ".tmp") && entry.LastWriteTimeUtc < now - Lifetime)
                        entry.Delete();
                }
            }
            catch (Exception ex) when (IsCacheError(ex)) { }
        }

        private static bool IsCacheError(Exception ex) => ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or CryptographicException or JsonException
            or PlatformNotSupportedException or WaitHandleCannotBeOpenedException or HttpRequestException
            or OperationCanceledException or InvalidOperationException;
    }
}
