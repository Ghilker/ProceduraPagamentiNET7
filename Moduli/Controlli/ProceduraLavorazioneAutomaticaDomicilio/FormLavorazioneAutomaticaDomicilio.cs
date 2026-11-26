namespace ProcedureNet7
{
    public sealed class FormLavorazioneAutomaticaDomicilio : Form
    {
        private readonly MasterForm _masterForm;
        private readonly TextBox _academicYear = new() { Width = 130, PlaceholderText = "20262027" };
        private readonly CheckBox _simulation = new()
        {
            Text = "Simulazione (nessuna modifica)", Checked = true, AutoSize = true
        };
        private readonly CheckBox _useOcr = new()
        {
            Text = "Usa OCR per i PDF scannerizzati", Checked = true, AutoSize = true
        };
        private readonly CheckBox _useUnlimitedOcr = new() { Text = "Usa Unlimited-OCR 2", AutoSize = true };
        private readonly Label _unlimitedOcrStatus = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        private readonly Button _run = new() { Text = "Avvia", Width = 180, Height = 42 };

        public FormLavorazioneAutomaticaDomicilio(MasterForm masterForm)
        {
            _masterForm = masterForm;
            AutoScaleMode = AutoScaleMode.Font;
            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(28), AutoScroll = true
            };
            layout.Controls.Add(new Label
            {
                Text = "LAVORAZIONE AUTOMATICA ISTANZE DOMICILIO", AutoSize = true,
                Font = new Font("Segoe UI", 15F), Margin = new Padding(0, 0, 0, 22)
            });
            layout.Controls.Add(new Label { Text = "Anno accademico", AutoSize = true });
            layout.Controls.Add(_academicYear);
            _simulation.Margin = new Padding(0, 20, 0, 12);
            layout.Controls.Add(_simulation);
            layout.Controls.Add(_useOcr);
            layout.Controls.Add(_useUnlimitedOcr);
            layout.Controls.Add(_unlimitedOcrStatus);
            _useOcr.CheckedChanged += (_, _) => _useUnlimitedOcr.Enabled = _useOcr.Checked;
            layout.Controls.Add(new Label
            {
                Text = "La simulazione mostra gli accoglimenti e le correzioni documentate proposte.\nIn lavorazione, le correzioni idonee vengono applicate insieme all'accoglimento.\nSolo le istanze libere e interamente verificabili vengono lavorate. Risultati in CSV.",
                AutoSize = true, Margin = new Padding(0, 0, 0, 20)
            });
            _run.Click += Run;
            layout.Controls.Add(_run);
            Controls.Add(layout);
        }

        private async void Run(object? sender, EventArgs e)
        {
            var academicYear = _academicYear.Text.Trim();
            if (academicYear.Length != 8 || !int.TryParse(academicYear, out var aa) ||
                aa < 20262027 || aa % 10000 != aa / 10000 + 1)
            {
                MessageBox.Show("Inserire un anno accademico dal 20262027 nel formato xxxxyyyy.");
                return;
            }

            using var folder = new FolderBrowserDialog { Description = "Cartella di salvataggio del CSV" };
            if (folder.ShowDialog(this) != DialogResult.OK) return;

            var args = new ArgsLavorazioneAutomaticaDomicilio
            {
                AnnoAccademico = academicYear,
                CartellaCsv = folder.SelectedPath,
                Simulazione = _simulation.Checked, UsaOcr = _useOcr.Checked
            };
            if (args.UsaOcr && _useUnlimitedOcr.Checked)
            {
                _run.Enabled = false;
                try
                {
                    _unlimitedOcrStatus.Text = "Verifica di Unlimited-OCR 2 in corso…";
                    var progress = new Progress<string>(message =>
                    {
                        if (!IsDisposed) _unlimitedOcrStatus.Text = message;
                    });
                    var status = await DomicilioUnlimitedOcrRuntime.CheckAsync(progress);
                    if (IsDisposed) return;
                    _unlimitedOcrStatus.Text = status.Message;
                    if (status.Url == null)
                    {
                        MessageBox.Show(this, status.Message, "Unlimited-OCR 2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    args.UnlimitedOcrUrl = status.Url;
                }
                finally
                {
                    if (!IsDisposed) _run.Enabled = true;
                }
            }

            _masterForm.RunBackgroundWorker(connection =>
            {
                new ArgsValidation().Validate(args);
                using var procedure = new LavorazioneAutomaticaDomicilio(_masterForm, connection);
                procedure.RunProcedure(args);
            });
        }
    }
}
