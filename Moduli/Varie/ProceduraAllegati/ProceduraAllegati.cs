using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ProcedureNet7.ProceduraAllegatiSpace
{
    internal class ProceduraAllegati : BaseProcedure<ArgsProceduraAllegati>
    {
        public ProceduraAllegati(MasterForm masterForm, SqlConnection connection_string) : base(masterForm, connection_string) { }


        string selectedAA = string.Empty;
        string selectedCfFile = string.Empty;
        string selectedSavePath = string.Empty;
        string selectedTipoAllegato = string.Empty;
        string selectedNomeAllegato = string.Empty;
        string selectedBeneficio = "BS";
        string AAsplit = string.Empty;

        SqlTransaction sqlTransaction;

        private readonly Dictionary<string, string> provvedimentiItems = new()
        {
            { "01", "Riammissione come vincitore" },
            { "02", "Riammissione come idoneo" },
            { "03", "Revoca senza recupero somme" },
            { "04", "Decadenza" },
            { "05", "Modifica importo" },
            { "06", "Revoca con recupero somme" },
            { "09", "Da idoneo a vincitore" },
            { "10", "Rinuncia con recupero somme" },
            { "11", "Rinuncia senza recupero somme" },
            { "13", "Cambio status sede" }
        };
        private readonly Dictionary<string, string> decodTipoBando = new()
        {
            { "BS", "Borsa di studio" },
            { "PA", "Posto alloggio" },
            { "CI", "Contributo integrativo" },
            { "PL", "Premio di laurea" },
            { "BL", "Buono libro" }
        };
        string selectedTipoBando = string.Empty;

        List<Studente> studenti = new List<Studente>();

        public override void RunProcedure(ArgsProceduraAllegati args)
        {
            _masterForm.inProcedure = true;
            Logger.Log(0, "Inizio procedura allegati", LogLevel.INFO);

            selectedAA = args._selectedAA;
            selectedAA = "20242025";
            selectedCfFile = args._selectedFileExcel;
            selectedSavePath = args._selectedSaveFolder;
            selectedTipoAllegato = args._selectedTipoAllegato;

            //DataTable cfDaLavorare = Utilities.ReadExcelToDataTable(selectedCfFile);

            //_ = _masterForm.Invoke((MethodInvoker)delegate
            //{
            //    using FormTipoBeneficioAllegato tipoBeneficioAllegato = new(CONNECTION, selectedAA);
            //    tipoBeneficioAllegato.StartPosition = FormStartPosition.CenterParent;
            //    DialogResult result = tipoBeneficioAllegato.ShowDialog(_masterForm);
            //    if (result == DialogResult.OK)
            //    {
            //        // Retrieve the list of selected BenefitSelection objects.
            //        var selectedBenefits = tipoBeneficioAllegato.SelectedBenefici;

            //        // Process each selection.
            //        foreach (var selection in selectedBenefits)
            //        {
            //            // Log the benefit details. You can adjust the log message as needed.
            //            Logger.LogInfo(null, $"{selection.ImpegnoPair} - {selection.BenefitKey}");
            //        }
            //    }

            //});

            Logger.Log(10, "Creazione dataTable", LogLevel.INFO);
            DataTable cfDaLavorareDT = Utilities.ReadExcelToDataTable(selectedCfFile);
            List<string> cfs = new List<string>();
            foreach (DataRow row in cfDaLavorareDT.Rows)
            {
                string codFiscale = row[0].ToString().ToUpper().Trim();
                cfs.Add(codFiscale);
            }


            #region CF TABLE INIZIALE
            try
            {
                Logger.LogInfo(30, "Preparazione studenti – temp table #CFEstrazione");

                //------------------------------------------------------------------
                // 1.  (Re)create the temporary table with a PK clustered index
                //------------------------------------------------------------------
                const string tempTableScript = @"
        IF OBJECT_ID('tempdb..#CFEstrazione') IS NOT NULL
            DROP TABLE #CFEstrazione;

        CREATE TABLE #CFEstrazione
        (
            Cod_fiscale NVARCHAR(16) NOT NULL,
            CONSTRAINT PK_CFEstrazione PRIMARY KEY CLUSTERED (Cod_fiscale)
        );";

                using (var cmd = new SqlCommand(tempTableScript, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 120
                })
                {
                    cmd.ExecuteNonQuery();
                }

                //------------------------------------------------------------------
                // 2.  Bulk‑insert DISTINCT fiscal codes
                //------------------------------------------------------------------
                Logger.LogDebug(null, "Inserimento dei codici fiscali distinti");
                Logger.LogInfo(30, $"Codici fiscali da inserire: {cfs.Count}");

                var distinctCfs = new HashSet<string>(cfs, StringComparer.OrdinalIgnoreCase);

                using (var cfTable = new DataTable())
                {
                    cfTable.Columns.Add("Cod_fiscale", typeof(string));
                    foreach (var cf in distinctCfs)
                        cfTable.Rows.Add(cf);

                    using var bulk = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.TableLock, sqlTransaction)
                    {
                        DestinationTableName = "#CFEstrazione",
                        BulkCopyTimeout = 120,
                        BatchSize = 5_000
                    };
                    bulk.WriteToServer(cfTable);
                }

                //------------------------------------------------------------------
                // 3.  Update statistics (fast, keeps the optimiser happy)
                //------------------------------------------------------------------
                Logger.LogDebug(null, "Aggiornamento statistiche della tabella CF");
                using (var cmd = new SqlCommand("UPDATE STATISTICS #CFEstrazione;", CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 120
                })
                {
                    cmd.ExecuteNonQuery();
                }

                Logger.LogInfo(30, $"Temp table pronta: {distinctCfs.Count} CF distinti caricati");
            }
            catch (Exception ex)
            {
                Logger.LogError(100, $"Errore durante la preparazione della tabella temporanea dei codici fiscali. {ex}");
                throw;   // Re‑throw so the caller can handle / roll back
            }
            #endregion


            DataTable dt;
            switch (selectedTipoAllegato)
            {
                case "01": //Riammissione come vincitore
                    dt = RiammissioneVincitore(selectedAA, selectedBeneficio);
                    Utilities.ExportDataTableToExcel(dt, selectedSavePath);
                    break;
                case "02": //Riammissione come idoneo
                    dt = RiammissioneIdoneo(selectedAA, selectedBeneficio);
                    Utilities.ExportDataTableToExcel(dt, selectedSavePath);
                    break;
            }



        }

        /// <summary>
        /// Reads data for “Riammissione come vincitore” using the temp table #CFEstrazione
        /// and returns a populated DataTable ready for export.
        /// </summary>
        private DataTable RiammissioneVincitore(
    string annoAccademico,
    string codBeneficio)
        {

            // Ask the user for impegno numbers and an optional note.
            string? impegnoPrimaRata = null;
            string? impegnoSecondaRata = null;
            string? notaGenerale = null;

            // Ask for the first instalment commitment number
            _masterForm.Invoke((MethodInvoker)(() =>
            {
                impegnoPrimaRata = PromptDialog.Show(
                    "Impegno prima rata",
                    "Numero impegno della prima rata:",
                    _masterForm);           // TopMost
            }));

            if (impegnoPrimaRata is null) return new DataTable();   // user pressed Cancel

            // Ask for the second instalment commitment number
            _masterForm.Invoke((MethodInvoker)(() =>
            {
                impegnoSecondaRata = PromptDialog.Show(
                    "Impegno seconda rata",
                    "Numero impegno della seconda rata:",
                    _masterForm);
            }));

            if (impegnoSecondaRata is null) return new DataTable();

            // Ask for the optional note
            _masterForm.Invoke((MethodInvoker)(() =>
            {
                notaGenerale = PromptDialog.Show(
                    "Nota",
                    "Nota da inserire (facoltativa):",
                    _masterForm);
            }));

            // ------------------------------------------------------------------
            // 1) Build the schema
            // ------------------------------------------------------------------
            var table = new DataTable("RiammissioneVincitore");
            table.Columns.AddRange(new[]
            {
        new DataColumn("N°"                       , typeof(int)),
        new DataColumn("NumDomanda"               , typeof(int)),
        new DataColumn("CodFiscale"               , typeof(string)),
        new DataColumn("Nome"                     , typeof(string)),
        new DataColumn("Cognome"                  , typeof(string)),
        new DataColumn("ImportoBeneficio"         , typeof(decimal)),
        new DataColumn($"Imp. {impegnoPrimaRata}"  , typeof(decimal)),
        new DataColumn($"Imp. {impegnoSecondaRata}", typeof(decimal)),
        new DataColumn("Nota"                     , typeof(string))
    });

            // ------------------------------------------------------------------
            // 2) SQL with proper JOINs – uses @AnnoAcc, @CodBeneficio
            // ------------------------------------------------------------------
            string sql = $@"
        SELECT
            d.Num_domanda,
            s.Cod_fiscale,
            s.Nome,
            s.Cognome,
            ec.Cod_beneficio        AS Beneficio,
            ec.Imp_beneficio        AS ImportoBeneficio
        FROM   #CFEstrazione               cf
        JOIN   Studente                    s  ON s.Cod_fiscale   = cf.Cod_fiscale
        JOIN   Domanda                     d  ON d.Cod_fiscale   = s.Cod_fiscale
        JOIN   vEsiti_concorsi             ec ON ec.Num_domanda  = d.Num_domanda
                                             AND ec.Anno_accademico = d.Anno_accademico
        WHERE  d.tipo_bando = 'lz' and d.anno_accademico = {annoAccademico} and ec.cod_beneficio = '{codBeneficio}'
        ORDER  BY s.Cognome, s.Nome;
    ";

            using var cmd = new SqlCommand(sql, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 120
            };

            // ------------------------------------------------------------------
            // 3) Fill the DataTable
            // ------------------------------------------------------------------
            using var rdr = cmd.ExecuteReader();
            int n = 1;

            while (rdr.Read())
            {
                decimal importoTotale = rdr.GetDecimal(rdr.GetOrdinal("ImportoBeneficio"));
                decimal importoPrimo = Math.Round(importoTotale / 2m, 2, MidpointRounding.AwayFromZero);
                decimal importoSecond = importoTotale - importoPrimo;

                table.Rows.Add(
                    n++,                                                // N°
                    rdr.GetDecimal(rdr.GetOrdinal("Num_domanda")),
                    rdr.GetString(rdr.GetOrdinal("Cod_fiscale")),
                    rdr.GetString(rdr.GetOrdinal("Nome")),
                    rdr.GetString(rdr.GetOrdinal("Cognome")),
                    importoTotale,
                    importoPrimo,
                    importoSecond,
                    notaGenerale ?? string.Empty);
            }

            Logger.LogInfo(null, $"Creati {table.Rows.Count} record per riammissione idoneo.");
            return table;
        }
        private DataTable RiammissioneIdoneo(
    string annoAccademico,
    string codBeneficio)
        {
            string? notaGenerale = null;

            // Ask for the optional note
            _masterForm.Invoke((MethodInvoker)(() =>
            {
                notaGenerale = PromptDialog.Show(
                    "Nota",
                    "Nota da inserire:",
                    _masterForm);
            }));

            // ------------------------------------------------------------------
            // 1) Build the schema
            // ------------------------------------------------------------------
            var table = new DataTable("RiammissioneIdonei");
            table.Columns.AddRange(new[]
            {
        new DataColumn("N°"                       , typeof(int)),
        new DataColumn("NumDomanda"               , typeof(int)),
        new DataColumn("CodFiscale"               , typeof(string)),
        new DataColumn("Nome"                     , typeof(string)),
        new DataColumn("Cognome"                  , typeof(string)),
        new DataColumn("Nota"                     , typeof(string))
    });

            // ------------------------------------------------------------------
            // 2) SQL with proper JOINs – uses @AnnoAcc, @CodBeneficio
            // ------------------------------------------------------------------
            string sql = $@"
        SELECT
            d.Num_domanda,
            s.Cod_fiscale,
            s.Nome,
            s.Cognome,
            ec.Cod_beneficio        AS Beneficio,
            ec.Imp_beneficio        AS ImportoBeneficio
        FROM   #CFEstrazione               cf
        JOIN   Studente                    s  ON s.Cod_fiscale   = cf.Cod_fiscale
        JOIN   Domanda                     d  ON d.Cod_fiscale   = s.Cod_fiscale
        JOIN   vEsiti_concorsi             ec ON ec.Num_domanda  = d.Num_domanda
                                             AND ec.Anno_accademico = d.Anno_accademico
        WHERE  d.tipo_bando = 'lz' and d.anno_accademico = {annoAccademico} and ec.cod_beneficio = '{codBeneficio}'
        ORDER  BY s.Cognome, s.Nome;
    ";

            using var cmd = new SqlCommand(sql, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 120
            };

            // ------------------------------------------------------------------
            // 3) Fill the DataTable
            // ------------------------------------------------------------------
            using var rdr = cmd.ExecuteReader();
            int n = 1;

            while (rdr.Read())
            {
                table.Rows.Add(
                    n++,                                                // N°
                    rdr.GetDecimal(rdr.GetOrdinal("Num_domanda")),
                    rdr.GetString(rdr.GetOrdinal("Cod_fiscale")),
                    rdr.GetString(rdr.GetOrdinal("Nome")),
                    rdr.GetString(rdr.GetOrdinal("Cognome")),
                    notaGenerale ?? string.Empty);
            }

            Logger.LogInfo(null, $"Creati {table.Rows.Count} record per riammissione idoneo.");
            return table;
        }

    }
}

