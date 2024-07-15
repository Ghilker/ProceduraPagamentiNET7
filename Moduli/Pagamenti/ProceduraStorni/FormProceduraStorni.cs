using ProcedureNet7.Storni;
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
    public partial class FormProceduraStorni : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        public FormProceduraStorni(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
        }

        private void StorniFileBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(storniFilelbl, openFileDialog, ref selectedFilePath);
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunFlussoRitorno);
        }

        private void RunFlussoRitorno(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsProceduraStorni argsStorni = new ArgsProceduraStorni
                {
                    _selectedFile = selectedFilePath,
                    _esercizioFinanziario = storniSelectedEseFinanziarioTxt.Text
                };
                argsValidation.Validate(argsStorni);
                using ProceduraStorni storni = new(_masterForm, mainConnection);
                storni.RunProcedure(argsStorni);
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
    }
}
