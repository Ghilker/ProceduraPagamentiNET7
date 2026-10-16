using System.Data.SqlClient;
using System.Text;
using System.Text.Json;
using ProcedureNet7;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--asset-tests")
        {
            if (args.Length != 2) throw new ArgumentException("Uso: --asset-tests <cartella artefatti>.");
            Task.Run(() => DrosoAssetTests.RunAsync(Path.GetFullPath(args[1]), Check)).GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {checks} verifiche del pacchetto Droso, senza SQL o inferenza.");
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        using var logHost = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
        using var logText = new RichTextBox { Dock = DockStyle.Fill };
        using var progressBar = new ProgressBar();
        logHost.Controls.Add(logText);
        Logger.GetInstance(logHost, progressBar, logText);
        if (args.Length > 0 && args[0] == "--sql-status-tests")
        {
            Task.Run(() => DrosoSqlStatusTests.RunAsync(Check)).GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {checks} verifiche SQL su dati sintetici.");
            return;
        }
        logHost.Show();
        string folder = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "droso-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Task.Run(() => DownloadAndCleanupTests.RunAsync(Path.Combine(folder, "downloads"), Check)).GetAwaiter().GetResult();
        DrosoDecisionTests.Run(Path.Combine(folder, "decisions"), Check);
        DrosoExtractionTests.Run(Check);
        Task.Run(() => DrosoStatusTests.RunAsync(Path.Combine(folder, "status"), Check)).GetAwaiter().GetResult();
        Task.Run(() => DrosoMessageTests.RunAsync(Path.Combine(folder, "messages"), Check)).GetAwaiter().GetResult();
        Task.Run(() => DrosoPdfRejectionTests.RunAsync(Path.Combine(folder, "pdf-rejection"), Check)).GetAwaiter().GetResult();
        string pdf = Path.Combine(folder, "synthetic.pdf");
        Pdf(pdf, 2);
        var document = new DrosoDocument { Path = pdf, Sha256 = DrosoSession.Hash(pdf), DeclaredLabel = "PERMESSO_SOGGIORNO" };
        var session = new DrosoSession { Directory = folder, Documents = new() { document } };
        var row = new DrosoBatchRow { Path = pdf, Sha256 = document.Sha256, Predicted = "PASSAPORTO", Confidence = .99, Margin = .8, Pages = 1 };
        string Result(params DrosoBatchRow[] rows) => JsonSerializer.Serialize(new DrosoBatchResult { Version = 1, CheckpointSha256 = "MODEL", Rows = rows });
        session.ApplyResult(Result(row), "MODEL", session.Documents);
        Check(document.ProposedStatus == "02", "Categoria riconosciuta oltre il 90%, indipendentemente dalla sezione");
        row.Pages = 2;
        session.ApplyResult(Result(row), "MODEL", session.Documents);
        Check(document.ProposedStatus == "02", "PDF multipagina discordante segue la categoria riconosciuta");
        foreach (string label in new[] { "PASSAPORTO", "PERMESSO_SOGGIORNO", "RICEVUTA" })
        {
            document.DeclaredLabel = label; row.Predicted = label;
            row.Pages = label == "RICEVUTA" ? 4 : 2;
            session.ApplyResult(Result(row), "MODEL", session.Documents);
            Check(document.ProposedStatus == "02", "PDF completo riconosciuto: " + label);
        }
        document.DeclaredLabel = "PERMESSO_SOGGIORNO"; row.Predicted = "PASSAPORTO";
        row.NeedsReview = true;
        session.ApplyResult(Result(row), "MODEL", session.Documents);
        Check(document.ProposedStatus == "02", "Flag di revisione del motore non modifica la regola sul punteggio");
        row.NeedsReview = false; row.Margin = .1;
        session.ApplyResult(Result(row), "MODEL", session.Documents);
        Check(document.ProposedStatus == "02", "Margine basso non modifica lo stato proposto");
        row.Margin = .8; row.Confidence = .7;
        session.ApplyResult(Result(row), "MODEL", session.Documents);
        Check(document.ProposedStatus == "04" && document.StatusReason == "non riconosciuto", "Punteggio basso: respinto non riconosciuto");
        row.Predicted = "ERRATO"; row.Confidence = .99;
        session.ApplyResult(Result(row), "MODEL", session.Documents);
        Check(document.ProposedStatus == "04", "ERRATO anche ad alta confidenza: respinto");
        row.Sha256 = "OTHER";
        Reject(() => session.ApplyResult(Result(row), "MODEL", session.Documents), "Hash errato");
        Check(document.Predicted == "ERRATO", "Risposta scartata senza modifiche parziali");
        row.Sha256 = document.Sha256;
        Reject(() => session.ApplyResult(Result(row), "OTHER", session.Documents), "Checkpoint diverso");
        Reject(() => session.ApplyResult(Result(), "MODEL", session.Documents), "Risposta incompleta");
        row.Confidence = 1.01;
        Reject(() => session.ApplyResult(Result(row), "MODEL", session.Documents), "Punteggio fuori intervallo");
        row.Confidence = .99; row.Predicted = "01";
        Reject(() => session.ApplyResult(Result(row), "MODEL", session.Documents), "Classe non canonica");
        row.Predicted = "PASSAPORTO";
        var second = new DrosoDocument { Path = Path.Combine(folder, "second.pdf"), Sha256 = "SECOND" };
        session.Documents.Add(second);
        Reject(() => session.ApplyResult(Result(row, row), "MODEL", session.Documents), "Documento duplicato nella risposta");
        Check(second.Predicted == "" && !second.AnalysisCompleted && document.Predicted == "ERRATO", "Validazione atomica del lotto");
        session.Documents.Remove(second);
        document.CorrectedLabel = "PERMESSO_SOGGIORNO";
        session.Save();
        Check(DrosoSession.Load(Path.Combine(folder, "session.json")).Documents[0].CorrectedLabel == "PERMESSO_SOGGIORNO", "Persistenza revisione locale");
        document.CodiceFiscale = "=SYNTHETIC";
        session.ExportCsv(Path.Combine(folder, "results.csv"));
        Check(File.ReadAllText(Path.Combine(folder, "results.csv")).Contains("'="), "CSV neutralizza formule");

        if (args.Length == 2)
            Task.Run(() => DrosoPdfRejectionTests.RunRealAsync(Path.GetFullPath(args[1]), Path.Combine(folder, "real-pdf-rejection"), pdf, Check))
                .GetAwaiter().GetResult();

        if (args.Length == 2)
            Task.Run(async () =>
            {
                string integration = Path.Combine(folder, "integration");
                Directory.CreateDirectory(integration);
                string duplicate = Path.Combine(integration, "duplicate.pdf"), tooLong = Path.Combine(integration, "21-pages.pdf");
                File.Copy(pdf, duplicate, true);
                Pdf(tooLong, 21);
                var real = new DrosoSession { Directory = integration, Documents = new[] { pdf, duplicate, tooLong }
                    .Select(p => new DrosoDocument { Path = p, Sha256 = DrosoSession.Hash(p), DeclaredLabel = "PERMESSO_SOGGIORNO" }).ToList() };
                var assetsBefore = Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories)
                    .ToDictionary(p => p, File.GetLastWriteTimeUtc);
                FileStream? heldProgress = null;
                try
                {
                    await DrosoProcessClient.AnalyzeAsync(args[1], real, new CallbackProgress(message =>
                    {
                        Console.WriteLine(message);
                        if (heldProgress == null && !message.StartsWith("Verifica del modello"))
                        {
                            string run = Directory.GetDirectories(integration, "analysis-*").OrderByDescending(Directory.GetCreationTimeUtc).First();
                            string progressFile = Path.Combine(run, "progress.json");
                            File.WriteAllText(progressFile, "held-open-by-reader");
                            heldProgress = new FileStream(progressFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                        }
                    }), CancellationToken.None);
                    Check(heldProgress != null, "Analisi riuscita anche con progress.json bloccato in lettura: regressione accesso negato");
                }
                finally { heldProgress?.Dispose(); }
                var assetsAfter = Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).ToDictionary(p => p, File.GetLastWriteTimeUtc);
                Check(assetsBefore.Count == assetsAfter.Count && assetsBefore.All(p => assetsAfter.TryGetValue(p.Key, out var value) && value == p.Value), "Nessuna scrittura nel modello o nelle risorse della solution");
                Check(real.Documents.Take(2).All(d => d.Predicted.Length > 0 && d.Pages == 2), "Motore reale: entrambi i PDF duplicati associati correttamente");
                Check(real.Documents.Take(2).All(d => d.AnalysisCompleted && d.ProposedStatus ==
                    (d.Predicted is "PASSAPORTO" or "RICEVUTA" or "PERMESSO_SOGGIORNO" && d.Confidence > .90 ? "02" : "04")),
                    "Motore reale: stato multipagina determinato da categoria e confidenza oltre il 90%");
                Check(real.Documents[2].Error.Length > 0 && real.Documents[2].AnalysisCompleted && real.Documents[2].ProposedStatus == "04", "PDF oltre limite mantiene errore ed esito respinto nel lotto");
                Check(real.CheckpointSha256 == DrosoSession.Hash(Path.Combine(args[1], "model", "best.pt")), "Identità modello reale verificata");
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                try { await DrosoProcessClient.AnalyzeAsync(args[1], real, new Progress<string>(), cancellation.Token); throw new Exception("Annullamento non rispettato"); }
                catch (OperationCanceledException) { Check(true, "Annullamento prima dell'avvio"); }
                using var duringRun = new CancellationTokenSource();
                var cancelled = new DrosoSession { Directory = Path.Combine(folder, "cancelled"), Documents = new() { new DrosoDocument { Path = pdf, Sha256 = DrosoSession.Hash(pdf) } } };
                try
                {
                    await DrosoProcessClient.AnalyzeAsync(args[1], cancelled,
                        new CallbackProgress(message => { if (!message.StartsWith("Verifica del modello")) duringRun.Cancel(); }), duringRun.Token);
                    throw new Exception("Il processo non è stato annullato.");
                }
                catch (OperationCanceledException)
                {
                    Check(Directory.EnumerateFiles(cancelled.Directory, "cancel.request", SearchOption.AllDirectories).Any(), "Annullamento inviato al processo avviato");
                    Check(cancelled.Documents[0].Predicted == "" && !cancelled.Documents[0].AnalysisCompleted && cancelled.Documents[0].ProposedStatus == "", "Lotto annullato senza applicare risultati o stati");
                }
            }).GetAwaiter().GetResult();

        DrosoFormTests.Run(Path.Combine(folder, "form-modes"), Check);
        using var form = new FormProceduraDroso(null);
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var confident = new DrosoDocument { Path = "ricevuta-test.pdf", CodiceFiscale = "TEST000000000001", AnalysisCompleted = true, Predicted = "RICEVUTA", Confidence = .99, DeclaredLabel = "PASSAPORTO", Pages = 4, NeedsReview = true };
        var uncertain = new DrosoDocument { Path = "passaporto-test.pdf", CodiceFiscale = "TEST000000000002", AnalysisCompleted = true, Predicted = "PASSAPORTO", Confidence = .9, Pages = 2, NeedsReview = false };
        var failed = new DrosoDocument { Path = "errore-test.pdf", AnalysisCompleted = true, Pages = 2, NeedsReview = false, Error = "synthetic error" };
        var pending = new DrosoDocument { Path = "da-analizzare.pdf", NeedsReview = true };
        typeof(FormProceduraDroso).GetField("session", fields)!.SetValue(form,
            new DrosoSession { Directory = Path.Combine(folder, "ui-preview"), Documents = new() { confident, uncertain, failed, pending } });
        var filter = (ComboBox)typeof(FormProceduraDroso).GetField("filter", fields)!.GetValue(form)!;
        var grid = (DataGridView)typeof(FormProceduraDroso).GetField("grid", fields)!.GetValue(form)!;
        filter.SelectedIndex = 2;
        var visible = ((IEnumerable<DrosoDocument>)grid.DataSource).ToArray();
        Check(visible.SequenceEqual(new[] { uncertain, failed }) && visible.All(d => d.ProposedStatus == "04"), "Filtro Respinti include solo stati 04 dopo analisi, anche con flag di revisione opposto");
        filter.SelectedIndex = 0;
        if (args.Length == 2)
        {
            typeof(FormProceduraDroso).GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(form, DrosoSession.Load(Path.Combine(folder, "integration", "session.json")));
            typeof(FormProceduraDroso).GetMethod("RefreshRows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, null);
        }
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        var details = (Form)typeof(FormProceduraDroso).GetMethod("GetDetailsWindow", fields)!.Invoke(form, null)!;
        details.StartPosition = FormStartPosition.Manual;
        details.Location = new Point(-30000, -30000);
        typeof(FormProceduraDroso).GetMethod("ShowDetails", fields)!.Invoke(form, null);
        Application.DoEvents();
        var toolsButton = FindButton(details, "Altre azioni ▾");
        var toolsMenu = (ContextMenuStrip)typeof(FormProceduraDroso).GetField("toolsMenu", fields)!.GetValue(form)!;
        var writeFlag = (CheckBox)typeof(FormProceduraDroso).GetField("saveToDatabase", fields)!.GetValue(form)!;
        var cancelButton = (Button)typeof(FormProceduraDroso).GetField("cancel", fields)!.GetValue(form)!;
        Check(toolsButton != null && toolsButton.Visible && toolsMenu.Items.Cast<ToolStripItem>().Any(i => i.Text == "Elimina download"),
            "Pulizia download disponibile in Altre azioni");
        var pendingWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(FormProceduraDroso).GetMethod("RunAsync", fields)!.Invoke(form,
            new object?[] { (Func<CancellationToken, Task>)(_ => pendingWork.Task), null });
        Check(!toolsButton!.Enabled, "Pulizia disabilitata durante download o analisi");
        pendingWork.SetResult();
        var finish = System.Diagnostics.Stopwatch.StartNew();
        while ((!toolsButton.Enabled || !writeFlag.Enabled || cancelButton.Enabled) && finish.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(10); }
        Check(toolsButton.Enabled && writeFlag.Enabled && !cancelButton.Enabled, "Pulizia e modalità DB riabilitate al termine della lavorazione");
        if (args.Length != 2)
        {
            ((Task)typeof(FormProceduraDroso).GetMethod("SaveStatusesAsync", fields)!.Invoke(form, new object[] { CancellationToken.None })!).GetAwaiter().GetResult();
            typeof(FormProceduraDroso).GetMethod("RefreshRows", fields)!.Invoke(form, null);
        }
        using var bitmap = new Bitmap(800, 350);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, 800, 350));
        bitmap.Save(Path.Combine(folder, "screen.png"));
        writeFlag.Checked = true;
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, 800, 350));
        bitmap.Save(Path.Combine(folder, "screen-database.png"));
        form.ClientSize = new Size(1280, 720);
        form.PerformLayout();
        using (var expanded = new Bitmap(1280, 720))
        {
            form.DrawToBitmap(expanded, new Rectangle(0, 0, 1280, 720));
            expanded.Save(Path.Combine(folder, "screen-expanded.png"));
        }
        writeFlag.Checked = false;
        form.ClientSize = new Size(800, 350);
        Exception denied;
        try { throw new UnauthorizedAccessException("ACCESS_DENIED_TEST", new IOException("INNER_ERROR_TEST")); }
        catch (UnauthorizedAccessException ex) { denied = ex; }
        typeof(FormProceduraDroso).GetMethod("ShowError", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, new object[] { denied });
        Application.DoEvents();
        Check(logText.Text.Contains("System.UnauthorizedAccessException") && logText.Text.Contains("INNER_ERROR_TEST") && logText.Text.Contains("Program.Main"), "Logger integrato riceve eccezione completa, causa interna e stack trace");
        typeof(FormProceduraDroso).GetMethod("Report", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, new object?[] { "PROGRESS_TEST", 42 });
        Application.DoEvents();
        Check(logText.Text.Contains("PROGRESS_TEST") && progressBar.Value == 42, "Avanzamento nel Logger e nella barra integrata");
        form.Hide();
        logHost.Hide();
        Check(form.Controls.Count == 1, "Schermata creata a 800×350");
        Console.WriteLine($"PASS: {checks} verifiche. Artefatti: {folder}");
    }
    private static void Check(bool ok, string description)
    {
        if (!ok) throw new Exception("FAIL: " + description);
        checks++; Console.WriteLine("PASS: " + description);
    }
    private static Button? FindButton(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button && button.Text == text) return button;
            if (FindButton(child, text) is { } found) return found;
        }
        return null;
    }
    private sealed class CallbackProgress : IProgress<string>
    {
        private readonly Action<string> action;
        public CallbackProgress(Action<string> action) => this.action = action;
        public void Report(string value) => action(value);
    }
    private static void Reject(Action action, string description)
    {
        try { action(); } catch (InvalidDataException) { Check(true, description); return; }
        throw new Exception("FAIL: accettato " + description);
    }
    private static void Pdf(string path, int pages)
    {
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, pages).Select(i => (3 + i) + " 0 R")) + $"] /Count {pages} >>" };
        int content = 3 + pages, font = content + 1;
        for (int i = 0; i < pages; i++) objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents {content} 0 R /Resources << /Font << /F1 {font} 0 R >> >> >>");
        const string stream = "BT /F1 20 Tf 40 760 Td (SYNTHETIC TEST - NO PERSONAL DATA) Tj ET\n0.8 g 40 400 400 180 re f\n";
        objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}endstream");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets) pdf.Append(offset.ToString("D10") + " 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
}

// The UI is exercised without opening the application's authentication/database screen.
namespace ProcedureNet7
{
    public sealed class MasterForm : Form
    {
        public SqlConnection? MainConnection => null;
        public void SetDrosoBusy(bool busy) { }
    }
}
