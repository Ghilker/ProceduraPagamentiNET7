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
    public partial class Form_procedure_name_ : Form
    {
        MasterForm? _masterForm;
        public Form_procedure_name_(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(Run_procedure_name_);
        }

        private void Run_procedure_name_(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                Args_procedure_name_ _args_procedure_Name_ = new Args_procedure_name_
                {
                };
                argsValidation.Validate(_args_procedure_Name_);
                _procedure_name_ _procedure_Name_ = new(_masterForm, mainConnection);
                _procedure_Name_.RunProcedure(_args_procedure_Name_);
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
