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
        private string selectedFolderPath = string.Empty;
        private string selectedFilePath = string.Empty;
        private string selectedFilePathSecondary = string.Empty;

        public string CONNECTION_STRING = string.Empty;

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
            logger.Dispose();
            backgroundWorker.Dispose();
        }

        private void AddProcedures()
        {
            chooseProcedure.Items.Clear();
            int maxWidth = 0;
            using (Graphics g = chooseProcedure.CreateGraphics())
            {
                foreach (object? procedure in Enum.GetValues(typeof(ProcedureType)))
                {
                    string itemText = procedure.ToString();
                    _ = chooseProcedure.Items.Add(itemText);

                    int itemWidth = TextRenderer.MeasureText(g, itemText, chooseProcedure.Font).Width;

                    // Update the maximum width if this item's width is greater
                    if (itemWidth > maxWidth)
                    {
                        maxWidth = itemWidth;
                    }
                }
            }

            chooseProcedure.DropDownWidth = maxWidth + SystemInformation.VerticalScrollBarWidth;

            PagamentiSettings.CreatePagamentiComboBox(ref pagamentiTipoProceduraCombo, PagamentiSettings.pagamentiTipoProcedura);
        }

        private void DisableAllPanels()
        {
            panelProceduraPagamenti.Visible = false;
            panelProceduraFlussoDiRitorno.Visible = false;
            panelStorni.Visible = false;
            panelProceduraControlloIBAN.Visible = false;
        }

        private void ToggleProcedurePanels(ProcedureType selectedProcedure)
        {
            logger.ClearLogs();
            panelProceduraPagamenti.Visible = false;
            panelProceduraFlussoDiRitorno.Visible = false;
            panelStorni.Visible = false;
            panelProceduraControlloIBAN.Visible = false;
            selectedFolderPath = string.Empty;
            selectedFilePath = string.Empty;
            selectedFilePathSecondary = string.Empty;

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
                case ProcedureType.ProceduraControlloIBAN:
                    panelProceduraControlloIBAN.Visible = true;
                    break;
            }
        }

        private void BackgroundWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            ProcedureType selectedProcedure = ProcedureType.ProceduraPagamenti;
            _ = Invoke(new MethodInvoker(() =>
            {
                string selectedText = chooseProcedure.SelectedItem.ToString();
                _ = Enum.TryParse(selectedText, out selectedProcedure);
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
                    case ProcedureType.ProceduraControlloIBAN:
                        ArgsProceduraControlloIBAN argsIBAN = new ArgsProceduraControlloIBAN
                        {
                            _annoAccademico = controlloIbanAATxt.Text
                        };
                        argsValidation.Validate(argsIBAN);
                        using (ProceduraControlloIBAN controlloIBAN = new ProceduraControlloIBAN(this, CONNECTION_STRING))
                        {
                            controlloIBAN.RunProcedure(argsIBAN);
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

        private void BackgroundWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = e.ProgressPercentage;

            if (e.UserState is string message)
            {
                // Update your UI with the message
                progressReport.AppendText(message + Environment.NewLine);
            }
        }

        private void BackgroundWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            //ReportProgress(100, "Fine lavorazione");
        }

        private void StartProcedureBtn_Click(object? sender, EventArgs e)
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
                Dictionary<string, Hashtable>? nullableCredentials = SaveCredentials.LoadCredentialsFromFile();
                if (nullableCredentials == null)
                {
                    Logger.LogWarning(null, "No saved credentials found. Please enter the connection details.");
                    return;
                }
                credentials = nullableCredentials;
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
            credentialDropdownCombo.Items.Clear(); // Clear existing items
            var allCredentials = SaveCredentials.LoadCredentialsFromFile();
            int maxWidth = 0; // Initialize a variable to keep track of the maximum width

            if (allCredentials != null)
            {
                using (Graphics g = credentialDropdownCombo.CreateGraphics())
                {
                    foreach (var entry in allCredentials)
                    {
                        if (entry.Value.ContainsKey("databaseName"))
                        {
                            string itemText = $"{entry.Key}: {entry.Value["databaseName"]}";
                            credentialDropdownCombo.Items.Add(itemText);

                            // Measure the width of the text
                            int itemWidth = TextRenderer.MeasureText(g, itemText, credentialDropdownCombo.Font).Width;

                            // Update the maximum width if this item's width is greater
                            if (itemWidth > maxWidth)
                            {
                                maxWidth = itemWidth;
                            }
                        }
                        else
                        {
                            Logger.LogDebug(null, $"Invalid credential format for key: {entry.Key}");
                        }
                    }
                }
            }
            else
            {
                Logger.LogDebug(null, "No credentials loaded from file");
            }

            // Set the dropdown width, adding some padding
            credentialDropdownCombo.DropDownWidth = maxWidth + SystemInformation.VerticalScrollBarWidth;

            if (credentialDropdownCombo.Items.Count > 0)
            {
                credentialDropdownCombo.SelectedIndex = 0; // Optionally, select the first item by default
            }
        }

        private void credentialDropdownCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string? nullableIdentifier = credentialDropdownCombo.SelectedItem.ToString();
            if (nullableIdentifier == null)
            {
                Logger.LogDebug(null, "No credentials found");
                return;
            }
            // Split the selected item to get the key part
            string[] parts = nullableIdentifier.Split(':');
            if (parts.Length < 2)
            {
                Logger.LogDebug(null, "Invalid credential format");
                return;
            }
            string selectedIdentifier = parts[0].Trim();
            var allCredentials = SaveCredentials.LoadCredentialsFromFile();
            if (allCredentials != null && allCredentials.TryGetValue(selectedIdentifier, out Hashtable? value))
            {
                Hashtable credentials = value;
                // Now set your text fields based on `credentials` Hashtable
                serverIP.Text = credentials["serverIP"]?.ToString();
                databaseName.Text = credentials["databaseName"]?.ToString();
                userID.Text = credentials["userID"]?.ToString();
                password.Text = credentials["password"]?.ToString();
            }
        }

        private void ChooseProcedure_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (chooseProcedure.SelectedIndex != -1)
            {
                string selectedText = chooseProcedure.SelectedItem.ToString();
                if (Enum.TryParse<ProcedureType>(selectedText, out var selectedProcedure))
                {
                    ToggleProcedurePanels(selectedProcedure);
                }
                else
                {
                    // Handle the case where the selected item cannot be parsed to a ProcedureType
                    MessageBox.Show("Invalid procedure selected.");
                }
            }
        }

        private void proceduraFlussoRitornoFileBtn_Click(object? sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(proceduraFlussoRitornoFileLbl, openFileDialog, ref selectedFilePath);
        }

        private void pagamentiSalvataggioBTN_Click(object? sender, EventArgs e)
        {
            Utilities.ChooseFolder(pagamentiSalvataggiolbl, folderBrowserDialog, ref selectedFolderPath);
        }

        private void storniFileBtn_Click(object? sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(storniFilelbl, openFileDialog, ref selectedFilePath);
        }
    }

    public enum ProcedureType
    {
        ProceduraPagamenti,
        ProceduraFlussoDiRitorno,
        ProceduraStorni,
        ProceduraControlloIBAN
    }
}
