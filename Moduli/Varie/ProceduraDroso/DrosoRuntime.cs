using System.Text.Json;

namespace ProcedureNet7
{
    internal static class DrosoRuntime
    {
        public static string AssetsDirectory => Path.Combine(AppContext.BaseDirectory, "DrosoAssets");
        public static string ModelDirectory => Path.Combine(AssetsDirectory, "model");

        public static async Task<string> VerifyAssetsAsync(string directory, CancellationToken cancellation)
        {
            string manifest = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifest)) throw new FileNotFoundException("Pacchetto Droso assente. Ricompilare ProcedureNet7 includendo DrosoAssets.", manifest);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, cancellation));
            var root = document.RootElement;
            if (root.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException("Versione del pacchetto Droso non supportata.");
            foreach (var pair in new[] { ("model/best.pt", "CheckpointSha256"), ("model/model.json", "MetadataSha256"), ("brain/visual-graph.pt", "GraphSha256") })
            {
                cancellation.ThrowIfCancellationRequested();
                string path = Path.Combine(directory, pair.Item1);
                string actual = await Task.Run(() => DrosoSession.Hash(path), cancellation);
                if (!string.Equals(actual, root.GetProperty(pair.Item2).GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("File del pacchetto Droso modificato o incompleto: " + path);
            }
            string checkpoint = root.GetProperty("CheckpointSha256").GetString()!;
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "model", "model.json"), cancellation));
            bool learnedVisualInput = metadata.RootElement.TryGetProperty("learned_visual_input", out var learned)
                && learned.ValueKind == JsonValueKind.True;
            if (!root.TryGetProperty("DecisionPolicySha256", out var policyHash))
            {
                if (learnedVisualInput) throw new InvalidDataException("Policy di decisione assente dal manifest del modello Droso con encoder visivo.");
                return checkpoint;
            }
            if (policyHash.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(policyHash.GetString()))
                throw new InvalidDataException("SHA256 della policy di decisione Droso assente o non valido.");
            cancellation.ThrowIfCancellationRequested();
            string policyPath = Path.Combine(directory, "model", "decision-visual-only.json");
            if (!File.Exists(policyPath)) throw new FileNotFoundException("Policy di decisione assente dal pacchetto Droso.", policyPath);
            string actualPolicyHash = await Task.Run(() => DrosoSession.Hash(policyPath), cancellation);
            if (!string.Equals(actualPolicyHash, policyHash.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("File del pacchetto Droso modificato o incompleto: " + policyPath);
            using var policy = JsonDocument.Parse(await File.ReadAllTextAsync(policyPath, cancellation));
            if (!policy.RootElement.TryGetProperty("Checkpoint", out var policyCheckpoint)
                || policyCheckpoint.ValueKind != JsonValueKind.String
                || !string.Equals(policyCheckpoint.GetString(), checkpoint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La policy di decisione Droso non corrisponde al checkpoint del pacchetto.");
            if (!policy.RootElement.TryGetProperty("InputMode", out var inputMode)
                || inputMode.ValueKind != JsonValueKind.String || inputMode.GetString() != "visual_only")
                throw new InvalidDataException("La policy di decisione Droso non usa l'ingresso visual_only previsto.");
            return checkpoint;
        }

        public static string Python(string assets)
        {
            string bundled = Path.Combine(assets, "runtime", "python", "Scripts", "python.exe");
            if (File.Exists(bundled)) return bundled;
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrosoDocLab");
            string pointer = Path.Combine(root, "python-runtime.txt");
            if (File.Exists(pointer))
            {
                string saved = Path.GetFullPath(File.ReadAllText(pointer).Trim());
                if (saved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(saved)) return saved;
            }
            string installed = Path.Combine(root, "python", "Scripts", "python.exe");
            if (File.Exists(installed)) return installed;
            throw new FileNotFoundException("Runtime Python/PyTorch non installato per questo utente. Installare i requisiti Droso sul PC di elaborazione.", installed);
        }

        public static string Poppler(string assets)
        {
            static bool Valid(string path) => File.Exists(Path.Combine(path, "pdfinfo.exe")) && File.Exists(Path.Combine(path, "pdftoppm.exe"));
            string bundled = Path.Combine(assets, "runtime", "poppler", "Library", "bin");
            if (Valid(bundled)) return bundled;
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrosoDocLab", "tools", "poppler");
            string pointer = Path.Combine(root, "installed.txt");
            if (File.Exists(pointer))
            {
                string saved = Path.GetFullPath(File.ReadAllText(pointer).Trim());
                if (saved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Valid(saved)) return saved;
            }
            throw new DirectoryNotFoundException("Poppler non installato per questo utente. Cartella prevista: " + root);
        }

        public static void CheckWritableDirectory(string directory)
        {
            string probe = Path.Combine(directory, ".droso-write-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                stream.WriteByte(0);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                throw new IOException("Impossibile scrivere nella cartella della lavorazione: " + directory, ex);
            }
        }
    }
}
