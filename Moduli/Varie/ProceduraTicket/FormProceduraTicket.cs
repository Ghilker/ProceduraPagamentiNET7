using ProcedureNet7.ProceduraAllegatiSpace;
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
    public partial class FormProceduraTicket : Form
    {
        MasterForm? _masterForm;
        string selectedTicketFilePath = string.Empty;
        string selectedMailFilePath = string.Empty;
        public FormProceduraTicket(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
        }

        private void TicketFileBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(ticketFilelbl, openFileDialog, ref selectedTicketFilePath);
        }

        private void TicketMailFilebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(ticketMaillbl, openFileDialog, ref selectedMailFilePath);
        }

        private void RunProcedureBTNClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunTicketProcedure);
        }

        private void RunTicketProcedure(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new();
                List<bool> ticketCheckList = new() { ticketDeleteChiusiCheck.Checked, ticketDeleteRedCheck.Checked, ticketSendMailCheck.Checked };
                ArgsProceduraTicket argsProceduraTicket = new()
                {
                    _mailFilePath = selectedMailFilePath,
                    _ticketChecks = ticketCheckList,
                    _ticketFilePath = selectedTicketFilePath
                };
                argsValidation.Validate(argsProceduraTicket);
                ProceduraTicket processTicketData = new(_masterForm, mainConnection);
                processTicketData.RunProcedure(argsProceduraTicket);

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
