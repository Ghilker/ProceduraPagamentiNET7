
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
    public partial class FormProceduraPagamenti : Form
    {
        MasterForm? _masterForm;
        string selectedFolderPath = string.Empty;
        public FormProceduraPagamenti(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
            PagamentiSettings.CreatePagamentiComboBox(ref pagamentiTipoProceduraCombo, PagamentiSettings.pagamentiTipoProcedura);
            PagamentiSettings.CreatePagamentiComboBox(ref elaborazioneMassivaBox, PagamentiSettings.pagamentiDirect);
        }

        private void PagamentiSalvataggioBTN_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(pagamentiSalvataggiolbl, folderBrowserDialog, ref selectedFolderPath);
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunPagamentiProcedure);
        }

        private void RunPagamentiProcedure(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                string pagamentiSelectedTipoProcedura = "";
                string pagamentiRiepilogoTipoProcedura = "";
                string pagamentiElaborazioneMassiva = "";
                _ = Invoke(new MethodInvoker(() =>
                {
                    dynamic selectedItem = pagamentiTipoProceduraCombo.SelectedItem;
                    pagamentiSelectedTipoProcedura = selectedItem?.Value ?? "";

                    dynamic selectedItemRiepilogo = pagamentiTipoProceduraCombo.SelectedItem;
                    pagamentiRiepilogoTipoProcedura = selectedItemRiepilogo?.Text ?? "";

                    dynamic selectedItemMassivo = elaborazioneMassivaBox.SelectedItem;
                    pagamentiElaborazioneMassiva = selectedItemMassivo?.Value ?? "";
                }));

                RiepilogoArguments.Instance.tipoProcedura = pagamentiRiepilogoTipoProcedura;

                ArgsPagamenti argsPagamenti = new ArgsPagamenti
                {
                    _selectedSaveFolder = selectedFolderPath,
                    _annoAccademico = pagamentiAATxt.Text,
                    _dataRiferimento = pagamentiDataRiftxt.Text,
                    _numeroMandato = pagamentiNuovoMandatoTxt.Text,
                    _tipoProcedura = pagamentiSelectedTipoProcedura,
                    _vecchioMandato = pagamentiOldMandatoTxt.Text,
                    _filtroManuale = proceduraPagamentiFiltroCheck.Checked,
                    _elaborazioneMassivaCheck = lavorazioneMassivaCheck.Checked,
                    _elaborazioneMassivaString = pagamentiElaborazioneMassiva,
                    _forzareStudenteCheck = forzareStudenteCheck.Checked,
                    _forzareStudenteString = forzareStudenteTxt.Text,
                };
                argsValidation.Validate(argsPagamenti);
                using ProceduraPagamenti pagamenti = new(_masterForm, mainConnection);
                pagamenti.RunProcedure(argsPagamenti);
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
    }
}
