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
    public partial class FormProceduraBlocchi : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        public FormProceduraBlocchi(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
        }

        private void BlockFileChooseBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(blockFilePath, openFileDialog, ref selectedFilePath);
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
                ArgsProceduraBlocchi blocchiArgs = new ArgsProceduraBlocchi
                {
                    _blocksFilePath = selectedFilePath,
                    _blocksYear = blocksYear.Text,
                    _blocksUsername = blocksUsername.Text,
                    _blocksGiaRimossi = blocksGiaRimossi.Checked,
                    _blocksInsertMessaggio = blocksInsertMessaggioCheck.Checked,
                };
                argsValidation.Validate(blocchiArgs);
                ProceduraBlocchi proceduraBlocchi = new(_masterForm, mainConnection);
                proceduraBlocchi.RunProcedure(blocchiArgs);
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

        private void TemplateBlocchiDownload_Click(object sender, EventArgs e)
        {
            // Create and configure the folder browser dialog
            using FolderBrowserDialog saveFolder = new();
            // Show the dialog and check if the user selected a folder
            if (saveFolder.ShowDialog() == DialogResult.OK)
            {
                // Get the selected folder path
                string folderPath = saveFolder.SelectedPath;

                // Create a DataTable
                DataTable dataTable = CreateTemplateDataTable();

                // Define the file name
                string fileName = "ModelloModificaBlocchi.xlsx";

                // Export the DataTable to Excel
                Utilities.ExportDataTableToExcel(dataTable, folderPath, true, fileName);

                // Inform the user that the file has been created
                Logger.LogInfo(null, $"Creato il modello e salvato in {folderPath}.");
            }
        }

        private static DataTable CreateTemplateDataTable()
        {
            DataTable dataTable = new DataTable("Template");
            dataTable.Columns.Add("Cod_fiscale", typeof(string));
            dataTable.Columns.Add("Blocchi da togliere", typeof(string));
            dataTable.Columns.Add("Blocchi da mettere", typeof(string));
            dataTable.Columns.Add("Messaggi da inviare (opzionale)", typeof(string));

            dataTable.Rows.Add("XXXXXXXXXXXXXXXX", "VBA;BPP;IMD", "BVI;DVI", "#MotivoBVI#MotivoDVI");

            return dataTable;
        }
    }
}
