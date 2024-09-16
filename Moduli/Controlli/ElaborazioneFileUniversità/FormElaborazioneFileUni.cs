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
    public partial class FormElaborazioneFileUni : Form
    {
        MasterForm? _masterForm;
        string selectedFolderPath = string.Empty;
        public FormElaborazioneFileUni(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunElaborazioneFileUni);
        }
        private void RunElaborazioneFileUni(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new();

                ArgsElaborazioneFileUni argsElaborazioneFileUni = new()
                {
                    _selectedUniFolder = selectedFolderPath,
                    _selectedAA = "20232024"
                };
                argsValidation.Validate(argsElaborazioneFileUni);
                ElaborazioneFileUni elaborazioneFileUni = new(_masterForm, mainConnection);
                elaborazioneFileUni.RunProcedure(argsElaborazioneFileUni);
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

        private void CaricamentoFileUnibtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(elaborazioneFileUniExcellbl, folderBrowserDialog1, ref selectedFolderPath);
        }
    }
}
