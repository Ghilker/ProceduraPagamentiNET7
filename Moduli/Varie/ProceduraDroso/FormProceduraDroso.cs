using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace ProcedureNet7
{
    public sealed class FormProceduraDroso : Form
    {
        private readonly MasterForm? master;
        private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        private readonly TableLayoutPanel detailsLayout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(223, 230, 239),
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, EnableHeadersVisualStyles = false };
        private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label modelLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label modeHint = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
        private readonly Label summary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly CheckBox saveToDatabase = new() { Text = "Scrivi stati nel DB", AutoSize = true, Checked = false, Margin = new Padding(0, 4, 0, 0) };
        private readonly CheckBox sendRejectedMessages = new() { Text = "Invia automaticamente un messaggio ai respinti", AutoSize = true, Checked = false, Enabled = false, Margin = new Padding(0, 4, 0, 0) };
        private readonly DateTimePicker startDate = new() { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false, Width = 146 };
        private readonly DateTimePicker endDate = new() { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false, Width = 146 };
        private readonly NumericUpDown maximum = new() { Minimum = 0, Maximum = 10000, Value = 100, Width = 84 };
        private readonly ComboBox filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        private readonly TextBox search = new() { Width = 150, PlaceholderText = "Cerca CF, ID o file" };
        private readonly List<Button> actions = new();
        private readonly ContextMenuStrip toolsMenu = new();
        private readonly Button cancel;
        private readonly Button expand;
        private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 10 };
        private readonly TextBox detailsInformation = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
        private readonly RichTextBox detailsLog = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White };
        private readonly Label detailsStatus = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolTip help = new();
        private Form? detailsWindow;
        private int summaryCount = -1;
        private CancellationTokenSource? operation;
        private DrosoSession session = new();
        private DrosoSettings settings;
        private static readonly string[] Corrections = { "", "PASSAPORTO", "RICEVUTA", "PERMESSO_SOGGIORNO", "ERRATO", "DA_VERIFICARE" };

        public FormProceduraDroso(MasterForm? masterForm)
        {
            master = masterForm;
            settings = DrosoSettings.Load();
            Text = "Prelavorazione documenti con Droso";
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(800, 350);
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(247, 249, 252);
            Controls.Add(layout);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modelLabel.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
            modelLabel.ForeColor = Color.FromArgb(30, 55, 85);
            top.Controls.Add(modelLabel, 0, 0);
            expand = Button("Documenti e dettagli…", ShowDetails, false);
            expand.Margin = new Padding(8, 7, 0, 7);
            top.Controls.Add(expand, 1, 0);
            layout.Controls.Add(top, 0, 0);

            var selection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty };
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            selection.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            selection.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            selection.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            selection.Controls.Add(new Label { Text = "Data presentazione · dal", AutoSize = true }, 0, 0);
            selection.Controls.Add(new Label { Text = "al (giorno compreso)", AutoSize = true }, 1, 0);
            selection.Controls.Add(new Label { Text = "Massimo allegati", AutoSize = true }, 2, 0);
            selection.Controls.Add(startDate, 0, 1);
            selection.Controls.Add(endDate, 1, 1);
            selection.Controls.Add(maximum, 2, 1);
            var dateHelp = new Label { Text = "Date facoltative: spunta la casella per applicare il filtro.", AutoSize = true, ForeColor = Color.FromArgb(80, 95, 115) };
            selection.Controls.Add(dateHelp, 0, 2); selection.SetColumnSpan(dateHelp, 2);
            selection.Controls.Add(new Label { Text = "0 = tutti gli allegati", AutoSize = true, ForeColor = Color.FromArgb(80, 95, 115) }, 2, 2);
            help.SetToolTip(startDate, "Data dello stato 01 dell’allegato. Senza spunta non si applica un limite iniziale.");
            help.SetToolTip(endDate, "Data dello stato 01 dell’allegato. Il giorno finale è incluso; senza spunta non si applica un limite finale.");
            help.SetToolTip(maximum, "0 seleziona tutti gli allegati idonei; un numero positivo limita la lavorazione.");
            layout.Controls.Add(selection, 0, 1);

            var commands = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var buttons = Bar();
            buttons.AutoScroll = false;
            var start = Button("Avvia", () => RunAsync(ExtractAndAnalyzeAsync));
            start.Width = 90; start.AutoSize = false;
            start.BackColor = Color.FromArgb(32, 94, 169); start.ForeColor = Color.White;
            start.FlatStyle = FlatStyle.Flat; start.FlatAppearance.BorderSize = 0;
            buttons.Controls.Add(start);
            cancel = new Button { Text = "Annulla", Width = 90, Height = 28, Enabled = false };
            cancel.Click += (_, _) => operation?.Cancel();
            buttons.Controls.Add(cancel);
            commands.Controls.Add(buttons, 0, 0);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            saveToDatabase.CheckedChanged += (_, _) =>
            {
                if (!saveToDatabase.Checked) sendRejectedMessages.Checked = false;
                UpdateWriteMode();
                Report(saveToDatabase.Checked
                    ? "Scrittura DB attivata: gli stati verranno salvati al termine dell’analisi."
                    : "Test locale: risultati nella sessione; nessuno stato o messaggio scritto nel DB.");
            };
            sendRejectedMessages.CheckedChanged += (_, _) =>
            {
                if (!saveToDatabase.Checked && sendRejectedMessages.Checked) { sendRejectedMessages.Checked = false; return; }
                UpdateWriteMode();
            };
            options.Controls.Add(saveToDatabase); options.Controls.Add(sendRejectedMessages);
            help.SetToolTip(sendRejectedMessages, "Invio automatico al termine, solo per gli stati 04 salvati in questa lavorazione. Testo provvisorio: #DA SCRIVERE#.");
            commands.Controls.Add(options, 1, 0);
            layout.Controls.Add(commands, 0, 2);
            modeHint.Padding = new Padding(8, 3, 8, 3);
            layout.Controls.Add(modeHint, 0, 3);
            var outcome = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            outcome.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
            outcome.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            outcome.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outcome.Controls.Add(progress, 0, 0); outcome.Controls.Add(summary, 0, 1);
            status.ForeColor = Color.FromArgb(60, 75, 95); outcome.Controls.Add(status, 0, 2);
            layout.Controls.Add(outcome, 0, 4);
            BuildDetailsPanel();
            UpdateModel(); UpdateWriteMode(); RefreshRows();
            Report("Pronto. Imposta i filtri e premi Avvia; i documenti e le altre azioni sono nei dettagli.");
        }

        private void BuildDetailsPanel()
        {
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            var tools = Bar();
            var actionsButton = Button("Altre azioni ▾", () => toolsMenu.Show(tools, new Point(0, tools.Height)));
            tools.Controls.Add(actionsButton);
            tools.Controls.Add(new Label { Text = "Sessioni, PDF locali, esportazione, pulizia e impostazioni", AutoSize = true, Margin = new Padding(10, 7, 0, 0) });
            MenuAction("Carica PDF locali…", Import);
            MenuAction("Analizza sessione", () => RunAsync(AnalyzeAsync));
            toolsMenu.Items.Add(new ToolStripSeparator());
            MenuAction("Apri sessione…", OpenSession); MenuAction("Esporta CSV…", Export); MenuAction("Elimina download", DeleteDownloads);
            toolsMenu.Items.Add(new ToolStripSeparator()); MenuAction("Impostazioni…", Configure);
            detailsLayout.Controls.Add(tools, 0, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var documents = new TabPage("Documenti e revisione") { BackColor = BackColor, Padding = new Padding(8) };
            var documentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            documentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            documentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            documentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            documentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            var filters = Bar();
            filter.Items.AddRange(new object[] { "Tutti", "In lavorazione (02)", "Respinti (04)", "Revisionati" }); filter.SelectedIndex = 0;
            filter.SelectedIndexChanged += (_, _) => RefreshRows(); search.TextChanged += (_, _) => RefreshRows();
            filters.Controls.Add(filter); filters.Controls.Add(search);
            documentLayout.Controls.Add(filters, 0, 0);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(231, 237, 245);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(30, 55, 85);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(3); grid.DefaultCellStyle.Padding = new Padding(3);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 252);
            Column("FileName", "Documento", 180); Column("CodiceFiscale", "Codice fiscale", 140);
            Column("CategoriaProposta", "Riconosciuto come", 160); Column("Confidence", "Confidenza", 90).DefaultCellStyle.Format = "P1";
            var outcome = Column("Esito", "Esito", 230); outcome.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; outcome.MinimumWidth = 200;
            outcome.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].DataPropertyName == "Esito"
                    && grid.Rows[e.RowIndex].DataBoundItem is DrosoDocument document && e.CellStyle is { } style)
                    style.ForeColor = document.ProposedStatus == "02" ? Color.FromArgb(25, 110, 65)
                        : document.ProposedStatus == "04" ? Color.FromArgb(170, 55, 45) : Color.DimGray;
            };
            grid.CellDoubleClick += (_, e) => { if (operation == null && e.RowIndex >= 0) { try { OpenPdf(); } catch (Exception ex) { ShowError(ex); } } };
            documentLayout.Controls.Add(grid, 0, 1);
            var review = Bar(); review.Controls.Add(Button("Apri PDF", OpenPdf)); review.Controls.Add(Button("Dettagli / revisione…", ReviewDocument));
            documentLayout.Controls.Add(review, 0, 2);
            documentLayout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Passaporto, ricevuta o permesso oltre il 90% → 02; altri esiti → 04. La revisione resta locale." }, 0, 3);
            documents.Controls.Add(documentLayout); tabs.TabPages.Add(documents);
            var information = new TabPage("Sessione e modello") { Padding = new Padding(8) }; information.Controls.Add(detailsInformation); tabs.TabPages.Add(information);
            var log = new TabPage("Registro") { Padding = new Padding(8) }; log.Controls.Add(detailsLog); tabs.TabPages.Add(log);
            detailsLayout.Controls.Add(tabs, 0, 1); detailsLayout.Controls.Add(detailsStatus, 0, 2);
        }
        private static FlowLayoutPanel Bar() => new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Margin = Padding.Empty };
        private Button Button(string text, Action action, bool lockDuringWork = true)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 28 };
            button.Click += (_, _) => { try { Logger.LogInfo(null, "[Droso] " + text); action(); } catch (Exception ex) { ShowError(ex); } };
            if (lockDuringWork) actions.Add(button);
            return button;
        }
        private void MenuAction(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) => { try { Logger.LogInfo(null, "[Droso] " + text); action(); } catch (Exception ex) { ShowError(ex); } };
            toolsMenu.Items.Add(item);
        }
        private DataGridViewColumn Column(string property, string title, int width)
        {
            var column = new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
            grid.Columns.Add(column);
            return column;
        }
        private DrosoDocument? Selected => grid.CurrentRow?.DataBoundItem as DrosoDocument;
        private void UpdateModel()
        {
            modelLabel.Text = "Prelavorazione documenti";
            UpdateDetailsInformation();
        }
        private void UpdateWriteMode()
        {
            sendRejectedMessages.Enabled = operation == null && saveToDatabase.Checked;
            modeHint.Text = saveToDatabase.Checked
                ? sendRejectedMessages.Checked
                    ? "DATABASE · Stati 02/04 salvati e messaggio inviato automaticamente ai respinti al termine della lavorazione."
                    : "DATABASE · Al termine si salvano gli stati 02/04. Invio messaggi disattivato; puoi abilitarlo qui sopra."
                : "TEST LOCALE · Risultati nella sessione. Per scrivere stati e inviare messaggi occorre attivare la scrittura DB.";
            modeHint.BackColor = saveToDatabase.Checked ? Color.FromArgb(255, 241, 209) : Color.FromArgb(225, 237, 250);
            modeHint.ForeColor = saveToDatabase.Checked ? Color.FromArgb(115, 75, 15) : Color.FromArgb(30, 75, 125);
            UpdateDetailsInformation();
        }
        private void UpdateDetailsInformation()
        {
            detailsInformation.Text = string.Join(Environment.NewLine, new[] {
                "Sessione: " + (session.Directory.Length == 0 ? "nessuna sessione aperta" : session.Directory),
                "Allegati: " + session.Documents.Count,
                "Database di origine: " + (session.SourceDatabase.Length == 0 ? "PDF locali / nessuno" : session.SourceDatabase),
                "Modello incluso in ProcedureNet7: " + DrosoRuntime.ModelDirectory,
                "Checkpoint della sessione: " + (session.CheckpointSha256.Length == 0 ? "verificato all'avvio dell'analisi" : session.CheckpointSha256),
                "", "DATE E LIMITI", "Data presentazione = data dello stato 01 dell’allegato. Le date sono facoltative; il giorno finale è incluso.",
                "Massimo allegati: 0 = tutti gli allegati idonei, un numero positivo limita la selezione.",
                "", "STATI E MESSAGGI",
                "Passaporto, ricevuta o permesso con punteggio strettamente maggiore di 90%: stato 02; altri esiti: 04, non riconosciuto.",
                "La revisione modifica soltanto la categoria locale nella sessione.",
                "I PDF importati restano locali: non scrivono stati e non inviano messaggi.",
                "Invio automatico opzionale al termine, dopo il salvataggio degli stati nel DB, soltanto ai respinti idonei.",
                "Testo provvisorio del messaggio: " + DrosoMessageRepository.MessageText });
        }
        private void Report(string message, int? percent = null)
        {
            Logger.LogInfo(percent, "[Droso] " + message);
            if (IsDisposed) return;
            int sessionMarker = message.IndexOf("Sessione:", StringComparison.OrdinalIgnoreCase);
            status.Text = message.StartsWith("Sessione aperta:", StringComparison.OrdinalIgnoreCase)
                ? "Sessione aperta. Documenti e percorso disponibili nei dettagli."
                : sessionMarker >= 0 ? message[..sessionMarker].Trim() : message;
            detailsStatus.Text = message;
            detailsLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + " · " + message + Environment.NewLine);
            if (percent.HasValue) progress.Value = Math.Clamp(percent.Value, 0, 100);
            if (summaryCount != session.Documents.Count) UpdateSummary();
            grid.Invalidate();
        }
        private void ShowError(Exception ex)
        {
            Logger.LogError(null, $"[Droso] Operazione non completata. Sessione: {session.Directory}\n{ex}");
            if (IsDisposed) return;
            status.Text = "Errore: " + ex.Message + " — dettagli completi nel log di ProcedureNet7.";
            detailsStatus.Text = status.Text;
            detailsLog.AppendText(ex + Environment.NewLine);
        }
        private async void RunAsync(Func<CancellationToken, Task> action, string? completedMessage = null)
        {
            if (operation != null) return;
            bool locked = false;
            using var source = new CancellationTokenSource();
            operation = source;
            try
            {
                master?.SetDrosoBusy(true);
                locked = true;
                foreach (var button in actions) button.Enabled = false;
                foreach (ToolStripItem item in toolsMenu.Items) item.Enabled = false;
                maximum.Enabled = startDate.Enabled = endDate.Enabled = saveToDatabase.Enabled = sendRejectedMessages.Enabled = false;
                cancel.Enabled = true;
                Report(saveToDatabase.Checked ? "Lavorazione avviata con scrittura degli stati nel DB." : "Test locale avviato: nessuno stato scritto nel DB.", 0);
                await action(source.Token);
                Report(completedMessage ?? $"Completato ({(saveToDatabase.Checked && session.SourceDatabase.Length > 0 ? "modalità DB" : "test locale")}): "
                    + $"{session.Documents.Count} allegati, {session.Documents.Count(d => d.Error.Length > 0)} con errori. Sessione: {session.Directory}", 100);
            }
            catch (OperationCanceledException)
            {
                const string message = "Operazione annullata. La sessione e i risultati restano disponibili.";
                Logger.LogWarning(null, "[Droso] " + message);
                if (!IsDisposed) status.Text = message;
            }
            catch (Exception ex) { ShowError(ex); }
            finally
            {
                operation = null;
                if (locked && master is { IsDisposed: false }) master.SetDrosoBusy(false);
                if (!IsDisposed)
                {
                    foreach (var button in actions) button.Enabled = true;
                    foreach (ToolStripItem item in toolsMenu.Items) item.Enabled = true;
                    maximum.Enabled = startDate.Enabled = endDate.Enabled = saveToDatabase.Enabled = true;
                    cancel.Enabled = false;
                    UpdateWriteMode();
                    RefreshRows();
                }
            }
        }
        private DrosoSession NewSession()
        {
            DrosoRuntime.CheckWritableDirectory(settings.Output);
            var created = new DrosoSession { Directory = Path.Combine(settings.Output, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]), ModelDirectory = DrosoRuntime.ModelDirectory };
            created.Save();
            Logger.LogInfo(null, "[Droso] Sessione creata: " + created.Directory);
            return created;
        }
        private IProgress<string> Progress() => new Progress<string>(message => Report(message));
        private async Task ExtractAndAnalyzeAsync(CancellationToken ct)
        {
            DateTime? from = startDate.Checked ? startDate.Value.Date : null;
            DateTime? until = endDate.Checked ? endDate.Value.Date : null;
            if (from.HasValue && until.HasValue && from > until)
                throw new ArgumentException("La data iniziale non può essere successiva alla data finale.");
            var connection = master?.MainConnection ?? throw new InvalidOperationException("Collegare prima ProcedureNet7 al database.");
            session = NewSession();
            await DrosoDocumentRepository.ExtractAsync(connection, settings.FilesDatabase, (int)maximum.Value, session, Progress(), ct,
                from, until);
            ct.ThrowIfCancellationRequested();
            if (session.Documents.Any(d => File.Exists(d.Path))) await AnalyzeAsync(ct);
            else
            {
                if (session.Documents.Any(d => d.AnalysisCompleted && !d.PdfUnreadable))
                    session.CheckpointSha256 = await DrosoRuntime.VerifyAssetsAsync(DrosoRuntime.AssetsDirectory, ct);
                await SaveStatusesAsync(ct);
                Logger.LogWarning(null, "[Droso] Nessun PDF disponibile nell'estrazione; controllare gli eventuali errori nel riepilogo.");
            }
        }
        private async Task AnalyzeAsync(CancellationToken ct)
        {
            await DrosoProcessClient.AnalyzeAsync(DrosoRuntime.AssetsDirectory, session, Progress(), ct);
            await SaveStatusesAsync(ct);
        }
        private async Task SaveStatusesAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!saveToDatabase.Checked || session.SourceDatabase.Length == 0)
            {
                foreach (var document in session.Documents.Where(d => d.AnalysisCompleted && d.AppliedStatus.Length == 0))
                    document.StatusUpdateNote = $"Test locale: {document.ProposedStatus} — "
                        + (document.ProposedStatus == "02" ? "in lavorazione." : "respinto, " + document.StatusReason + ".");
                session.Save();
                Report("Analisi locale completata: stati proposti disponibili, nessun aggiornamento del database.");
                return;
            }
            var connection = master?.MainConnection ?? throw new InvalidOperationException("Collegare ProcedureNet7 al database della sessione per salvare gli stati.");
            await DrosoStatusRepository.ApplyAsync(connection, session, Progress(), ct);
            if (sendRejectedMessages.Checked)
                await DrosoMessageRepository.SendRejectedAsync(connection, session, Progress(), ct);
        }
        private void Import()
        {
            using var dialog = new OpenFileDialog { Filter = "Documenti PDF|*.pdf", Multiselect = true, Title = "PDF locali da analizzare" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string[] files = dialog.FileNames;
            RunAsync(async ct =>
            {
                session = NewSession();
                string folder = Path.Combine(session.Directory, "pdf");
                Directory.CreateDirectory(folder);
                try
                {
                    foreach (string file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (new FileInfo(file).Length > 30L * 1024 * 1024) throw new InvalidDataException("PDF oltre 30 MB: " + Path.GetFileName(file));
                        string path = Path.Combine(folder, (session.Documents.Count + 1) + "-" + Path.GetFileName(file));
                        await using (var source = File.OpenRead(file))
                        await using (var output = File.Create(path)) await source.CopyToAsync(output, ct);
                        session.Documents.Add(new DrosoDocument { Path = path, Sha256 = DrosoSession.Hash(path), Bytes = new FileInfo(path).Length });
                    }
                }
                finally { session.Save(); }
            });
        }
        private void OpenSession()
        {
            using var dialog = new OpenFileDialog { Filter = "Sessione Droso|session.json", InitialDirectory = settings.Output };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            session = DrosoSession.Load(dialog.FileName);
            UpdateModel();
            RefreshRows();
            Report("Sessione aperta: " + session.Directory);
        }
        private void Export()
        {
            if (session.Documents.Count == 0) throw new InvalidOperationException("Non ci sono risultati da esportare.");
            using var dialog = new SaveFileDialog { Filter = "Riepilogo CSV|*.csv", FileName = "riepilogo-droso.csv", InitialDirectory = session.Directory };
            if (dialog.ShowDialog(this) == DialogResult.OK) { session.ExportCsv(dialog.FileName); Report("Riepilogo esportato: " + dialog.FileName); }
        }
        private void DeleteDownloads()
        {
            if (operation != null) return;
            if (session.Directory.Length == 0) throw new InvalidOperationException("Aprire prima la sessione da pulire.");
            if (MessageBox.Show(this, "Eliminare le copie locali dei PDF e le immagini preparate della sessione aperta?\n\n"
                + session.Directory + "\n\nRiepilogo, risultati e log saranno conservati. I PDF originali e il database non vengono modificati. "
                + "Per analizzarli di nuovo occorrerà riscaricarli o importarli.", "Elimina download", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RunAsync(async ct =>
            {
                var removed = await Task.Run(() => DrosoSessionFiles.DeleteDownloads(session, ct), ct);
                Report($"Eliminati {removed.Files} file locali, liberati {removed.Bytes / 1048576d:F1} MB. Sessione: {session.Directory}");
            }, "Pulizia completata. Riepilogo, risultati e log conservati.");
        }
        private void ReviewDocument()
        {
            var document = Selected ?? throw new InvalidOperationException("Selezionare un documento.");
            using var dialog = new Form { Text = "Dettagli del documento", ClientSize = new Size(580, 390), StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog, Font = Font };
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            fields.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = string.Join(Environment.NewLine, new[] {
                    "File: " + document.FileName, "ID allegato: " + document.IdAllegato, "Codice fiscale: " + document.CodiceFiscale,
                    "Sezione di caricamento: " + document.CategoriaCaricata, "Categoria riconosciuta: " + document.CategoriaProposta,
                    "Confidenza: " + (document.Confidence?.ToString("P2") ?? "—"), "Pagine: " + document.Pages,
                    "Stato previsto: " + document.ProposedStatus, "Stato salvato nel DB: " + (document.AppliedStatus.Length == 0 ? "nessuno" : document.AppliedStatus),
                    "ID stato salvato: " + (document.AppliedStatusId?.ToString() ?? "nessuno"),
                    "Messaggio inviato: " + (document.MessageSentAt?.ToString("g") ?? "no"), "Nota messaggio: " + document.MessageUpdateNote,
                    "Nota: " + document.StatusUpdateNote, "Errore: " + document.Error }) }, 0, 0);
            var correction = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            correction.Items.AddRange(Corrections.Select(c => c.Length == 0 ? "Nessuna revisione locale" : DrosoSession.Label(c)).ToArray());
            correction.SelectedIndex = Math.Max(0, Array.IndexOf(Corrections, document.CorrectedLabel));
            fields.Controls.Add(correction, 0, 1);
            fields.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "La revisione corregge la categoria nella sessione locale. Non modifica gli stati nel database." }, 0, 2);
            var buttons = Bar();
            var save = new Button { Text = "Salva revisione locale", AutoSize = true };
            save.Click += (_, _) =>
            {
                try { SaveReview(document, Corrections[correction.SelectedIndex]); dialog.DialogResult = DialogResult.OK; }
                catch (Exception ex) { ShowError(ex); }
            };
            buttons.Controls.Add(save);
            var close = new Button { Text = "Chiudi", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(close);
            fields.Controls.Add(buttons, 0, 3);
            dialog.Controls.Add(fields);
            dialog.CancelButton = close;
            dialog.ShowDialog(this);
        }
        private void SaveReview(DrosoDocument document, string correctedLabel)
        {
            string previous = document.CorrectedLabel;
            document.CorrectedLabel = correctedLabel;
            try { session.Save(); }
            catch { document.CorrectedLabel = previous; throw; }
            RefreshRows();
            Report("Revisione salvata nella sessione locale.");
        }
        private void OpenPdf()
        {
            var document = Selected ?? throw new InvalidOperationException("Selezionare un documento.");
            if (!File.Exists(document.Path)) throw new FileNotFoundException("PDF non disponibile; consultare il dettaglio errore.");
            Process.Start(new ProcessStartInfo(document.Path) { UseShellExecute = true });
        }
        private void RefreshRows()
        {
            if (IsDisposed) return;
            UpdateSummary();
            UpdateDetailsInformation();
            IEnumerable<DrosoDocument> rows = session.Documents;
            rows = filter.SelectedIndex switch
            {
                1 => rows.Where(d => d.ProposedStatus == "02"),
                2 => rows.Where(d => d.ProposedStatus == "04"),
                3 => rows.Where(d => d.CorrectedLabel.Length > 0),
                _ => rows
            };
            string value = search.Text.Trim();
            if (value.Length > 0) rows = rows.Where(d => (d.CodiceFiscale + " " + d.IdAllegato + " " + d.FileName).Contains(value, StringComparison.OrdinalIgnoreCase));
            var selected = Selected;
            grid.DataSource = new BindingList<DrosoDocument>(rows.ToList());
            if (selected != null)
                foreach (DataGridViewRow row in grid.Rows)
                    if (ReferenceEquals(row.DataBoundItem, selected)) { grid.CurrentCell = row.Cells[0]; break; }
        }
        private void UpdateSummary()
        {
            summaryCount = session.Documents.Count;
            summary.Text = $"{summaryCount} allegati · 02: {session.Documents.Count(d => d.ProposedStatus == "02")} · "
                + $"04: {session.Documents.Count(d => d.ProposedStatus == "04")} · da analizzare: {session.Documents.Count(d => !d.AnalysisCompleted)}";
        }
        private void Configure()
        {
            using var dialog = new Form { Text = "Impostazioni prelavorazione", ClientSize = new Size(710, 170), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog };
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3, RowCount = 4 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
            var output = new TextBox { Text = settings.Output, Dock = DockStyle.Fill };
            var database = new TextBox { Text = settings.FilesDatabase, Dock = DockStyle.Fill };
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            fields.Controls.Add(new Label { Text = "Cartella sessioni", AutoSize = true }, 0, 0);
            fields.Controls.Add(output, 1, 0);
            fields.Controls.Add(new Label { Text = "Database allegati", AutoSize = true }, 0, 1);
            fields.Controls.Add(database, 1, 1);
            var browse = new Button { Text = "Sfoglia", AutoSize = true };
            browse.Click += (_, _) =>
            {
                using var picker = new FolderBrowserDialog { SelectedPath = output.Text };
                if (picker.ShowDialog(dialog) == DialogResult.OK) output.Text = picker.SelectedPath;
            };
            fields.Controls.Add(browse, 2, 0);
            var note = new Label { Text = "Modello e motore sono inclusi in ProcedureNet7. Non occorre configurare un eseguibile.", AutoSize = true };
            fields.Controls.Add(note, 0, 2); fields.SetColumnSpan(note, 3);
            var save = new Button { Text = "Salva", DialogResult = DialogResult.OK, AutoSize = true };
            fields.Controls.Add(save, 2, 3);
            dialog.AcceptButton = save;
            dialog.Controls.Add(fields);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (string.IsNullOrWhiteSpace(output.Text) || string.IsNullOrWhiteSpace(database.Text)) throw new ArgumentException("Indicare cartella sessioni e database allegati.");
            var updated = new DrosoSettings { Output = Path.GetFullPath(output.Text.Trim()), FilesDatabase = database.Text.Trim() };
            DrosoRuntime.CheckWritableDirectory(updated.Output);
            updated.Save();
            settings = updated;
            Report("Impostazioni salvate.");
        }
        private Form GetDetailsWindow()
        {
            if (detailsWindow == null || detailsWindow.IsDisposed)
            {
                detailsWindow = new Form { Text = "Documenti e dettagli · Prelavorazione", ClientSize = new Size(1100, 680),
                    MinimumSize = new Size(900, 500), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                    Font = Font, BackColor = BackColor };
                detailsWindow.Controls.Add(detailsLayout);
                detailsWindow.FormClosing += (_, e) =>
                {
                    if (e.CloseReason == CloseReason.FormOwnerClosing && operation != null) { e.Cancel = true; return; }
                    if (e.CloseReason != CloseReason.UserClosing) return;
                    e.Cancel = true; detailsWindow.Hide();
                };
            }
            return detailsWindow;
        }
        private void ShowDetails()
        {
            if (IsDisposed) return;
            var window = GetDetailsWindow();
            RefreshRows();
            if (!window.Visible)
            {
                Form? owner = master ?? (TopLevel ? this : TopLevelControl as Form);
                if (owner != null) window.Show(owner); else window.Show();
            }
            window.BringToFront();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (operation != null && e.CloseReason == CloseReason.UserClosing)
            {
                operation.Cancel(); e.Cancel = true;
                Report("Annullamento in corso. Chiudere la schermata al termine della lavorazione.");
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                operation?.Cancel();
                detailsWindow?.Dispose();
                detailsLayout.Dispose();
                help.Dispose();
                toolsMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class DrosoSettings
    {
        private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcedureNet7", "droso-settings.json");
        public string Output { get; set; } = @"D:\DrosoDocLab\ProceduraDroso\Sessioni";
        public string FilesDatabase { get; set; } = "ADISU_ALLEGATI";
        public static DrosoSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var saved = JsonSerializer.Deserialize<DrosoSettings>(File.ReadAllText(SettingsPath));
                    if (saved != null && !string.IsNullOrWhiteSpace(saved.Output) && !string.IsNullOrWhiteSpace(saved.FilesDatabase))
                    {
                        string previousDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcedureNet7", "DrosoSessioni");
                        if (string.Equals(Path.TrimEndingDirectorySeparator(saved.Output), previousDefault, StringComparison.OrdinalIgnoreCase))
                            saved.Output = new DrosoSettings().Output;
                        return saved;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Logger.LogWarning(null, $"[Droso] Impostazioni non leggibili; uso i valori predefiniti. {SettingsPath}\n{ex}");
            }
            return new DrosoSettings();
        }
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath + ".tmp", JsonSerializer.Serialize(this, DrosoSession.JsonOptions));
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
        }
    }
}
