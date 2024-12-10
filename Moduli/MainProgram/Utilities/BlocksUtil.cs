using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public static class BlocksUtil
    {
        public static void AddBlock(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleCol,
            string blockCode,
            string annoAccademico,
            string utente,
            bool inserisciGiaRimossi = false)
        {
            // Use temporary table and bulk insert
            CreateAndPopulateTempTable(conn, transaction, codFiscaleCol);

            // Now, get the list of columns from DatiGenerali_dom
            List<string> columnNames = GetColumnNames(conn, transaction, "DatiGenerali_dom");

            // Get the list of columns from vDATIGENERALI_dom
            List<string> vColumns = GetColumnNames(conn, transaction, "vDATIGENERALI_dom");

            // Define the columns that need explicit values
            Dictionary<string, string> explicitValues = new Dictionary<string, string>()
            {
                { "Data_validita", "CURRENT_TIMESTAMP" },
                { "Utente", "@utenteValue" },
                { "Blocco_pagamento", "1" },
                { "Id_domanda", "d.id_domanda" }
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

            // Conditionally include the additional WHERE clause
            string additionalCondition = "";
            if (!inserisciGiaRimossi)
            {
                additionalCondition = @"
            AND NOT EXISTS (
                SELECT 1
                FROM dbo.Motivazioni_blocco_pagamenti mbp
                INNER JOIN dbo.Domanda dd ON mbp.Num_domanda = dd.Num_domanda
                WHERE mbp.Anno_accademico = @annoAcademico
                  AND mbp.Cod_tipologia_blocco = @blockCode
                  AND dd.Cod_fiscale = d.Cod_fiscale
                  AND mbp.Data_fine_validita IS NOT NULL
            )";
            }

            string sql = $@"
            INSERT INTO dbo.Motivazioni_blocco_pagamenti
                (Anno_accademico, Num_domanda, Cod_tipologia_blocco, Blocco_pagamento_attivo,
                    Data_validita, Utente, Data_fine_validita, Utente_sblocco)
            SELECT d.Anno_accademico, d.Num_domanda, @blockCode, '1', 
                    CURRENT_TIMESTAMP, @utenteValue, NULL, NULL
            FROM dbo.Domanda d
            INNER JOIN #CodFiscaleTempTable cf ON d.Cod_fiscale = cf.CodFiscale
            WHERE d.Anno_accademico = @annoAcademico 
                AND d.tipo_bando IN ('lz', 'l2') 
                AND d.Num_domanda NOT IN
                    (SELECT DISTINCT Num_domanda
                        FROM dbo.Motivazioni_blocco_pagamenti
                        WHERE Anno_accademico = @annoAcademico 
                            AND Cod_tipologia_blocco = @blockCode
                            AND Data_fine_validita IS NULL)
            {additionalCondition};

            INSERT INTO [DatiGenerali_dom] ({insertColumnsList})
            SELECT DISTINCT {selectColumnsList}
            FROM 
                Domanda d
                INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico AND 
                                                 d.Num_domanda = v.Num_domanda
                INNER JOIN #CodFiscaleTempTable cf ON d.Cod_fiscale = cf.CodFiscale
            WHERE 
                d.Anno_accademico = @annoAcademico AND
                d.tipo_bando IN ('lz', 'l2') AND
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

                int affectedRows = command.ExecuteNonQuery();
                Logger.LogInfo(null, $"{affectedRows} rows affected in AddBlock operation for BlockCode: {blockCode}.");
            }

            // Drop temporary table
            DropTempTable(conn, transaction);
        }

        public static void RemoveBlock(SqlConnection conn, SqlTransaction transaction, List<string> codFiscaleCol, string blockCode, string annoAccademico, string utente)
        {
            // Use temporary table and bulk insert
            CreateAndPopulateTempTable(conn, transaction, codFiscaleCol);

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
                { "Id_domanda", "d.id_domanda" }
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
                            (SELECT d.Num_domanda
                             FROM dbo.Domanda d
                             INNER JOIN #CodFiscaleTempTable cf ON d.Cod_fiscale = cf.CodFiscale
                             WHERE d.Anno_accademico = @annoAcademico 
                                 AND d.tipo_bando IN ('lz'));

                    INSERT INTO [DatiGenerali_dom] ({insertColumnsList})
                    SELECT DISTINCT {selectColumnsList}
                    FROM 
                        Domanda d
                        INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico AND 
                                                         d.Num_domanda = v.Num_domanda
                        INNER JOIN #CodFiscaleTempTable cf ON d.Cod_fiscale = cf.CodFiscale
                    WHERE 
                        d.Anno_accademico = @annoAcademico AND
                        d.tipo_bando IN ('lz', 'l2') AND
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

                int affectedRows = command.ExecuteNonQuery();
                Logger.LogInfo(null, $"{affectedRows} rows affected in RemoveBlock operation for BlockCode: {blockCode}.");
            }

            // Drop temporary table
            DropTempTable(conn, transaction);
        }

        public static void CreateAndPopulateTempTable(SqlConnection conn, SqlTransaction transaction, List<string> codFiscaleCol)
        {
            // Create temporary table
            string createTempTableSql = "CREATE TABLE #CodFiscaleTempTable (CodFiscale NVARCHAR(16) NOT NULL);";
            using (SqlCommand createCmd = new SqlCommand(createTempTableSql, conn, transaction))
            {
                createCmd.ExecuteNonQuery();
            }

            // Bulk insert into temporary table
            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, transaction))
            {
                bulkCopy.DestinationTableName = "#CodFiscaleTempTable";
                DataTable tempTable = new DataTable();
                tempTable.Columns.Add("CodFiscale", typeof(string));

                foreach (string cf in codFiscaleCol)
                {
                    tempTable.Rows.Add(cf);
                }

                bulkCopy.WriteToServer(tempTable);
            }
        }

        public static void DropTempTable(SqlConnection conn, SqlTransaction transaction)
        {
            string dropTempTableSql = "DROP TABLE #CodFiscaleTempTable;";
            using (SqlCommand dropCmd = new SqlCommand(dropTempTableSql, conn, transaction))
            {
                dropCmd.ExecuteNonQuery();
            }
        }

        // Helper method to get column names
        public static List<string> GetColumnNames(SqlConnection conn, SqlTransaction transaction, string tableName)
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
                        string columnName = reader.GetString(0);
                        if (columnName == "Id_DatiGenerali_dom")
                        {
                            continue;
                        }
                        columnNames.Add(columnName);
                    }
                }
            }
            return columnNames;
        }
    }
}
