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
    public partial class FormEstrazioneIstanze : Form
    {
        MasterForm? _masterForm;
        string mailFilePath = string.Empty;
        string saveFolderPath = string.Empty;
        public FormEstrazioneIstanze(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunEstrazioneIstanze);
        }

        private void RunEstrazioneIstanze(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsEstrazioneIstanze _argsEstrazioneIstanze = new ArgsEstrazioneIstanze
                {
                    _mailFilePath = mailFilePath,
                    _savePath = saveFolderPath
                };
                argsValidation.Validate(_argsEstrazioneIstanze);
                ProceduraEstrazioneIstanze EstrazioneIstanze = new(_masterForm, mainConnection);
                EstrazioneIstanze.RunProcedure(_argsEstrazioneIstanze);
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

        private void SavePathButtonClick(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(savePathlbl, folderBrowserDialog1, ref saveFolderPath);
        }

        private void MailPathButtonClick(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(mailPathlbl, openFileDialog1, ref mailFilePath);
        }
    }
}
