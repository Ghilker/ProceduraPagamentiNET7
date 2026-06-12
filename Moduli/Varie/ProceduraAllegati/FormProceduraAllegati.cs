using System.ComponentModel.DataAnnotations;
using System.Data;
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
            { "00", "Tutti i benefici" },
            { "BS", "Borsa di studio" },
            { "PA", "Posto alloggio" },
            { "CI", "Contributo integrativo" }
        };

        private readonly Dictionary<string, string> allegatiProvvItems = new()
        {
            { "01", "Riammissione come vincitore" },
            { "02", "Riammissione come idoneo" },
            { "03", "Revoca senza recupero somme" },
            { "04", "Decadenza" },
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
            return Utilities.GetCheckBoxSelectedCodes(
                selectedBeneficiStrip.Items);
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
            switch (tipoAllegato)
            {
                case "04":
                    GeneraTemplateDecadenza(annoAccademico);
                    break; 

                default:
                    MessageBox.Show(
                        "Template non ancora implementato");
                    break;
            }
        }
        private void GeneraTemplateDecadenza(string annoAccademico)
        {
            string? filePath = null;

            _masterForm.Invoke(() =>
            {
                using SaveFileDialog saveDialog = new();

                saveDialog.Filter =
                    "Excel (*.xlsx)|*.xlsx";

                saveDialog.FileName =
                    $"Template_Decadenza_{annoAccademico}.xlsx";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    filePath = saveDialog.FileName;
                }
            });

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Logger.LogInfo(null, "Salvataggio annullato.");
                return;
            }

            DataTable dt = new();

            dt.Columns.Add("CodiceFiscale");
            dt.Columns.Add("Motivazione");

            using XLWorkbook wb = new();
            wb.Worksheets.Add(dt, "Decadenza");
            wb.SaveAs(filePath);

            Logger.LogInfo(
                null,
                $"Creato template: {filePath}");
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