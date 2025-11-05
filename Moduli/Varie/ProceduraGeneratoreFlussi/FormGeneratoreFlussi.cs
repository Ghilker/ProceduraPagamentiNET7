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
using ClosedXML.Excel;

namespace ProcedureNet7
{
    public partial class FormGeneratoreFlussi : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        string selectedFolderPath = string.Empty;
        public FormGeneratoreFlussi(MasterForm masterForm)
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

            _masterForm.RunBackgroundWorker(RunGeneratoreFlussi);
        }

        private void RunGeneratoreFlussi(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsProceduraGeneratoreFlussi argsProceduraGeneratoreFlussi = new ArgsProceduraGeneratoreFlussi
                {
                    FilePath = selectedFilePath,
                    FolderPath = selectedFolderPath
                };
                argsValidation.Validate(argsProceduraGeneratoreFlussi);
                ProceduraGeneratoreFlussi proceduraGeneratoreFlussi = new(_masterForm, mainConnection);
                proceduraGeneratoreFlussi.RunProcedure(argsProceduraGeneratoreFlussi);
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

        private void GenflussiFilebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(GenflussiFilelbl, openFileDialog, ref selectedFilePath);
        }

        private void GenflussiSavebtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(GenflussiSavelbl, saveFolderDialog, ref selectedFolderPath);
        }

        private void DownloadmdlBtn_Click(object sender, EventArgs e)
        {
            try
            {
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "File Excel (*.xlsx)|*.xlsx";
                    saveFileDialog.Title = "Salva modello Excel";
                    saveFileDialog.FileName = "ModelloGeneratoreFlussi.xlsx";

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        // Creazione DataTable con 4 colonne
                        DataTable modello = new DataTable("FlussoDati");
                        modello.Columns.Add("Codice fiscale", typeof(string));
                        modello.Columns.Add("Totale lordo", typeof(decimal));
                        modello.Columns.Add("Reversali", typeof(string));
                        modello.Columns.Add("Importo netto", typeof(decimal));

                        // Crea file Excel
                        using (var workbook = new XLWorkbook())
                        {
                            var worksheet = workbook.Worksheets.Add(modello, "Modello");
                            worksheet.Columns().AdjustToContents();
                            workbook.SaveAs(saveFileDialog.FileName);
                        }

                        MessageBox.Show("File modello Excel generato con successo!", "Completato",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante la creazione del modello Excel: " + ex.Message, "Errore",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
