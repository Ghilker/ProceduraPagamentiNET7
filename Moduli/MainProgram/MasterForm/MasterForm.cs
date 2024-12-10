
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ProcedureNet7
{
    public partial class MasterForm : Form
    {
        public delegate void RunProcedureDelegate(SqlConnection sqlConnection);
        public SqlConnection? MainConnection { get; private set; }
        private BackgroundWorker? _backgroundWorker = null;
        private Form? currentForm = null;
        Logger? logger;

        public bool inProcedure = false;

        private string userTier;

        public MasterForm(int userID, string userTier)
        {
            this.userTier = "Programmatore";
            InitializeComponent();
            Initialize();
            InitializeBackgroundWorker();
        }

        private void Initialize()
        {
            ConnectionForm connectionForm = new ConnectionForm(this);
            connectionForm.TopLevel = false;
            connectionPanel.Controls.Add(connectionForm);
            connectionForm.Location = new Point(0, 0);
            connectionForm.Show();

            logger = Logger.GetInstance(this, progressBar, masterLogbox, LogLevel.DEBUG);
            logger.ClearLogs();

            FillProcedureSelectDropDown();
        }

        private void InitializeBackgroundWorker()
        {
            _backgroundWorker = new BackgroundWorker();
            _backgroundWorker.DoWork += new DoWorkEventHandler(BackgroundWorker_DoWork);
        }

        private void FillProcedureSelectDropDown()
        {
            procedureSelect.Items.Clear();
            int maxWidth = 0;
            using (Graphics g = procedureSelect.CreateGraphics())
            {
                var procedures = new List<string>();

#if PAGAMENTI || DEBUG
                // Add ProcedurePagamenti
                foreach (var value in Enum.GetValues(typeof(ProcedurePagamenti)))
                {
                    var fieldInfo = typeof(ProcedurePagamenti).GetField(value.ToString());
                    var attribute = (ProcedureCategoryAttribute)fieldInfo.GetCustomAttribute(typeof(ProcedureCategoryAttribute));
                    if (IsTierAllowed(attribute.Tier))
                    {
                        procedures.Add($"{attribute.Category} - {value}");
                    }
                }
#endif

#if VARIE || DEBUG
                // Add ProcedureVarie
                foreach (var value in Enum.GetValues(typeof(ProcedureVarie)))
                {
                    var fieldInfo = typeof(ProcedureVarie).GetField(value.ToString());
                    var attribute = (ProcedureCategoryAttribute)fieldInfo.GetCustomAttribute(typeof(ProcedureCategoryAttribute));
                    if (IsTierAllowed(attribute.Tier))
                    {
                        procedures.Add($"{attribute.Category} - {value}");
                    }
                }
#endif
#if VERIFICHE || DEBUG
                foreach (var value in Enum.GetValues(typeof(ProcedureVerifiche)))
                {
                    var fieldInfo = typeof(ProcedureVerifiche).GetField(value.ToString());
                    var attribute = (ProcedureCategoryAttribute)fieldInfo.GetCustomAttribute(typeof(ProcedureCategoryAttribute));
                    if (IsTierAllowed(attribute.Tier))
                    {
                        procedures.Add($"{attribute.Category} - {value}");
                    }
                }
#endif

                procedureSelect.DataSource = procedures;
            }
            procedureSelect.DropDownWidth = maxWidth + SystemInformation.VerticalScrollBarWidth;
        }

        private bool IsTierAllowed(string procedureTier)
        {
            var tiers = new Dictionary<string, int>
            {
                { "Operatore", 1 },
                { "Funzionario", 2 },
                { "Programmatore", 3 }
            };

            return tiers[userTier] >= tiers[procedureTier];
        }

        private void ShowFormInPanel(Form form)
        {
            if (currentForm != null)
            {
                ClearCurrentForm();
            }
            currentForm = form;
            currentForm.TopLevel = false;
            proceduresPanel.Controls.Clear();
            proceduresPanel.Controls.Add(currentForm);
            currentForm.Show();
        }
        private void ClearCurrentForm()
        {
            if (currentForm != null)
            {
                currentForm.Close();
                currentForm.Dispose();
            }
        }

        public void SetupConnection(SqlConnection? connection)
        {
            MainConnection = connection;
        }

        private void ProcedureSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            ClearCurrentForm();
            string selectedText = procedureSelect.SelectedItem.ToString();
            string[] parts = selectedText.Split(new[] { " - " }, StringSplitOptions.None);
            string category = parts[0];
            string procedureName = parts[1];
            _ = Invoke(new MethodInvoker(() =>
            {
                Logger.LogInfo(null, $"Selezionata la procedura: {procedureName}");
#if PAGAMENTI || DEBUG
                if (category == "Pagamenti" && Enum.TryParse(procedureName, out ProcedurePagamenti selectedPagamentiProcedure))
                {
                    switch (selectedPagamentiProcedure)
                    {
                        case ProcedurePagamenti.ProceduraPagamenti:
                            FormProceduraPagamenti proceduraPagamenti = new FormProceduraPagamenti(this);
                            ShowFormInPanel(proceduraPagamenti);
                            break;
                        case ProcedurePagamenti.ProceduraFlussoDiRitorno:
                            FormProceduraFlussi proceduraFlussi = new FormProceduraFlussi(this);
                            ShowFormInPanel(proceduraFlussi);
                            break;
                        case ProcedurePagamenti.ProceduraStorni:
                            FormProceduraStorni proceduraStorni = new FormProceduraStorni(this);
                            ShowFormInPanel(proceduraStorni);
                            break;
                        case ProcedurePagamenti.ProceduraRendiconto:
                            FormProceduraRendiconto proceduraRendiconto = new FormProceduraRendiconto(this);
                            ShowFormInPanel(proceduraRendiconto);
                            break;
                    }
                }

#endif
#if VARIE || DEBUG
                if (category == "Varie" && Enum.TryParse(procedureName, out ProcedureVarie selectedVarieProcedure))
                {
                    switch (selectedVarieProcedure)
                    {
                        case ProcedureVarie.ProceduraBlocchi:
                            FormProceduraBlocchi proceduraBlocchi = new FormProceduraBlocchi(this);
                            ShowFormInPanel(proceduraBlocchi);
                            break;
                        case ProcedureVarie.ProceduraTicket:
                            FormProceduraTicket proceduraTicket = new FormProceduraTicket(this);
                            ShowFormInPanel(proceduraTicket);
                            break;
                        case ProcedureVarie.ProceduraAllegati:
                            FormProceduraAllegati proceduraAllegati = new FormProceduraAllegati(this);
                            ShowFormInPanel(proceduraAllegati);
                            break;
                        case ProcedureVarie.SpecificheImpegni:
                            FormSpecificheImpegni specificheImpegni = new FormSpecificheImpegni(this);
                            ShowFormInPanel(specificheImpegni);
                            break;
                        case ProcedureVarie.ProceduraAggiuntaProvvedimento:
                            FormAggiuntaProvvedimenti provvedimenti = new FormAggiuntaProvvedimenti(this);
                            ShowFormInPanel(provvedimenti);
                            break;
                        case ProcedureVarie.EstrazionePermessiSoggiorno:
                            FormProceduraEstrazionePermessiSoggiorno estrazionePermessiSoggiorno = new FormProceduraEstrazionePermessiSoggiorno(this);
                            ShowFormInPanel(estrazionePermessiSoggiorno);
                            break;
                        case ProcedureVarie.ControlloISEEUP:
                            FormControlloISEEUP controlloISEEUP = new FormControlloISEEUP(this);
                            ShowFormInPanel(controlloISEEUP);
                            break;
                        case ProcedureVarie.ProceduraControlloPS:
                            FormProceduraControlloPS controlloPS = new FormProceduraControlloPS(this);
                            ShowFormInPanel(controlloPS);
                            break;
                        case ProcedureVarie.ProceduraRendicontoMiur:
                            FormRendicontoMiur rendicontoMiur = new FormRendicontoMiur(this);
                            ShowFormInPanel(rendicontoMiur);
                            break;
                    }
                }
#endif
#if VERIFICHE || DEBUG
                if (category == "Verifiche" && Enum.TryParse(procedureName, out ProcedureVerifiche selectedVerificheProcedure))
                {
                    switch (selectedVerificheProcedure)
                    {
                        case ProcedureVerifiche.ProceduraControlloPuntiBonus:
                            FormControlloPuntiBonus controlloPuntiBonus = new FormControlloPuntiBonus(this);
                            ShowFormInPanel(controlloPuntiBonus);
                            break;
                        case ProcedureVerifiche.Verifica:
                            FormVerifica formVerifica = new FormVerifica(this);
                            ShowFormInPanel(formVerifica);
                            break;
                        case ProcedureVerifiche.ElaborazioneFileUni:
                            FormElaborazioneFileUni formElaborazioneFileUni = new FormElaborazioneFileUni(this);
                            ShowFormInPanel(formElaborazioneFileUni);
                            break;
                    }
                }
#endif
            }));
        }

        private void BackgroundWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            try
            {
                if (e.Argument is RunProcedureDelegate runProcedure)
                {
                    if (MainConnection == null || !Utilities.CanConnectToDatabase(MainConnection))
                    {
                        throw new Exception("Connettersi al database prima di continuare!");
                    }
                    runProcedure(MainConnection);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"ERROR: {ex.Message}");
            }
        }

        public void RunBackgroundWorker(RunProcedureDelegate runProcedure)
        {
            if (runProcedure is null)
            {
                throw new ArgumentNullException(nameof(runProcedure));
            }

            if (backgroundWorker.IsBusy)
            {
                Logger.LogWarning(null, "Attendere che la procedura finisca!");
                return;
            }

            backgroundWorker.RunWorkerAsync(runProcedure);
        }

        public Panel GetProcedurePanel()
        {
            return proceduresPanel;
        }

        private void ChangeUserButton_Click(object sender, EventArgs e)
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\ProcedureNet7", false);
            Application.Restart();
        }

        public enum ProcedurePagamenti
        {
            [ProcedureCategory("Pagamenti", "Programmatore")]
            ProceduraPagamenti,
            [ProcedureCategory("Pagamenti", "Funzionario")]
            ProceduraFlussoDiRitorno,
            [ProcedureCategory("Pagamenti", "Funzionario")]
            ProceduraStorni,
            [ProcedureCategory("Pagamenti", "Funzionario")]
            ProceduraRendiconto
        }

        public enum ProcedureVarie
        {
            [ProcedureCategory("Varie", "Funzionario")]
            ProceduraBlocchi,
            [ProcedureCategory("Varie", "Programmatore")]
            ProceduraTicket,
            [ProcedureCategory("Varie", "Programmatore")]
            ProceduraAggiuntaProvvedimento,
            [ProcedureCategory("Varie", "Operatore")]
            ProceduraAllegati,
            [ProcedureCategory("Varie", "Funzionario")]
            SpecificheImpegni,
            [ProcedureCategory("Varie", "Funzionario")]
            EstrazionePermessiSoggiorno,
            [ProcedureCategory("Varie", "Funzionario")]
            ControlloISEEUP,
            [ProcedureCategory("Varie", "Funzionario")]
            ProceduraControlloPS,
            [ProcedureCategory("Varie", "Funzionario")]
            ProceduraRendicontoMiur
        }

        public enum ProcedureVerifiche
        {
            [ProcedureCategory("Verifiche", "Programmatore")]
            ProceduraControlloPuntiBonus,
            [ProcedureCategory("Verifiche", "Programmatore")]
            Verifica,
            [ProcedureCategory("Verifiche", "Programmatore")]
            ElaborazioneFileUni
        }

    }
}
