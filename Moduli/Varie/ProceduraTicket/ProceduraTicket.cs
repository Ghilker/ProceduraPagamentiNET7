using System;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Text;
using Excel = Microsoft.Office.Interop.Excel;

namespace ProcedureNet7
{
    internal class ProceduraTicket : BaseProcedure<ArgsProceduraTicket>
    {
        private bool _deleteChiusi;
        private bool _deleteAnniPrecedenti;
        private bool _sendMail;

        public ProceduraTicket(MasterForm masterForm, SqlConnection mainConn) : base(masterForm, mainConn) { }

        public override void RunProcedure(ArgsProceduraTicket args)
        {
            _deleteChiusi = args._ticketChecks[0];
            _deleteAnniPrecedenti = args._ticketChecks[1];
            _sendMail = args._ticketChecks[2];
            string ticketFilePath = args._ticketFilePath;
            string mailFilePath = args._mailFilePath;
            string senderMail = string.Empty;
            string senderPassword = string.Empty;



            _masterForm.inProcedure = true;
            try
            {

                Logger.Log(1, $"Caricamento file", LogLevel.INFO);

                DataTable tickets = Utilities.CsvToDataTable(ticketFilePath);

                // 1. Build a HashSet for unwanted CATEGORIA values
                var unwantedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Contributi Alloggio",
                    "Posti Alloggio",
                    "MENSA",
                    "Accettazione Posto Alloggio"
                };

                // 2. Filter the table in one shot
                var filteredRows = tickets.AsEnumerable()
                    .Where(row =>
                        // Keep row if: Stato != "CHIUSO"
                        !row.Field<string>("STATO").Equals("CHIUSO", StringComparison.OrdinalIgnoreCase)

                        // AND CATEGORIA not in the unwanted list
                        && !unwantedCategories.Contains(row.Field<string>("CATEGORIA"))

                        // AND AZIONE != "PRESA_IN_CARICO"
                        && !row.Field<string>("AZIONE").Equals("PRESA_IN_CARICO", StringComparison.OrdinalIgnoreCase)

                        // AND NUM_RICHIESTE_STUDENTE != 0
                        && row.Field<string>("NUM_RICHIESTE_STUDENTE") != "0"
                    );

                // 3. Copy the filtered rows back to a DataTable
                //    (Handle case where all rows are removed)
                if (filteredRows.Any())
                {
                    tickets = filteredRows.CopyToDataTable();
                }
                else
                {
                    throw new Exception("Nessun ticket presente nel file che soddisfa i requisiti");
                }

                // Define an array of columns to remove
                string[] columnsToRemove = new[]
                {
                    "NREC",
                    "NUM_TICKET_CREATI_STUDENTE",
                    "NUM_RICHIESTE_STUDENTE",
                    "NUM_RISPOSTE_OPERATORE",
                    "DATA_ULTIMO_MESSAGGIO",
                    "AZIONE",
                    "UID",
                    "DATA_LOG"
                };

                // Loop through each column name and remove if it exists in the DataTable
                foreach (string columnName in columnsToRemove)
                {
                    if (tickets.Columns.Contains(columnName))
                    {
                        tickets.Columns.Remove(columnName);
                    }
                }

                // Sort using the DefaultView
                tickets.DefaultView.Sort = "CODFISC ASC";

                // Convert the view back into a DataTable
                tickets = tickets.DefaultView.ToTable();

                DataTable joinedData = GetJoinedData(tickets);

                MergeJoinedDataAsStrings(tickets, joinedData);
                string directoryPath = Path.GetDirectoryName(ticketFilePath);
                string excelFile = Utilities.ExportDataTableToExcel(tickets, directoryPath);



                if (_sendMail)
                {
                    // 1) Read mail settings from the file
                    List<string> toEmails = new();
                    List<string> ccEmails = new();

                    using (StreamReader sr = new StreamReader(mailFilePath))
                    {
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.StartsWith("TO#"))
                            {
                                toEmails.Add(line[3..]);
                            }
                            else if (line.StartsWith("CC#"))
                            {
                                ccEmails.Add(line[3..]);
                            }
                            else if (line.StartsWith("ID#") && string.IsNullOrEmpty(senderMail))
                            {
                                senderMail = line[3..];
                            }
                            else if (line.StartsWith("PW#") && string.IsNullOrEmpty(senderPassword))
                            {
                                // IMPORTANT: This should be your app password now, not the normal Gmail password
                                senderPassword = line[3..];
                            }
                        }
                    }

