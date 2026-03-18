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
    public partial class FormGenerazioneFileRevoche : Form
    {
        MasterForm? _masterForm;
        string selectedFolderPath;
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
            { "1", "Roma 2" },
            { "2", "Roma 3" },
            { "3", "Cassino" },
            { "4", "Viterbo" },
        };
        void Initialize()
        {
            genRevEnteComboBox.Items.Clear();
            foreach (KeyValuePair<string, string> item in genRevEnti)
            {
                _ = genRevEnteComboBox.Items.Add(new { Text = item.Value, Value = item.Key });
            }

            genRevEnteComboBox.DisplayMember = "Text";
            genRevEnteComboBox.ValueMember = "Value";
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunGenerazioneFileRevoche);
        }

        private void RunGenerazioneFileRevoche(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                string selectedEnte = "";
                _ = Invoke(new MethodInvoker(() =>
                {
                    dynamic selectedItem = genRevEnteComboBox.SelectedItem;
                    selectedEnte = selectedItem?.Value ?? "";
                }));
                ArgsGenerazioneFileRevoche _argsGenerazioneFileRevoche = new ArgsGenerazioneFileRevoche
                {
                    _aaGenerazioneRev = genRevAAText.Text,
                    _selectedCodEnte = selectedEnte,
                    _selectedFolderPath = selectedFolderPath,
                };
                argsValidation.Validate(_argsGenerazioneFileRevoche);
                GenerazioneFileRevoche GenerazioneFileRevoche = new(_masterForm, mainConnection);
                GenerazioneFileRevoche.RunProcedure(_argsGenerazioneFileRevoche);
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

        private void genRevSaveBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(genRevSaveLbl, folderBrowserDialog, ref selectedFolderPath);
        }
    }
}
