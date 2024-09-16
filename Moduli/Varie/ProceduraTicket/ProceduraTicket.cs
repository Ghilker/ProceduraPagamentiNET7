using Microsoft.Office.Interop.Excel;
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

            Excel.Application? excelApp = null;
            _Workbook? excelWorkbook = null;
            _Worksheet? ws = null;

            _masterForm.inProcedure = true;
            try
            {

                Logger.Log(1, $"Caricamento file", LogLevel.INFO);
                if (Path.GetExtension(ticketFilePath).ToLower() == ".csv")
                {
                    Logger.Log(2, $"Caricamento file - conversione a .xlsx", LogLevel.INFO);
                    string xlsFilePath = Path.ChangeExtension(ticketFilePath, "xlsx");
                    excelApp = new Excel.Application();
                    _Workbook wb = excelApp.Workbooks.Open(ticketFilePath);
                    wb.SaveAs(xlsFilePath, XlFileFormat.xlOpenXMLWorkbook, AccessMode: XlSaveAsAccessMode.xlExclusive);

                    _Worksheet sourceWs = wb.Worksheets[1];
                    Excel.Range usedRange = sourceWs.UsedRange;
                    int rowCount = usedRange.Rows.Count;
                    int maxColCount = 0;

                    object[,] dataArray = usedRange.Value2;
                    List<object[]> newData = new();

                    for (int row = 1; row <= rowCount; row++)
                    {
                        string? data = Convert.ToString(dataArray[row, 1]);
                        string[] splitData = data.Split(';');
                        maxColCount = Math.Max(maxColCount, splitData.Length);
                        newData.Add(splitData);
                    }

                    object[,] writeArray = new object[rowCount, maxColCount];
                    for (int i = 0; i < newData.Count; i++)
                    {
                        for (int j = 0; j < newData[i].Length; j++)
                        {
                            writeArray[i, j] = newData[i][j];
                        }
                    }

                    excelWorkbook = wb;
                    ws = excelWorkbook.Worksheets[1];
                    ws.Range[ws.Cells[1, 1], ws.Cells[rowCount, maxColCount]].Value2 = writeArray;

                }
                else
                {
                    excelApp = new Excel.Application();
                    excelWorkbook = excelApp.Workbooks.Open(ticketFilePath);
                    ws = excelWorkbook.Sheets[1];
                }
                int lastRow = ws.Cells[ws.Rows.Count, "E"].End(XlDirection.xlUp).Row;
                int lastCol = ws.Cells[1, ws.Columns.Count].End(XlDirection.xlToLeft).Column;
                if (_deleteChiusi)
                {
                    ws.AutoFilterMode = false;
                    Logger.Log(11, $"Cancellazione chiusi", LogLevel.INFO);
                    Excel.Range filterRange = ws.Range["K1:K" + lastRow];
                    filterRange.AutoFilter(1, "CHIUSO", XlAutoFilterOperator.xlFilterValues);
                    Excel.Range? visibleCells = null;
                    visibleCells = filterRange.Offset[1, 0].SpecialCells(XlCellType.xlCellTypeVisible, Type.Missing);
                    visibleCells?.EntireRow.Delete(XlDeleteShiftDirection.xlShiftUp);
                    ws.AutoFilterMode = false;
                }

                ws.AutoFilterMode = false;
                Logger.Log(11, $"Cancellazione in carico", LogLevel.INFO);
                Excel.Range filterRangeCarico = ws.Range["Q1:Q" + lastRow];
                filterRangeCarico.AutoFilter(1, "PRESA_IN_CARICO", XlAutoFilterOperator.xlFilterValues);
                Excel.Range? visibleCellsCarico = null;
                visibleCellsCarico = filterRangeCarico.Offset[1, 0].SpecialCells(XlCellType.xlCellTypeVisible, Type.Missing);
                visibleCellsCarico?.EntireRow.Delete(XlDeleteShiftDirection.xlShiftUp);
                ws.AutoFilterMode = false;

                Logger.Log(4, $"Cancellazione colonne", LogLevel.INFO);
                ws.Range["A:A"].Delete(XlDeleteShiftDirection.xlShiftToLeft);
                for (int i = 0; i < 5; i++) // M, N, O, P, Q (R and S become Q and R after deletions)
                {
                    ws.Range["M:M"].Delete(XlDeleteShiftDirection.xlShiftToLeft);
                }

                lastRow = ws.Cells[ws.Rows.Count, "E"].End(XlDirection.xlUp).Row;
                lastCol = ws.Cells[1, ws.Columns.Count].End(XlDirection.xlToLeft).Column;

                Logger.Log(10, $"Riordino codici fiscali", LogLevel.INFO);
                Excel.Range sortRange = ws.Range[ws.Cells[1, 1], ws.Cells[lastRow, lastCol]];
                ws.Sort.SortFields.Clear();

                _ = ws.Sort.SortFields.Add(
                    Key: ws.Range["E1:E" + lastRow],
                    SortOn: XlSortOn.xlSortOnValues,
                    Order: XlSortOrder.xlAscending,
                    DataOption: XlSortDataOption.xlSortNormal);

                ws.Sort.SetRange(sortRange);
                ws.Sort.Header = XlYesNoGuess.xlYes;
                ws.Sort.MatchCase = false;
                ws.Sort.SortMethod = XlSortMethod.xlPinYin;
                ws.Sort.Apply();

                Logger.Log(15, $"Cancellazione duplicati", LogLevel.INFO);
                ws.Range[ws.Cells[2, 1], ws.Cells[lastRow, lastCol]].RemoveDuplicates(Columns: 5, Header: XlYesNoGuess.xlNo);

                Logger.Log(20, $"Estrazione database", LogLevel.INFO);
                ExtractFromDB(ws);
                Logger.Log(75, $"Unione dati", LogLevel.INFO);
                CompareAndMove(ws);
                if (_deleteAnniPrecedenti)
                {
                    Logger.Log(90, $"Cancellazione anni precedenti", LogLevel.INFO);
                    DeleteRedRows(ws);
                }

                Logger.Log(96, $"Spostamento colonne", LogLevel.INFO);
                // Delete column M
                ws.Columns["M:M"].Delete(XlDeleteShiftDirection.xlShiftToLeft);

                // Insert a new column at F, copy M to F, then delete M
                ws.Columns["F:F"].Insert(XlInsertShiftDirection.xlShiftToRight, Type.Missing);
                ws.Columns["M:M"].Copy(ws.Range["F1"]);
                ws.Columns["M:M"].Delete(XlDeleteShiftDirection.xlShiftToLeft);

                // Insert a new column at G, copy N to G, then delete N
                ws.Columns["G:G"].Insert(XlInsertShiftDirection.xlShiftToRight, Type.Missing);
                ws.Columns["N:N"].Copy(ws.Range["G1"]);
                ws.Columns["N:N"].Delete(XlDeleteShiftDirection.xlShiftToLeft);

                // Insert a new column at H, copy O to H, then delete O
                ws.Columns["H:H"].Insert(XlInsertShiftDirection.xlShiftToRight, Type.Missing);
                ws.Columns["O:O"].Copy(ws.Range["H1"]);
                ws.Columns["O:O"].Delete(XlDeleteShiftDirection.xlShiftToLeft);

                excelWorkbook.Save();

                // Open the template workbook
                string templateFilePath = @"C:\Users\giacomo_pavone\Desktop\Giacomo\Tickets\TicketMacroOnTemplate.xlsm";
                _Workbook templateWorkbook = excelApp.Workbooks.Open(templateFilePath);

                // Copy content from the current workbook to the second sheet of the template workbook
                _Worksheet templateWs = templateWorkbook.Worksheets[2];
                ws.UsedRange.Copy(templateWs.Range["A1"]);
                _Worksheet templateWsFirst = templateWorkbook.Worksheets[1];
                ws.UsedRange.Copy(templateWsFirst.Range["E10"]);

                // Rename and move the template file
                string currentDate = DateTime.Now.ToString("dd-MM-yyyy");
                string newFileName = $"Ticket aperti-nuovi aa 2324 {currentDate} macro on.xlsm";
                string newFilePath = Path.Combine(@"C:\Users\giacomo_pavone\Desktop\Giacomo\Tickets", newFileName);

                // Save and close the template workbook
                templateWorkbook.SaveAs(newFilePath, XlFileFormat.xlOpenXMLWorkbookMacroEnabled);
                templateWorkbook.Close(false);

                if (_sendMail)
                {
                    List<string> toEmails = new();
                    List<string> ccEmails = new();

                    using (StreamReader sr = new(mailFilePath))
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
                                senderPassword = line[3..];
                            }
                        }
                    }



                    Logger.Log(97, $"Preparazione mail", LogLevel.INFO);
                    SmtpClient smtpClient = new("smtp.gmail.com")
                    {
                        Port = 587,
                        Credentials = new NetworkCredential(senderMail, senderPassword),
                        EnableSsl = true,
                    };

                    MailMessage mailMessage = new()
                    {
                        From = new MailAddress("giacomo.pavone@laziodisco.it"),
                        Subject = $"Estrazione tickets con esiti e blocchi {DateTime.Now:dd/MM}",
                        Body = @"<p>Buongiorno,</p>
                             <p>vi invio l'estrazione dei ticket aperti e nuovi dal 01/07/2024 con gli esiti di borsa e i blocchi presenti, integrato con le università di appartenenza dello studente.</p>
                             <p><strong>Attenzione</strong>, è stata modificata l'estrazione in modo tale da comprendere anche i ticket di studenti che hanno presentato domanda per gli anni accademici precedenti; questi risulteranno senza numero domanda, esiti BS o PA o blocchi.</p>
                             <p>Una volta aperto il file dovete <strong>abilitare la modifica e le macro</strong>, altrimenti le funzioni di ricerca e riordino non funzioneranno.</p>
                             <ul>
                                <li>Se cliccate sulla cella ""Ordina per data"" l'intera lista verrà ordinata dal ticket più vecchio al più nuovo.</li>
                                <li>Se cliccate sulla cella ""Ordina per cod fis"" l'intera lista verrà ordinata per codice fiscale in ordine alfabetico.</li>
                                <li>Se cliccate sulla cella ""Ordina per status tk"" verranno messi prima i ticket aperti.</li>
                             </ul>
                             <p>Gli ordinamenti possono essere cambiati tra ascendente e discendente cliccando sul bottone ""Ordine"".</p>
                             <p>Per cercare direttamente un codice fiscale, basta inserirlo nel riquadro arancione presente nel foglio, la lista sparirà facendo rimanere solo la fila del codice ricercato. Cancellando la ricerca si ripristinerà l'intera lista. Stessa modalità per codice studente e numero domanda.</p>
                             <p>Le intestazioni hanno i filtri attivi, in modo da facilitare le ricerche.</p>
                             <p>In ogni caso, è presente un secondo foglio denominato ""Tickets"" che contiene tutti i dati senza automatizzazioni che potrete usare.</p>
                             <p>Faccio presente che nel file c'è solo un record per ogni studente, per cui bisogna controllare la presenza di altri tickets aperti da ognuno tramite codice studente.</p>
                             <p>Per domande, chiarimenti e suggerimenti resto a disposizione.</p>
                             <p>Buona giornata e buon lavoro.</p>",
                        IsBodyHtml = true
                    };

                    foreach (string toEmail in toEmails)
                    {
                        mailMessage.To.Add(toEmail);
                    }

                    foreach (string ccEmail in ccEmails)
                    {
                        mailMessage.CC.Add(ccEmail);
                    }

                    Logger.Log(99, $"Invio mail", LogLevel.INFO);
                    mailMessage.Attachments.Add(new Attachment(newFilePath));
                    smtpClient.Send(mailMessage);
                    mailMessage.Dispose();
                }
            }

            finally
            {
                // Ensure Excel objects are released
                if (ws != null)
                {
                    _ = Marshal.ReleaseComObject(ws);
                }

                if (excelWorkbook != null)
                {
                    excelWorkbook.Close();
                    _ = Marshal.ReleaseComObject(excelWorkbook);
                }
                if (excelApp != null)
                {
                    excelApp.Quit();
                    _ = Marshal.ReleaseComObject(excelApp);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                _masterForm.inProcedure = false;
                Logger.Log(100, $"Fine Lavorazione", LogLevel.INFO);
            }
        }

        private void ExtractFromDB(_Worksheet ws)
        {
            int lastRowE = ws.Cells[ws.Rows.Count, "E"].End(XlDirection.xlUp).Row;
            StringBuilder sqlInsertsBuilder = new("SET NOCOUNT ON; DECLARE @CFEstrazione dbo.CFEstrazione; ");

            const int batchSize = 1000;
            int batchCount = 0;

            Logger.Log(30, $"Estrazione database - creazione lista CF", LogLevel.INFO);

            // Retrieve all values at once
            Excel.Range range = ws.Range[ws.Cells[2, "E"], ws.Cells[lastRowE, "E"]];
            object[,]? values = range.Value as object[,];

            for (int i = 1; i <= values.GetLength(0); i++)
            {
                if (batchCount >= batchSize)
                {
                    sqlInsertsBuilder.Length--; // Remove last comma
                    _ = sqlInsertsBuilder.Append("; ");
                    batchCount = 0;
                }

                if (batchCount == 0)
                {
                    _ = sqlInsertsBuilder.Append("INSERT INTO @CFEstrazione (Cod_fiscale) VALUES ");
                }

                string value = values[i, 1]?.ToString().Replace("'", "''") ?? string.Empty;
                _ = sqlInsertsBuilder.Append("('").Append(value).Append("'),");
                batchCount++;
            }

            sqlInsertsBuilder.Length--; // Remove last comma
            _ = sqlInsertsBuilder.Append("; ");

            string sqlInserts = sqlInsertsBuilder.ToString();

            // Add your SQL query here
            string sqlQuery = sqlInserts + @"
                SELECT Domanda.Num_domanda, 
                       Domanda.Cod_fiscale, 
                       vEsiti_concorsiBS.esito_BS, 
                       vEsiti_concorsiPA.esito_PA, 
                       dbo.SlashDescrBlocchi(Domanda.Num_domanda, Domanda.Anno_accademico, 'BS') AS Blocchi, 
                       Sede_studi.Descrizione as Sede_Università_23_24 
                FROM Sede_studi 
                INNER JOIN vIscrizioni ON Sede_studi.Cod_sede_studi = vIscrizioni.Cod_sede_studi 
                INNER JOIN Domanda 
                LEFT OUTER JOIN vEsiti_concorsiBS ON Domanda.Anno_accademico = vEsiti_concorsiBS.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsiBS.Num_domanda 
                ON vIscrizioni.Cod_fiscale = Domanda.Cod_fiscale AND vIscrizioni.Anno_accademico = Domanda.Anno_accademico 
                LEFT OUTER JOIN vEsiti_concorsiPA ON Domanda.Anno_accademico = vEsiti_concorsiPA.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsiPA.Num_domanda 
                INNER JOIN @CFEstrazione f ON Domanda.Cod_fiscale = f.Cod_fiscale 
                WHERE (Domanda.Anno_accademico = '20232024') AND (Domanda.Tipo_bando in ('LZ', 'L2')) 
                ORDER BY Domanda.Cod_fiscale

                ";

            using SqlCommand command = new(sqlQuery, CONNECTION);
            using SqlDataReader reader = command.ExecuteReader();
            List<object[]> dataList = new();
            Logger.Log(50, $"Estrazione database - lettura dati", LogLevel.INFO);
            // Extract headers from the reader
            object[] headers = new object[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                headers[i] = reader.GetName(i);
            }
            dataList.Add(headers); // Add headers to the list

            while (reader.Read())
            {
                object[] row = new object[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.GetValue(i);
                }
                dataList.Add(row);
            }
            Logger.Log(60, $"Estrazione database - scrittura su file", LogLevel.INFO);
            // Now write all data including headers to Excel in one go
            WriteAllDataToExcel(ws, dataList);
        }

        private static void WriteAllDataToExcel(_Worksheet ws, List<object[]> dataList)
        {
            int rows = dataList.Count;
            int cols = dataList[0].Length;
            object[,] excelDataArray = new object[rows, cols];

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    excelDataArray[i, j] = dataList[i][j];
                }
            }

            Excel.Range startCell = ws.Cells[1, "AA"]; // Starting from row 1, column AA for headers
            Excel.Range endCell = ws.Cells[rows, 26 + cols]; // Adjust for 0-based index
            Excel.Range writeRange = ws.Range[startCell, endCell];

            writeRange.Value = excelDataArray;
        }

        private void CompareAndMove(_Worksheet ws)
        {
            int lastRowE = ws.Cells[ws.Rows.Count, "E"].End(XlDirection.xlUp).Row;
            int lastRowAB = ws.Cells[ws.Rows.Count, "AB"].End(XlDirection.xlUp).Row;

            // Read columns E and AB into arrays
            dynamic columnE = ws.Range["E2:E" + lastRowE].Value2;
            dynamic columnAB = ws.Range["AB2:AB" + lastRowAB].Value2;

            Dictionary<string, int> dictMatches = new();

            // Populate dictionary with data from column AB
            Logger.Log(80, $"Unione dati - Creazione dizionario di chiavi", LogLevel.INFO);
            for (int i = 1; i <= lastRowAB - 1; i++)
            {
                string? nullableKey = columnAB[i, 1] as string;
                if (nullableKey == null)
                {
                    continue;
                }
                string key = nullableKey;
                if (!dictMatches.ContainsKey(key))
                {
                    dictMatches.Add(key, i + 1);  // Offset by 1 as array is 1-based
                }
            }

            object[,] outputValues = new object[lastRowE - 1, 6];

            Logger.Log(80, $"Unione dati - Inserimento dati nel dizionario", LogLevel.INFO);
            for (int i = 1; i <= lastRowE - 1; i++)
            {
                string? nullableKey = columnE[i, 1] as string;
                if (nullableKey == null)
                {
                    continue;
                }
                string key = nullableKey;
                if (dictMatches.TryGetValue(key, out int rowIndex))
                {
                    dynamic range = ws.Range["AA" + rowIndex + ":AF" + rowIndex].Value2;
                    for (int k = 0; k < 6; k++)
                    {
                        outputValues[i - 1, k] = range[1, k + 1];
                    }
                }
                else if (_deleteAnniPrecedenti)
                {
                    // Mark for deletion later
                    outputValues[i - 1, 0] = "DELETE";
                }
            }

            // Write the output values back to Excel
            ws.Range["L2:Q" + lastRowE].Value2 = outputValues;

            // Apply color formatting for deletion
            if (_deleteAnniPrecedenti)
            {
                Logger.Log(82, $"Unione dati - Segnare righe da eliminare", LogLevel.INFO);
                for (int i = 1; i <= lastRowE - 1; i++)
                {
                    if ((outputValues[i - 1, 0] as string) == "DELETE")
                    {
                        ws.Cells[i + 1, "E"].Interior.Color = ColorTranslator.ToOle(Color.Red);
                    }
                }
            }

            // Copy headers from AA1:AF1 to L1:Q1
            Logger.Log(85, $"Unione dati - Copia dati", LogLevel.INFO);
            Excel.Range sourceRange = ws.Range["AA1:AF1"];
            Excel.Range destinationRange = ws.Range["L1:Q1"];
            sourceRange.Copy(destinationRange);

            // Delete columns AA:AF
            Logger.Log(89, $"Unione dati - Cancellamento esportazione DB", LogLevel.INFO);
            ws.Range["AA:AF"].Delete(XlDeleteShiftDirection.xlShiftToLeft);
        }


        private static void DeleteRedRows(_Worksheet ws)
        {
            int lastRow = ws.Cells[ws.Rows.Count, "E"].End(XlDirection.xlUp).Row;
            ws.AutoFilterMode = false;
            Excel.Range filterRange = ws.Range["L1:L" + lastRow];
            filterRange.AutoFilter(1, "DELETE", XlAutoFilterOperator.xlFilterValues);
            Excel.Range? visibleCells = filterRange.Offset[1, 0].SpecialCells(XlCellType.xlCellTypeVisible, Type.Missing);
            visibleCells?.EntireRow.Delete(XlDeleteShiftDirection.xlShiftUp);
            ws.AutoFilterMode = false;
        }

    }
}
