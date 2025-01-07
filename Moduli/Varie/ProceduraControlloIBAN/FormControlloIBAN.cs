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
    public partial class FormControlloIBAN : Form
    {
        MasterForm? _masterForm;
        public FormControlloIBAN(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunControlloIBAN);
        }

        private void RunControlloIBAN(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsProceduraControlloIBAN argsProceduraControlloIBAN = new ArgsProceduraControlloIBAN
                {
                    _annoAccademico = textBox1.Text
                };
                argsValidation.Validate(argsProceduraControlloIBAN);
                ProceduraControlloIBAN proceduraControlloIBAN = new(_masterForm, mainConnection);
                proceduraControlloIBAN.RunProcedure(argsProceduraControlloIBAN);
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
