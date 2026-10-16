using System.Text.Json;
using ProcedureNet7;

internal static class DrosoAssetTests
{
    public static async Task RunAsync(string folder, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        string Fixture(string name, bool learned = true, bool includePolicyHash = true, string? policyCheckpoint = null,
            string inputMode = "visual_only", bool includePolicyCheckpoint = true)
        {
            string directory = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.Combine(directory, "model"));
            Directory.CreateDirectory(Path.Combine(directory, "brain"));
            string checkpoint = Path.Combine(directory, "model", "best.pt");
            string metadata = Path.Combine(directory, "model", "model.json");
            string graph = Path.Combine(directory, "brain", "visual-graph.pt");
            string policy = Path.Combine(directory, "model", "decision-visual-only.json");
            File.WriteAllText(checkpoint, "synthetic-checkpoint");
            File.WriteAllText(metadata, learned ? JsonSerializer.Serialize(new { engine_version = 4, learned_visual_input = true }) : "{\"engine_version\":3}");
            File.WriteAllText(graph, "synthetic-graph");
            var policyData = new Dictionary<string, object> { ["InputMode"] = inputMode };
            if (includePolicyCheckpoint) policyData["Checkpoint"] = policyCheckpoint ?? DrosoSession.Hash(checkpoint);
            File.WriteAllText(policy, JsonSerializer.Serialize(policyData));
            var manifest = new Dictionary<string, object>
            {
                ["Version"] = 1, ["ModelName"] = name,
                ["CheckpointSha256"] = DrosoSession.Hash(checkpoint),
                ["MetadataSha256"] = DrosoSession.Hash(metadata),
                ["GraphSha256"] = DrosoSession.Hash(graph)
            };
            if (includePolicyHash) manifest["DecisionPolicySha256"] = DrosoSession.Hash(policy);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
            return directory;
        }
        async Task<Exception?> Error(string directory)
        {
            try { await DrosoRuntime.VerifyAssetsAsync(directory, CancellationToken.None); return null; }
            catch (Exception ex) { return ex; }
        }
        string complete = Fixture("complete");
        var before = Directory.EnumerateFiles(complete, "*", SearchOption.AllDirectories).ToDictionary(path => path, DrosoSession.Hash);
        string expectedCheckpoint = DrosoSession.Hash(Path.Combine(complete, "model", "best.pt"));
        check(await DrosoRuntime.VerifyAssetsAsync(complete, CancellationToken.None) == expectedCheckpoint,
            "Pacchetto con pesi, metadati, grafo e policy corrispondenti verificato");
        check(before.All(pair => DrosoSession.Hash(pair.Key) == pair.Value)
            && Directory.EnumerateFiles(complete, "*", SearchOption.AllDirectories).Count() == before.Count,
            "Verifica del pacchetto non modifica né aggiunge risorse");

        string tampered = Fixture("tampered-policy");
        string tamperedPolicy = Path.Combine(tampered, "model", "decision-visual-only.json");
        File.AppendAllText(tamperedPolicy, " ");
        check(await Error(tampered) is InvalidDataException tamperedError && tamperedError.Message.Contains(tamperedPolicy),
            "Policy alterata rifiutata anche quando il JSON resta valido");

        string missing = Fixture("missing-policy");
        string missingPolicy = Path.Combine(missing, "model", "decision-visual-only.json");
        File.Delete(missingPolicy);
        check(await Error(missing) is FileNotFoundException missingError && missingError.FileName == missingPolicy,
            "Policy dichiarata nel manifest ma assente rifiutata con il percorso del file");

        string mismatch = Fixture("mismatched-checkpoint", policyCheckpoint: "DIFFERENT-CHECKPOINT");
        check(await Error(mismatch) is InvalidDataException checkpointError && checkpointError.Message.Contains("checkpoint"),
            "Policy con hash valido ma checkpoint diverso rifiutata");

        string missingCheckpoint = Fixture("missing-policy-checkpoint", includePolicyCheckpoint: false);
        check(await Error(missingCheckpoint) is InvalidDataException,
            "Policy senza identità del checkpoint rifiutata");
        string wrongMode = Fixture("wrong-input-mode", inputMode: "ocr");
        check(await Error(wrongMode) is InvalidDataException modeError && modeError.Message.Contains("visual_only"),
            "Policy per un ingresso diverso da visual_only rifiutata");

        string missingHash = Fixture("missing-policy-hash", includePolicyHash: false);
        check(await Error(missingHash) is InvalidDataException manifestError && manifestError.Message.Contains("manifest"),
            "Modello con encoder visivo richiede la policy nel manifest");

        string legacy = Fixture("legacy-v3", learned: false, includePolicyHash: false);
        File.Delete(Path.Combine(legacy, "model", "decision-visual-only.json"));
        check(await DrosoRuntime.VerifyAssetsAsync(legacy, CancellationToken.None)
            == DrosoSession.Hash(Path.Combine(legacy, "model", "best.pt")),
            "Pacchetto V3 storico con soli tre asset conserva la compatibilità");

        string legacyDeclared = Fixture("legacy-declared-policy", learned: false);
        File.AppendAllText(Path.Combine(legacyDeclared, "model", "decision-visual-only.json"), " ");
        check(await Error(legacyDeclared) is InvalidDataException,
            "Una policy dichiarata nel manifest viene verificata anche nei pacchetti storici");

        string sessionDirectory = Path.Combine(folder, "blocked-inference");
        Directory.CreateDirectory(sessionDirectory);
        string pdf = Path.Combine(sessionDirectory, "synthetic.pdf");
        File.WriteAllText(pdf, "%PDF-1.4\nsynthetic\n%%EOF");
        var session = new DrosoSession
        {
            Directory = sessionDirectory,
            Documents = new() { new DrosoDocument { Path = pdf, Sha256 = DrosoSession.Hash(pdf) } }
        };
        Exception? inferenceError = null;
        try { await DrosoProcessClient.AnalyzeAsync(tampered, session, new Progress<string>(), CancellationToken.None); }
        catch (Exception ex) { inferenceError = ex; }
        check(inferenceError is InvalidDataException && inferenceError.Message.Contains(tamperedPolicy)
            && !Directory.EnumerateDirectories(sessionDirectory, "analysis-*").Any() && session.CheckpointSha256 == "",
            "Policy non valida blocca il client prima della scelta del runtime e dell'avvio dell'inferenza");
    }
}
