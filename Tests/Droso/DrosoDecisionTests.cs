using System.Text.Json;
using ProcedureNet7;

internal static class DrosoDecisionTests
{
    public static void Run(string folder, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        var document = new DrosoDocument { Path = Path.Combine(folder, "synthetic.pdf"), Sha256 = "SYNTHETIC", DeclaredLabel = "PERMESSO_SOGGIORNO" };
        var session = new DrosoSession { Directory = folder, Documents = new() { document } };
        var row = new DrosoBatchRow { Path = document.Path, Sha256 = document.Sha256, Predicted = "PASSAPORTO", Confidence = .99, Margin = .8, Pages = 20 };
        string Result(params DrosoBatchRow[] rows) => JsonSerializer.Serialize(new DrosoBatchResult { Version = 1, CheckpointSha256 = "MODEL", Rows = rows });
        void Apply() => session.ApplyResult(Result(row), "MODEL", session.Documents);

        check(!document.AnalysisCompleted && document.ProposedStatus == "" && document.StatusReason == "", "Prima dell'analisi non viene proposto alcuno stato");
        document.Predicted = "PASSAPORTO";
        document.Confidence = 1;
        document.CorrectedLabel = "RICEVUTA";
        check(document.ProposedStatus == "", "Punteggio e correzione locale non sostituiscono un'analisi completata");
        document.CorrectedLabel = "";

        foreach (string label in new[] { "PASSAPORTO", "RICEVUTA", "PERMESSO_SOGGIORNO" })
        foreach (var (confidence, status) in new[] { (.8999, "04"), (.9, "04"), (.900001, "02"), (1.0, "02") })
        {
            row.Predicted = label;
            row.Confidence = confidence;
            Apply();
            check(document.AnalysisCompleted && document.ProposedStatus == status,
                $"{label}, confidenza {confidence:R}: stato {status}, soglia strettamente maggiore di 0,90");
            check(document.StatusReason == (status == "04" ? "non riconosciuto" : ""), $"Motivazione coerente con stato {status}");
        }
        check(document.Pages == 20 && document.ProposedStatus == "02", "Venti pagine ammesse senza modificare la regola dell'esito");

        row.Predicted = "PASSAPORTO";
        row.Confidence = .900001;
        row.Margin = 0;
        row.NeedsReview = true;
        foreach (string declared in new[] { "", "PASSAPORTO", "PERMESSO_SOGGIORNO", "RICEVUTA" })
        {
            document.DeclaredLabel = declared;
            Apply();
            check(document.ProposedStatus == "02", "Sezione dichiarata, margine zero e flag di revisione non cambiano lo stato 02");
        }
        document.CorrectedLabel = "ERRATO";
        check(document.ProposedStatus == "02", "Correzione locale ERRATO non cambia la proposta riconosciuta");
        Apply();
        check(document.CorrectedLabel == "ERRATO" && document.ProposedStatus == "02", "Nuova analisi conserva la revisione locale separata dalla proposta");

        row.Predicted = "ERRATO";
        row.Confidence = .99;
        row.Margin = .9;
        row.NeedsReview = false;
        Apply();
        document.CorrectedLabel = "PASSAPORTO";
        check(document.ProposedStatus == "04" && document.StatusReason == "non riconosciuto", "ERRATO al 99% resta respinto anche dopo correzione locale");

        row.Predicted = "PASSAPORTO";
        row.Confidence = 1;
        row.Error = "Errore sintetico per file";
        Apply();
        check(document.AnalysisCompleted && document.ProposedStatus == "04", "Errore per file prevale sulla classe valida con punteggio 1");
        row.Predicted = "";
        row.Confidence = 0;
        row.Margin = null;
        row.Pages = 0;
        Apply();
        check(document.AnalysisCompleted && document.ProposedStatus == "04" && document.StatusReason == "non riconosciuto", "Risposta di errore senza punteggi o pagine conclude il file con stato 04");

        document.AppliedStatus = "02";
        document.StatusUpdateNote = "Storico sintetico";
        Apply();
        check(document.AppliedStatus == "02" && document.StatusUpdateNote == "Storico sintetico", "Analisi conserva lo storico della persistenza senza simulare aggiornamenti DB");
        session.Save();
        var loaded = DrosoSession.Load(Path.Combine(folder, "session.json")).Documents.Single();
        check(loaded.AnalysisCompleted && loaded.ProposedStatus == "04" && loaded.StatusReason == "non riconosciuto", "Salvataggio e caricamento conservano analisi completata ed esito");
        check(loaded.AppliedStatus == "02" && loaded.StatusUpdateNote == "Storico sintetico" && loaded.CorrectedLabel == "PASSAPORTO", "Sessione conserva stato applicato, nota e correzione locale distinti");

        string historicalPath = Path.Combine(folder, "historical-session.json");
        File.WriteAllText(historicalPath, JsonSerializer.Serialize(new
        {
            Version = 1,
            Documents = new[] { new { document.Path, document.Sha256, Predicted = "PASSAPORTO", Confidence = .99, Pages = 2 } }
        }));
        var historical = DrosoSession.Load(historicalPath).Documents.Single();
        check(!historical.AnalysisCompleted && historical.ProposedStatus == "", "Sessione storica senza indicatore di analisi non propone aggiornamenti retroattivi");

        var first = new DrosoDocument { Path = Path.Combine(folder, "first.pdf"), Sha256 = "FIRST" };
        var second = new DrosoDocument { Path = Path.Combine(folder, "second.pdf"), Sha256 = "SECOND" };
        var batch = new DrosoSession { Documents = new() { first, second } };
        var valid = new DrosoBatchRow { Path = first.Path, Sha256 = first.Sha256, Predicted = "PASSAPORTO", Confidence = 1, Margin = .9, Pages = 1 };
        var invalid = new DrosoBatchRow { Path = second.Path, Sha256 = "MISMATCH", Predicted = "RICEVUTA", Confidence = 1, Margin = .9, Pages = 1 };
        bool rejected = false;
        try { batch.ApplyResult(Result(valid, invalid), "MODEL", batch.Documents); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && batch.Documents.All(d => !d.AnalysisCompleted && d.ProposedStatus == "" && d.Predicted == ""), "Lotto non valido non completa né modifica alcun documento");
        check(batch.CheckpointSha256 == "", "Lotto non valido non aggiorna l'identità del modello");

        var missing = new DrosoDocument { Path = Path.Combine(folder, "missing.pdf"), Sha256 = "MISSING",
            AnalysisCompleted = true, Predicted = "PASSAPORTO", Confidence = .99 };
        bool unavailable = false;
        try
        {
            DrosoProcessClient.AnalyzeAsync("unused-assets", new DrosoSession { Documents = new() { missing } },
                new Progress<string>(), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException) { unavailable = true; }
        check(unavailable && !missing.AnalysisCompleted && missing.ProposedStatus == "",
            "PDF locale scomparso durante il riesame non viene respinto sul database");
    }
}
