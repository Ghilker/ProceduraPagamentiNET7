using ProcedureNet7.Storni;
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace ProcedureNet7
{
    public partial class MainUI : Form
    {
        private string? selectedFolderPath;
        private string? selectedFilePath;
        private string? selectedFilePathSecondary;

        public string? CONNECTION_STRING;

        public bool inProcedure = false;

        private Logger logger;
        public MainUI()
        {
            inProcedure = false;
            InitializeComponent();
            AddProcedures();
            DisableAllPanels();
            LoadCredentialsDropdown();

            logger = Logger.GetInstance(this, progressBar, progressReport, LogLevel.DEBUG);
            logger.ClearLogs();

            backgroundWorker.WorkerReportsProgress = true;
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
            backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;

            this.FormClosing += Main_FormClosing;
        }
        private void Main_FormClosing(object? sender, FormClosingEventArgs e)
        {
            logger?.Stop();
        }

        private void AddProcedures()
        {
            chooseProcedure.Items.Clear();
            foreach (object? procedure in Enum.GetValues(typeof(ProcedureType)))
            {
                _ = chooseProcedure.Items.Add(procedure);
            }
            PagamentiSettings.CreatePagamentiComboBox(ref pagamentiTipoProceduraCombo, PagamentiSettings.pagamentiTipoProcedura);
        }

        private void DisableAllPanels()
        {
            panelProceduraPagamenti.Visible = false;
            panelProceduraFlussoDiRitorno.Visible = false;
            panelStorni.Visible = false;
        }

        private void ToggleProcedurePanels(ProcedureType selectedProcedure)
        {
            logger.ClearLogs();
            panelProceduraPagamenti.Visible = false;
            panelProceduraFlussoDiRitorno.Visible = false;
            panelStorni.Visible = false;
            selectedFolderPath = null;
            selectedFilePath = null;
            selectedFilePathSecondary = null;

            switch (selectedProcedure)
            {
                case ProcedureType.ProceduraPagamenti:
                    panelProceduraPagamenti.Visible = true;
                    break;
                case ProcedureType.ProceduraFlussoDiRitorno:
                    panelProceduraFlussoDiRitorno.Visible = true;
                    break;
                case ProcedureType.ProceduraStorni:
                    panelStorni.Visible = true;
                    break;
            }
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            ProcedureType selectedProcedure = ProcedureType.ProceduraPagamenti;
            _ = Invoke(new MethodInvoker(() =>
            {
                selectedProcedure = (ProcedureType)chooseProcedure.SelectedItem;
            }));

            ArgsValidation argsValidation = new ArgsValidation();
            try
            {
                switch (selectedProcedure)
                {
                    case ProcedureType.ProceduraPagamenti:

                        string pagamentiSelectedTipoProcedura = "";
                        string pagamentiRiepilogoTipoProcedura = "";
                        _ = Invoke(new MethodInvoker(() =>
                        {
                            dynamic selectedItem = pagamentiTipoProceduraCombo.SelectedItem;
                            pagamentiSelectedTipoProcedura = selectedItem?.Value ?? "";

                            dynamic selectedItemRiepilogo = pagamentiTipoProceduraCombo.SelectedItem;
                            pagamentiRiepilogoTipoProcedura = selectedItemRiepilogo?.Text ?? "";
                        }));

                        RiepilogoArguments.Instance.tipoProcedura = pagamentiRiepilogoTipoProcedura;

                        ArgsPagamenti argsPagamenti = new ArgsPagamenti
                        {
                            _selectedSaveFolder = selectedFolderPath,
                            _annoAccademico = pagamentiAATxt.Text,
                            _dataRiferimento = pagamentiDataRiftxt.Text,
                            _numeroMandato = pagamentiNuovoMandatoTxt.Text,
                            _tipoProcedura = pagamentiSelectedTipoProcedura,
                            _vecchioMandato = pagamentiOldMandatoTxt.Text,
                            _filtroManuale = proceduraPagamentiFiltroCheck.Checked,
                        };
                        argsValidation.Validate(argsPagamenti);
                        using (ProceduraPagamenti pagamenti = new ProceduraPagamenti(this, CONNECTION_STRING))
                        {
                            pagamenti.RunProcedure(argsPagamenti);
                        }
                        break;
                    case ProcedureType.ProceduraFlussoDiRitorno:

                        bool useFlussoNome = proceduraFlussoRitornoNomeFileCheck.Checked;
                        string nomeFile = Path.GetFileNameWithoutExtension(selectedFilePath);

                        ArgsProceduraFlussoDiRitorno argsFlusso = new ArgsProceduraFlussoDiRitorno
                        {
                            _selectedFileFlusso = selectedFilePath,
                            _selectedImpegnoProvv = useFlussoNome ? nomeFile : proceduraFlussoRitornoNumMandatoTxt.Text,
                            _selectedTipoBando = proceduraFlussoRitornoTipoBandoTxt.Text
                        };
                        argsValidation.Validate(argsFlusso);
                        using (ProceduraFlussoDiRitorno flusso = new ProceduraFlussoDiRitorno(this, CONNECTION_STRING))
                        {
                            flusso.RunProcedure(argsFlusso);
                        }
                        break;
                    case ProcedureType.ProceduraStorni:
                        ArgsProceduraStorni argsStorni = new ArgsProceduraStorni
                        {
                            _selectedFile = selectedFilePath,
                            _esercizioFinanziario = storniSelectedEseFinanziarioTxt.Text
                        };
                        argsValidation.Validate(argsStorni);
                        using (ProceduraStorni storni = new ProceduraStorni(this, CONNECTION_STRING))
                        {
                            storni.RunProcedure(argsStorni);
                        }
                        break;
                }
            }
            catch (ValidationException ex)
            {
                Logger.Log(100, "Errore compilazione procedura: " + ex.Message, LogLevel.WARN);
                inProcedure = false;
            }
        }

        private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = e.ProgressPercentage;

            if (e.UserState is string message)
            {
                // Update your UI with the message
                progressReport.AppendText(message + Environment.NewLine);
            }
        }

        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //ReportProgress(100, "Fine lavorazione");
        }

        private void StartProcedureBtn_Click(object sender, EventArgs e)
        {
            if (inProcedure)
            {
                _ = MessageBox.Show("Wait for the previous procedure to finish!");
                return;
            }

            if (chooseProcedure.SelectedIndex == -1)
            {
                _ = MessageBox.Show("Please select a procedure.");
                return;
            }

            Dictionary<string, Hashtable> credentials = new();
            Hashtable credential = new();

            // Load credentials from file if text fields are empty
            if (string.IsNullOrEmpty(serverIP.Text) &&
                string.IsNullOrEmpty(databaseName.Text) &&
                string.IsNullOrEmpty(userID.Text) &&
                string.IsNullOrEmpty(password.Text))
            {
                credentials = SaveCredentials.LoadCredentialsFromFile();
                if (credentials == null)
                {
                    _ = MessageBox.Show("No saved credentials found. Please enter the connection details.");
                    return;
                }
            }
            else
            {
                // Update credentials from text boxes
                credential["serverIP"] = serverIP.Text;
                credential["databaseName"] = databaseName.Text;
                credential["userID"] = userID.Text;
                credential["password"] = password.Text;
            }

            // Save the credentials to a file if the checkbox is checked
            if (memorizeConnectionCheckBox.Checked)
            {
                SaveCredentials.SaveCredentialsToFile(userID.Text, credential);
            }

            // Construct the connection string
            CONNECTION_STRING = $"Server={credential["serverIP"]};Database={credential["databaseName"]};User Id={credential["userID"]};Password={credential["password"]};";

            // Check if the database can be reached
            if (!Utilities.CanConnectToDatabase(CONNECTION_STRING))
            {
                _ = MessageBox.Show("Unable to connect to the database. Please check your connection details.");
                return;
            }

            try
            {
                backgroundWorker.RunWorkerAsync();
                inProcedure = true;
            }
            catch (Exception ex)
            {
                inProcedure = false;
                Logger.LogError(0, $"Error: {ex.Message}");
            }
        }

        private void LoadCredentialsDropdown()
        {
            var allCredentials = SaveCredentials.LoadCredentialsFromFile();
            if (allCredentials != null)
            {
                foreach (var entry in allCredentials.Keys)
                {
                    credentialDropdownCombo.Items.Add(entry); // Assuming `credentialsDropdown` is your dropdown control
                }
            }
        }

        private void credentialDropdownCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedIdentifier = credentialDropdownCombo.SelectedItem.ToString();
            var allCredentials = SaveCredentials.LoadCredentialsFromFile();
            if (allCredentials != null && allCredentials.ContainsKey(selectedIdentifier))
            {
                Hashtable credentials = allCredentials[selectedIdentifier];
                // Now set your text fields based on `credentials` Hashtable
                serverIP.Text = credentials["serverIP"]?.ToString();
                databaseName.Text = credentials["databaseName"]?.ToString();
                userID.Text = credentials["userID"]?.ToString();
                password.Text = credentials["password"]?.ToString();
            }
        }

        private void ChooseProcedure_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (chooseProcedure.SelectedIndex != -1)
            {
                ProcedureType selectedProcedure = (ProcedureType)chooseProcedure.SelectedItem;
                ToggleProcedurePanels(selectedProcedure);
            }
        }
        private void proceduraFlussoRitornoFileBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(proceduraFlussoRitornoFileLbl, openFileDialog, ref selectedFilePath);
        }

        private void pagamentiSalvataggioBTN_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFolder(pagamentiSalvataggiolbl, folderBrowserDialog, ref selectedFolderPath);
        }

        private void storniFileBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(storniFilelbl, openFileDialog, ref selectedFilePath);
        }
    }

    public enum ProcedureType
    {
        ProceduraPagamenti,
        ProceduraFlussoDiRitorno,
        ProceduraStorni
    }
}
