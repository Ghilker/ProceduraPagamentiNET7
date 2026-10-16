using System.Reflection;
using ProcedureNet7;

internal static class DrosoFormTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(string folder, Action<bool, string> check)
    {
        var previousContext = SynchronizationContext.Current;
        using var uiContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(uiContext);
        try { RunWithUiContext(folder, check); }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void RunWithUiContext(string folder, Action<bool, string> check)
    {
        using var form = new FormProceduraDroso(null);
        var flag = typeof(FormProceduraDroso).GetField("saveToDatabase", Fields)?.GetValue(form) as CheckBox;
        check(flag is { Checked: false }, "Scrittura DB disattivata all'apertura: test locale predefinito");
        var messages = (CheckBox)typeof(FormProceduraDroso).GetField("sendRejectedMessages", Fields)!.GetValue(form)!;
        var maximum = (NumericUpDown)typeof(FormProceduraDroso).GetField("maximum", Fields)!.GetValue(form)!;
        var startDate = (DateTimePicker)typeof(FormProceduraDroso).GetField("startDate", Fields)!.GetValue(form)!;
        var endDate = (DateTimePicker)typeof(FormProceduraDroso).GetField("endDate", Fields)!.GetValue(form)!;
        check(!messages.Checked && !messages.Enabled && messages.Text.Contains("automaticamente"),
            "Invio automatico predefinito disattivato e disponibile soltanto con scrittura DB");
        check(maximum.Minimum == 0 && maximum.Value == 100, "Limite iniziale conservato e zero ammesso per tutti gli allegati");
        maximum.Value = 0;
        check(maximum.Value == 0, "Selezione senza limite rappresentabile nel controllo");
        maximum.Value = 100;
        check(startDate.ShowCheckBox && endDate.ShowCheckBox && !startDate.Checked && !endDate.Checked,
            "Intervallo date facoltativo e senza limiti all'apertura");
        flag!.Checked = true; messages.Checked = true;
        check(messages.Enabled && messages.Checked, "Scrittura DB abilita l'opzione di invio automatico");
        flag.Checked = false;
        check(!messages.Enabled && !messages.Checked, "Disattivare scrittura DB deseleziona e blocca l'invio");
        startDate.Value = new DateTime(2026, 10, 15); startDate.Checked = true;
        endDate.Value = new DateTime(2026, 10, 14); endDate.Checked = true;
        var initialSession = typeof(FormProceduraDroso).GetField("session", Fields)!.GetValue(form);
        try
        {
            ((Task)typeof(FormProceduraDroso).GetMethod("ExtractAndAnalyzeAsync", Fields)!.Invoke(form, new object[] { CancellationToken.None })!).GetAwaiter().GetResult();
            throw new Exception("Intervallo invertito accettato.");
        }
        catch (ArgumentException)
        {
            check(ReferenceEquals(initialSession, typeof(FormProceduraDroso).GetField("session", Fields)!.GetValue(form)),
                "Date invertite rifiutate prima di richiedere SQL o creare una sessione");
        }
        startDate.Checked = endDate.Checked = false;
        var accepted = new DrosoDocument { IdAllegato = 1, AnalysisCompleted = true, Predicted = "RICEVUTA", Confidence = .99 };
        var rejected = new DrosoDocument { IdAllegato = 2, AnalysisCompleted = true, Predicted = "PASSAPORTO", Confidence = .9 };
        var alreadyApplied = new DrosoDocument { IdAllegato = 3, AnalysisCompleted = true, Predicted = "RICEVUTA", Confidence = .99,
            AppliedStatus = "02", AppliedAt = new DateTime(2026, 10, 1), StatusUpdateNote = "Stato già salvato nel DB." };
        var pending = new DrosoDocument();
        var session = new DrosoSession { Directory = folder, SourceDatabase = "SyntheticDatabase", SourceServer = "SyntheticServer",
            CheckpointSha256 = "SYNTHETIC", Documents = new() { accepted, rejected, alreadyApplied, pending } };
        typeof(FormProceduraDroso).GetField("session", Fields)!.SetValue(form, session);
        SaveStatuses(form).GetAwaiter().GetResult();
        check(accepted.ProposedStatus == "02" && rejected.ProposedStatus == "04"
            && accepted.AppliedStatus == "" && rejected.AppliedStatus == "",
            "Test di allegati estratti dal DB: esiti 02/04 locali senza richiedere alcuna connessione SQL");
        var saved = DrosoSession.Load(Path.Combine(folder, "session.json"));
        check(saved.Documents[0].StatusUpdateNote.Contains("locale", StringComparison.OrdinalIgnoreCase)
            && saved.Documents[1].StatusReason == "non riconosciuto" && saved.Documents[1].AppliedStatus == "",
            "Test locale persiste gli esiti e il motivo senza dichiarare uno stato SQL salvato");
        check(alreadyApplied.AppliedStatus == "02" && alreadyApplied.AppliedAt == new DateTime(2026, 10, 1)
            && alreadyApplied.StatusUpdateNote == "Stato già salvato nel DB." && pending.StatusUpdateNote == "",
            "Test locale conserva lo storico SQL precedente e lascia intatti gli allegati non analizzati");
        session.ExportCsv(Path.Combine(folder, "results.csv"));
        check(File.ReadAllText(Path.Combine(folder, "results.csv")).Contains("non riconosciuto"),
            "CSV del test locale conserva la motivazione del respingimento");

        flag!.Checked = true;
        try
        {
            SaveStatuses(form).GetAwaiter().GetResult();
            throw new Exception("La modalità DB non ha richiesto la connessione.");
        }
        catch (InvalidOperationException ex)
        {
            check(ex.Message.Contains("database"), "Solo il flag attivato abilita il percorso di scrittura DB");
        }
        session.SourceDatabase = "";
        SaveStatuses(form).GetAwaiter().GetResult();
        check(accepted.AppliedStatus == "" && rejected.AppliedStatus == "",
            "PDF importati restano locali anche con il flag DB attivato");

        flag.Checked = false;
        var pendingWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(FormProceduraDroso).GetMethod("RunAsync", Fields)!.Invoke(form,
            new object?[] { (Func<CancellationToken, Task>)(_ => pendingWork.Task), null });
        check(!flag.Enabled, "Modalità locale/DB bloccata per l'intera lavorazione");
        pendingWork.SetResult();
        var finish = System.Diagnostics.Stopwatch.StartNew();
        while ((!flag.Enabled || ((Button)typeof(FormProceduraDroso).GetField("cancel", Fields)!.GetValue(form)!).Enabled)
            && finish.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(10); }
        check(flag.Enabled && !flag.Checked, "Flag riabilitato al termine senza attivare la scrittura DB");
        using var reopened = new FormProceduraDroso(null);
        var reopenedFlag = (CheckBox)typeof(FormProceduraDroso).GetField("saveToDatabase", Fields)!.GetValue(reopened)!;
        check(!reopenedFlag.Checked, "Nuova apertura sempre in test locale anche dopo avere abilitato il DB");
        CheckLayoutAndDetails(form, folder, check);
    }

    private static void CheckLayoutAndDetails(FormProceduraDroso form, string folder, Action<bool, string> check)
    {
        object Field(string name) => typeof(FormProceduraDroso).GetField(name, Fields)!.GetValue(form)!;
        void Invoke(string name, params object?[] arguments) => typeof(FormProceduraDroso).GetMethod(name, Fields)!.Invoke(form, arguments);
        Directory.CreateDirectory(folder);
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000); form.ClientSize = new Size(800, 350); form.Show();
        Application.DoEvents(); form.PerformLayout();
        var flag = (CheckBox)Field("saveToDatabase");
        var messages = (CheckBox)Field("sendRejectedMessages");
        var grid = (DataGridView)Field("grid");
        var filter = (ComboBox)Field("filter");
        var detailsButton = (Button)Field("expand");
        check(((Label)Field("modelLabel")).Text == "Prelavorazione documenti", "Titolo neutro senza versione del modello inventata");
        check(!Descendants(form).Contains(grid) && detailsButton.Text == "Documenti e dettagli…",
            "Schermata principale compatta e griglia trasferita nei dettagli");
        check(Descendants(form).Where(control => control.Visible && control is Label or Button or CheckBox or DateTimePicker or NumericUpDown)
            .All(control => FullyVisible(control, form)), "Controlli principali interamente visibili a 800×350");
        Save(form, Path.Combine(folder, "screen-main-local.png"));
        flag.Checked = true; messages.Checked = true; form.PerformLayout();
        check(FullyVisible(messages, form) && ((Label)Field("modeHint")).Text.Contains("automaticamente"),
            "Modalità DB rende visibile l'invio automatico al termine");
        Save(form, Path.Combine(folder, "screen-main-database-messages.png"));
        flag.Checked = false;
        var details = (Form)typeof(FormProceduraDroso).GetMethod("GetDetailsWindow", Fields)!.Invoke(form, null)!;
        details.StartPosition = FormStartPosition.Manual; details.Location = new Point(-30000, -30000);
        Invoke("ShowDetails"); Application.DoEvents(); details.PerformLayout();
        check(details.Visible && !details.Modal && grid.FindForm() == details && detailsButton.Enabled,
            "Dettagli non modali separati, con stessi controlli e pulsante disponibile");
        check(FullyVisible(grid, details) && FullyVisible(filter, details), "Griglia e filtri interamente visibili nei dettagli");
        var tools = (ContextMenuStrip)Field("toolsMenu");
        check(new[] { "Carica PDF locali…", "Analizza sessione", "Apri sessione…", "Esporta CSV…", "Elimina download", "Impostazioni…" }
            .All(text => tools.Items.Cast<ToolStripItem>().Any(item => item.Text == text)), "Azioni locali, sessione, pulizia e impostazioni conservate nei dettagli");
        check(((TextBox)Field("detailsInformation")).Text.Contains("Data presentazione = data dello stato 01")
            && ((TextBox)Field("detailsInformation")).Text.Contains("#DA SCRIVERE#"), "Dettagli spiegano la data di riferimento e il testo provvisorio dei messaggi");
        Save(details, Path.Combine(folder, "screen-details-documents.png"));
        var tabs = Descendants(details).OfType<TabControl>().Single();
        tabs.SelectedIndex = 1; Application.DoEvents(); Save(details, Path.Combine(folder, "screen-details-session.png"));
        tabs.SelectedIndex = 2; Application.DoEvents(); Save(details, Path.Combine(folder, "screen-details-log.png"));
        tabs.SelectedIndex = 0;
        filter.SelectedIndex = 2;
        details.Close();
        check(!details.Visible && !details.IsDisposed, "Chiudere i dettagli conserva i controlli e gli handler");
        Invoke("ShowDetails");
        check(ReferenceEquals(details, Field("detailsWindow")) && filter.SelectedIndex == 2 && grid.FindForm() == details,
            "Riapertura dei dettagli conserva finestra, filtro e griglia");

        var pendingWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke("RunAsync", (Func<CancellationToken, Task>)(_ => pendingWork.Task), null);
        var activeOperation = (CancellationTokenSource)Field("operation");
        check(!flag.Enabled && !messages.Enabled && !((DateTimePicker)Field("startDate")).Enabled
            && !((DateTimePicker)Field("endDate")).Enabled && !((NumericUpDown)Field("maximum")).Enabled,
            "Date, limite e opzioni DB/messaggi bloccati durante la lavorazione");
        check(((List<Button>)Field("actions")).All(button => !button.Enabled) && detailsButton.Enabled
            && filter.Enabled && grid.ReadOnly && ((Button)Field("cancel")).Enabled,
            "Azioni di modifica bloccate, dettagli consultabili e annullamento disponibile");
        Invoke("Report", "Avanzamento sintetico", 42);
        check(((RichTextBox)Field("detailsLog")).Text.Contains("Avanzamento sintetico") && ((ProgressBar)Field("progress")).Value == 42,
            "Avanzamento e registro nei dettagli continuano ad aggiornarsi durante il lavoro");
        details.Close();
        check(!details.IsDisposed && !activeOperation.IsCancellationRequested, "Chiudere i dettagli durante il lavoro non annulla l'operazione");
        Invoke("ShowDetails");
        form.Close();
        check(!form.IsDisposed && !details.IsDisposed && activeOperation.IsCancellationRequested,
            "Chiudere la schermata principale richiede annullamento senza perdere il pannello dettagli");
        pendingWork.SetResult();
        var finish = System.Diagnostics.Stopwatch.StartNew();
        bool Restored() => flag.Enabled && ((DateTimePicker)Field("startDate")).Enabled && ((DateTimePicker)Field("endDate")).Enabled
            && ((NumericUpDown)Field("maximum")).Enabled && !messages.Checked && !messages.Enabled
            && !((Button)Field("cancel")).Enabled && typeof(FormProceduraDroso).GetField("operation", Fields)!.GetValue(form) is null;
        while (!Restored() && finish.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(10); }
        check(Restored(),
            "Controlli ripristinati dopo il lavoro, invio ancora disattivato");
        Invoke("Report", "Completato. Sessione: " + folder, 100);
        check(!((Label)Field("status")).Text.Contains(folder) && ((Label)Field("detailsStatus")).Text.Contains(folder),
            "Percorso completo della sessione nei dettagli, stato principale sintetico");
        details.Hide(); form.Hide();
    }

    private static bool FullyVisible(Control control, Control root)
    {
        var bounds = root.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        return control.Width > 0 && control.Height > 0 && root.ClientRectangle.Contains(bounds);
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Save(Form form, string path)
    {
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path);
    }

    private static Task SaveStatuses(FormProceduraDroso form) =>
        (Task)typeof(FormProceduraDroso).GetMethod("SaveStatusesAsync", Fields)!.Invoke(form, new object[] { CancellationToken.None })!;
}
