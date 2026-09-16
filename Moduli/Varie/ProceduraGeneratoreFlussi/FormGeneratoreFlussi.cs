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
            label1.Text = "GENERATORE FLUSSI";
            openFileDialog.Filter = "File Excel (*.xlsx)|*.xlsx";
            openFileDialog.CheckFileExists = true;
            Controls.Add(new Label
            {
                Location = new Point(29, 145), Size = new Size(735, 125),
                Text = "Compilare il primo foglio del modello Excel. Reversali vuote = zero.\r\n" +
                    "Il netto deve coincidere con lordo meno reversali. Importi con massimo 2 decimali.\r\n" +
                    "Impegno facoltativo: lasciarlo tutto vuoto per un flusso unico; altrimenti compilare ogni riga.\r\n" +
                    "Le anomalie bloccano la generazione e vengono elencate in un report.\r\n" +
                    "I flussi e il riepilogo vengono salvati nella sottocartella flussi_gg_mm_aaaa."
            });
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            // Congela i percorsi sul thread UI prima di avviare il lavoro.
            var args = new ArgsProceduraGeneratoreFlussi
            {
                FilePath = selectedFilePath,
                FolderPath = selectedFolderPath
            };
            try
            {
                new ArgsValidation().Validate(args);
                if (!File.Exists(args.FilePath))
                    throw new ValidationException("Il file Excel selezionato non esiste.");
                _masterForm.RunBackgroundWorker(connection => RunGeneratoreFlussi(connection, args));
            }
            catch (ValidationException ex)
            {
                MessageBox.Show(this, ex.Message, "Dati mancanti", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RunGeneratoreFlussi(SqlConnection mainConnection, ArgsProceduraGeneratoreFlussi args)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                using ProceduraGeneratoreFlussi proceduraGeneratoreFlussi = new(_masterForm, mainConnection);
                proceduraGeneratoreFlussi.RunProcedure(args);
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
                        GeneratoreFlussiFile.CreaModello(saveFileDialog.FileName);

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
