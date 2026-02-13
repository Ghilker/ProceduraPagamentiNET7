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
    public partial class FormControlloDomicilio : Form
    {
        string folderPath;
        MasterForm? _masterForm;
        public FormControlloDomicilio(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunControlloDomicilio);
        }

        private void RunControlloDomicilio(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsControlloDomicilio _argsControlloDomicilio = new ArgsControlloDomicilio
                {
                    _folderPath = folderPath,
                    _selectedAA = selectedAA.Text,
                };
                argsValidation.Validate(_argsControlloDomicilio);
                ControlloDomicilio ControlloDomicilio = new(_masterForm, mainConnection);
                ControlloDomicilio.RunProcedure(_argsControlloDomicilio);
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

        private void folderPathBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(folderPathlbl, folderBrowserDialog1, ref folderPath);
        }
    }
}
