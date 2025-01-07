using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace ProcedureNet7
{
    public static class MessageUtils
    {
        /// <summary>
        /// Inserts a message for each student (identified by CodFiscale) in bulk into MESSAGGI_STUDENTE.
        /// *Each CodFiscale can have its own distinct message.*
        /// </summary>
        /// <param name="conn">An open SqlConnection.</param>
        /// <param name="transaction">An active SqlTransaction.</param>
        /// <param name="messagesByCodFiscale">Dictionary of CodFiscale -> Message.</param>
        /// <param name="utente">UTENTE who is inserting the message (defaults to 'Area4').</param>
        public static void InsertMessages(
            SqlConnection conn,
            SqlTransaction transaction,
            Dictionary<string, string> messagesByCodFiscale,
            string utente = "Area4")
        {
            if (messagesByCodFiscale == null || messagesByCodFiscale.Count == 0)
            {
                Logger.LogInfo(null, "No messages to insert because dictionary is empty.");
                return;
            }

            // 1. Create and populate the temporary table with (CodFiscale, Message)
            CreateAndPopulateMessagesTempTable(conn, transaction, messagesByCodFiscale);

            // 2. Insert into MESSAGGI_STUDENTE using a SELECT from the temporary table
            string sql = @"
            INSERT INTO [dbo].[MESSAGGI_STUDENTE]
                   ([COD_FISCALE]
                   ,[DATA_INSERIMENTO_MESSAGGIO]
                   ,[MESSAGGIO]
                   ,[LETTO]
                   ,[DATA_LETTURA]
                   ,[UTENTE])
             SELECT mt.CodFiscale,
                    CURRENT_TIMESTAMP,
                    mt.[Message],
                    0,
                    NULL,
                    @Utente
               FROM #MessagesTempTable mt
            ";
            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@Utente", utente);

                int affectedRows = cmd.ExecuteNonQuery();
                Logger.LogInfo(null, $"{affectedRows} rows inserted into MESSAGGI_STUDENTE (dictionary-based).");
            }

            // 3. Drop the temporary table
            DropMessagesTempTable(conn, transaction);
        }

        // ------------------------------
        // HELPER METHODS FOR DICTIONARY
        // ------------------------------

        /// <summary>
        /// Creates #MessagesTempTable (CodFiscale, Message) and bulk-inserts from the dictionary.
        /// </summary>
        private static void CreateAndPopulateMessagesTempTable(
            SqlConnection conn,
            SqlTransaction transaction,
            Dictionary<string, string> messagesByCodFiscale)
        {
            // Create the temporary table
            string createTempTableSql = @"
            IF OBJECT_ID('tempdb..#MessagesTempTable') IS NOT NULL
                DROP TABLE #MessagesTempTable;

            CREATE TABLE #MessagesTempTable 
            (
                CodFiscale NVARCHAR(16) NOT NULL,
                [Message]  NVARCHAR(MAX) NOT NULL
            );
            ";
            using (SqlCommand createCmd = new SqlCommand(createTempTableSql, conn, transaction))
            {
                createCmd.ExecuteNonQuery();
            }

            // Prepare a DataTable with matching schema
            DataTable tempTable = new DataTable();
            tempTable.Columns.Add("CodFiscale", typeof(string));
            tempTable.Columns.Add("Message", typeof(string));

            foreach (var kvp in messagesByCodFiscale)
            {
                string cf = kvp.Key?.Trim() ?? string.Empty;
                string msg = kvp.Value ?? string.Empty;

                // If a row has an empty CF, skip it
                if (string.IsNullOrEmpty(cf))
                    continue;

                tempTable.Rows.Add(cf, msg);
            }

            // Bulk copy into #MessagesTempTable
            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, transaction))
            {
                bulkCopy.DestinationTableName = "#MessagesTempTable";
                bulkCopy.WriteToServer(tempTable);
            }
        }

        /// <summary>
        /// Drops the #MessagesTempTable.
        /// </summary>
        private static void DropMessagesTempTable(SqlConnection conn, SqlTransaction transaction)
        {
            string dropTempTableSql = "DROP TABLE #MessagesTempTable;";
            using (SqlCommand dropCmd = new SqlCommand(dropTempTableSql, conn, transaction))
            {
                dropCmd.ExecuteNonQuery();
            }
        }

        // ----------------------------
        // ORIGINAL (LIST-BASED) METHOD
        // ----------------------------

        /// <summary>
        /// Inserts the *same* message for a list of CodFiscali in bulk.
        /// </summary>
        public static void InsertMessages(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleList,
            string message,
            string utente = "Area4")
        {
            if (codFiscaleList == null || codFiscaleList.Count == 0)
            {
                Logger.LogInfo(null, "No codFiscali to insert because list is empty.");
                return;
            }

            CreateAndPopulateTempTable(conn, transaction, codFiscaleList);

            string sql = @"
                INSERT INTO [dbo].[MESSAGGI_STUDENTE]
                       ([COD_FISCALE]
                       ,[DATA_INSERIMENTO_MESSAGGIO]
                       ,[MESSAGGIO]
                       ,[LETTO]
                       ,[DATA_LETTURA]
                       ,[UTENTE])
                 SELECT cf.CodFiscale,
                        CURRENT_TIMESTAMP,
                        @Message,
                        0,
                        NULL,
                        @Utente
                   FROM #CodFiscaleTempTable cf
                ";
            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@Message", message);
                cmd.Parameters.AddWithValue("@Utente", utente);

                int affectedRows = cmd.ExecuteNonQuery();
                Logger.LogInfo(null, $"{affectedRows} rows inserted into MESSAGGI_STUDENTE (list-based).");
            }

            DropTempTable(conn, transaction);
        }

        private static void CreateAndPopulateTempTable(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleList)
        {
            string createTempTableSql = @"
            IF OBJECT_ID('tempdb..#CodFiscaleTempTable') IS NOT NULL
                DROP TABLE #CodFiscaleTempTable;

            CREATE TABLE #CodFiscaleTempTable 
            (
                CodFiscale NVARCHAR(16) NOT NULL
            );
            ";
            using (SqlCommand createCmd = new SqlCommand(createTempTableSql, conn, transaction))
            {
                createCmd.ExecuteNonQuery();
            }

            DataTable tempTable = new DataTable();
            tempTable.Columns.Add("CodFiscale", typeof(string));

            foreach (string cf in codFiscaleList)
            {
                if (!string.IsNullOrWhiteSpace(cf))
                    tempTable.Rows.Add(cf.Trim());
            }

            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, transaction))
            {
                bulkCopy.DestinationTableName = "#CodFiscaleTempTable";
                bulkCopy.WriteToServer(tempTable);
            }
        }

        private static void DropTempTable(SqlConnection conn, SqlTransaction transaction)
        {
            string dropTempTableSql = "DROP TABLE #CodFiscaleTempTable;";
            using (SqlCommand dropCmd = new SqlCommand(dropTempTableSql, conn, transaction))
            {
                dropCmd.ExecuteNonQuery();
            }
        }
    }
}
