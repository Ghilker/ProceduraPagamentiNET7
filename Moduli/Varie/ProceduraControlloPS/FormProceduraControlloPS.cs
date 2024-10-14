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
    public partial class FormProceduraControlloPS : Form
    {
        MasterForm? _masterForm;
        public FormProceduraControlloPS(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunProceduraControlloPS);
        }

        private void RunProceduraControlloPS(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsProceduraControlloPS _argsProceduraControlloPS = new ArgsProceduraControlloPS
                {
                };
                argsValidation.Validate(_argsProceduraControlloPS);
                ProceduraControlloPS ProceduraControlloPS = new(_masterForm, mainConnection);
                ProceduraControlloPS.RunProcedure(_argsProceduraControlloPS);
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
