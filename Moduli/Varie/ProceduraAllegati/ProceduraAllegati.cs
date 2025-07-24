using ClosedXML.Excel;
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

            string headerText;
            DataTable dt;
            switch (selectedTipoAllegato)
            {
                case "01": // Riammissione come vincitore
                    dt = RiammissioneVincitore(selectedAA, selectedBeneficio);
                    headerText = provvedimentiItems["01"];
                    break;
                case "02": // Riammissione come idoneo
                    dt = RiammissioneIdoneo(selectedAA, selectedBeneficio);
                    headerText = provvedimentiItems["02"];
                    break;
                case "05": // Modifica importo
                    dt = ModificaImporto(selectedAA, selectedBeneficio);
                    headerText = provvedimentiItems["05"];
                    break;
                default:
                    throw new InvalidOperationException($"Tipo allegato {selectedTipoAllegato} non gestito.");
            }

            if (dt.Rows.Count == 0)
                return;    // niente da esportare

            // 1) export and grab the file path
            string excelPath = Utilities.ExportDataTableToExcel(dt, selectedSavePath);

            FormatAndEnhanceExcel(excelPath, dt, headerText);
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
        new DataColumn($"Impegno prima rata {impegnoPrimaRata}"  , typeof(decimal)),
        new DataColumn($"Impegno saldo {impegnoSecondaRata}", typeof(decimal)),
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
        private DataTable ModificaImporto(
            string annoAccademico,
            string codBeneficio)
        {
            DataTable table = new DataTable("Modifica importo");


            string sql = $@"
        SELECT
            d.Num_domanda,
            s.Cod_fiscale,
            s.Nome,
            s.Cognome,
            si.Importo_assegnato	AS ImportoPrecedente,
            ec.Imp_beneficio        AS ImportoAttuale,
			(ec.Imp_beneficio - si.Importo_assegnato) as differenzaImporto,
			si.num_impegno_saldo,
			CASE 
				WHEN g.Anno_corso = vi.Anno_corso THEN 0
				ELSE 1
			END AS cambioAnno,
			CASE 
				WHEN corsoGrad.Corso_Stem = corsoAtt.Corso_Stem THEN 0
				ELSE 1
			END AS cambioStem,
			CASE 
				WHEN g.ISEEDSU = vv.ISEEDSU THEN 0
				ELSE 1
			END AS cambioISEE,
			CASE 
				WHEN vc.cod_avvenimento = 'DI' THEN 1
				ELSE 0
			END AS doppiaIscr,
			vdom.Invalido
        FROM   Domanda                     d
        JOIN   #CFEstrazione cf ON cf.cod_fiscale = d.cod_fiscale
        JOIN   Studente                    s  ON s.Cod_fiscale   = d.Cod_fiscale
        JOIN   vEsiti_concorsi             ec ON ec.Num_domanda  = d.Num_domanda
                                             AND ec.Anno_accademico = d.Anno_accademico
        JOIN   specifiche_impegni          si ON si.num_domanda = d.num_domanda
                                             AND si.anno_accademico = d.anno_accademico
                                             AND si.cod_beneficio = ec.cod_beneficio
                                            
		JOIN Graduatorie	g on g.Num_domanda = d.Num_domanda and g.Cod_fiscale = d.Cod_fiscale and g.Anno_accademico = d.Anno_accademico and g.Cod_beneficio = ec.Cod_beneficio and g.Cod_tipo_graduat = 1
		JOIN vIscrizioni vi on vi.Anno_accademico = d.Anno_accademico and vi.Cod_fiscale = d.Cod_fiscale and vi.tipo_bando = d.Tipo_bando

		JOIN Corsi_laurea corsoGrad on g.Cod_corso_laurea = corsoGrad.Cod_corso_laurea and corsoGrad.Anno_accad_fine is null
		JOIN Corsi_laurea corsoAtt on vi.Cod_corso_laurea = corsoAtt.Cod_corso_laurea and corsoAtt.Anno_accad_fine is null

		join vValori_calcolati vv on vv.Anno_accademico = d.Anno_accademico and vv.Num_domanda = d.Num_domanda
		left outer	join vCARRIERA_PREGRESSA vc on vc.Anno_accademico = d.Anno_accademico and vc.Cod_fiscale = d.Cod_fiscale and vc.Cod_avvenimento = 'DI'
		join vDATIGENERALI_dom vdom on vdom.Anno_accademico = d.Anno_accademico and vdom.Num_domanda = d.Num_domanda
        WHERE  d.tipo_bando = 'lz' and d.anno_accademico = '{annoAccademico}' and ec.cod_beneficio = '{codBeneficio}' and si.data_fine_validita is null
        ORDER  BY s.Cognome, s.Nome;
    ";

            using var cmd = new SqlCommand(sql, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 120000000
            };

            using var rdr = cmd.ExecuteReader();
            

            List<ModificaImportoDTO> dtoRows = new List<ModificaImportoDTO>();

            while (rdr.Read())
            {
                var dtoRow = new ModificaImportoDTO() { 
                NumDomanda = Utilities.SafeGetString(rdr, "Num_Domanda"),
                CodFiscale = Utilities.SafeGetString(rdr,"Cod_fiscale"),
                Nome = Utilities.SafeGetString(rdr, "Nome"),
                Cognome = Utilities.SafeGetString(rdr, "Cognome"),
                ImpPrecedente = Utilities.SafeGetDouble(rdr, "ImportoPrecedente"),
                ImpAttuale = Utilities.SafeGetDouble(rdr,"ImportoAttuale"),
                DifferenzaImp = Utilities.SafeGetDouble(rdr,"DifferenzaImporto"),
                ImpegnoSaldo = Utilities.SafeGetString(rdr, "num_impegno_saldo"),
                CambioAnno = Utilities.SafeGetInt(rdr, "CambioAnno") == 1,
                CambioStem = Utilities.SafeGetInt(rdr, "CambioStem") == 1,
                CambioISEE = Utilities.SafeGetInt(rdr, "CambioISEE") == 1,
                DoppiaIscr = Utilities.SafeGetInt(rdr, "DoppiaIscr") == 1,
                Invalido = Utilities.SafeGetInt(rdr, "Invalido") == 1
                };

                dtoRows.Add(dtoRow);
            }

            List<string> impegni = dtoRows.Select(q=>q.ImpegnoSaldo).Distinct().ToList();

            table.Columns.AddRange(new[]
            {
                new DataColumn("N°"                       , typeof(int)),
                new DataColumn("NumDomanda"               , typeof(int)),
                new DataColumn("CodFiscale"               , typeof(string)),
                new DataColumn("Nome"                     , typeof(string)),
                new DataColumn("Cognome"                  , typeof(string)),
                new DataColumn("Importo Precedente"         , typeof(decimal)),
                new DataColumn("Importo Attuale"         , typeof(decimal)),
                new DataColumn("Differenza Importo"         , typeof(decimal))
            });
            int impegnoColumn = 0;
            foreach(string impegno in impegni)
            {
                table.Columns.Add($"Impegno n°{impegno}");
                impegnoColumn++;
            }

            table.Columns.Add("Motivazioni");

            int n = 1;
            foreach (ModificaImportoDTO dtoRow in dtoRows) 
            {

                DataRow row = table.NewRow();

                row[0] = n;
                row[1] = dtoRow.NumDomanda;
                row[2] = dtoRow.CodFiscale;
                row[3] = dtoRow.Nome;
                row[4] = dtoRow.Cognome;
                row[5] = dtoRow.ImpPrecedente;
                row[6] = dtoRow.ImpAttuale;
                row[7] = dtoRow.DifferenzaImp;

                string impegnoColName = $"Impegno n°{dtoRow.ImpegnoSaldo}";
                int impegnoColIndex = table.Columns.IndexOf(impegnoColName);
                if (impegnoColIndex >= 0)
                {
                    row[impegnoColIndex] = dtoRow.DifferenzaImp;
                }

                List<string> motivazioni = new List<string>();
                if (dtoRow.CambioAnno)
                {
                    motivazioni.Add( "Cambio anno corso");
                }
                if (dtoRow.CambioStem)
                {
                    motivazioni.Add("Cambio stem");
                }
                if (dtoRow.CambioISEE)
                {
                    motivazioni.Add("Modifica ISEE");
                }
                if (dtoRow.DoppiaIscr)
                {
                    motivazioni.Add("Doppia iscrizione");
                }
                if (dtoRow.Invalido)
                {
                    motivazioni.Add("Modifica disabilità");
                }
                int motivColIndex = table.Columns.IndexOf("Motivazioni");
                row[motivColIndex] = string.Join(";", motivazioni);

                table.Rows.Add(row);

                n++;
            }

            return table;
        }
        private void FormatAndEnhanceExcel(string excelPath, DataTable dt, string headerText)
        {
            using var wb = new XLWorkbook(excelPath);
            var ws = wb.Worksheet(1);
            int lastCol = dt.Columns.Count;

            // 1) Insert three rows above the existing header
            ws.Row(1).InsertRowsAbove(3);

            // 2) Row 1: merged title with headerText
            ws.Range(1, 1, 1, lastCol)
              .Merge()
              .Value = headerText;
            ws.Cell(1, 1).Style
                .Font.SetBold()
                .Font.SetFontSize(14)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // 3) Row 2: merged "Allegato Determina"
            ws.Range(2, 1, 2, lastCol)
              .Merge()
              .Value = "Allegato Determina";
            ws.Cell(2, 1).Style
                .Font.SetBold()
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // 4) Row 3: empty merged row
            ws.Range(3, 1, 3, lastCol).Merge();

            // 5) Row 4: recolor real column headers (only up to lastCol)
            var blueHeader = XLColor.FromArgb(100, 140, 180);
            ws.Range(4, 1, 4, lastCol).Style
                .Fill.SetBackgroundColor(blueHeader)
                .Font.SetFontColor(XLColor.White);

            // 6) Compute data boundaries
            int headerRows = 4;
            int dataStart = headerRows + 1;
            int dataEnd = dataStart + dt.Rows.Count - 1;
            int totalRow = dataEnd + 1;

            // 7) Write "TOTALE" under the "Cognome" column
            int cognomeCol = dt.Columns.IndexOf("Cognome") + 1;
            ws.Cell(totalRow, cognomeCol).Value = "TOTALE";
            ws.Cell(totalRow, cognomeCol).Style.Font.SetBold();

            // 8) Sum each numeric column in C# and write into the TOTALE row,
            //    also collect sums for the small summary table
            var summary = new List<(string Desc, decimal Val)>();
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                string colName = dt.Columns[i].ColumnName;
                if (colName.StartsWith("Importo") ||
                    colName.StartsWith("Imp.") ||
                    colName.StartsWith("Differenza") ||
                    colName.StartsWith("Impegno"))
                {
                    decimal sum = 0m;
                    foreach (DataRow row in dt.Rows)
                    {
                        var val = row[colName];
                        if (val != DBNull.Value)
                            sum += Convert.ToDecimal(val);
                    }

                    int colIdx = i + 1;
                    ws.Cell(totalRow, colIdx).Value = sum;
                    ws.Cell(totalRow, colIdx).Style.Font.SetBold();

                    summary.Add(($"Totale {colName}", sum));
                }
            }

            // 9) Highlight the TOTALE row’s filled cells in light orange
            var lightOrange = XLColor.FromArgb(255, 150, 80);
            ws.Cell(totalRow, cognomeCol).Style.Fill.SetBackgroundColor(lightOrange);
            foreach (var (desc, _) in summary)
            {
                string colName = desc.Substring("Totale ".Length);
                int colIdx = dt.Columns.IndexOf(colName) + 1;
                if (colIdx > 0)
                    ws.Cell(totalRow, colIdx).Style.Fill.SetBackgroundColor(lightOrange);
            }

            // 10) Build the small 2-column summary table separated by two rows and aligned to the TOTALE column
            int summaryStart = totalRow + 3;
            for (int i = 0; i < summary.Count; i++)
            {
                int row = summaryStart + i;
                ws.Cell(row, cognomeCol).Value = summary[i].Desc;
                ws.Cell(row, cognomeCol + 1).Value = summary[i].Val;
                ws.Cell(row, cognomeCol).Style.Font.SetBold();
                ws.Cell(row, cognomeCol + 1).Style.Font.SetBold();
            }

            // 11) Draw a thin grid around & inside the main table (rows 4 → totalRow, cols 1 → lastCol)
            var mainRange = ws.Range(1, 1, totalRow, lastCol);
            mainRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            mainRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // 12) Draw a thin grid around & inside the small summary table (aligned to the TOTALE column)
            int summaryEnd = summaryStart + summary.Count - 1;
            var sumRange = ws.Range(summaryStart, cognomeCol, summaryEnd, cognomeCol + 1);
            sumRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sumRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // 13) Fill the small summary table with light orange
            sumRange.Style.Fill.SetBackgroundColor(lightOrange);

            // Auto-fit all columns to their contents
            ws.Columns(1, lastCol).AdjustToContents();

            // Add a new "Config" sheet containing NumDomanda, CodFiscale, Importo attuale, Impegno
            var configWs = wb.Worksheets.Add("Config");
            string[] configCols = { "NumDomanda", "CodFiscale", "Importo attuale", "Impegno" };
            // Write headers
            for (int i = 0; i < configCols.Length; i++)
            {
                configWs.Cell(1, i + 1).Value = configCols[i];
                configWs.Cell(1, i + 1).Style.Font.SetBold();
            }

            // Identify all Impegno-number columns in the DataTable (in left-to-right order)
            var impegnoCols = dt.Columns
                .Cast<DataColumn>()
                .Where(c => c.ColumnName.StartsWith("Impegno"))
                .Select(c => c.ColumnName)
                .ToList();

            // Populate rows
            for (int r = 0; r < dt.Rows.Count; r++)
            {
                // static fields
                configWs.Cell(r + 2, 1).Value = (int)dt.Rows[r]["NumDomanda"];
                configWs.Cell(r + 2, 2).Value = (string)dt.Rows[r]["CodFiscale"];
                configWs.Cell(r + 2, 3).Value = (decimal)dt.Rows[r]["Importo attuale"];

                // build the Impegno string by checking each Impegno-column for a value
                var parts = new List<string>();
                foreach (var colName in impegnoCols)
                {
                    var val = dt.Rows[r][colName];
                    if (val != DBNull.Value && !string.IsNullOrWhiteSpace(val.ToString()))
                    {
                        // extract the number from the column name, e.g. "Impegno2" → "2"
                        var match = System.Text.RegularExpressions.Regex.Match(colName, @"\d+");
                        parts.Add(match.Success ? match.Value : val.ToString());
                    }
                }
                configWs.Cell(r + 2, 4).Value = string.Join("|", parts);
            }

            // Auto-fit config columns
            configWs.Columns(1, configCols.Length).AdjustToContents();

            // Save changes
            wb.Save();

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