/// <summary>
/// A very small modal dialog that shows a label + a textbox + OK/Cancel.
/// Usage:  var text = PromptDialog.Show("Title", "Question?");
/// Returns null if the user presses Cancel.
/// </summary>
internal sealed class PromptDialog : Form
{
    private readonly TextBox _textBox = new() { Dock = DockStyle.Fill };

    private PromptDialog(string title, string question)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new(300, 90);

        Controls.AddRange(new System.Windows.Forms.Control[]
        {
            new Label
            {
                Text      = question,
                Dock      = DockStyle.Top,
                AutoSize  = true
            },
            _textBox,
            new FlowLayoutPanel
            {
                Dock          = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height        = 35,
                Controls =
                {
                    new Button { Text = "OK",     DialogResult = DialogResult.OK },
                    new Button { Text = "Cancel", DialogResult = DialogResult.Cancel }
                }
            }
                });


        AcceptButton = Controls.OfType<FlowLayoutPanel>().First().Controls[0] as Button;
        CancelButton = Controls.OfType<FlowLayoutPanel>().First().Controls[1] as Button;
    }

    public static string? Show(string title, string question, IWin32Window? owner = null)
    {
        using var dlg = new PromptDialog(title, question);
        return dlg.ShowDialog(owner) == DialogResult.OK
            ? dlg._textBox.Text.Trim()
            : null;
    }
}
