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
            layout.Controls.Add(new Label
            {
                Text = "La simulazione mostra gli accoglimenti e le correzioni documentate proposte.\nIn lavorazione, le correzioni idonee vengono applicate insieme all'accoglimento.\nSolo le istanze libere e interamente verificabili vengono lavorate. Risultati in CSV.",
                AutoSize = true, Margin = new Padding(0, 0, 0, 20)
            });
            var run = new Button { Text = "Avvia", Width = 180, Height = 42 };
            run.Click += Run;
            layout.Controls.Add(run);
            Controls.Add(layout);
        }

        private void Run(object? sender, EventArgs e)
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
            // Read controls on the UI thread, before dispatching background work.
            var args = new ArgsLavorazioneAutomaticaDomicilio
            {
                AnnoAccademico = academicYear, CartellaCsv = folder.SelectedPath, Simulazione = _simulation.Checked
            };
            _masterForm.RunBackgroundWorker(connection =>
            {
                new ArgsValidation().Validate(args);
                using var procedure = new LavorazioneAutomaticaDomicilio(_masterForm, connection);
                procedure.RunProcedure(args);
            });
        }
    }
}