                    Logger.Log(97, "Preparazione mail", LogLevel.INFO);

                    // 2) Configure the SMTP client for Gmail
                    SmtpClient smtpClient = new("smtp.gmail.com")
                    {
                        Port = 587,
                        Credentials = new NetworkCredential(senderMail, senderPassword),
                        EnableSsl = true
                    };

                    // 3) Build the MailMessage
                    MailMessage mailMessage = new()
                    {
                        // You can also set From = new MailAddress(senderMail) if desired
                        From = new MailAddress("giacomo.pavone@laziodisco.it"),
                        Subject = $"Estrazione tickets con esiti e blocchi {DateTime.Now:dd/MM}",
                        Body = @"<p>Buongiorno,</p>
                 <p>vi invio l'estrazione dei ticket aperti e nuovi dal 01/09/2024 
                    con gli esiti di borsa e i blocchi presenti, integrato con le università
                    di appartenenza dello studente.</p>
                 <p>Per domande, chiarimenti e suggerimenti resto a disposizione.</p>
                 <p>Buona giornata e buon lavoro.</p>",
                        IsBodyHtml = true
                    };

                    // 4) Add recipients
                    foreach (var toEmail in toEmails)
                    {
                        mailMessage.To.Add(toEmail);
                    }

                    foreach (var ccEmail in ccEmails)
                    {
                        mailMessage.CC.Add(ccEmail);
                    }

                    Logger.Log(99, "Invio mail", LogLevel.INFO);

                    // 5) Add the Excel attachment
                    mailMessage.Attachments.Add(new Attachment(excelFile));

