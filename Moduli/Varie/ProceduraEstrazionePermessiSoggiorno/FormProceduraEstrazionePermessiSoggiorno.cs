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
    public partial class FormProceduraEstrazionePermessiSoggiorno : Form
    {
        MasterForm? _masterForm;
        string mailFilePath = string.Empty;
        string saveFolderPath = string.Empty;
        public FormProceduraEstrazionePermessiSoggiorno(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunBlocchiProcedure);
        }

        private void RunBlocchiProcedure(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsProceduraEstrazionePermessiSoggiorno psArgs = new ArgsProceduraEstrazionePermessiSoggiorno
                {
                    _mailFilePath = mailFilePath,
                    _savePath = saveFolderPath
                };
                argsValidation.Validate(psArgs);
                ProceduraEstrazionePermessiSoggiorno proceduraPS = new(_masterForm, mainConnection);
                proceduraPS.RunProcedure(psArgs);
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

        private void savePathBTN_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(savePathLbl, folderBrowserDialog1, ref saveFolderPath);
        }

        private void mailPathBTN_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(mailPathLbl, openFileDialog1, ref mailFilePath);
        }
    }
}
