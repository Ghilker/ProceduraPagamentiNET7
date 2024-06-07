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
    public partial class FormProceduraFlussi : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        public FormProceduraFlussi(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
        }

        private void ProceduraFlussoRitornoFileBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(proceduraFlussoRitornoFileLbl, openFileDialog, ref selectedFilePath);
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunFlussoRitorno);
        }

        private void RunFlussoRitorno(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                bool useFlussoNome = proceduraFlussoRitornoNomeFileCheck.Checked;
                string nomeFile = Path.GetFileNameWithoutExtension(selectedFilePath);

                ArgsProceduraFlussoDiRitorno argsFlusso = new()
                {
                    _selectedFileFlusso = selectedFilePath,
                    _selectedImpegnoProvv = useFlussoNome ? nomeFile : proceduraFlussoRitornoNumMandatoTxt.Text
                };
                argsValidation.Validate(argsFlusso);
                using ProceduraFlussoDiRitorno flusso = new(_masterForm, mainConnection);
                flusso.RunProcedure(argsFlusso);
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
