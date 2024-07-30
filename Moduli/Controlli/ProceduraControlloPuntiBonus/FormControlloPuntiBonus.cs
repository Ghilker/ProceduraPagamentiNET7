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
    public partial class FormControlloPuntiBonus : Form
    {
        MasterForm? _masterForm;
        string selectedFolderPath = string.Empty;
        public FormControlloPuntiBonus(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunControlloBonusProcedure);
        }

        private void RunControlloBonusProcedure(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsControlloPuntiBonus argsControlloPuntiBonus = new ArgsControlloPuntiBonus
                {
                    _selectedSaveFolder = selectedFolderPath
                };
                argsValidation.Validate(argsControlloPuntiBonus);
                ControlloPuntiBonus controlloPuntiBonus = new(_masterForm, mainConnection);
                controlloPuntiBonus.RunProcedure(argsControlloPuntiBonus);
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

        private void controlloBonusSaveFolder_Click_1(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(controlloBonusFolderLbl, folderBrowserDialog1, ref selectedFolderPath);
        }
    }
}
