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
    public partial class FormRendicontoMiur : Form
    {
        string folderPath = string.Empty;

        MasterForm? _masterForm;
        public FormRendicontoMiur(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunRendicontoMiur);
        }

        private void RunRendicontoMiur(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsRendicontoMiur argsRendicontoMiur = new ArgsRendicontoMiur
                {
                    _folderPath = folderPath,
                };
                argsValidation.Validate(argsRendicontoMiur);
                RendicontoMiur rendicontoMiur = new(_masterForm, mainConnection);
                rendicontoMiur.RunProcedure(argsRendicontoMiur);
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

        private void rendicontoFolderBTN_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(folderLBL, folderBrowserDialog1, ref folderPath);
        }
    }
}
