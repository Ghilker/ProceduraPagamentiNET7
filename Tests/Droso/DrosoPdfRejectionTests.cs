using System.Text.Json;
using ProcedureNet7;

internal static class DrosoPdfRejectionTests
{
    public static async Task RunAsync(string folder, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        DrosoDocument FileDocument(string name, byte[] bytes)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllBytes(path, bytes);
            return new() { Path = path, Sha256 = DrosoSession.Hash(path), Bytes = bytes.Length };
        }
        DrosoSession Session(string name, params DrosoDocument[] documents)
        {
            string directory = Path.Combine(folder, name);
            Directory.CreateDirectory(directory);
            return new() { Directory = directory, Documents = documents.ToList() };
        }
        var progress = new InlineProgress();
        var empty = FileDocument("empty.pdf", Array.Empty<byte>());
        var invalid = FileDocument("not-a-pdf.pdf", "testo"u8.ToArray());
        invalid.Predicted = "PASSAPORTO"; invalid.Confidence = .99; invalid.Pages = 2;
        var rejected = Session("rejected", empty, invalid);
        await DrosoProcessClient.AnalyzeAsync(Path.Combine(folder, "assets-do-not-exist"), rejected, progress, CancellationToken.None);
        check(rejected.Documents.All(d => d.PdfUnreadable && d.AnalysisCompleted && d.ProposedStatus == "04"
            && d.StatusReason == "PDF non leggibile" && !d.NeedsReview && d.Predicted == "" && d.Confidence is null && d.Pages == 0),
            "PDF vuoti/non PDF rifiutati senza modello, senza punteggi residui o revisione");
        check(!Directory.GetDirectories(rejected.Directory, "analysis-*").Any() && rejected.CheckpointSha256 == "",
            "Lotto tutto illeggibile non avvia Python, Poppler, OCR o verifica del modello");
        var reloaded = DrosoSession.Load(Path.Combine(rejected.Directory, "session.json"));
        using (var locked = new FileStream(invalid.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            await DrosoProcessClient.AnalyzeAsync(Path.Combine(folder, "still-no-assets"), reloaded, progress, CancellationToken.None);
        check(reloaded.Documents.All(d => d.PdfUnreadable && d.ProposedStatus == "04"),
            "Sessione riaperta conserva il rifiuto: file gia' illeggibile non riletto ne' ritentato");
        var good = FileDocument("header-only.pdf", "%PDF-1.4 synthetic"u8.ToArray());
        var bad = FileDocument("mixed-invalid.pdf", "non PDF"u8.ToArray());
        check((await DrosoProcessClient.SelectReadableInputsAsync(Session("mixed", bad, good), CancellationToken.None)).SequenceEqual(new[] { good })
            && bad.PdfUnreadable && !good.PdfUnreadable,
            "Lotto misto: non PDF escluso, candidato PDF conservato per la verifica Poppler");
        var knownBad = FileDocument("bad-before-tampered.pdf", "not PDF"u8.ToArray());
        var tampered = FileDocument("tampered.pdf", "%PDF-1.4 first"u8.ToArray());
        File.WriteAllText(tampered.Path, "modified after import");
        var partial = Session("partial", knownBad, tampered);
        try
        {
            await DrosoProcessClient.AnalyzeAsync(Path.Combine(folder, "no-assets"), partial, progress, CancellationToken.None);
            check(false, "Identita' modificata non deve essere analizzata");
        }
        catch (InvalidDataException ex)
        {
            check(ex.Message.Contains("modificato") && !tampered.PdfUnreadable && !tampered.AnalysisCompleted,
                "Hash modificato e' errore di integrita', non un rifiuto PDF");
            check(DrosoSession.Load(Path.Combine(partial.Directory, "session.json")).Documents[0].PdfUnreadable,
                "Rifiuto confermato conservato anche se un altro allegato interrompe il lotto");
        }
        var missing = new DrosoDocument { Path = Path.Combine(folder, "missing.pdf") };
        check((await DrosoProcessClient.SelectReadableInputsAsync(Session("missing", missing), CancellationToken.None)).Length == 0
            && !missing.AnalysisCompleted && !missing.PdfUnreadable && missing.ProposedStatus == "",
            "File mancante non viene confuso con un PDF illeggibile");
        var notStarted = FileDocument("cancelled.pdf", "non PDF"u8.ToArray());
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { await DrosoProcessClient.SelectReadableInputsAsync(Session("cancelled", notStarted), cancel.Token); check(false, "Atteso annullamento"); }
            catch (OperationCanceledException) { check(!notStarted.PdfUnreadable, "Annullamento non inventa rifiuti PDF"); }
        }
        var rowDocument = FileDocument("corrupt-with-header.pdf", "%PDF-1.4 corrupt"u8.ToArray());
        var rows = Session("engine-result", rowDocument);
        rows.ApplyResult(JsonSerializer.Serialize(new DrosoBatchResult { Version = 1, CheckpointSha256 = "MODEL", Rows = new[] {
            new DrosoBatchRow { Path = rowDocument.Path, Sha256 = rowDocument.Sha256, PdfUnreadable = true,
                Error = "PDF non leggibile: struttura danneggiata", NeedsReview = false }
        } }), "MODEL", rows.Documents);
        check(rowDocument.PdfUnreadable && rowDocument.ProposedStatus == "04" && !rowDocument.NeedsReview,
            "Rifiuto Poppler diventa definitivo senza passare alla classificazione");
        var databaseDocument = new DrosoDocument { IdAllegato = 100, CodiceFiscale = "TESTCF0000000001", Coorte = "NEW",
            SelectedStatusId = 100, SubmittedAt = new DateTime(2026, 10, 1) };
        databaseDocument.RejectUnreadablePdf("PDF non leggibile: struttura danneggiata");
        var database = Session("database-outcome", databaseDocument);
        database.SourceDatabase = "SyntheticDatabase"; database.SourceServer = "SyntheticServer";
        int writes = 0;
        await DrosoStatusRepository.ApplyOutcomesAsync(database, (_, _) =>
        {
            writes++;
            return Task.FromResult(new DrosoStatusResult(true, new DateTime(2026, 10, 14), 101, "04"));
        }, progress, CancellationToken.None);
        check(writes == 1 && databaseDocument.AppliedStatus == "04" && databaseDocument.StatusUpdateNote.Contains("PDF non leggibile"),
            "Rifiuto di formato salva 04 senza modello caricato (writer simulato)");
        check(DrosoMessageRepository.UnavailableReason(database, databaseDocument) is null,
            "Rifiuto PDF salvato segue l'invio opzionale con guard di provenienza e versione");
        databaseDocument.PdfUnreadable = false;
        check(DrosoMessageRepository.UnavailableReason(database, databaseDocument) is not null,
            "Errore generico senza checkpoint non aggira i guard dei messaggi");
        var unclassified = new DrosoDocument { IdAllegato = 101, CodiceFiscale = "TESTCF0000000001", Coorte = "NEW",
            SelectedStatusId = 102, SubmittedAt = new DateTime(2026, 10, 1), Error = "runtime error", AnalysisCompleted = true };
        var unavailable = Session("runtime-error", unclassified);
        unavailable.SourceDatabase = database.SourceDatabase; unavailable.SourceServer = database.SourceServer;
        await DrosoStatusRepository.ApplyOutcomesAsync(unavailable, (_, _) => throw new Exception("Non deve scrivere"), progress, CancellationToken.None);
        check(unclassified.AppliedStatus == "" && unclassified.StatusUpdateNote.Contains("Modello"),
            "Guasto generico senza modello non diventa un rifiuto di formato");
    }
    public static async Task RunRealAsync(string assets, string folder, string validPdf, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        string damaged = Path.Combine(folder, "damaged.pdf");
        await File.WriteAllTextAsync(damaged, "%PDF-1.7\n1 0 obj\n<< /Type /Catalog");
        DrosoDocument Document(string path) => new() { Path = path, Bytes = new FileInfo(path).Length, Sha256 = DrosoSession.Hash(path) };
        var invalid = Document(damaged);
        var session = new DrosoSession { Directory = Path.Combine(folder, "unreadable"), Documents = new() { invalid } };
        await DrosoProcessClient.AnalyzeAsync(assets, session, new InlineProgress(), CancellationToken.None);
        check(invalid.PdfUnreadable && invalid.ProposedStatus == "04" && invalid.Pages == 0 && invalid.Predicted == "",
            "Lettore reale: PDF troncato con intestazione rifiutato direttamente");
        string[] analyses = Directory.GetDirectories(session.Directory, "analysis-*");
        string events = await File.ReadAllTextAsync(Path.Combine(analyses.Single(), "events.jsonl"));
        check(events.Contains("PDF rifiutato") && !events.Contains("Classificazione") && !events.Contains("GPU disponibile"),
            "Lettore reale: lotto tutto danneggiato non carica il modello o la GPU");
        await DrosoProcessClient.AnalyzeAsync(Path.Combine(folder, "no-assets"), session, new InlineProgress(), CancellationToken.None);
        check(Directory.GetDirectories(session.Directory, "analysis-*").SequenceEqual(analyses),
            "Rianalisi reale del rifiuto non avvia un nuovo processo");
        var good = Document(validPdf);
        var bad = Document(damaged);
        var mixed = new DrosoSession { Directory = Path.Combine(folder, "mixed"), Documents = new() { bad, good } };
        await DrosoProcessClient.AnalyzeAsync(assets, mixed, new InlineProgress(), CancellationToken.None);
        check(bad.PdfUnreadable && bad.ProposedStatus == "04" && good.AnalysisCompleted && !good.PdfUnreadable
            && good.Predicted.Length > 0 && good.Pages == 2,
            "Lotto reale misto: PDF danneggiato respinto, PDF valido classificato normalmente");
    }

    private sealed class InlineProgress : IProgress<string> { public void Report(string value) { } }
}
