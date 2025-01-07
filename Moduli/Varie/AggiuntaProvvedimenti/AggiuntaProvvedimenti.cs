using Microsoft.VisualBasic.ApplicationServices;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcedureNet7
{
    internal class AggiuntaProvvedimenti : BaseProcedure<ArgsAggiuntaProvvedimenti>
    {

        private readonly Dictionary<string, string> variazCodVariazioneItems = new()
        {
            {"01","Variazione generica"},
            {"02","Rinuncia"},
            {"03","Revoca"},
            {"04","Decadenza"},
            {"05","Riammissione come idoneo"},
            {"06","Riammissione come vincitore"},
            {"07","Da idoneo a vincitore"},
            {"08","Da vincitore a idoneo"},
            {"09","Rinuncia idoneità"},
            {"010","Rinuncia vincitore"},
            {"011","Revoca per incompatibilità col bando"},
            {"012","Da PENDOLARE a IN SEDE"},
            {"013","Da PENDOLARE a FUORI SEDE"},
            {"014","Da FUORI SEDE a PENDOLARE"},
            {"015","Da FUORI SEDE a IN SEDE"},
            {"016","Da IN SEDE a FUORI SEDE"},
            {"017","Da IN SEDE a PENDOLARE"},
            {"018","Revoca per sede distaccata"},
            {"019","Revoca per mancata iscrizione"},
            {"020","Revoca per studente iscritto ripetente"},
            {"021","Revoca per ISEE non presente in banca dati"},
            {"022","Revoca per studente già laureato"},
            {"023","Revoca per patrimonio oltre il limite"},
            {"024","Revoca per reddito oltre il limite"},
            {"025","Revoca per mancanza esami o crediti"},
            {"026","Rinuncia a tutti i benefici"},
            {"027","Revoca per iscrizione fuori temine"},
            {"028","Revoca per ISEE fuori termine"},
            {"029","Revoca per ISEE non prodotta"},
            {"030","Decadenza per mancata comunicazione modalità di pagamento"},
            {"031","Revoca per mancanza contratto di affitto"},
            {"032","Variazione I.S.E.E."},
            {"033","Revoca premio di laurea"},
            {"034","Rinuncia premio di laurea"},
            {"035","Riammissione come idoneo premio di laurea"},
            {"036","Riammisione come vincitore premio di laurea"},
        };
        private readonly Dictionary<string, string> variazCodBeneficioItems = new()
        {
            { "00", "Tutti i benefici" },
            { "BS", "Borsa di studio" },
            { "PA", "Posto alloggio" },
            { "CI", "Contributo integrativo" },
        };

        public AggiuntaProvvedimenti(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        private List<string> _studentInformation = new List<string>();
        Dictionary<string, double> _studenteImportoPairs = new Dictionary<string, double>();
        Dictionary<string, string> _studenteFondoPairs = new Dictionary<string, string>();
        Dictionary<string, List<bool>> _studenteChecks = new Dictionary<string, List<bool>>();
        private ManualResetEvent _waitHandle = new ManualResetEvent(false);

        SqlTransaction? sqlTransaction = null;

        string selectedFolderPath = string.Empty;
        string numProvvedimento = string.Empty;
        string aaProvvedimento = string.Empty;
        string dataProvvedimento = string.Empty;
        string provvedimentoSelezionato = string.Empty;
        string notaProvvedimento = string.Empty;
        string beneficioProvvedimento = string.Empty;
        bool requireNuovaSpecifica;

        string tipoFondo = string.Empty;
        string capitolo = string.Empty;
        string esePR = string.Empty;
        string eseSA = string.Empty;
        string impegnoPR = string.Empty;
        string impegnoSA = string.Empty;

        public override void RunProcedure(ArgsAggiuntaProvvedimenti args)
        {
            try
            {


                if (CONNECTION == null)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure: connessione o transazione null");
                    return;
                }
                sqlTransaction = CONNECTION.BeginTransaction();
                selectedFolderPath = args._selectedFolderPath;
                numProvvedimento = args._numProvvedimento;
                aaProvvedimento = args._aaProvvedimento;
                dataProvvedimento = args._dataProvvedimento;
                provvedimentoSelezionato = args._provvedimentoSelezionato;
                notaProvvedimento = args._notaProvvedimento;
                beneficioProvvedimento = args._beneficioProvvedimento;
                requireNuovaSpecifica = args._requireNuovaSpecifica;
                capitolo = args._capitolo;
                esePR = args._esePR;
                eseSA = args._eseSA;
                impegnoPR = args._impegnoPR;
                impegnoSA = args._impegnoSA;
                tipoFondo = args._tipoFondo;

                string pattern = $@"\bdet{numProvvedimento}\b";
                // Get all the subdirectories in the selectedFolderPath
                string[] subDirectories = Directory.GetDirectories(selectedFolderPath);

                string foundFolder = string.Empty;

                // Iterate through each subdirectory
                foreach (string subDirectory in subDirectories)
                {
                    // Get the name of the subdirectory
                    string directoryName = Path.GetFileName(subDirectory);

                    // Check if the directory name contains the numProvvedimento
                    if (Regex.IsMatch(directoryName, pattern))
                    {
                        Logger.Log(0, directoryName, LogLevel.INFO);
                        foundFolder = subDirectory;
                        // Perform your logic here when a match is found
                        break;
                    }
                }

                if (foundFolder == string.Empty)
                {
                    _masterForm.inProcedure = false;
                    return;
                }

                // Get all Excel files in the foundFolder
                string[] excelFilePaths = Directory.GetFiles(foundFolder, "*.xls*");

                // Check if any Excel files were found
                if (excelFilePaths.Length == 0)
                {
                    _masterForm.inProcedure = false;
                    return;
                }

                Panel? provvedimentiPanel = null;
                _masterForm.Invoke((MethodInvoker)delegate
                {
                    provvedimentiPanel = _masterForm.GetProcedurePanel();
                });
                foreach (string excelFilePath in excelFilePaths)
                {
                    if (excelFilePath.StartsWith("~"))
                    {
                        continue;
                    }
                    DataTable allStudentsData = Utilities.ReadExcelToDataTable(excelFilePath, true);
                    DataGridView? studentsGridView = null;
                    if (beneficioProvvedimento == "BS" || beneficioProvvedimento == "CI")
                    {
                        MessageBox.Show("Selezionare il primo numero domanda", "Seleziona", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        studentsGridView = Utilities.CreateDataGridView(allStudentsData, _masterForm, provvedimentiPanel, OnNumDomandaClicked);
                    }
                    else
                    {
                        MessageBox.Show("Selezionare il primo codice fiscale", "Seleziona", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        studentsGridView = Utilities.CreateDataGridView(allStudentsData, _masterForm, provvedimentiPanel, OnCodFiscaleClicked);
                    }

                    _waitHandle.Reset();
                    _waitHandle.WaitOne();
                    _masterForm.Invoke((MethodInvoker)delegate
                    {
                        studentsGridView.Dispose();
                    });

                    HandleProvvedimenti(numProvvedimento, aaProvvedimento, dataProvvedimento, provvedimentoSelezionato, notaProvvedimento);
                    if (requireNuovaSpecifica)
                    {
                        HandleSpecificheImpegni(excelFilePath);
                    }
                }
                sqlTransaction.Commit();
                Logger.Log(100, "Fine lavorazione", LogLevel.INFO);
                _masterForm.inProcedure = false;
            }
            catch
            {
                sqlTransaction.Rollback();
                throw;
            }
        }

        private void OnNumDomandaClicked(object? sender, DataGridViewCellEventArgs e)
        {
            if (sender is DataGridView dataGridView)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    _studentInformation = new List<string>();

                    for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                    {
                        var cellValue = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                        if (cellValue != null)
                        {
                            string? numDomanda = cellValue.ToString();
                            if (string.IsNullOrEmpty(numDomanda) || !int.TryParse(numDomanda, out int _))
                            {
                                continue;
                            }
                            _studentInformation.Add(numDomanda);
                        }
                    }
                }
            }
            _waitHandle.Set();
        }

        private void OnCodFiscaleClicked(object? sender, DataGridViewCellEventArgs e)
        {
            if (sender is DataGridView dataGridView)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    _studentInformation = new List<string>();

                    for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                    {
                        var cellValue = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                        if (cellValue != null)
                        {
                            string? codFiscale = cellValue.ToString();
                            if (string.IsNullOrEmpty(codFiscale))
                            {
                                continue;
                            }
                            _studentInformation.Add(codFiscale);
                        }
                    }
                }
            }
            _waitHandle.Set();
        }

        private void HandleProvvedimenti(
            string numProvvedimento,
            string aaProvvedimento,
            string dataProvvedimento,
            string provvedimentoSelezionato,
            string notaProvvedimento
        )
        {
            try
            {

                // 1) Create temporary table(s) for storing the codes
                //    Here we assume one for fiscal codes and one for num_domanda, 
                //    but you might only need one, depending on your logic.
                string createTempFiscalTable = @"
                        IF OBJECT_ID('tempdb..#TempFiscalCodes') IS NOT NULL
                            DROP TABLE #TempFiscalCodes;

                        CREATE TABLE #TempFiscalCodes (
                            FiscalCode VARCHAR(50) NOT NULL
                        );
                    ";

                string createTempNumDomandaTable = @"
                        IF OBJECT_ID('tempdb..#TempNumDom') IS NOT NULL
                            DROP TABLE #TempNumDom;

                        CREATE TABLE #TempNumDom (
                            NumDomanda VARCHAR(50) NOT NULL
                        );
                    ";

                using (var cmd = new SqlCommand(createTempFiscalTable, CONNECTION, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqlCommand(createTempNumDomandaTable, CONNECTION, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // 2) Bulk insert the data into #TempFiscalCodes (if needed)
                //    In your scenario, you have _studentInformation which might contain either
                //    fiscal codes or num_domanda. We'll assume they're fiscal codes for now.
                //    If they are sometimes one or the other, adapt accordingly.
                using (var bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction))
                {
                    bulkCopy.DestinationTableName = "#TempFiscalCodes";

                    // Construct a DataTable with one column named "FiscalCode"
                    DataTable dtFiscalCodes = new DataTable();
                    dtFiscalCodes.Columns.Add("FiscalCode", typeof(string));

                    foreach (string code in _studentInformation)
                    {
                        dtFiscalCodes.Rows.Add(code);
                    }

                    bulkCopy.WriteToServer(dtFiscalCodes);
                }

                // 3) Depending on your requirement, retrieve num_domanda for codes that are not "BS" or "CI"
                //    Instead of building an IN(...) query, do a JOIN from #TempFiscalCodes to Domanda
                //    and store results in #TempNumDom. This is effectively your localStudentInfo step.
                if (beneficioProvvedimento != "BS" && beneficioProvvedimento != "CI")
                {
                    // Retrieve corresponding num_domanda for the given codes
                    string fillTempNumDomanda = @"
                            INSERT INTO #TempNumDom (NumDomanda)
                            SELECT d.num_domanda
                            FROM Domanda d
                            INNER JOIN #TempFiscalCodes t
                                ON d.Cod_fiscale = t.FiscalCode
                            WHERE d.Anno_accademico = @aaProvvedimento
                              AND d.Tipo_bando = 'LZ'
                        ";

                    using (var cmd = new SqlCommand(fillTempNumDomanda, CONNECTION, sqlTransaction))
                    {
                        cmd.Parameters.AddWithValue("@aaProvvedimento", aaProvvedimento);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    // If "BS" or "CI", we assume the _studentInformation already contains num_domanda?
                    // Then just bulk-insert those num_domandas in #TempNumDom
                    using (var bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction))
                    {
                        bulkCopy.DestinationTableName = "#TempNumDom";

                        DataTable dtNumDom = new DataTable();
                        dtNumDom.Columns.Add("NumDomanda", typeof(string));

                        foreach (string code in _studentInformation)
                        {
                            dtNumDom.Rows.Add(code);
                        }

                        bulkCopy.WriteToServer(dtNumDom);
                    }
                }

                // 4) Now, #TempNumDom contains all of the NumDomanda we need to process.
                //    We'll see which ones are already in PROVVEDIMENTI:
                string createTempCommonTable = @"
                        IF OBJECT_ID('tempdb..#TempCommonCodes') IS NOT NULL
                            DROP TABLE #TempCommonCodes;

                        CREATE TABLE #TempCommonCodes (
                            NumDomanda VARCHAR(50) NOT NULL
                        );
                    ";
                using (var cmd = new SqlCommand(createTempCommonTable, CONNECTION, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // Insert into #TempCommonCodes all those that already exist in PROVVEDIMENTI
                string fillTempCommonCodes = @"
                        INSERT INTO #TempCommonCodes (NumDomanda)
                        SELECT d.Num_domanda
                        FROM Domanda d
                        INNER JOIN PROVVEDIMENTI p
                            ON d.Num_domanda = p.Num_domanda
                            AND d.Anno_accademico = p.Anno_accademico
                        INNER JOIN #TempNumDom t
                            ON d.Num_domanda = t.NumDomanda
                        WHERE d.Anno_accademico = @aaProvvedimento
                            AND p.Anno_accademico = @aaProvvedimento
                            AND p.num_provvedimento = @numProvvedimento
                            AND d.Tipo_bando = 'lz'
                    ";

                using (var cmd = new SqlCommand(fillTempCommonCodes, CONNECTION, sqlTransaction))
                {
                    cmd.Parameters.AddWithValue("@aaProvvedimento", aaProvvedimento);
                    cmd.Parameters.AddWithValue("@numProvvedimento", numProvvedimento);
                    cmd.ExecuteNonQuery();
                }

                // 5) If we want to list out common codes, we can select them from #TempCommonCodes
                //    This replaces the localStudentInfo.Intersect(retrievedNumDomandas) logic.
                List<string> commonCodes = new List<string>();
                string getCommonCodesSql = @"
                        SELECT NumDomanda 
                        FROM #TempCommonCodes
                    ";
                using (var cmd = new SqlCommand(getCommonCodesSql, CONNECTION, sqlTransaction))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        commonCodes.Add(reader["NumDomanda"].ToString());
                    }
                }

                // Log the common ones
                foreach (string code in commonCodes)
                {
                    Logger.Log(30, code + " - Provvedimento #" + numProvvedimento + " già aggiunto", LogLevel.INFO);
                }

                if (commonCodes.Count > 0 && (provvedimentoSelezionato == "01" || provvedimentoSelezionato == "02"))
                {
                    // If needed, remove block for these codes and update DatiGenerali_dom
                    UpdateDatiGeneraliDom(commonCodes, aaProvvedimento, "Area4", 0);
                }

                // 6) Now find the remaining codes that are not in #TempCommonCodes
                //    We'll do a single INSERT for those new records in PROVVEDIMENTI
                //    We can do this in a single statement with a LEFT JOIN filter, or by
                //    using a NOT EXISTS sub-select. For example:
                string insertNewProvvedimenti = @"
                        INSERT INTO [dbo].[PROVVEDIMENTI] (
                            [Num_domanda],
                            [tipo_provvedimento],
                            [data_provvedimento],
                            [Anno_accademico],
                            [note],
                            [num_provvedimento],
                            [riga_valida],
                            [data_validita]
                        )
                        SELECT d.num_domanda,
                               @provvedimentoSelezionato,
                               @dataProvvedimento,
                               @aaProvvedimento,
                               @notaProvvedimento,
                               @numProvvedimento,
                               1,
                               CURRENT_TIMESTAMP
                        FROM Domanda d
                        INNER JOIN #TempNumDom t
                            ON d.Num_domanda = t.NumDomanda
                        WHERE d.Anno_accademico = @aaProvvedimento
                          AND d.Tipo_bando = 'lz'
                          AND NOT EXISTS (
                              SELECT 1 FROM #TempCommonCodes c
                              WHERE c.NumDomanda = d.Num_domanda
                          )
                    ";

                int affectedRows;
                using (var cmd = new SqlCommand(insertNewProvvedimenti, CONNECTION, sqlTransaction))
                {
                    cmd.Parameters.AddWithValue("@provvedimentoSelezionato", provvedimentoSelezionato);
                    cmd.Parameters.AddWithValue("@dataProvvedimento", dataProvvedimento);
                    cmd.Parameters.AddWithValue("@aaProvvedimento", aaProvvedimento);
                    cmd.Parameters.AddWithValue("@notaProvvedimento", notaProvvedimento);
                    cmd.Parameters.AddWithValue("@numProvvedimento", numProvvedimento);

                    affectedRows = cmd.ExecuteNonQuery();
                }

                if (affectedRows > 0)
                {
                    Logger.Log(60, $"Modificati: {affectedRows} studenti", LogLevel.INFO);

                    // If you want to list them, you can SELECT them right back out:
                    var newCodes = new List<string>();
                    string selectNewlyInserted = @"
                            SELECT t.NumDomanda
                            FROM #TempNumDom t
                            WHERE NOT EXISTS (
                                SELECT 1 FROM #TempCommonCodes c
                                WHERE c.NumDomanda = t.NumDomanda
                            )
                        ";

                    using (var cmd = new SqlCommand(selectNewlyInserted, CONNECTION, sqlTransaction))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            newCodes.Add(reader["NumDomanda"].ToString());
                        }
                    }

                    // Log the newly inserted
                    foreach (string code in newCodes)
                    {
                        Logger.Log(40, code + ": aggiunto provvedimento #" + numProvvedimento, LogLevel.INFO);
                    }

                    // If needed, also update DatiGeneraliDom for these newly inserted codes
                    if (provvedimentoSelezionato == "01" || provvedimentoSelezionato == "02")
                    {
                        UpdateDatiGeneraliDom(newCodes, aaProvvedimento, "Area4", 0);
                    }
                }
                else
                {
                    Logger.Log(100, "Nessun provvedimento da aggiungere", LogLevel.INFO);
                }

                // 7) Log the total number of students in the original file
                Logger.Log(80, "Studenti nel file: " + _studentInformation.Count.ToString(), LogLevel.INFO);


            }
            catch
            {
                // In case of exceptions, roll back
                sqlTransaction?.Rollback();
                throw;
            }
        }


        // Helper method to update DatiGenerali_dom dynamically
        private void UpdateDatiGeneraliDom(List<string> numDomandas, string aaProvvedimento, string utenteSblocco, int bloccoPagamento)
        {
            // Retrieve column names
            List<string> columnNames = GetColumnNames(CONNECTION, sqlTransaction, "DatiGenerali_dom");
            List<string> vColumns = GetColumnNames(CONNECTION, sqlTransaction, "vDATIGENERALI_dom");

            // Define explicit values
            Dictionary<string, string> explicitValues = new Dictionary<string, string>()
            {
                { "Data_validita", "CURRENT_TIMESTAMP" },    // SQL expression
                { "Utente", $"'{utenteSblocco}'" },          // User who is updating
                { "Blocco_pagamento", bloccoPagamento.ToString() }, // Blocco_pagamento value
            };

            List<string> insertColumns = new List<string>();
            List<string> selectColumns = new List<string>();

            foreach (string columnName in columnNames)
            {
                insertColumns.Add($"[{columnName}]");

                if (explicitValues.ContainsKey(columnName))
                {
                    selectColumns.Add(explicitValues[columnName]);
                }
                else if (vColumns.Contains(columnName))
                {
                    selectColumns.Add($"v.[{columnName}]");
                }
                else if (columnName == "Anno_accademico" || columnName == "Num_domanda")
                {
                    selectColumns.Add($"d.[{columnName}]");
                }
                else
                {
                    // Assign NULL for columns not in vDATIGENERALI_dom and not in explicitValues
                    selectColumns.Add("NULL");
                }
            }

            string insertColumnsList = string.Join(", ", insertColumns);
            string selectColumnsList = string.Join(", ", selectColumns);

            // Build parameterized query for Num_domanda
            List<string> numDomParamList = numDomandas.Select((numDom, index) => $"@numDom{index}").ToList();

            string sql = $@"
                    UPDATE Motivazioni_blocco_pagamenti
                    SET Blocco_pagamento_attivo = 0, 
                        Data_fine_validita = CURRENT_TIMESTAMP, 
                        Utente_sblocco = @utenteSblocco
                    WHERE Anno_accademico = @aaProvvedimento 
                        AND Cod_tipologia_blocco = 'BRM' 
                        AND Blocco_pagamento_attivo = 1
                        AND Num_domanda IN 
                            (SELECT Num_domanda
                             FROM Domanda d
                             WHERE Anno_accademico = @aaProvvedimento
                                 AND tipo_bando IN ('lz') 
                                 AND d.Num_domanda IN ({string.Join(", ", numDomParamList)}));

                    INSERT INTO DatiGenerali_dom ({insertColumnsList})
                    SELECT DISTINCT {selectColumnsList}
                    FROM 
                        Domanda d
                        INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico AND 
                                                         d.Num_domanda = v.Num_domanda 
                    WHERE 
                        d.Anno_accademico = @aaProvvedimento AND
                        d.tipo_bando IN ('lz','l2') AND
                        d.Num_domanda IN ({string.Join(", ", numDomParamList)}) AND
                        d.Num_domanda NOT IN (
                            SELECT DISTINCT Num_domanda
                            FROM Motivazioni_blocco_pagamenti
                            WHERE Anno_accademico = @aaProvvedimento 
                                AND Data_fine_validita IS NOT NULL
                                AND Blocco_pagamento_attivo = 1
                        );
                ";

            using (SqlCommand command = new SqlCommand(sql, CONNECTION, sqlTransaction))
            {
                command.Parameters.AddWithValue("@aaProvvedimento", aaProvvedimento);
                command.Parameters.AddWithValue("@utenteSblocco", utenteSblocco);

                for (int i = 0; i < numDomandas.Count; i++)
                {
                    command.Parameters.AddWithValue($"@numDom{i}", numDomandas[i]);
                }

                command.ExecuteNonQuery();
            }
        }

        // Helper method to get column names
        private List<string> GetColumnNames(SqlConnection conn, SqlTransaction transaction, string tableName)
        {
            List<string> columnNames = new List<string>();
            string query = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = 'dbo'
            ";

            using (SqlCommand cmd = new SqlCommand(query, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(0) == "Id_DatiGenerali_dom" || reader.GetString(0) == "Id_Domanda")
                        {
                            continue;
                        }
                        columnNames.Add(reader.GetString(0));
                    }
                }
            }
            return columnNames;
        }

        private void HandleSpecificheImpegni(string selectedFile)
        {

            Dictionary<string, string> provvedimentiItems = new()
            {
                { "00", "Varie" },
                { "01", "Riammissione come vincitore" },
                { "02", "Riammissione come idoneo" },
                { "03", "Revoca senza recupero somme" },
                { "04", "Decadenza" },
                { "05", "Modifica importo" },
                { "06", "Revoca con recupero somme" },
                { "07", "Pagamento" },
                { "08", "Rinuncia" },
                { "09", "Da idoneo a vincitore" },
                { "10", "Rinuncia con recupero somme" },
                { "11", "Rinuncia senza recupero somme" },
                { "12", "Rimborso tassa regionale indebitamente pagata" },
                { "13", "Cambio status sede" }
            };


            bool needNewSpecific = false;

            switch (provvedimentoSelezionato)
            {
                case "01": //Riammissione come vincitore
                case "02": //Riammissione come idoneo
                case "05": //Modifica importo 
                case "09": //Da idoneo a vincitore
                case "13": //Cambio status sede
                    needNewSpecific = true;
                    break;
                case "07": //Pagamento
                case "12": //Rimborso tassa regionale indebitamente pagata
                    break;
                case "04": //Decadenza
                case "03": //Revoca senza recupero somme
                case "06": //Revoca con recupero somme
                case "08": //Rinuncia
                case "10": //Rinuncia con recupero somme
                case "11": //Rinuncia senza recupero somme
                    break;
            }

            string selectedFilePath = "";
            ArgsSpecificheImpegni argsSpecificheImpegni = new ArgsSpecificheImpegni
            {
                _selectedFile = selectedFile,
                _selectedDate = dataProvvedimento,
                _tipoFondo = tipoFondo,
                _aperturaNuovaSpecifica = needNewSpecific,
                _capitolo = capitolo,
                _descrDetermina = notaProvvedimento,
                _esePR = esePR,
                _eseSA = eseSA,
                _impegnoPR = impegnoPR,
                _impegnoSA = impegnoSA,
                _numDetermina = numProvvedimento,
                _selectedAA = aaProvvedimento,
                _selectedCodBeneficio = beneficioProvvedimento,
                _sqlTransaction = sqlTransaction,
                _isProvvedimentoAdded = true
            };
            SpecificheImpegni specificheImpegni = new(_masterForm, CONNECTION);
            specificheImpegni.RunProcedure(argsSpecificheImpegni);
        }

    }
}
