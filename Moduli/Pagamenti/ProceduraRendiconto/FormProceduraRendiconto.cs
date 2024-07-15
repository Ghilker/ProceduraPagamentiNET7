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
    public partial class FormProceduraRendiconto : Form
    {

        MasterForm? _masterForm;
        string selectedFolderPath = string.Empty;
        public FormProceduraRendiconto(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunRendiconto);
        }

        private void RunRendiconto(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();

                ArgsProceduraRendiconto argsProceduraRendiconto = new()
                {
                    _selectedSaveFolder = selectedFolderPath,
                    _annoAccademicoInizio = procedureAAstartText.Text,
                    _annoAccademicoFine = procedureAAendText.Text
                };
                argsValidation.Validate(argsProceduraRendiconto);
                using ProceduraRendiconto rendiconto = new(_masterForm, mainConnection);
                rendiconto.RunProcedure(argsProceduraRendiconto);
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

        private void ProcedureFolderSelectBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(procedureFolderSelectLbl, folderBrowserDialog1, ref selectedFolderPath);
        }
    }
}
