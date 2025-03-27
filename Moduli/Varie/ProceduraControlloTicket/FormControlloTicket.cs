using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class FormControlloTicket : Form
    {
        private MasterForm? _masterForm;
        private string? _selectedFilePath;

        public FormControlloTicket(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();

            // Wire up event handlers here or in the Designer file
            button1.Click += RunProcedureBtnClick;      // "AVVIA LA PROCEDURA"
            buttonSelectCSV.Click += ButtonSelectCSV_Click;
        }

        // Select CSV button
        private void ButtonSelectCSV_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "CSV Files (*.csv)|*.csv";
                dlg.Title = "Select Student Tickets CSV File";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _selectedFilePath = dlg.FileName;
                    txtSelectedFile.Text = _selectedFilePath;
                }
            }
        }

        // "AVVIA LA PROCEDURA" button click
        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }
            // We call the MasterForm's method to run in background
            _masterForm.RunBackgroundWorker(RunControlloTicket);
        }

        // This method is called by the background worker with a valid SqlConnection
        private void RunControlloTicket(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }

                // Validate arguments
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsControlloTicket _argsControlloTicket = new ArgsControlloTicket()
                {
                    // If needed, you can pass other parameters to your Args (e.g. _selectedFilePath)
                    SelectedCsvPath = _selectedFilePath
                };
                argsValidation.Validate(_argsControlloTicket);

                // Run the procedure
                ControlloTicket procedure = new(_masterForm, mainConnection);
                procedure.RunProcedure(_argsControlloTicket);

            }
            catch (ValidationException ex)
            {
                Logger.LogWarning(100, "Errore compilazione procedura: " + ex.Message);
            }
            catch (Exception ex)
            {
                // Rethrow or handle as needed
                throw new Exception("Errore durante RunControlloTicket: " + ex.Message, ex);
            }
        }
    }
}
