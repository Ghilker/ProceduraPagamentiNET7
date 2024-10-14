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
    public partial class FormSpecificheImpegni : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        public FormSpecificheImpegni(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
            newSpecificaPanel.Visible = false;
        }

        private void SpecificheFileExcelBTN_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(specificheFileExcelLbL, openFileDialog, ref selectedFilePath);
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
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsSpecificheImpegni argsSpecificheImpegni = new ArgsSpecificheImpegni
                {
                    _selectedFile = selectedFilePath,
                    _selectedDate = specificheDataDetBox.Text,
                    _tipoFondo = specificheTipoFondoBox.Text,
                    _aperturaNuovaSpecifica = specificheNewLineCheck.Checked,
                    _soloApertura = soloAperturaCheck.Checked,
                    _capitolo = specificheCapitoloBox.Text,
                    _descrDetermina = specificheDescrDetBox.Text,
                    _esePR = specificheEsePRBox.Text,
                    _eseSA = specificheEseSABox.Text,
                    _impegnoPR = specificheImpPRBox.Text,
                    _impegnoSA = specificheImpSABox.Text,
                    _numDetermina = specificheNumDetBox.Text,
                    _selectedAA = specificheAABox.Text,
                    _selectedCodBeneficio = specificheCodBeneficioBox.Text
                };
                argsValidation.Validate(argsSpecificheImpegni);
                SpecificheImpegni specificheImpegni = new(_masterForm, mainConnection);
                specificheImpegni.RunProcedure(argsSpecificheImpegni);
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

        private void SpecificheNewLineCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (specificheNewLineCheck.Checked)
            {
                newSpecificaPanel.Visible = true;
            }
            else
            {
                newSpecificaPanel.Visible = false;
            }
        }
    }
}
