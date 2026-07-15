using System;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using ClosedXML.Excel;
using ProcedureNet7.ProceduraAllegatiSpace;

namespace ProcedureNet7
{
    public partial class FormProceduraAllegati : Form
    {
        private readonly MasterForm _masterForm;

        private string selectedFilePath = string.Empty;
        private string selectedFolderPath = string.Empty;

        private ContextMenuStrip selectedBeneficiStrip = new();

        private readonly Dictionary<string, string> proceduraAllegatiBeneficiItems = new()
        {
            { "00", "Tutti" },
            { "BS", "BS" },
            { "PA", "PA" },
            { "CI", "CI" }
        };

        private readonly Dictionary<string, string> allegatiProvvItems = new()
        {
            { "01", "Riammissione come vincitore" },
            { "02", "Riammissione come idoneo" },
            { "03", "Revoca senza recupero somme" },
            { "40", "Decadenza senza recupero somme" },
            { "41", "Decadenza con recupero somme" },
            { "05", "Modifica importo" },
            { "06", "Revoca con recupero somme" },
            { "09", "Da idoneo a vincitore" },
            { "10", "Rinuncia con recupero somme" },
            { "11", "Rinuncia senza recupero somme" },
            { "13", "Cambio status sede" }
        };

        public FormProceduraAllegati(MasterForm masterForm)
        {
            _masterForm = masterForm;

            InitializeComponent();

            Initialize();
        }

        private void Initialize()
        {
            LoadTipoAllegatoCombo();

            Utilities.CreateDropDownMenu(
                ref proceduraAllegatiBeneficiBtn,
                ref selectedBeneficiStrip,
                proceduraAllegatiBeneficiItems, clean:true);

            btnScaricaModello.Click += btnScaricaModello_Click;
        }

        private void LoadTipoAllegatoCombo()
        {
            proceduraAllegatiTipoCombo.Items.Clear();

            foreach (var item in allegatiProvvItems)
            {
                proceduraAllegatiTipoCombo.Items.Add(new ComboBoxItem
                {
                    Text = item.Value,
                    Value = item.Key
                });
            }

            proceduraAllegatiTipoCombo.DisplayMember = nameof(ComboBoxItem.Text);
            proceduraAllegatiTipoCombo.ValueMember = nameof(ComboBoxItem.Value);

            if (proceduraAllegatiTipoCombo.Items.Count > 0)
            {
                proceduraAllegatiTipoCombo.SelectedIndex = 0;
            }
        }

        private string GetSelectedTipoAllegato()
        {
            if (proceduraAllegatiTipoCombo.SelectedItem is ComboBoxItem item)
            {
                return item.Value;
            }

            return string.Empty;
        }

        private string GetSelectedTipoAllegatoName()
        {
            if (proceduraAllegatiTipoCombo.SelectedItem is ComboBoxItem item)
            {
                return item.Text;
            }

            return string.Empty;
        }

        private string GetSelectedBenefici()
        {
            List<string> selectedCodes = new();

            foreach (ToolStripMenuItem item in selectedBeneficiStrip.Items)
            {
                if (!item.Checked)
                    continue;

                string code = item.Tag?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(code))
                    selectedCodes.Add("'" + code.Replace("'", "''") + "'");
            }

            return selectedCodes.Count == 0
                ? "''"
                : string.Join(", ", selectedCodes);
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            _masterForm.RunBackgroundWorker(RunAllegatiProcedure);
        }

        private void RunAllegatiProcedure(SqlConnection mainConnection)
        {
            try
            {
                string selectedTipoAllegatoValue = string.Empty;
                string selectedTipoAllegatoName = string.Empty;
                string selectedBenefici = string.Empty;

                Invoke(new MethodInvoker(() =>
                {
                    selectedTipoAllegatoValue = GetSelectedTipoAllegato();
                    selectedTipoAllegatoName = GetSelectedTipoAllegatoName();
                    selectedBenefici = GetSelectedBenefici();
                }));

                ArgsProceduraAllegati args = new()
                {
                    _selectedAA = proceduraAllegatiAA.Text,
                    _selectedFileExcel = selectedFilePath,
                    _selectedSaveFolder = selectedFolderPath,
                    _selectedTipoAllegato = selectedTipoAllegatoValue,
                    _selectedTipoAllegatoName = selectedTipoAllegatoName,
                    _selectedTipoBeneficio = selectedBenefici
                };

                ProceduraAllegati procedura =
                    new(_masterForm, mainConnection);

                procedura.RunProcedure(args);
            }
            catch (ValidationException ex)
            {
                Logger.LogWarning(
                    100,
                    $"Errore compilazione procedura: {ex.Message}");
            }
            catch
            {
                throw;
            }
        }

        private void ProceduraAllegatiCFbtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(
                proceduraAllegatiCFlbl,
                excelFileDialog,
                ref selectedFilePath);
        }

        private void btnScaricaModello_Click(object? sender, EventArgs e)
        {
            string tipoAllegato = GetSelectedTipoAllegato();

            if (string.IsNullOrWhiteSpace(tipoAllegato))
            {
                MessageBox.Show(
                    "Selezionare un tipo allegato.",
                    "Attenzione",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            GeneraTemplate(
                    tipoAllegato,
                    proceduraAllegatiAA.Text);
        }

        public void GeneraTemplate(string tipoAllegato, string annoAccademico)
        {
            if (!CatalogoModelliAllegati.TryGet(tipoAllegato, out ModelloAllegatoDefinition? modello)
                || modello is null)
            {
                MessageBox.Show(
                    "Tipo allegato non gestito.",
                    "Attenzione",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            GeneraTemplateAllegato(modello, annoAccademico);
        }

        private void GeneraTemplateAllegato(
            ModelloAllegatoDefinition modello,
            string annoAccademico)
        {
            string? filePath = null;

            _masterForm.Invoke(() =>
            {
                using SaveFileDialog saveDialog = new();

                saveDialog.Filter = "Excel (*.xlsx)|*.xlsx";
                saveDialog.FileName = GetTemplateFileName(modello, annoAccademico);

                if (saveDialog.ShowDialog() == DialogResult.OK)
                    filePath = saveDialog.FileName;
            });

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Logger.LogInfo(null, "Salvataggio annullato.");
                return;
            }

            using XLWorkbook wb = new();
            var ws = wb.Worksheets.Add(modello.NomeFoglio);

            for (int index = 0; index < modello.Colonne.Count; index++)
            {
                int colonna = index + 1;
                string intestazione = modello.Colonne[index];

                ws.Cell(1, colonna).Value = intestazione;
                ws.Column(colonna).Width = Math.Max(16, intestazione.Length + 2);

                if (intestazione.Equals(CatalogoModelliAllegati.CodiceFiscale, StringComparison.OrdinalIgnoreCase))
                    ws.Column(colonna).Style.NumberFormat.Format = "@";
            }

            var intestazioni = ws.Range(1, 1, 1, modello.Colonne.Count);
            intestazioni.Style
                .Fill.SetBackgroundColor(XLColor.CornflowerBlue)
                .Font.SetBold()
                .Font.SetFontColor(XLColor.White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Alignment.SetWrapText(true);

            ws.Row(1).Height = 28;
            ws.SheetView.FreezeRows(1);

            wb.SaveAs(filePath);

            Logger.LogInfo(
                null,
                $"Creato template per '{modello.Descrizione}': {filePath}");
        }

        private static string GetTemplateFileName(
            ModelloAllegatoDefinition modello,
            string annoAccademico)
        {
            string aa = annoAccademico?.Trim() ?? string.Empty;
            string suffissoAnno = string.IsNullOrWhiteSpace(aa) ? string.Empty : $"_{aa}";

            return $"Template_{modello.Codice}_{modello.NomeFile}{suffissoAnno}.xlsx";
        }
        private void ProceduraAllegatiSavebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(
                proceduraAllegatiSavelbl,
                saveFolderDialog,
                ref selectedFolderPath);
        }
    }

    public class ComboBoxItem
    {
        public string Text { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public override string ToString()
        {
            return Text;
        }
    }

}
