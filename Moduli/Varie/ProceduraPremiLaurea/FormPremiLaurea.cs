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
    public partial class FormPremiLaurea : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        string selectedFolderPath = string.Empty;
        public FormPremiLaurea(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunPremiLaurea);
        }

        private void RunPremiLaurea(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsPremiLaurea _argsPremiLaurea = new ArgsPremiLaurea
                {
                    FileExcelInput = selectedFilePath, FileExcelOutput = selectedFolderPath,
                };
                argsValidation.Validate(_argsPremiLaurea);
                PremiLaurea PremiLaurea = new(_masterForm, mainConnection);
                PremiLaurea.RunProcedure(_argsPremiLaurea);
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

        private void PremiLaureaFilebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(PremiLaureaFilelbl, openFileDialog, ref selectedFilePath);
        }

        private void PremiLaureaSavebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(PremiLaureaSavelbl, saveFolderDialog, ref selectedFolderPath);
        }
    }
}
