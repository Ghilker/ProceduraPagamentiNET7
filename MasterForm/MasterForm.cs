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

        public MasterForm()
        {
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
                foreach (object? procedure in Enum.GetValues(typeof(ProcedureType)))
                {
                    string itemText = procedure.ToString();
                    _ = procedureSelect.Items.Add(itemText);

                    int itemWidth = TextRenderer.MeasureText(g, itemText, procedureSelect.Font).Width;

                    // Update the maximum width if this item's width is greater
                    if (itemWidth > maxWidth)
                    {
                        maxWidth = itemWidth;
                    }
                }
            }
            procedureSelect.DropDownWidth = maxWidth + SystemInformation.VerticalScrollBarWidth;
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
            ProcedureType selectedProcedure = ProcedureType.ProceduraPagamenti;
            _ = Invoke(new MethodInvoker(() =>
            {
                string selectedText = procedureSelect.SelectedItem.ToString();
                _ = Enum.TryParse(selectedText, out selectedProcedure);
            }));

            switch (selectedProcedure)
            {
                case ProcedureType.ProceduraPagamenti:
                    FormProceduraPagamenti proceduraPagamenti = new FormProceduraPagamenti(this);
                    ShowFormInPanel(proceduraPagamenti);
                    break;
                case ProcedureType.ProceduraFlussoDiRitorno:
                    FormProceduraFlussi proceduraFlussi = new FormProceduraFlussi(this);
                    ShowFormInPanel(proceduraFlussi);
                    break;
                case ProcedureType.ProceduraStorni:
                    FormProceduraStorni proceduraStorni = new FormProceduraStorni(this);
                    ShowFormInPanel(proceduraStorni);
                    break;
            }
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
        enum ProcedureType
        {
            ProceduraPagamenti,
            ProceduraFlussoDiRitorno,
            ProceduraStorni
        }
    }
}
