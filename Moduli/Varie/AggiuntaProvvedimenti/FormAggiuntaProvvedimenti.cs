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
    public partial class FormAggiuntaProvvedimenti : Form
    {
        MasterForm? _masterForm;
        string selectedFolderPath = string.Empty;
        private readonly Dictionary<string, string> provvedimentiItems = new()
        {
            { "00", "Varie" },
            { "01", "Riammissione come vincitore" },
            { "02", "Riammissione come idoneo" },
            { "03", "Revoca senza recupero somme" },
            { "04", "Decadenza" },
            { "05", "Modifica importo" },
            { "06", "Revoca con recupero somme" },
            { "07", "Pagamento" },
            { "08", "Rinuncia" },
            { "09", "Da idoneo a vincitore" },
            { "10", "Rinuncia con recupero somme" },
            { "11", "Rinuncia senza recupero somme" },
            { "12", "Rimborso tassa regionale indebitamente pagata" },
            { "13", "Cambio status sede" }
        };
        private readonly Dictionary<string, string> provvedimentiCodBeneficioItems = new()
        {
            { "BS", "Borsa di studio, Posto alloggio" },
            { "CI", "Contributo integrativo" },
            { "PL", "Premio di laurea" },
            { "BL", "Buono libro" },
        };
        public FormAggiuntaProvvedimenti(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
            Initialize();
        }

        void Initialize()
        {
            panelInserimentoImpegni.Visible = false;
            provvedimentiBox.Items.Clear();
            foreach (KeyValuePair<string, string> item in provvedimentiItems)
            {
                _ = provvedimentiBox.Items.Add(new { Text = item.Value, Value = item.Key });
            }

            provvedimentiBox.DisplayMember = "Text";
            provvedimentiBox.ValueMember = "Value";

            provvedimentiBeneficioBox.Items.Clear();
            foreach (KeyValuePair<string, string> item in provvedimentiCodBeneficioItems)
            {
                _ = provvedimentiBeneficioBox.Items.Add(new { Text = item.Value, Value = item.Key });
            }

            provvedimentiBeneficioBox.DisplayMember = "Text";
            provvedimentiBeneficioBox.ValueMember = "Value";
        }

        private void ProvvedimentiFolderbtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(provvedimentiFolderlbl, folderBrowserDialog, ref selectedFolderPath);
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
                string selectedProvvedimentiValue = "";
                _ = Invoke(new MethodInvoker(() =>
                {
                    dynamic selectedItem = provvedimentiBox.SelectedItem;
                    selectedProvvedimentiValue = selectedItem?.Value ?? "";
                }));

                string selectedProvvedimentiBeneficio = "";
                _ = Invoke(new MethodInvoker(() =>
                {
                    dynamic selectedItem = provvedimentiBeneficioBox.SelectedItem;
                    selectedProvvedimentiBeneficio = selectedItem?.Value ?? "";
                }));

                ArgsAggiuntaProvvedimenti provvArgs = new()
                {
                    _selectedFolderPath = selectedFolderPath,
                    _numProvvedimento = provvedimentiNumeroText.Text,
                    _aaProvvedimento = provvedimentiAAText.Text,
                    _dataProvvedimento = provvedimentiDataText.Text,
                    _provvedimentoSelezionato = selectedProvvedimentiValue,
                    _notaProvvedimento = provvedimentiNotaText.Text,
                    _beneficioProvvedimento = selectedProvvedimentiBeneficio,
                    _requireNuovaSpecifica = provvedimentiRequiredSpecificheImpegni.Checked,
                    _impegnoPR = specificheImpPRBox.Text,
                    _impegnoSA = specificheImpSABox.Text,
                    _eseSA = specificheEseSABox.Text,
                    _esePR = specificheEsePRBox.Text,
                    _capitolo = specificheCapitoloBox.Text,
                    _tipoFondo = specificheTipoFondoBox.Text
                };
                argsValidation.Validate(provvArgs);
                using AggiuntaProvvedimenti aggiuntaProvvedimenti = new(_masterForm, mainConnection);
                aggiuntaProvvedimenti.RunProcedure(provvArgs);
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

        private void ProvvedimentiBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedProvvedimentiValue = "";
            _ = Invoke(new MethodInvoker(() =>
            {
                dynamic selectedItem = provvedimentiBox.SelectedItem;
                selectedProvvedimentiValue = selectedItem?.Value ?? "";
            }));

            if (!provvedimentiRequiredSpecificheImpegni.Checked)
            {
                panelInserimentoImpegni.Visible = false;
                return;
            }

            switch (selectedProvvedimentiValue)
            {
                case "01":
                case "02":
                case "05":
                case "09":
                case "13":
                    panelInserimentoImpegni.Visible = false;
                    break;
                case "03":
                case "04":
                case "06":
                case "07":
                case "08":
                case "10":
                case "11":
                case "12":
                    panelInserimentoImpegni.Visible = false;
                    break;
            }
        }

        private void ProvvedimentiRequiredSpecificheImpegni_CheckedChanged(object sender, EventArgs e)
        {
            ProvvedimentiBox_SelectedIndexChanged(sender, e);
        }
    }
}
