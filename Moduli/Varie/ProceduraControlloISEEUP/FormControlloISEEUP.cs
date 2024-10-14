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
    public partial class FormControlloISEEUP : Form
    {
        MasterForm? _masterForm;
        public FormControlloISEEUP(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunISEEUPProcedure);
        }

        private void RunISEEUPProcedure(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsControlloISEEUP iseeupArgs = new ArgsControlloISEEUP
                {
                    _annoAccademico = iseeupAABox.Text
                };
                argsValidation.Validate(iseeupArgs);
                ProceduraControlloISEEUP proceduraISEEUP = new(_masterForm, mainConnection);
                proceduraISEEUP.RunProcedure(iseeupArgs);
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
