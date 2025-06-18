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
    public partial class FormGeneratoreFlussi : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        string selectedFolderPath = string.Empty;
        public FormGeneratoreFlussi(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunGeneratoreFlussi);
        }

        private void RunGeneratoreFlussi(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsProceduraGeneratoreFlussi argsProceduraGeneratoreFlussi = new ArgsProceduraGeneratoreFlussi
                {
                    FilePath = selectedFilePath,
                    FolderPath = selectedFolderPath
                };
                argsValidation.Validate(argsProceduraGeneratoreFlussi);
                ProceduraGeneratoreFlussi proceduraGeneratoreFlussi = new(_masterForm, mainConnection);
                proceduraGeneratoreFlussi.RunProcedure(argsProceduraGeneratoreFlussi);
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

        private void GenflussiFilebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(GenflussiFilelbl, openFileDialog, ref selectedFilePath);
        }

        private void GenflussiSavebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(GenflussiSavelbl, saveFolderDialog, ref selectedFolderPath);
        }
    }
}
