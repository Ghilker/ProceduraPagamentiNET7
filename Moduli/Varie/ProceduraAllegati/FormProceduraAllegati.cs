using ProcedureNet7.ProceduraAllegatiSpace;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class FormProceduraAllegati : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        string selectedFolderPath = string.Empty;
        ContextMenuStrip selectedBeneficiStrip;

        private readonly Dictionary<string, string> proceduraAllegatiBeneficiItems = new()
        {
            { "BS", "Borsa di studio" },
            { "PA", "Posto alloggio" },
            { "CI", "Contributo integrativo" },
            { "PL", "Premio di laurea" },
            { "BL", "Buono libro" }
        };

        private readonly Dictionary<string, string> allegatiProvvItems = new()
        {
            { "01", "Riammissione come vincitore" },
            { "02", "Riammissione come idoneo" },
            { "03", "Revoca senza recupero somme" },
            { "04", "Decadenza" },
            { "05", "Modifica importo - NON IMPLEMENTATO" },
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
            proceduraAllegatiTipoCombo.Items.Clear();
            foreach (KeyValuePair<string, string> item in allegatiProvvItems)
            {
                if (item.Key == "00")
                {
                    continue;
                }
                _ = proceduraAllegatiTipoCombo.Items.Add(new { Text = item.Value, Value = item.Key });
            }

            proceduraAllegatiTipoCombo.DisplayMember = "Text";
            proceduraAllegatiTipoCombo.ValueMember = "Value";

            Utilities.CreateDropDownMenu(ref filtroBeneficioBTN, ref selectedBeneficiStrip, proceduraAllegatiBeneficiItems);
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunAllegatiProcedure);
        }

        private void RunAllegatiProcedure(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new();
                string selectedTipoAllegatoValue = "";
                _ = Invoke(new MethodInvoker(() =>
                {
                    dynamic selectedItem = proceduraAllegatiTipoCombo.SelectedItem;
                    selectedTipoAllegatoValue = selectedItem?.Value ?? "";
                }));

                string selectedTipoAllegatoName = "";
                _ = Invoke(new MethodInvoker(() =>
                {
                    dynamic selectedItem = proceduraAllegatiTipoCombo.SelectedItem;
                    selectedTipoAllegatoName = selectedItem?.Text ?? "";
                }));

                string selectedTipoBeneficioValue = "";
                _ = Invoke(new MethodInvoker(() =>
                {
                    dynamic selectedItem = Utilities.GetCheckBoxSelectedCodes(selectedBeneficiStrip?.Items);
                    selectedTipoBeneficioValue = selectedItem ?? "";
                }));
                ArgsProceduraAllegati argsProceduraAllegati = new()
                {
                    _selectedAA = proceduraAllegatiAA.Text,
                    _selectedFileExcel = selectedFilePath,
                    _selectedSaveFolder = selectedFolderPath,
                    _selectedTipoAllegato = selectedTipoAllegatoValue,
                    _selectedTipoAllegatoName = selectedTipoAllegatoName,
                    _selectedTipoBeneficio = selectedTipoBeneficioValue
                };
                argsValidation.Validate(argsProceduraAllegati);
                ProceduraAllegati proceduraAllegati = new(_masterForm, mainConnection);
                proceduraAllegati.RunProcedure(argsProceduraAllegati);
            }
            catch (ValidationException ex)
            {
                Logger.LogWarning(100, "Errore compilazione procedura: " + ex.Message);
            }
            catch
            {
                throw;
            }
        }

        private void ProceduraAllegatiCFbtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(proceduraAllegatiCFlbl, excelFileDialog, ref selectedFilePath);
        }

        private void ProceduraAllegatiSavebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(proceduraAllegatiSavelbl, saveFolderDialog, ref selectedFolderPath);
        }
    }
}
