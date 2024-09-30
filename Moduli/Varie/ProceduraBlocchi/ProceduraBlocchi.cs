using ProcedureNet7.ProceduraAllegatiSpace;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace ProcedureNet7
{
    internal class ProceduraBlocchi : BaseProcedure<ArgsProceduraBlocchi>
    {
        public string _blocksYear = string.Empty;
        public string _blocksUsername = string.Empty;
        private readonly Dictionary<string, List<string>> blocksToRemove = new();
        private readonly Dictionary<string, List<string>> blocksToAdd = new();

        private List<string> codiciFiscaliTotali = new();
        private List<string> codiciFiscaliConErrori = new();

        public ProceduraBlocchi(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsProceduraBlocchi args)
        {
            string blocksFilePath = args._blocksFilePath;
            _blocksYear = args._blocksYear;
            _blocksUsername = args._blocksUsername;

            _masterForm.inProcedure = true;
            try
            {
                DataTable dataTable = Utilities.ReadExcelToDataTable(blocksFilePath);
                ProcessWorksheet(dataTable);
            }
            catch (Exception ex)
            {
                Logger.LogInfo(0, $"Error: {ex.Message}");
                _masterForm.inProcedure = false;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _masterForm.inProcedure = false;
                Logger.LogInfo(100, $"Fine Lavorazione");
            }
        }

        private void ProcessWorksheet(DataTable dataTable)
        {
            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                DataRow row = dataTable.Rows[i];
                string? nullableCodFiscale = row[0].ToString();

                if (nullableCodFiscale == null)
                {
                    Logger.LogWarning(null, $"Riga {i} con cella codice fiscale nulla");
                    continue;
                }

                string codFiscale = nullableCodFiscale;
                ProcessRowInMemory(codFiscale, row);
            }

            Logger.LogInfo(50, "Processata memoria");

            using SqlTransaction transaction = CONNECTION.BeginTransaction();
            try
            {
                // Process blocks to remove and add within the transaction
                ApplyBlocks(CONNECTION, transaction);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Logger.LogError(0, $"Transaction Error: {ex.Message}");
                transaction.Rollback();
                throw;
            }
        }

        private void ProcessRowInMemory(string codFiscale, DataRow data)
        {
            if (string.IsNullOrEmpty(data[1].ToString()) && string.IsNullOrEmpty(data[2].ToString()))
            {
                return;
            }

            // Process codFiscale values to remove
            if (!string.IsNullOrEmpty(data[1].ToString()))
            {
                foreach (string block in data[1].ToString().Split(';'))
                {
                    if (!blocksToRemove.TryGetValue(block, out List<string>? value))
                    {
                        value = new List<string>();
                        blocksToRemove[block] = value;
                    }

                    value.Add(codFiscale);
                }
            }

            // Process codFiscale values to add
            if (!string.IsNullOrEmpty(data[2].ToString()))
            {
                foreach (string block in data[2].ToString().Split(';'))
                {
                    if (!blocksToAdd.TryGetValue(block, out List<string>? value))
                    {
                        value = new List<string>();
                        blocksToAdd[block] = value;
                    }

                    value.Add(codFiscale);
                }
            }
        }

        private void ApplyBlocks(SqlConnection conn, SqlTransaction transaction)
        {
            foreach (string block in blocksToRemove.Keys)
            {
                try
                {
                    RemoveBlock(conn, transaction, blocksToRemove[block], block);
                    Logger.LogInfo(75, $"Processato blocco {block} da togliere");
                }
                catch (Exception ex)
                {
                    Logger.LogError(0, $"Error processing block to remove: {block} - {ex.Message}");
                }
            }
            foreach (string block in blocksToAdd.Keys)
            {
                try
                {
                    AddBlock(conn, transaction, blocksToAdd[block], block);
                    Logger.LogInfo(75, $"Processato blocco {block} da mettere");
                }
                catch (Exception ex)
                {
                    Logger.LogError(0, $"Error processing block to add: {block} - {ex.Message}");
                }
            }
        }

        private void AddBlock(SqlConnection conn, SqlTransaction transaction, List<string> codFiscaleCol, string blockCode)
        {
            string annoAccademico = _blocksYear;
            string utente = _blocksUsername;

            // Build the list of Cod_fiscale parameters
            List<string> codFiscaleParamNames = new List<string>();
            List<SqlParameter> codFiscaleParams = new List<SqlParameter>();
            int index = 0;
            foreach (string cf in codFiscaleCol)
            {
                string paramName = "@cf" + index;
                codFiscaleParamNames.Add(paramName);
                codFiscaleParams.Add(new SqlParameter(paramName, cf));
                index++;
            }

            string codFiscaleInClause = string.Join(", ", codFiscaleParamNames);

            // Now, get the list of columns from DatiGenerali_dom
            List<string> columnNames = GetColumnNames(conn, transaction, "DatiGenerali_dom");

            // Get the list of columns from vDATIGENERALI_dom
            List<string> vColumns = GetColumnNames(conn, transaction, "vDATIGENERALI_dom");

            // Define the columns that need explicit values
            Dictionary<string, string> explicitValues = new Dictionary<string, string>()
            {
                { "Data_validita", "CURRENT_TIMESTAMP" }, // SQL expression
                { "Utente", "@utenteValue" },             // Parameter
                { "Blocco_pagamento", "1" },              // For AddBlock
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
                else
                {
                    // Assign NULL for columns not in vDATIGENERALI_dom and not in explicitValues
                    selectColumns.Add("NULL");
                }
            }

            string insertColumnsList = string.Join(", ", insertColumns);
            string selectColumnsList = string.Join(", ", selectColumns);

            string sql = $@"
                    INSERT INTO dbo.Motivazioni_blocco_pagamenti
                        (Anno_accademico, Num_domanda, Cod_tipologia_blocco, Blocco_pagamento_attivo,
                            Data_validita, Utente, Data_fine_validita, Utente_sblocco)
                    SELECT d.Anno_accademico, d.Num_domanda, @blockCode, '1', 
                            CURRENT_TIMESTAMP, @utenteValue, NULL, NULL
                    FROM dbo.Domanda d
                    WHERE d.Anno_accademico = @annoAcademico 
                        AND d.tipo_bando IN ('lz', 'l2') 
                        AND d.Cod_fiscale IN ({codFiscaleInClause})
                        AND d.Num_domanda NOT IN
                            (SELECT DISTINCT Num_domanda
                                FROM dbo.Motivazioni_blocco_pagamenti
                                WHERE Anno_accademico = @annoAcademico 
                                    AND Cod_tipologia_blocco = @blockCode
                                    AND Data_fine_validita IS NULL);

                    INSERT INTO [DatiGenerali_dom] ({insertColumnsList})
                    SELECT DISTINCT {selectColumnsList}
                    FROM 
                        Domanda d
                        INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico AND 
                                                         d.Num_domanda = v.Num_domanda
                    WHERE 
                        d.Anno_accademico = @annoAcademico AND
                        d.tipo_bando IN ('lz', 'l2') AND
                        d.Cod_fiscale IN ({codFiscaleInClause}) AND
                        d.Num_domanda NOT IN (
                            SELECT DISTINCT Num_domanda
                            FROM Motivazioni_blocco_pagamenti
                            WHERE Anno_accademico = @annoAcademico 
                                AND Data_fine_validita IS NOT NULL
                                AND Blocco_pagamento_attivo = 1
                        );
                    ";

            using (SqlCommand command = new SqlCommand(sql, conn, transaction))
            {
                command.Parameters.AddWithValue("@annoAcademico", annoAccademico);
                command.Parameters.AddWithValue("@utenteValue", utente);
                command.Parameters.AddWithValue("@blockCode", blockCode);
                foreach (var param in codFiscaleParams)
                {
                    command.Parameters.Add(param);
                }

                command.ExecuteNonQuery();
            }
        }

        private void RemoveBlock(SqlConnection conn, SqlTransaction transaction, List<string> codFiscaleCol, string blockCode)
        {
            string annoAccademico = _blocksYear;
            string utente = _blocksUsername;

            // Build the list of Cod_fiscale parameters
            List<string> codFiscaleParamNames = new List<string>();
            List<SqlParameter> codFiscaleParams = new List<SqlParameter>();
            int index = 0;
            foreach (string cf in codFiscaleCol)
            {
                string paramName = "@cf" + index;
                codFiscaleParamNames.Add(paramName);
                codFiscaleParams.Add(new SqlParameter(paramName, cf));
                index++;
            }

            string codFiscaleInClause = string.Join(", ", codFiscaleParamNames);

            // Now, get the list of columns from DatiGenerali_dom
            List<string> columnNames = GetColumnNames(conn, transaction, "DatiGenerali_dom");

            // Get the list of columns from vDATIGENERALI_dom
            List<string> vColumns = GetColumnNames(conn, transaction, "vDATIGENERALI_dom");

            // Define the columns that need explicit values
            Dictionary<string, string> explicitValues = new Dictionary<string, string>()
            {
                { "Data_validita", "CURRENT_TIMESTAMP" }, // SQL expression
                { "Utente", "@utenteValue" },             // Parameter
                { "Blocco_pagamento", "0" },              // For RemoveBlock
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
                else
                {
                    // Assign NULL for columns not in vDATIGENERALI_dom and not in explicitValues
                    selectColumns.Add("NULL");
                }
            }

            string insertColumnsList = string.Join(", ", insertColumns);
            string selectColumnsList = string.Join(", ", selectColumns);

            string sql = $@"
                    UPDATE Motivazioni_blocco_pagamenti
                    SET Blocco_pagamento_attivo = 0, 
                        Data_fine_validita = CURRENT_TIMESTAMP, 
                        Utente_sblocco = @utenteValue
                    WHERE Anno_accademico = @annoAcademico 
                        AND Cod_tipologia_blocco = @blockCode 
                        AND Blocco_pagamento_attivo = 1
                        AND Num_domanda IN 
                            (SELECT Num_domanda
                             FROM Domanda d
                             WHERE Anno_accademico = @annoAcademico 
                                 AND d.tipo_bando IN ('lz') 
                                 AND d.Cod_fiscale IN ({codFiscaleInClause}));

                    INSERT INTO [DatiGenerali_dom] ({insertColumnsList})
                    SELECT DISTINCT {selectColumnsList}
                    FROM 
                        Domanda d
                        INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico AND 
                                                         d.Num_domanda = v.Num_domanda
                    WHERE 
                        d.Anno_accademico = @annoAcademico AND
                        d.tipo_bando IN ('lz', 'l2') AND
                        d.Cod_fiscale IN ({codFiscaleInClause}) AND
                        d.Num_domanda NOT IN (
                            SELECT DISTINCT Num_domanda
                            FROM Motivazioni_blocco_pagamenti
                            WHERE Anno_accademico = @annoAcademico 
                                AND Data_fine_validita IS NULL
                                AND Blocco_pagamento_attivo = 1
                        );
                    ";

            using (SqlCommand command = new SqlCommand(sql, conn, transaction))
            {
                command.Parameters.AddWithValue("@annoAcademico", annoAccademico);
                command.Parameters.AddWithValue("@utenteValue", utente);
                command.Parameters.AddWithValue("@blockCode", blockCode);
                foreach (var param in codFiscaleParams)
                {
                    command.Parameters.Add(param);
                }

                command.ExecuteNonQuery();
            }
        }

        // Helper method to get column names
        private List<string> GetColumnNames(SqlConnection conn, SqlTransaction transaction, string tableName)
        {
            List<string> columnNames = new List<string>();
            string query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName";

            using (SqlCommand cmd = new SqlCommand(query, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columnNames.Add(reader.GetString(0));
                    }
                }
            }
            return columnNames;
        }

    }
}
