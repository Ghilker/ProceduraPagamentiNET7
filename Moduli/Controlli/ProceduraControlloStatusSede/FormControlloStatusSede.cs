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
    public partial class FormControlloStatusSede : Form
    {
        MasterForm? _masterForm;
        string selectedFolderPath = string.Empty;
        public FormControlloStatusSede(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunControlloStatusSede);
        }

        private void RunControlloStatusSede(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsControlloStatusSede _argsControlloStatusSede = new ArgsControlloStatusSede
                {
                    _folderPath = selectedFolderPath,
                    _selectedAA = selectedAAText.Text
                };
                argsValidation.Validate(_argsControlloStatusSede);
                ControlloStatusSede ControlloStatusSede = new(_masterForm, mainConnection);
                ControlloStatusSede.RunProcedure(_argsControlloStatusSede);
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

        private void saveFolderBTN_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(saveFolderLbl, folderBrowserDialog1, ref selectedFolderPath);
        }
    }
}
