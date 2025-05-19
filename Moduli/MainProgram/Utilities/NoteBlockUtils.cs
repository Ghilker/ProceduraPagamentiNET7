using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    static class NoteBlockUtils
    {
        /// <summary>
        /// Inserts notes in bulk for the given (CF, Block) pairs, capturing the newly inserted Id_nota_blocco
        /// and updating the corresponding rows in Motivazioni_blocco_pagamenti.
        /// </summary>
        /// <param name="conn">An open SqlConnection.</param>
        /// <param name="transaction">Active SqlTransaction.</param>
        /// <param name="blocksNotes">
        /// A dictionary whose key is (CF, Block), and value is the note/“messaggio” to be inserted.
        /// </param>
        /// <param name="utente">The user performing the operation.</param>
        /// <param name="annoAccademico">The academic year to filter on when updating Motivazioni_blocco_pagamenti.</param>
        public static void InsertBlockNotes(
            SqlConnection conn,
            SqlTransaction transaction,
            Dictionary<(string CF, string Block), string> blocksNotes,
            string utente,
            string annoAccademico)
        {
            if (blocksNotes == null || blocksNotes.Count == 0)
                return; // nothing to insert

            // 1. Build a DataTable from the blocksNotes dictionary.
            DataTable notesTable = new DataTable();
            notesTable.Columns.Add("CodFiscale", typeof(string));
            notesTable.Columns.Add("Block", typeof(string));
            notesTable.Columns.Add("Messaggio", typeof(string));

            foreach (var kvp in blocksNotes)
            {
                (string CF, string Block) key = kvp.Key;
                string note = kvp.Value;

                DataRow row = notesTable.NewRow();
                row["CodFiscale"] = key.CF;
                row["Block"] = key.Block;
                row["Messaggio"] = note;
                notesTable.Rows.Add(row);
            }

            // 2. Create a temporary table to hold the bulk note data.
            string createTempTableSql = @"
                IF OBJECT_ID('tempdb..#NotesTemp') IS NOT NULL
                    DROP TABLE #NotesTemp;
                CREATE TABLE #NotesTemp (
                    CodFiscale NVARCHAR(16) NOT NULL,
                    Block NVARCHAR(50) NOT NULL,
                    Messaggio NVARCHAR(MAX) NOT NULL
                );
            ";
            using (SqlCommand cmd = new SqlCommand(createTempTableSql, conn, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // 3. Bulk copy the data into the temporary table.
            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, transaction))
            {
                bulkCopy.DestinationTableName = "#NotesTemp";
                bulkCopy.WriteToServer(notesTable);
            }

            // 4. Create a temporary table to capture the output mapping.
            string createOutputTableSql = @"
                IF OBJECT_ID('tempdb..#OutputNotes') IS NOT NULL
                    DROP TABLE #OutputNotes;
                CREATE TABLE #OutputNotes (
                    Id_nota_blocco INT,
                    CodFiscale NVARCHAR(16),
                    Block NVARCHAR(50)
                );
            ";
            using (SqlCommand cmd = new SqlCommand(createOutputTableSql, conn, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // 5. Use MERGE to insert into Note_blocchi in bulk and capture new IDs.
            string insertNotesSql = @"
                MERGE INTO Note_blocchi WITH (HOLDLOCK) AS target
                USING (SELECT CodFiscale, Block, Messaggio FROM #NotesTemp) AS source
                    ON 1 = 0
                WHEN NOT MATCHED THEN
                    INSERT (Messaggio_blocco, Data_validita, Utente, Letto, Data_lettura)
                    VALUES (source.Messaggio, CURRENT_TIMESTAMP, @utente, 0, NULL)
                OUTPUT inserted.Id_nota_blocco, source.CodFiscale, source.Block
                    INTO #OutputNotes (Id_nota_blocco, CodFiscale, Block);
            ";
            using (SqlCommand cmd = new SqlCommand(insertNotesSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@utente", utente);
                cmd.ExecuteNonQuery();
            }

            // 6. Bulk update Motivazioni_blocco_pagamenti by joining the output mapping.
            string updateMbpSql = @"
                UPDATE mbp
                SET mbp.Id_nota_blocco = o.Id_nota_blocco
                FROM Motivazioni_blocco_pagamenti mbp
                INNER JOIN Domanda d ON d.Num_domanda = mbp.Num_domanda
                INNER JOIN #OutputNotes o ON d.Cod_fiscale = o.CodFiscale
                                          AND mbp.Cod_tipologia_blocco = o.Block
                WHERE mbp.Anno_accademico = @anno
                  AND mbp.Id_nota_blocco IS NULL;
            ";
            using (SqlCommand cmd = new SqlCommand(updateMbpSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@anno", annoAccademico);
                cmd.ExecuteNonQuery();
            }

            // 7. Clean up temporary tables.
            string dropTempSql = @"
                DROP TABLE #NotesTemp;
                DROP TABLE #OutputNotes;
            ";
            using (SqlCommand cmd = new SqlCommand(dropTempSql, conn, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Overload of InsertBlockNotes that applies a single note to every CF for the given block.
        /// </summary>
        /// <param name="conn">Open SqlConnection</param>
        /// <param name="transaction">Active SqlTransaction</param>
        /// <param name="codFiscaleList">List of CFs to which the note will be applied</param>
        /// <param name="blockCode">The block code for all the CFs</param>
        /// <param name="note">The single note to apply to every CF</param>
        /// <param name="utente">User name performing the operation</param>
        /// <param name="annoAccademico">Academic year filter</param>
        public static void InsertBlockNotes(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleList,
            string blockCode,
            string note,
            string utente,
            string annoAccademico)
        {
            if (codFiscaleList == null || codFiscaleList.Count == 0 || string.IsNullOrWhiteSpace(note))
                return; // nothing to insert

            // Build a dictionary where each CF in the list gets the same note for the specified block.
            var blocksNotes = new Dictionary<(string CF, string Block), string>();
            foreach (string cf in codFiscaleList)
            {
                blocksNotes[(cf, blockCode)] = note;
            }

            // Call the main bulk insert method that handles (CF, Block) notes.
            InsertBlockNotes(conn, transaction, blocksNotes, utente, annoAccademico);
        }

    }
}