                    // 6) Send the email
                    try
                    {
                        smtpClient.Send(mailMessage);
                    }
                    catch (Exception ex)
                    {
                        // Handle or log the error
                        Logger.Log(0, $"Errore invio mail: {ex.Message}", LogLevel.ERROR);
                        throw;
                    }
                    finally
                    {
                        mailMessage.Dispose();
                    }
                }

            }

            finally
            {

                GC.Collect();
                GC.WaitForPendingFinalizers();
                _masterForm.inProcedure = false;
                Logger.Log(100, $"Fine Lavorazione", LogLevel.INFO);
            }
        }
        public DataTable GetJoinedData(DataTable tickets)
        {
            // Create a result DataTable
            DataTable result = new DataTable();

            // 1) Build a small DataTable of distinct CODFISC values
            DataTable codFiscTable = new DataTable();
            codFiscTable.Columns.Add("Cod_fiscale", typeof(string));

            var distinctCodFisc = tickets.AsEnumerable()
                                         .Select(r => r.Field<string>("CODFISC"))
                                         .Where(cf => !string.IsNullOrEmpty(cf))
                                         .Distinct();

            foreach (string cf in distinctCodFisc)
            {
                codFiscTable.Rows.Add(cf);
            }

            // 2) Create a temporary table in SQL
            //    We assume CODFISC is at most 16-20 characters; adjust size if needed.
            using (SqlCommand cmdCreateTemp = new SqlCommand(
                @"IF OBJECT_ID('tempdb..#tmpCodFisc') IS NOT NULL
                     DROP TABLE #tmpCodFisc;

                  CREATE TABLE #tmpCodFisc
                  (
                      Cod_fiscale VARCHAR(20) NOT NULL
                  );",
                CONNECTION))
            {
                cmdCreateTemp.ExecuteNonQuery();
            }

            // 3) Bulk copy the distinct CODFISC values into the temp table
            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION))
            {
                bulkCopy.DestinationTableName = "#tmpCodFisc";
                bulkCopy.ColumnMappings.Add("Cod_fiscale", "Cod_fiscale");
                bulkCopy.WriteToServer(codFiscTable);
            }

            // 4) Build the SQL query that joins #tmpCodFisc to your other tables
            string sqlQuery = @"
                SELECT 
                    Domanda.Num_domanda, 
                    Domanda.Cod_fiscale, 
                    vEsiti_concorsiBS.esito_BS as Esito_BS_24_25, 
                    vEsiti_concorsiPA.esito_PA as Esito_PA_24_25, 
                    dbo.SlashDescrBlocchi(Domanda.Num_domanda, Domanda.Anno_accademico, 'BS') AS Blocchi_24_25, 
                    Sede_studi.Descrizione AS Sede_Università_24_25
                FROM #tmpCodFisc t
                    INNER JOIN Domanda
                        ON Domanda.Cod_fiscale = t.Cod_fiscale
                    LEFT JOIN vEsiti_concorsiBS
                        ON Domanda.Anno_accademico = vEsiti_concorsiBS.Anno_accademico
                        AND Domanda.Num_domanda = vEsiti_concorsiBS.Num_domanda
                    LEFT JOIN vEsiti_concorsiPA
                        ON Domanda.Anno_accademico = vEsiti_concorsiPA.Anno_accademico
                        AND Domanda.Num_domanda = vEsiti_concorsiPA.Num_domanda
                    INNER JOIN vIscrizioni
                        ON vIscrizioni.Cod_fiscale = Domanda.Cod_fiscale
                        AND vIscrizioni.Anno_accademico = Domanda.Anno_accademico
                    INNER JOIN Sede_studi
                        ON Sede_studi.Cod_sede_studi = vIscrizioni.Cod_sede_studi
                WHERE 
                    Domanda.Anno_accademico = '20242025'
                    AND Domanda.Tipo_bando IN ('LZ', 'L2')
                ORDER BY 
                    Domanda.Cod_fiscale;
        
                DROP TABLE #tmpCodFisc; -- Clean up temp table
            ";

            // 5) Execute the query and fill result
            using (SqlCommand cmd = new SqlCommand(sqlQuery, CONNECTION))
            {
                using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                {
                    adapter.Fill(result);
                }
            }

            return result;
        }
        public static void MergeJoinedDataAsStrings(
            DataTable tickets,    // original table with "CODFISC"
            DataTable joinedData) // from DB, with "Cod_fiscale" and other columns
        {
            // 1. Add missing columns from joinedData to tickets, **always** as string
            foreach (DataColumn dbCol in joinedData.Columns)
            {
                string colName = dbCol.ColumnName;
                if (colName.Equals("Cod_fiscale", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!tickets.Columns.Contains(colName))
                {
                    // Force the column type to be string
                    tickets.Columns.Add(colName, typeof(string));
                }
            }

            // 2. Build a lookup by "Cod_fiscale"
            //    (Assuming joinedData has 'Cod_fiscale' as string.)
            var joinedLookup = joinedData
                .AsEnumerable()
                .ToDictionary(
                    row => row.Field<string>("Cod_fiscale"),
                    row => row,
                    StringComparer.OrdinalIgnoreCase
                );

            // 3. Fill in the data
            foreach (DataRow ticketsRow in tickets.Rows)
            {
                // Our key in tickets is "CODFISC"
                string codFisc = ticketsRow.Field<string>("CODFISC");
                if (string.IsNullOrEmpty(codFisc)
                    || !joinedLookup.TryGetValue(codFisc, out DataRow dbRow))
                {
                    // No match => fill new columns with empty string
                    foreach (DataColumn dbCol in joinedData.Columns)
                    {
                        string colName = dbCol.ColumnName;
                        if (colName.Equals("Cod_fiscale", StringComparison.OrdinalIgnoreCase))
                            continue;

                        ticketsRow[colName] = "";
                    }
                }
                else
                {
                    // Match found => copy column values as strings
                    foreach (DataColumn dbCol in joinedData.Columns)
                    {
                        string colName = dbCol.ColumnName;
                        if (colName.Equals("Cod_fiscale", StringComparison.OrdinalIgnoreCase))
                            continue;

                        object value = dbRow[colName];

                        // Convert everything to string
                        // If value is null or DBNull, use empty string
                        if (value == null || value == DBNull.Value)
                        {
                            ticketsRow[colName] = "";
                        }
                        else
                        {
                            // Convert to string
                            ticketsRow[colName] = value.ToString();
                        }
                    }
                }
            }
        }
    }
}
