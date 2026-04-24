using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class FormGenerazioneFileRevoche : Form
    {
        MasterForm? _masterForm;

        string selectedFolderPath = "";

        public FormGenerazioneFileRevoche(MasterForm masterForm)
        {
            _masterForm = masterForm;

            InitializeComponent();
            Initialize();
        }

        private readonly Dictionary<string, string> genRevEnti = new()
        {
            { "-1", "Tutti gli enti e sedi" },
            { "0", "Roma 1" },
            { "1", "Roma 2 / Roma 3" },
            { "2", "Cassino" },
            { "3", "Viterbo" }
        };

        private void Initialize()
        {
            genRevEnteComboBox.Items.Clear();

            foreach (var item in genRevEnti)
            {
                genRevEnteComboBox.Items.Add(
                    new { Text = item.Value, Value = item.Key });
            }

            genRevEnteComboBox.DisplayMember = "Text";
            genRevEnteComboBox.ValueMember = "Value";
            genRevEnteComboBox.SelectedIndex = 0;
        }

        // =====================================================
        // FASE 1
        // =====================================================
        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
                return;

            _masterForm.RunBackgroundWorker(RunGenerazioneFileRevoche);
        }

        private void RunGenerazioneFileRevoche(SqlConnection mainConnection)
        {
            try
            {
                dynamic selectedItem = null;

                Invoke(new MethodInvoker(() =>
                {
                    selectedItem = genRevEnteComboBox.SelectedItem;
                }));

                string selectedEnte =
                    selectedItem?.Value?.ToString() ?? "-1";

                var args = new ArgsGenerazioneFileRevoche
                {
                    _aaGenerazioneRev = genRevAAText.Text,
                    _selectedCodEnte = selectedEnte,
                    _selectedFolderPath = selectedFolderPath
                };

                ArgsValidation validation = new();
                validation.Validate(args);

                var proc =
                    new GenerazioneFileRevoche(
                        _masterForm,
                        mainConnection);

                proc.RunProcedure(args);
            }
            catch (ValidationException ex)
            {
                Logger.LogWarning(100, ex.Message);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(100, ex.Message);
            }
        }

        // =====================================================
        // FASE 2
        // =====================================================
        private void btnGeneraLettere_Click(object sender, EventArgs e)
        {
            if (_masterForm == null)
                return;

            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description =
                    "Seleziona la cartella contenente le revoche generate";

                dlg.UseDescriptionForTitle = true;

                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                selectedFolderPath = dlg.SelectedPath;
            }

            _masterForm.RunBackgroundWorker(
                RunGenerazioneLettere);
        }

        private void RunGenerazioneLettere(SqlConnection mainConnection)
        {
            try
            {
                GenerazioneLettereRevoche proc =
                    new GenerazioneLettereRevoche(
                        selectedFolderPath);

                proc.RunProcedure();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(100, ex.Message);

                MessageBox.Show(
                    ex.Message,
                    "Errore",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // =====================================================
        // CARTELLA
        // =====================================================
        private void genRevSaveBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(
                genRevSaveLbl,
                folderBrowserDialog,
                ref selectedFolderPath);
        }

    }
}