using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    /// <summary>
    /// Describes the results of an AddBlock operation.
    /// </summary>
    public class BlockAddResult
    {
        /// <summary>
        /// CFs that already had an *active* block, so we skipped adding again.
        /// </summary>
        public List<string> AlreadyHasBlock { get; set; } = new();

        /// <summary>
        /// CFs that previously had the block but it was removed – hence we skip adding
        /// when <c>inserisciGiaRimossi = false</c>.
        /// </summary>
        public List<string> PreviouslyRemovedButSkipped { get; set; } = new();

        /// <summary>
        /// CFs for which the block was actually inserted.
        /// </summary>
        public List<string> ActuallyAdded { get; set; } = new();
    }

    /// <summary>
    /// Describes the results of a RemoveBlock operation.
    /// </summary>
    public class BlockRemoveResult
    {
        /// <summary>
        /// CFs that actually had an active block, and we successfully removed it.
        /// </summary>
        public List<string> ActuallyRemoved { get; set; } = new();

        /// <summary>
        /// CFs that did not have an active block, so there was nothing to remove.
        /// </summary>
        public List<string> NothingToRemove { get; set; } = new();
    }


    public static class BlocksUtil
    {
        /// <summary>
        /// Adds a block for the given CF list. Returns details about who actually got added,
        /// who was skipped because they already had it, etc.
        /// </summary>
        public static BlockAddResult AddBlock(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleList,
            string blockCode,
            string annoAccademico,
            string utente,
            bool inserisciGiaRimossi = false)
        {
            var result = new BlockAddResult();

            // 1) Create temp table
            CreateAndPopulateTempTable(conn, transaction, codFiscaleList);

            // Now, get the list of columns from DatiGenerali_dom
            List<string> columnNames = GetColumnNames(conn, transaction, "DatiGenerali_dom");

            // Get the list of columns from vDATIGENERALI_dom
            List<string> vColumns = GetColumnNames(conn, transaction, "vDATIGENERALI_dom");

            // Define the columns that need explicit values
            Dictionary<string, string> explicitValues = new Dictionary<string, string>()
            {
                { "Data_validita", "CURRENT_TIMESTAMP" }, // SQL expression
                { "Utente", "@utenteValue" },             // Parameter
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

            //------------------------------------------
            // 2) Get CFs that already have this block active
            //------------------------------------------
            string alreadyActiveSql = @"
                SELECT DISTINCT d.Cod_fiscale
                FROM dbo.Domanda d
                INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp
                    ON d.Num_domanda = mbp.Num_domanda
                INNER JOIN #CodFiscaleTempTable cf
                    ON cf.CodFiscale = d.Cod_fiscale
                WHERE mbp.Anno_accademico = @annoAccademico
                  AND mbp.Cod_tipologia_blocco = @blockCode
                  AND mbp.Blocco_pagamento_attivo = 1
                  AND mbp.Data_fine_validita IS NULL
                  AND d.Anno_accademico = @annoAccademico
                  AND d.tipo_bando IN ('lz','l2');
            ";

            using (SqlCommand alreadyActiveCmd = new SqlCommand(alreadyActiveSql, conn, transaction))
            {
                alreadyActiveCmd.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                alreadyActiveCmd.Parameters.AddWithValue("@blockCode", blockCode);

                using (SqlDataReader rdr = alreadyActiveCmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        result.AlreadyHasBlock.Add(rdr.GetString(0));
                    }
                }
            }

            //------------------------------------------
            // 3) If inserisciGiaRimossi = false, find who previously had the block but was removed
            //------------------------------------------
            if (!inserisciGiaRimossi)
            {
                string previouslyRemovedSql = @"
                    SELECT DISTINCT d.Cod_fiscale
                    FROM dbo.Domanda d
                    INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp
                        ON d.Num_domanda = mbp.Num_domanda
                    INNER JOIN #CodFiscaleTempTable cf
                        ON cf.CodFiscale = d.Cod_fiscale
                    WHERE d.Anno_accademico = @annoAccademico
                      AND d.tipo_bando IN ('lz','l2')
                      AND mbp.Anno_accademico = @annoAccademico
                      AND mbp.Cod_tipologia_blocco = @blockCode
                      AND mbp.Data_fine_validita IS NOT NULL
                ";

                using (SqlCommand previouslyRemovedCmd = new SqlCommand(previouslyRemovedSql, conn, transaction))
                {
                    previouslyRemovedCmd.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                    previouslyRemovedCmd.Parameters.AddWithValue("@blockCode", blockCode);

                    using (SqlDataReader rdr = previouslyRemovedCmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            result.PreviouslyRemovedButSkipped.Add(rdr.GetString(0));
                        }
                    }
                }
            }



            string skipPreviouslyRemovedCondition = "";
            if (!inserisciGiaRimossi && result.PreviouslyRemovedButSkipped.Count > 0)
            {
                var joined = string.Join("','", result.PreviouslyRemovedButSkipped.Select(x => x.Replace("'", "''")));
                skipPreviouslyRemovedCondition = $"AND d.Cod_fiscale NOT IN ('{joined}')";
            }

            // Combine them:
            string finalSkipCondition = skipPreviouslyRemovedCondition;

            // Example partial insert query:
            string insertSql = $@"
                INSERT INTO dbo.Motivazioni_blocco_pagamenti
                    (Anno_accademico, Num_domanda, Cod_tipologia_blocco, Blocco_pagamento_attivo,
                     Data_validita, Utente, Data_fine_validita, Utente_sblocco)
                SELECT d.Anno_accademico, d.Num_domanda, @blockCode, '1',
                       CURRENT_TIMESTAMP, @utenteValue, NULL, NULL
                FROM dbo.Domanda d
                INNER JOIN #CodFiscaleTempTable cf
                    ON d.Cod_fiscale = cf.CodFiscale
                WHERE d.Anno_accademico = @annoAccademico 
                    AND d.tipo_bando IN ('lz','l2') 
                    AND d.Num_domanda NOT IN
                        (SELECT DISTINCT Num_domanda
                         FROM dbo.Motivazioni_blocco_pagamenti
                         WHERE Anno_accademico = @annoAccademico 
                           AND Cod_tipologia_blocco = @blockCode
                           AND Data_fine_validita IS NULL)
                {finalSkipCondition};

                INSERT INTO [DatiGenerali_dom] ({insertColumnsList})
                SELECT DISTINCT {selectColumnsList}
                FROM 
                    Domanda d
                    INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico AND 
                                                     d.Num_domanda = v.Num_domanda
                    INNER JOIN #CodFiscaleTempTable cf ON d.Cod_fiscale = cf.CodFiscale
                WHERE 
                    d.Anno_accademico = @annoAccademico AND
                    d.tipo_bando IN ('lz','l2') AND
                    d.Num_domanda IN (
                        SELECT DISTINCT Num_domanda
                        FROM Motivazioni_blocco_pagamenti
                        WHERE Anno_accademico = @annoAccademico 
                            AND Data_fine_validita IS NULL
                            AND Blocco_pagamento_attivo = 1
                    )
                    {finalSkipCondition};
            ";

            using (SqlCommand command = new SqlCommand(insertSql, conn, transaction))
            {
                command.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                command.Parameters.AddWithValue("@utenteValue", utente);
                command.Parameters.AddWithValue("@blockCode", blockCode);

                int affectedRows = command.ExecuteNonQuery();
                Logger.LogInfo(null, $"{affectedRows} rows affected in AddBlock operation for BlockCode: {blockCode}.");
            }

            //------------------------------------------
            // 5) Determine who was actually added
            //    We'll do a quick query to see who now has the block (and wasn't already active).
            //------------------------------------------
            // You could refine this logic if your DB structure has multiple "domande" per CF, etc.
            string whoGotAddedSql = @"
                SELECT DISTINCT d.Cod_fiscale
                FROM dbo.Domanda d
                INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp
                    ON d.Num_domanda = mbp.Num_domanda
                INNER JOIN #CodFiscaleTempTable cf
                    ON cf.CodFiscale = d.Cod_fiscale
                WHERE mbp.Anno_accademico = @annoAccademico
                  AND mbp.Cod_tipologia_blocco = @blockCode
                  AND mbp.Blocco_pagamento_attivo = 1
                  AND mbp.Data_fine_validita IS NULL
                  AND d.Anno_accademico = @annoAccademico
                  AND d.tipo_bando IN ('lz','l2')
                  -- Exclude those that were already in that state before:
                  AND d.Cod_fiscale NOT IN (
                      SELECT DISTINCT d2.Cod_fiscale
                      FROM dbo.Domanda d2
                      INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp2
                          ON d2.Num_domanda = mbp2.Num_domanda
                      WHERE mbp2.Anno_accademico = @annoAccademico
                        AND mbp2.Cod_tipologia_blocco = @blockCode
                        AND mbp2.Blocco_pagamento_attivo = 1
                        AND mbp2.Data_fine_validita IS NULL
                        AND d2.Anno_accademico = @annoAccademico
                        AND d2.tipo_bando IN ('lz','l2')
                      GROUP BY d2.Cod_fiscale
                      HAVING COUNT(*) > 1
                  );
            ";

            using (SqlCommand cmdWhoGotAdded = new SqlCommand(whoGotAddedSql, conn, transaction))
            {
                cmdWhoGotAdded.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                cmdWhoGotAdded.Parameters.AddWithValue("@blockCode", blockCode);

                using (SqlDataReader rdr = cmdWhoGotAdded.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        result.ActuallyAdded.Add(rdr.GetString(0));
                    }
                }
            }

            // 6) Drop temp table
            DropTempTable(conn, transaction);

            // 7) Return the result
            return result;
        }


        /// <summary>
        /// Removes a payment block for the given list of num_domanda.
        /// </summary>
        /// <param name="conn">Open SqlConnection</param>
        /// <param name="transaction">Active SqlTransaction</param>
        /// <param name="numDomandaList">List of num_domanda</param>
        /// <param name="blockCode">Block code (Cod_tipologia_blocco)</param>
        /// <param name="annoAccademico">Anno Accademico</param>
        /// <param name="utente">Utente</param>
        /// <returns>A BlockRemoveResult with details of the removal.</returns>
        /// <summary>
        /// Removes a payment block for the given list of num_domanda (as strings).
        /// </summary>
        /// <param name="conn">Open SqlConnection</param>
        /// <param name="transaction">Active SqlTransaction</param>
        /// <param name="numDomandaList">List of num_domanda (string)</param>
        /// <param name="blockCode">Block code (Cod_tipologia_blocco)</param>
        /// <param name="annoAccademico">Anno Accademico</param>
        /// <param name="utente">Utente</param>
        /// <returns>A BlockRemoveResult with details of the removal.</returns>
        public static BlockRemoveResult RemoveBlockNumDomanda(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> numDomandaList,
            string blockCode,
            string annoAccademico,
            string utente)
        {
            var result = new BlockRemoveResult();

            // 1) Create a temp table for Num_domanda (string)
            CreateAndPopulateNumDomandaTempTable(conn, transaction, numDomandaList);

            // 2) Get the list of columns from DatiGenerali_dom and vDATIGENERALI_dom
            List<string> columnNames = GetColumnNames(conn, transaction, "DatiGenerali_dom");
            List<string> vColumns = GetColumnNames(conn, transaction, "vDATIGENERALI_dom");

            // Define columns needing explicit values
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

            // 3) Figure out which num_domanda actually have an active block (before the update)
            string activeBlocksSql = @"
                SELECT DISTINCT d.Num_domanda
                FROM dbo.Domanda d
                INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp
                    ON d.Num_domanda = mbp.Num_domanda
                INNER JOIN #NumDomandaTempTable nd
                    ON nd.NumDomanda = d.Num_domanda
                WHERE mbp.Anno_accademico = @annoAccademico
                  AND mbp.Cod_tipologia_blocco = @blockCode
                  AND mbp.Blocco_pagamento_attivo = 1
                  AND mbp.Data_fine_validita IS NULL
                  AND d.Anno_accademico = @annoAccademico
                  AND d.tipo_bando IN ('lz','l2');
            ";

            var domandeWithActiveBlock = new List<string>();
            using (SqlCommand activeCmd = new SqlCommand(activeBlocksSql, conn, transaction))
            {
                activeCmd.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                activeCmd.Parameters.AddWithValue("@blockCode", blockCode);

                using (SqlDataReader rdr = activeCmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        // Num_domanda is a string
                        domandeWithActiveBlock.Add(Utilities.SafeGetString(rdr, "Num_domanda"));
                    }
                }
            }

            // Everyone else in numDomandaList => no active block to remove
            result.NothingToRemove = numDomandaList
                .Where(dom => !domandeWithActiveBlock.Contains(dom))
                .ToList();

            // 4) Perform the actual remove logic
            string sql = $@"
                UPDATE Motivazioni_blocco_pagamenti
                SET Blocco_pagamento_attivo = 0, 
                    Data_fine_validita = CURRENT_TIMESTAMP, 
                    Utente_sblocco = @utenteValue
                WHERE Anno_accademico = @annoAccademico 
                    AND Cod_tipologia_blocco = @blockCode 
                    AND Blocco_pagamento_attivo = 1
                    AND Num_domanda IN 
                    (
                        SELECT d.Num_domanda
                        FROM dbo.Domanda d
                        INNER JOIN #NumDomandaTempTable nd ON d.Num_domanda = nd.NumDomanda
                        WHERE d.Anno_accademico = @annoAccademico 
                          AND d.tipo_bando IN ('lz','l2')
                    );

                INSERT INTO [DatiGenerali_dom] ({insertColumnsList})
                    SELECT DISTINCT {selectColumnsList}
                    FROM Domanda d
                         INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico 
                                                        AND d.Num_domanda = v.Num_domanda
                         INNER JOIN #NumDomandaTempTable nd ON d.Num_domanda = nd.NumDomanda
                    WHERE d.Anno_accademico = @annoAccademico
                      AND d.tipo_bando IN ('lz', 'l2')
                      AND d.Num_domanda NOT IN (
                          SELECT DISTINCT Num_domanda
                          FROM Motivazioni_blocco_pagamenti
                          WHERE Anno_accademico = @annoAccademico 
                            AND Data_fine_validita IS NULL
                            AND Blocco_pagamento_attivo = 1
                      );
            ";

            using (SqlCommand command = new SqlCommand(sql, conn, transaction))
            {
                command.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                command.Parameters.AddWithValue("@utenteValue", utente);
                command.Parameters.AddWithValue("@blockCode", blockCode);

                int affectedRows = command.ExecuteNonQuery();
                Logger.LogInfo(null, $"{affectedRows} rows affected in RemoveBlock operation for BlockCode: {blockCode}.");
            }

            // 5) Those who actually had it removed are exactly domandeWithActiveBlock
            result.ActuallyRemoved.AddRange(domandeWithActiveBlock);

            // 6) Drop temp table
            DropNumDomandaTempTable(conn, transaction);

            // 7) Return the result
            return result;
        }

        private static void CreateAndPopulateNumDomandaTempTable(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> numDomandaList)
        {
            // Adjust the length/type (VARCHAR(50), NVARCHAR(50), etc.) according to how Num_domanda is stored
            string createTableSql = @"
                IF OBJECT_ID('tempdb..#NumDomandaTempTable') IS NOT NULL
                    DROP TABLE #NumDomandaTempTable;

                CREATE TABLE #NumDomandaTempTable (
                    NumDomanda VARCHAR(50) COLLATE Latin1_General_CI_AS 
                );
            ";

            using (SqlCommand cmd = new SqlCommand(createTableSql, conn, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // Bulk insert approach
            using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, transaction))
            {
                bulk.DestinationTableName = "#NumDomandaTempTable";
                var table = new DataTable();
                table.Columns.Add("NumDomanda", typeof(string));

                foreach (string dom in numDomandaList)
                {
                    table.Rows.Add(dom);
                }

                bulk.WriteToServer(table);
            }
        }

        private static void DropNumDomandaTempTable(SqlConnection conn, SqlTransaction transaction)
        {
            string dropTableSql = @"
        IF OBJECT_ID('tempdb..#NumDomandaTempTable') IS NOT NULL
            DROP TABLE #NumDomandaTempTable;
    ";

            using (SqlCommand cmd = new SqlCommand(dropTableSql, conn, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Removes a block for the given CF list. Returns details about who actually got removed,
        /// and who had nothing to remove.
        /// </summary>
        public static BlockRemoveResult RemoveBlock(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleCol,
            string blockCode,
            string annoAccademico,
            string utente)
        {
            var result = new BlockRemoveResult();

            // 1) Create temp table
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

            // 2) Figure out who actually has an active block (before the update)
            string activeBlocksSql = @"
                SELECT DISTINCT d.Cod_fiscale
                FROM dbo.Domanda d
                INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp
                    ON d.Num_domanda = mbp.Num_domanda
                INNER JOIN #CodFiscaleTempTable cf
                    ON cf.CodFiscale = d.Cod_fiscale
                WHERE mbp.Anno_accademico = @annoAccademico
                  AND mbp.Cod_tipologia_blocco = @blockCode
                  AND mbp.Blocco_pagamento_attivo = 1
                  AND mbp.Data_fine_validita IS NULL
                  AND d.Anno_accademico = @annoAccademico
                  AND d.tipo_bando IN ('lz','l2');
            ";

            var cfsWithActiveBlock = new List<string>();
            using (SqlCommand activeCmd = new SqlCommand(activeBlocksSql, conn, transaction))
            {
                activeCmd.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                activeCmd.Parameters.AddWithValue("@blockCode", blockCode);

                using (SqlDataReader rdr = activeCmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        cfsWithActiveBlock.Add(rdr.GetString(0));
                    }
                }
            }

            // Everyone else in codFiscaleCol => no active block to remove
            result.NothingToRemove = codFiscaleCol
                .Where(cf => !cfsWithActiveBlock.Contains(cf))
                .ToList();

            // 3) Do the actual remove logic
            string sql = $@"
                UPDATE Motivazioni_blocco_pagamenti
                SET Blocco_pagamento_attivo = 0, 
                    Data_fine_validita = CURRENT_TIMESTAMP, 
                    Utente_sblocco = @utenteValue
                WHERE Anno_accademico = @annoAccademico 
                    AND Cod_tipologia_blocco = @blockCode 
                    AND Blocco_pagamento_attivo = 1
                    AND Num_domanda IN 
                        (SELECT d.Num_domanda
                         FROM dbo.Domanda d
                         INNER JOIN #CodFiscaleTempTable cf ON d.Cod_fiscale = cf.CodFiscale
                         WHERE d.Anno_accademico = @annoAccademico 
                           AND d.tipo_bando IN ('lz','l2'));

                INSERT INTO [DatiGenerali_dom] ({insertColumnsList})
                    SELECT DISTINCT {selectColumnsList}
                    FROM 
                        Domanda d
                        INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico AND 
                                                         d.Num_domanda = v.Num_domanda
                        INNER JOIN #CodFiscaleTempTable cf ON d.Cod_fiscale = cf.CodFiscale
                    WHERE 
                        d.Anno_accademico = @annoAccademico AND
                        d.tipo_bando IN ('lz', 'l2') AND
                        d.Num_domanda NOT IN (
                            SELECT DISTINCT Num_domanda
                            FROM Motivazioni_blocco_pagamenti
                            WHERE Anno_accademico = @annoAccademico 
                                AND Data_fine_validita IS NULL
                                AND Blocco_pagamento_attivo = 1
                        );
            ";

            using (SqlCommand command = new SqlCommand(sql, conn, transaction))
            {
                command.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                command.Parameters.AddWithValue("@utenteValue", utente);
                command.Parameters.AddWithValue("@blockCode", blockCode);

                int affectedRows = command.ExecuteNonQuery();
                Logger.LogInfo(null, $"{affectedRows} rows affected in RemoveBlock operation for BlockCode: {blockCode}.");
            }

            // 4) Those who actually had it removed are exactly cfsWithActiveBlock (before the update).
            result.ActuallyRemoved.AddRange(cfsWithActiveBlock);

            // 5) Drop temp table
            DropTempTable(conn, transaction);

            // 6) Return the result
            return result;
        }

        /// <summary>
        /// Returns a Dictionary for the given CF list, indicating whether an active block exists (true/false) 
        /// for each CF. 
        /// </summary>
        /// <param name="conn">An open SqlConnection.</param>
        /// <param name="transaction">An existing SqlTransaction, or null if not using transactions.</param>
        /// <param name="codFiscaleList">List of CFs to check.</param>
        /// <param name="blockCode">The block code to look for.</param>
        /// <param name="annoAccademico">Academic year to filter on.</param>
        /// <returns>Dictionary where key=CF, value=true if an active block is found, false otherwise.</returns>
        public static Dictionary<string, bool> HasBlock(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleList,
            string blockCode,
            string annoAccademico)
        {
            // Prepare the result with default "false" for every CF
            var result = codFiscaleList
                .Distinct() // ensure no duplicates cause key collisions
                .ToDictionary(cf => cf, cf => false);

            if (codFiscaleList == null || codFiscaleList.Count == 0)
            {
                return result; // nothing to do
            }

            // 1) Create temp table and insert CFs
            CreateAndPopulateTempTable(conn, transaction, codFiscaleList);

            // 2) Query for CFs which currently have an active block
            string sql = @"
            SELECT DISTINCT d.Cod_fiscale
            FROM dbo.Domanda d
            INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp ON d.Num_domanda = mbp.Num_domanda and d.anno_accademico = mbp.anno_accademico
            INNER JOIN #CodFiscaleTempTable cf
                ON cf.CodFiscale = d.Cod_fiscale
            WHERE mbp.Cod_tipologia_blocco = @blockCode
              AND mbp.Blocco_pagamento_attivo = 1
              AND mbp.Data_fine_validita IS NULL
              AND d.Anno_accademico = @annoAccademico
              AND d.tipo_bando IN ('lz','l2');
        ";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                cmd.Parameters.AddWithValue("@blockCode", blockCode);

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        string cfWithBlock = rdr.GetString(0);
                        if (result.ContainsKey(cfWithBlock))
                        {
                            result[cfWithBlock] = true;
                        }
                    }
                }
            }

            // 3) Drop temp table
            DropTempTable(conn, transaction);

            // 4) Return dictionary of CF -> bool
            return result;
        }

        /// <summary>
        /// Overload for a single CF check. 
        /// </summary>
        /// <param name="conn">An open SqlConnection.</param>
        /// <param name="transaction">An existing SqlTransaction, or null if not using transactions.</param>
        /// <param name="codFiscale">Single CF to check.</param>
        /// <param name="blockCode">The block code to look for.</param>
        /// <param name="annoAccademico">Academic year to filter on.</param>
        /// <returns>True if the CF has an active block, otherwise false.</returns>
        public static bool HasBlock(
            SqlConnection conn,
            SqlTransaction transaction,
            string codFiscale,
            string blockCode,
            string annoAccademico)
        {
            // Just reuse the bulk logic with a single item
            var singleList = new List<string> { codFiscale };
            Dictionary<string, bool> resultDict = HasBlock(conn, transaction, singleList, blockCode, annoAccademico);
            return resultDict[codFiscale];
        }

        /// <summary>
        /// Create a #CodFiscaleTempTable with a single NVARCHAR(16) column, then bulk insert the CFs.
        /// </summary>
        public static void CreateAndPopulateTempTable(SqlConnection conn, SqlTransaction transaction, List<string> codFiscaleList)
        {
            // Create temporary table
            string createTempTableSql = "CREATE TABLE #CodFiscaleTempTable (CodFiscale NVARCHAR(16) COLLATE Latin1_General_CI_AS);";
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

                foreach (string cf in codFiscaleList)
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


        /// <summary>
        /// Helper method to get column names from a table (example usage).
        /// You can still use this if you continue to do dynamic column inserts in DatiGenerali_dom.
        /// </summary>
        public static List<string> GetColumnNames(SqlConnection conn, SqlTransaction transaction, string tableName)
        {
            List<string> columnNames = new List<string>();
            string query = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName
            ";

            using (SqlCommand cmd = new SqlCommand(query, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string columnName = reader.GetString(0);
                        if (columnName == "Id_DatiGenerali_dom")
                            continue;

                        columnNames.Add(columnName);
                    }
                }
            }
            return columnNames;
        }
    }
}
