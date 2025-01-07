using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

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
        string selectedBeneficio = string.Empty;
        string AAsplit = string.Empty;
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
            { "BS", "LZ" },
            { "PA", "LZ" },
            { "CI", "LZ" },
            { "PL", "PL" },
            { "BL", "BL" }
        };
        string selectedTipoBando = string.Empty;

        List<Studente> studenti = new List<Studente>();

        public override void RunProcedure(ArgsProceduraAllegati args)
        {
            _masterForm.inProcedure = true;
            Logger.Log(0, "Inizio procedura allegati", LogLevel.INFO);
            selectedAA = args._selectedAA;
            selectedCfFile = args._selectedFileExcel;
            selectedSavePath = args._selectedSaveFolder;
            selectedTipoAllegato = args._selectedTipoAllegato;
            selectedNomeAllegato = args._selectedTipoAllegatoName;
            selectedBeneficio = args._selectedTipoBeneficio;
            selectedTipoBando = decodTipoBando[selectedBeneficio];


            Logger.Log(10, "Creazione dataTable", LogLevel.INFO);
            System.Data.DataTable cfDaLavorareDT = Utilities.ReadExcelToDataTable(selectedCfFile);
            List<string> cfs = new List<string>();
            foreach (DataRow row in cfDaLavorareDT.Rows)
            {
                string codFiscale = row[0].ToString().ToUpper().Trim();
                Studente studente = new Studente(codFiscale);
                cfs.Add(codFiscale);
                studenti.Add(studente);
            }

            List<Studente> sortedStudenti = studenti.OrderBy(s => s.GetFiscalCode()).ToList();
            studenti.Clear();
            studenti = sortedStudenti;

            Logger.Log(20, "Inserimento informazioni da database", LogLevel.INFO);
            string fiscalCodes = string.Join(", ", cfs.Select(cf => $"'{cf}'"));
            string sqlStudente = $@"
                        SELECT Domanda.Cod_fiscale, Domanda.num_domanda, Cognome, Nome, Data_nascita, Codice_studente
                        FROM Domanda INNER JOIN
                            Studente ON Domanda.Cod_fiscale = Studente.cod_fiscale
                        WHERE
                            Domanda.Anno_accademico = '{selectedAA}' AND Domanda.Tipo_bando = '{selectedTipoBando}' AND Domanda.Cod_fiscale in ({fiscalCodes})
                        ";
            using (SqlCommand cmd = new(sqlStudente, CONNECTION))
            {
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                    Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        DateTime.TryParse(Utilities.SafeGetString(reader, "data_nascita"), out DateTime dataNascita);
                        studente.AddInformations(
                            Utilities.SafeGetString(reader, "nome").Trim(),
                            Utilities.SafeGetString(reader, "Cognome").Trim(),
                            dataNascita,
                            Utilities.SafeGetString(reader, "codice_studente").Trim(),
                            Utilities.SafeGetString(reader, "num_domanda"));
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Studente con CF {codFiscale} non trovato");
                    }
                }
            }

            Logger.Log(30, "Creazione dataTable per file", LogLevel.INFO);
            System.Data.DataTable newTable = new DataTable();
            string firstHalfAA = selectedAA.Substring(0, 4);
            string secondHalfAA = selectedAA.Substring(4, 4);
            AAsplit = firstHalfAA + "/" + secondHalfAA;
            int newValueColumns = 0;
            switch (selectedTipoAllegato)
            {
                case "01"://Riammissione a vincitore
                    newTable = RiammissioneVincitore();
                    break;
                case "02"://Riammissione a idoneo
                    newTable = AddSoloImporto();
                    newValueColumns = 1;
                    break;
                case "03"://Revoca senza recupero somme
                case "11"://Rinuncia senza recupero somme
                    newTable = AddRevocaSenzaRecupero();
                    newValueColumns = 3;
                    break;
                case "04"://Decadenza
                    newTable = AddDecadenza();
                    newValueColumns = 1;
                    break;
                case "05"://Modifica importo
                    newTable = AddCambioAnnoCorso();
                    newValueColumns = 3;
                    break;
                case "06"://Revoca con recupero somme
                case "10"://Rinuncia con recupero somme
                    newTable = AddRecuperoSomme();
                    newValueColumns = 4;
                    break;
                case "09"://Da idoneo a vincitore
                    newTable = AddDaIdoneoVincitore();
                    newValueColumns = 1;
                    break;
                case "13"://Cambio status sede
                    newTable = AddCambioStatusSede();
                    newValueColumns = 3;
                    break;
            }

            if (newTable == null || newTable.Rows.Count <= 0)
            {
                _masterForm.inProcedure = false;
                Logger.Log(100, "Indicare una procedura implementata", LogLevel.INFO);
                return;
            }

            Logger.Log(40, "Creazione file excel", LogLevel.INFO);
            string excelFilePath = Utilities.ExportDataTableToExcel(newTable, selectedSavePath, true, $"{selectedNomeAllegato}_{selectedBeneficio}_{firstHalfAA.Substring(2, 2)}_{secondHalfAA.Substring(2, 2)}_{DateTime.Now:dd_mm_yyyy}");

            // Report progress: Opening Excel file
            Logger.Log(50, "Opening Excel file", LogLevel.INFO);
            // Initialize Excel application
            Excel.Application excelApp = new Excel.Application();
            // Open the workbook
            Excel.Workbook workbook = excelApp.Workbooks.Open(excelFilePath);
            try
            {
                // Access the first sheet
                Excel.Worksheet worksheet = (Excel.Worksheet)workbook.Sheets[1];

                // Report progress: Applying changes to the Excel file
                Logger.Log(60, "Applying changes to Excel file", LogLevel.INFO);

                // Delete the first row
                Excel.Range firstRow = worksheet.Rows[1];
                firstRow.Delete(Excel.XlDeleteShiftDirection.xlShiftUp);

                // Merge and format the new first row
                FormatFirstRow(worksheet);

                // Color the third row
                ColorThirdRow(worksheet);

                // Find the last row and column with data
                int lastRow = FindLastRow(worksheet);
                string lastColumn = FindLastColumn(worksheet);
                // Format the data range
                FormatDataRange(worksheet, $"A3", $"{lastColumn}{lastRow}");
                FormatLastColumnsAsMoney(worksheet, newValueColumns);

                Excel.Range lastRowRange = worksheet.Rows[lastRow];
                lastRowRange.Font.Bold = true;
                // Determine the last column in use on the last row
                int lastColumnNumber = worksheet.Cells[lastRow, worksheet.Columns.Count].End(Excel.XlDirection.xlToLeft).Column;

                // Loop through each column in the last row
                for (int col = 1; col <= lastColumnNumber; col++)
                {
                    // Check if the cell in the last row is filled
                    if (worksheet.Cells[lastRow, col].Value != null)
                    {
                        // If the cell is filled, auto-fit the column width
                        ((Excel.Range)worksheet.Cells[lastRow, col]).EntireColumn.AutoFit();
                    }
                }

                // Report progress: Saving Excel file
                Logger.Log(90, "Saving Excel file", LogLevel.INFO);

                if (selectedTipoAllegato == "06" || selectedTipoAllegato == "10")
                {
                    worksheet.Columns["B:B"].EntireColumn.Hidden = true;
                    worksheet.Columns["C:C"].EntireColumn.Hidden = true;
                    worksheet.Columns["F:F"].EntireColumn.Hidden = true;
                    worksheet.Columns["G:G"].EntireColumn.Hidden = true;
                    worksheet.Columns["H:H"].EntireColumn.Hidden = true;
                }
                else
                {
                    worksheet.Columns["B:B"].EntireColumn.Hidden = true;
                    worksheet.Columns["E:E"].EntireColumn.Hidden = true;
                    worksheet.Columns["F:F"].EntireColumn.Hidden = true;
                    worksheet.Columns["G:G"].EntireColumn.Hidden = true;
                }

                worksheet.PageSetup.Orientation = Excel.XlPageOrientation.xlLandscape;
                worksheet.PageSetup.CenterHorizontally = true;
                // Fit the sheet content to one page wide (the length can vary)
                worksheet.PageSetup.Zoom = false; // Disable zoom scaling
                worksheet.PageSetup.FitToPagesWide = 1; // Fit content to one page wide
                worksheet.PageSetup.FitToPagesTall = false; // Don't constrain the page height

                // Report progress: Preparing to save as PDF
                Logger.Log(95, "Preparing to save as PDF", LogLevel.INFO);

                // Define the PDF file path
                string pdfFilePath = System.IO.Path.ChangeExtension(excelFilePath, ".pdf");

                // Export to PDF
                workbook.ExportAsFixedFormat(Excel.XlFixedFormatType.xlTypePDF, pdfFilePath);

                // Optionally, unhide the columns if necessary
                worksheet.Columns["B:B"].EntireColumn.Hidden = false;
                worksheet.Columns["E:E"].EntireColumn.Hidden = false;
                worksheet.Columns["F:F"].EntireColumn.Hidden = false;
                worksheet.Columns["G:G"].EntireColumn.Hidden = false;

                Excel.Range deleteRange = worksheet.Columns[lastColumnNumber + 1];
                deleteRange.Delete();

                // Report progress: PDF generation complete
                Logger.Log(100, "PDF generation complete", LogLevel.INFO);

            }
            finally
            {
                // Save, close, and release resources
                workbook.Save();
                workbook.Close(true);
                Marshal.ReleaseComObject(workbook);
                Logger.Log(100, "Excel file processing complete", LogLevel.INFO);
                _masterForm.inProcedure = false;
                if (excelApp != null)
                {
                    excelApp.Quit();
                    Marshal.ReleaseComObject(excelApp);
                }
            }

            string PromptUserForName()
            {
                using (Form prompt = new Form())
                {
                    prompt.Width = 500;
                    prompt.Height = 150;
                    prompt.Text = "Nome Responsabile Trasparenza";

                    Label textLabel = new Label() { Left = 50, Top = 20, Text = "Inserisci il nome del responsabile della trasparenza:" };
                    TextBox textBox = new TextBox() { Left = 50, Top = 50, Width = 400 };
                    Button confirmation = new Button() { Text = "Ok", Left = 350, Width = 100, Top = 70 };
                    confirmation.Click += (sender, e) => { prompt.Close(); };

                    prompt.Controls.Add(textLabel);
                    prompt.Controls.Add(textBox);
                    prompt.Controls.Add(confirmation);
                    prompt.AcceptButton = confirmation;

                    prompt.ShowDialog();

                    return textBox.Text;
                }
            }

            void FormatFirstRow(Excel.Worksheet worksheet)
            {
                // Find the letter of the last column with data
                string lastColumn = FindLastColumn(worksheet);

                // Access the first row
                Excel.Range firstRow = worksheet.Rows[1];

                // Merge cells from A1 to the last column with data in the first row
                Excel.Range mergeRange = worksheet.Range["A1", $"{lastColumn}1"];
                mergeRange.Merge();

                // Set horizontal and vertical alignment
                mergeRange.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                mergeRange.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;

                // Set the row height and font size
                firstRow.RowHeight *= 2; // Double the height of the first row
                mergeRange.Font.Size = 16; // Set font size to 16
                mergeRange.Font.Bold = true; // Make font bold

                ApplyBorders(mergeRange.Borders, true);
            }
            void FormatLastColumnsAsMoney(Excel.Worksheet worksheet, int numberOfColumnsFromLast)
            {
                // Find the last column with data to know where to start
                string lastColumnLetter = FindLastColumn(worksheet);
                int lastColumnNumber = ConvertColumnLetterToNumber(lastColumnLetter);

                // Find the last row with data to define the range height
                int lastRow = FindLastRow(worksheet);

                // Calculate the starting column number (we subtract one less because we include the last column in the count)
                int startColumnNumber = lastColumnNumber - numberOfColumnsFromLast + 1;

                // Ensure the start column is at least 1 (in case numberOfColumnsFromLast is greater than the number of columns with data)
                startColumnNumber = Math.Max(startColumnNumber, 1);

                // Iterate over the specified number of columns from the last, backwards
                for (int col = startColumnNumber; col <= lastColumnNumber; col++)
                {
                    // Convert the current column number back to its letter representation
                    string columnLetter = ConvertColumnNumberToLetter(col);

                    // Iterate through each row in the current column, starting from row 4
                    for (int row = 4; row <= lastRow; row++)
                    {
                        Excel.Range cell = worksheet.Cells[row, col];
                        string? nullableCellValue = cell.Text as string; // Get the text representation of the cell value
                        if (nullableCellValue == null)
                        {
                            nullableCellValue = string.Empty;
                        }
                        string cellValue = nullableCellValue;

                        // Attempt to convert the text to a double
                        if (double.TryParse(cellValue, out double numericValue))
                        {
                            cell.Value2 = numericValue; // Set the cell value to its numeric form
                        }
                    }
                    // After converting text to numbers, apply the currency format to the entire column range
                    Excel.Range moneyRange = worksheet.Range[$"{columnLetter}4", $"{columnLetter}{lastRow}"];
                    moneyRange.NumberFormat = "€ #,##0.00"; // Apply the Euro currency format
                }
            }
            int ConvertColumnLetterToNumber(string columnLetter)
            {
                int sum = 0;
                foreach (char c in columnLetter.ToUpper())
                {
                    sum *= 26;
                    sum += (c - 'A' + 1);
                }
                return sum;
            }
            void ColorThirdRow(Excel.Worksheet worksheet)
            {
                // Access the third row
                Excel.Range thirdRow = worksheet.Rows[3];

                // Find the last non-empty cell in the third row to determine the range to color
                Excel.Range lastCellInRow3 = thirdRow.Cells[1, thirdRow.Columns.Count].End[Excel.XlDirection.xlToLeft];
                Excel.Range colorRange = worksheet.Range["A3", lastCellInRow3];

                // Set the background color of the range
                colorRange.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.FromArgb(70, 115, 200));

                // Optional: Adjust the row height and font if needed
                thirdRow.RowHeight *= 2; // Example: Double the height of the third row
                thirdRow.Font.Size = 16; // Example: Set font size to 16 for the third row
                thirdRow.Font.Bold = true; // Example: Make font bold for the third row
            }
            int FindLastRow(Excel.Worksheet worksheet)
            {
                // Define the column in which to search for the last non-empty cell. For this example, we use column B.
                string searchColumn = "B:B";

                // Use the Find method to search for the last non-empty cell in the specified column.
                Excel.Range lastCell = worksheet.Columns[searchColumn].Find(
                    What: "*", // What to search for; "*" represents any value.
                    After: worksheet.Cells[1, 2], // Start searching after the first cell in the column. Adjust the second parameter based on the column index.
                    LookIn: Excel.XlFindLookIn.xlValues, // Look in cell values.
                    LookAt: Excel.XlLookAt.xlPart, // Match part of the cell content.
                    SearchOrder: Excel.XlSearchOrder.xlByRows, // Search by rows.
                    SearchDirection: Excel.XlSearchDirection.xlPrevious, // Search from the bottom up.
                    MatchCase: false, // Do not match case.
                    MatchByte: false, // Parameter relevant for double-byte character set languages.
                    SearchFormat: false // Do not search using cell format as criteria.
                );

                // If a last cell is found, return its row number; otherwise, return 1.
                return lastCell?.Row ?? 1; // The "??" operator returns 1 if lastCell is null.
            }
            string FindLastColumn(Excel.Worksheet worksheet)
            {
                // Use the Find method to search for the last non-empty cell across all rows in the worksheet
                Excel.Range lastCell = worksheet.Cells.Find(
                    What: "*", // What to search for; "*" represents any value.
                    After: worksheet.Cells[1, 1], // Start searching after the first cell in the worksheet.
                    LookIn: Excel.XlFindLookIn.xlFormulas, // Look in formulas. Use xlValues if you want to search only values.
                    LookAt: Excel.XlLookAt.xlPart, // Match part of the cell content.
                    SearchOrder: Excel.XlSearchOrder.xlByColumns, // Search by columns.
                    SearchDirection: Excel.XlSearchDirection.xlPrevious, // Search from the end backwards.
                    MatchCase: false, // Do not match case.
                    MatchByte: false, // Parameter relevant for double-byte character set languages.
                    SearchFormat: false // Do not search using cell format as criteria.
                );

                // If a last cell is found, extract and return its column letter; otherwise, return "A".
                if (lastCell != null)
                {
                    int lastColumn = lastCell.Column;
                    // Convert column number to its corresponding letter
                    return ConvertColumnNumberToLetter(lastColumn);
                }
                return "A"; // Default to "A" if no data is found
            }
            string ConvertColumnNumberToLetter(int columnNumber)
            {
                string columnLetter = String.Empty;
                while (columnNumber > 0)
                {
                    int modulo = (columnNumber - 1) % 26;
                    columnLetter = Convert.ToChar('A' + modulo) + columnLetter;
                    columnNumber = (columnNumber - modulo) / 26;
                }
                return columnLetter;
            }
            void FormatDataRange(Excel.Worksheet worksheet, string startCell, string endCell)
            {
                Excel.Range dataRange = worksheet.Range[startCell, endCell];

                ApplyBorders(dataRange.Borders);

                dataRange.HorizontalAlignment = Excel.XlHAlign.xlHAlignLeft;
                dataRange.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;

                dataRange.Rows.RowHeight = 20;
                dataRange.Font.Size = 12;

                ModifyColumnWidth(dataRange);
            }
            void ModifyColumnWidth(Excel.Range range)
            {
                Excel.Range firstRow = range.Rows[1];
                for (int colIndex = 1; colIndex <= range.Columns.Count; colIndex++)
                {
                    Excel.Range column = firstRow.Columns[colIndex];
                    column.ColumnWidth *= 2;
                }
            }
            void ApplyBorders(Excel.Borders borders, bool thickBorders = false)
            {
                // Set the line style for each border of the range
                borders[Excel.XlBordersIndex.xlEdgeLeft].LineStyle = Excel.XlLineStyle.xlContinuous;
                borders[Excel.XlBordersIndex.xlEdgeTop].LineStyle = Excel.XlLineStyle.xlContinuous;
                borders[Excel.XlBordersIndex.xlEdgeBottom].LineStyle = Excel.XlLineStyle.xlContinuous;
                borders[Excel.XlBordersIndex.xlEdgeRight].LineStyle = Excel.XlLineStyle.xlContinuous;
                borders[Excel.XlBordersIndex.xlInsideVertical].LineStyle = Excel.XlLineStyle.xlContinuous;
                borders[Excel.XlBordersIndex.xlInsideHorizontal].LineStyle = Excel.XlLineStyle.xlContinuous;

                if (thickBorders)
                {
                    borders[Excel.XlBordersIndex.xlEdgeLeft].Weight = Excel.XlBorderWeight.xlMedium;
                    borders[Excel.XlBordersIndex.xlEdgeTop].Weight = Excel.XlBorderWeight.xlMedium;
                    borders[Excel.XlBordersIndex.xlEdgeBottom].Weight = Excel.XlBorderWeight.xlMedium;
                    borders[Excel.XlBordersIndex.xlEdgeRight].Weight = Excel.XlBorderWeight.xlMedium;
                    borders[Excel.XlBordersIndex.xlInsideVertical].Weight = Excel.XlBorderWeight.xlMedium;
                    borders[Excel.XlBordersIndex.xlInsideHorizontal].Weight = Excel.XlBorderWeight.xlMedium;
                }
            }
        }

        private DataTable RiammissioneVincitore()
        {
            DataTable table = new DataTable();
            List<string> fiscalCodesList = new List<string>();
            foreach (Studente studente in studenti)
            {
                fiscalCodesList.Add(studente.codFiscale);
            }
            string fiscalCodes = string.Join(", ", fiscalCodesList.Select(cf => $"'{cf}'"));



            return table;
        }

        private System.Data.DataTable AddDaIdoneoVincitore()
        {
            System.Data.DataTable dataTable = new System.Data.DataTable();

            List<string> fiscalCodesList = new List<string>();
            foreach (Studente studente in studenti)
            {
                fiscalCodesList.Add(studente.codFiscale);
            }
            string fiscalCodes = string.Join(", ", fiscalCodesList.Select(cf => $"'{cf}'"));

            string query = $@"
                SELECT Cod_fiscale, Imp_beneficio 
                FROM vEsiti_concorsi INNER JOIN 
                Domanda ON vEsiti_concorsi.Anno_accademico = Domanda.Anno_accademico AND vEsiti_concorsi.Num_domanda = Domanda.Num_domanda
                WHERE vEsiti_concorsi.Anno_accademico = '{selectedAA}' AND vEsiti_concorsi.cod_tipo_esito = 2 AND Domanda.Tipo_bando = '{selectedTipoBando}' AND Cod_beneficio = '{selectedBeneficio}' AND Cod_fiscale IN ({fiscalCodes})";

            using (SqlCommand cmd = new(query, CONNECTION))
            {
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                    Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        double.TryParse(Utilities.SafeGetString(reader, "Imp_beneficio"), out double importoBeneficio);
                        studente.AddImporto(importoBeneficio);
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Studente con CF {codFiscale} non trovato");
                    }

                }
            }

            dataTable.Columns.Add("0");
            dataTable.Columns.Add("1");
            dataTable.Columns.Add("2");
            dataTable.Columns.Add("3");
            dataTable.Columns.Add("4");
            dataTable.Columns.Add("5");
            dataTable.Columns.Add("6");
            dataTable.Columns.Add("7");

            dataTable.Rows.Add(" ");
            dataTable.Rows.Add($"{selectedNomeAllegato} - {selectedBeneficio} - {AAsplit}");
            dataTable.Rows.Add(" ");
            dataTable.Rows.Add("Num", "Codice Fiscale", "Num domanda", "Codice studente", "Nome", "Cognome", "Data di nascita", "Imp beneficio");
            int sequential = 1;
            double totaleImporto = 0;
            foreach (Studente studenteData in studenti)
            {
                dataTable.Rows.Add(sequential.ToString(), studenteData.codFiscale, studenteData.numDomanda, studenteData.codStudente, studenteData.nome, studenteData.cognome, studenteData.dataNascita.ToString("dd/MM/yyyy"), studenteData.importoBeneficio);
                totaleImporto += studenteData.importoBeneficio;
                sequential++;
            }
            dataTable.Rows.Add("Totale:", " ", " ", " ", " ", " ", " ", Math.Round(totaleImporto, 2).ToString());
            dataTable.Rows.Add(" ");
            return dataTable;
        }
        private System.Data.DataTable AddCambioStatusSede()
        {
            System.Data.DataTable dataTable = new System.Data.DataTable();

            List<string> fiscalCodesList = new List<string>();
            foreach (Studente studente in studenti)
            {
                fiscalCodesList.Add(studente.codFiscale);
            }
            string fiscalCodes = string.Join(", ", fiscalCodesList.Select(cf => $"'{cf}'"));


            string dataQuery = $@"
                SELECT        
                    Graduatorie.Cod_fiscale, 
                    Tipologie_status_sede_1.Descrizione AS status_sede_grad,
                    Graduatorie.ImportoBeneficio AS importo_beneficio_grad,
                    Tipologie_status_sede.Descrizione AS status_sede_ora, 
                    vEsiti_concorsi.Imp_beneficio AS importo_beneficio_ora
                FROM 
                    Graduatorie INNER JOIN
                    vEsiti_concorsi ON Graduatorie.Anno_accademico = vEsiti_concorsi.Anno_accademico AND Graduatorie.Num_domanda = vEsiti_concorsi.Num_domanda AND Graduatorie.Cod_beneficio = vEsiti_concorsi.Cod_beneficio INNER JOIN
                    vValori_calcolati ON Graduatorie.Num_domanda = vValori_calcolati.Num_domanda AND Graduatorie.Anno_accademico = vValori_calcolati.Anno_accademico INNER JOIN
                    Tipologie_status_sede ON vValori_calcolati.Status_sede = Tipologie_status_sede.Status_sede INNER JOIN
                    Tipologie_status_sede AS Tipologie_status_sede_1 ON Graduatorie.StatusSedeCalcolato = Tipologie_status_sede_1.Status_sede
                WHERE Cod_tipo_graduat = 1 AND Graduatorie.Anno_accademico = '{selectedAA}' AND Graduatorie.Cod_beneficio = '{selectedBeneficio}' AND vEsiti_concorsi.Cod_beneficio = '{selectedBeneficio}' AND Cod_fiscale IN ({fiscalCodes})";

            Dictionary<Studente, List<string>> studentiDict = new Dictionary<Studente, List<string>>();
            using (SqlCommand cmd = new(dataQuery, CONNECTION))
            {
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                    Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        List<string> datiStudente = new List<string>()
                        {
                            Utilities.SafeGetString(reader, "status_sede_grad"),
                            Utilities.SafeGetString(reader, "status_sede_ora"),
                            Utilities.SafeGetString(reader, "importo_beneficio_grad"),
                            Utilities.SafeGetString(reader, "importo_beneficio_ora")
                        };
                        studentiDict[studente] = datiStudente;
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Studente con CF {codFiscale} non trovato");
                    }
                }
            }

            dataTable.Columns.Add("0");
            dataTable.Columns.Add("1");
            dataTable.Columns.Add("2");
            dataTable.Columns.Add("3");
            dataTable.Columns.Add("4");
            dataTable.Columns.Add("5");
            dataTable.Columns.Add("6");
            dataTable.Columns.Add("7");
            dataTable.Columns.Add("8");
            dataTable.Columns.Add("9");
            dataTable.Columns.Add("10");
            dataTable.Columns.Add("11");

            dataTable.Rows.Add(" ");
            dataTable.Rows.Add($"{selectedNomeAllegato} - {selectedBeneficio} - {AAsplit}");
            dataTable.Rows.Add(" ");
            dataTable.Rows.Add(
                "Num",
                "Codice Fiscale",
                "Num domanda",
                "Codice studente",
                "Nome",
                "Cognome",
                "Data di nascita",
                "Status sede grad",
                "Status sede attuale",
                "Importo grad",
                "Importo attuale",
                "Differenza");

            int sequential = 1;
            double totaleImportoGrad = 0;
            double totaleImportoAttuale = 0;
            double totaleDifferenza = 0;
            foreach (KeyValuePair<Studente, List<string>> studenteKey in studentiDict)
            {
                double gradValue = double.Parse(studenteKey.Value[2]);
                double nowValue = double.Parse(studenteKey.Value[3]);
                double difference = Math.Round(nowValue - gradValue, 2);

                dataTable.Rows.Add(
                    sequential.ToString(),
                    studenteKey.Key.codFiscale,
                    studenteKey.Key.numDomanda,
                    studenteKey.Key.codStudente,
                    studenteKey.Key.nome,
                    studenteKey.Key.cognome,
                    studenteKey.Key.dataNascita.ToString("dd/MM/yyyy"),
                    studenteKey.Value[0],
                    studenteKey.Value[1],
                    studenteKey.Value[2],
                    studenteKey.Value[3],
                    difference.ToString()
                    );

                totaleImportoGrad += gradValue;
                totaleImportoAttuale += nowValue;
                totaleDifferenza += difference;
                sequential++;
            }
            dataTable.Rows.Add("Totale:", " ", " ", " ", " ", " ", " ", " ", " ", Math.Round(totaleImportoGrad, 2).ToString(), Math.Round(totaleImportoAttuale, 2).ToString(), Math.Round(totaleDifferenza, 2).ToString());
            dataTable.Rows.Add(" ");

            return dataTable;
        }
        private System.Data.DataTable AddDecadenza()
        {
            System.Data.DataTable dataTable = new System.Data.DataTable();

            List<string> fiscalCodesList = new List<string>();
            foreach (Studente studente in studenti)
            {
                fiscalCodesList.Add(studente.codFiscale);
            }
            string fiscalCodes = string.Join(", ", fiscalCodesList.Select(cf => $"'{cf}'"));

            string dataQuery = $@"

                    WITH UltimoBeneficioNonZero AS (
                            SELECT
                                domanda.cod_fiscale,
		                        Domanda.Anno_accademico,
		                        Domanda.Num_domanda,
                                imp_beneficio,
                                Esiti_concorsi.data_validita,
                                ROW_NUMBER() OVER (PARTITION BY domanda.cod_fiscale ORDER BY Esiti_concorsi.data_validita DESC) as rn
                            FROM
                                domanda inner join
		                        Esiti_concorsi on Domanda.Anno_accademico = Esiti_concorsi.Anno_accademico and Domanda.Num_domanda = Esiti_concorsi.Num_domanda
                            WHERE
                                Imp_beneficio != 0 and domanda.Anno_accademico = '{selectedAA}' and Domanda.Tipo_bando = '{selectedTipoBando}' and Esiti_concorsi.Cod_beneficio = '{selectedBeneficio}'
		                        and Cod_fiscale in ({fiscalCodes})
                        )
                    SELECT
                        RE.Cod_fiscale,
                        dbo.SlashMotiviEsclusioneTest(RE.Num_domanda, RE.Anno_accademico, '{selectedBeneficio}') AS Motivi_esclusione,
                        RE.Imp_beneficio
                    FROM
                        UltimoBeneficioNonZero RE
                    WHERE
                        RE.rn = 1
                    ORDER BY
                        RE.Cod_fiscale;
            ";

            Dictionary<Studente, List<string>> studentiDict = new Dictionary<Studente, List<string>>();
            using (SqlCommand cmd = new(dataQuery, CONNECTION))
            {
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                    Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        List<string> datiStudente = new List<string>()
                        {
                            Utilities.SafeGetString(reader, "Motivi_esclusione"),
                            Utilities.SafeGetString(reader, "Imp_beneficio")
                        };
                        studentiDict[studente] = datiStudente;
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Studente con CF {codFiscale} non trovato");
                    }
                }
            }

            dataTable.Columns.Add("0");
            dataTable.Columns.Add("1");
            dataTable.Columns.Add("2");
            dataTable.Columns.Add("3");
            dataTable.Columns.Add("4");
            dataTable.Columns.Add("5");
            dataTable.Columns.Add("6");
            dataTable.Columns.Add("7");
            dataTable.Columns.Add("8");

            dataTable.Rows.Add(" ");
            dataTable.Rows.Add($"{selectedNomeAllegato} - {selectedBeneficio} - {AAsplit}");
            dataTable.Rows.Add(" ");
            dataTable.Rows.Add(
                "Num",
                "Codice Fiscale",
                "Num domanda",
                "Codice studente",
                "Nome",
                "Cognome",
                "Data di nascita",
                "Motivi di esclusione",
                "Importo beneficio"
                );

            int sequential = 1;
            double totaleImporto = 0;
            foreach (KeyValuePair<Studente, List<string>> studenteKey in studentiDict)
            {
                dataTable.Rows.Add(
                    sequential.ToString(),
                    studenteKey.Key.codFiscale,
                    studenteKey.Key.numDomanda,
                    studenteKey.Key.codStudente,
                    studenteKey.Key.nome,
                    studenteKey.Key.cognome,
                    studenteKey.Key.dataNascita.ToString("dd/MM/yyyy"),
                    studenteKey.Value[0],
                    studenteKey.Value[1]
                    );

                totaleImporto += double.Parse(studenteKey.Value[1]);
                sequential++;
            }
            dataTable.Rows.Add("Totale:", " ", " ", " ", " ", " ", " ", " ", Math.Round(totaleImporto, 2).ToString());
            dataTable.Rows.Add(" ");

            return dataTable;
        }
        private System.Data.DataTable AddSoloImporto()
        {
            System.Data.DataTable dataTable = new System.Data.DataTable();

            List<string> fiscalCodesList = new List<string>();
            foreach (Studente studente in studenti)
            {
                fiscalCodesList.Add(studente.codFiscale);
            }
            string fiscalCodes = string.Join(", ", fiscalCodesList.Select(cf => $"'{cf}'"));

            string dataQuery = $@"

                    WITH UltimoBeneficioNonZero AS (
                            SELECT
                                domanda.cod_fiscale,
		                        Domanda.Anno_accademico,
		                        Domanda.Num_domanda,
                                imp_beneficio,
                                Esiti_concorsi.data_validita,
                                ROW_NUMBER() OVER (PARTITION BY domanda.cod_fiscale ORDER BY Esiti_concorsi.data_validita DESC) as rn
                            FROM
                                domanda inner join
		                        Esiti_concorsi on Domanda.Anno_accademico = Esiti_concorsi.Anno_accademico and Domanda.Num_domanda = Esiti_concorsi.Num_domanda
                            WHERE
                                Imp_beneficio != 0 and domanda.Anno_accademico = '{selectedAA}' and Domanda.Tipo_bando = '{selectedTipoBando}' and Esiti_concorsi.Cod_beneficio = '{selectedBeneficio}'
		                        and Cod_fiscale in ({fiscalCodes})
                        )
                        SELECT
                            cod_fiscale,
	                        Imp_beneficio
                        FROM
                            UltimoBeneficioNonZero
                        WHERE
                            rn = 1
	                        order by Cod_fiscale
            ";

            Dictionary<Studente, List<string>> studentiDict = new Dictionary<Studente, List<string>>();
            using (SqlCommand cmd = new(dataQuery, CONNECTION))
            {
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                    Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        List<string> datiStudente = new List<string>()
                        {
                            Utilities.SafeGetString(reader, "Imp_beneficio")
                        };
                        studentiDict[studente] = datiStudente;
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Studente con CF {codFiscale} non trovato");
                    }
                }
            }

            dataTable.Columns.Add("0");
            dataTable.Columns.Add("1");
            dataTable.Columns.Add("2");
            dataTable.Columns.Add("3");
            dataTable.Columns.Add("4");
            dataTable.Columns.Add("5");
            dataTable.Columns.Add("6");
            dataTable.Columns.Add("7");

            dataTable.Rows.Add(" ");
            dataTable.Rows.Add($"{selectedNomeAllegato} - {selectedBeneficio} - {AAsplit}");
            dataTable.Rows.Add(" ");
            dataTable.Rows.Add(
                "Num",
                "Codice Fiscale",
                "Num domanda",
                "Codice studente",
                "Nome",
                "Cognome",
                "Data di nascita",
                "Importo beneficio"
                );

            int sequential = 1;
            double totaleImporto = 0;
            foreach (KeyValuePair<Studente, List<string>> studenteKey in studentiDict)
            {
                dataTable.Rows.Add(
                    sequential.ToString(),
                    studenteKey.Key.codFiscale,
                    studenteKey.Key.numDomanda,
                    studenteKey.Key.codStudente,
                    studenteKey.Key.nome,
                    studenteKey.Key.cognome,
                    studenteKey.Key.dataNascita.ToString("dd/MM/yyyy"),
                    studenteKey.Value[0]
                    );

                totaleImporto += double.Parse(studenteKey.Value[0]);
                sequential++;
            }
            dataTable.Rows.Add("Totale:", " ", " ", " ", " ", " ", " ", Math.Round(totaleImporto, 2).ToString());
            dataTable.Rows.Add(" ");

            return dataTable;
        }
        private System.Data.DataTable AddRevocaSenzaRecupero()
        {
            System.Data.DataTable dataTable = new System.Data.DataTable();

            List<string> fiscalCodesList = new List<string>();
            foreach (Studente studente in studenti)
            {
                fiscalCodesList.Add(studente.codFiscale);
            }
            string fiscalCodes = string.Join(", ", fiscalCodesList.Select(cf => $"'{cf}'"));

            string dataQuery = $@"

                    WITH UltimoBeneficioNonZero AS (
                            SELECT
                                domanda.cod_fiscale,
		                        Domanda.Anno_accademico,
		                        Domanda.Num_domanda,
                                imp_beneficio,
                                Esiti_concorsi.data_validita,
                                ROW_NUMBER() OVER (PARTITION BY domanda.cod_fiscale ORDER BY Esiti_concorsi.data_validita DESC) as rn
                            FROM
                                domanda inner join
		                        Esiti_concorsi on Domanda.Anno_accademico = Esiti_concorsi.Anno_accademico and Domanda.Num_domanda = Esiti_concorsi.Num_domanda 
                            WHERE
                                Imp_beneficio != 0 and domanda.Anno_accademico = '{selectedAA}' and Domanda.Tipo_bando = '{selectedTipoBando}' and Esiti_concorsi.Cod_beneficio = '{selectedBeneficio}'
                                and domanda.Cod_fiscale in ({fiscalCodes})
                    ),
                    LastImpegno AS (
                            SELECT
                                domanda.cod_fiscale,
		                        Domanda.Anno_accademico,
		                        Domanda.Num_domanda,
                                importo_assegnato,
                                determina_conferimento,
                                num_impegno_primarata,
                                num_impegno_saldo,
                                ROW_NUMBER() OVER (PARTITION BY domanda.cod_fiscale ORDER BY specifiche_impegni.data_validita DESC) as rn
                            FROM
                                domanda inner join
                                specifiche_impegni on Domanda.Num_domanda = specifiche_impegni.Num_domanda and Domanda.Anno_accademico = specifiche_impegni.Anno_accademico
                            WHERE
                                domanda.Anno_accademico = '{selectedAA}' and Domanda.Tipo_bando = '{selectedTipoBando}' and cod_beneficio = '{selectedBeneficio}' and data_fine_validita is null
                                and domanda.Cod_fiscale in ({fiscalCodes})
                        )
                        SELECT
                            UltimoBeneficioNonZero.cod_fiscale,
	                        importo_assegnato,
                            determina_conferimento,
                            num_impegno_primarata,
                            num_impegno_saldo
                        FROM
                            UltimoBeneficioNonZero inner join LastImpegno ON UltimoBeneficioNonZero.num_domanda = lastImpegno.num_domanda and UltimoBeneficioNonZero.anno_accademico = lastImpegno.anno_accademico
                        WHERE
                            UltimoBeneficioNonZero.rn = 1 and LastImpegno.rn = 1
	                        order by Cod_fiscale
            ";

            Dictionary<Studente, List<string>> studentiDict = new Dictionary<Studente, List<string>>();
            using (SqlCommand cmd = new(dataQuery, CONNECTION))
            {
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                    Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        List<string> datiStudente = new List<string>()
                        {
                            Utilities.SafeGetString(reader, "num_impegno_primarata"),
                            Utilities.SafeGetString(reader, "num_impegno_saldo"),
                            Utilities.SafeGetString(reader, "determina_conferimento"),
                            Utilities.SafeGetString(reader, "importo_assegnato")
                        };
                        studentiDict[studente] = datiStudente;
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Studente con CF {codFiscale} non trovato");
                    }
                }
            }

            dataTable.Columns.Add("0");
            dataTable.Columns.Add("1");
            dataTable.Columns.Add("2");
            dataTable.Columns.Add("3");
            dataTable.Columns.Add("4");
            dataTable.Columns.Add("5");
            dataTable.Columns.Add("6");
            dataTable.Columns.Add("7");
            dataTable.Columns.Add("8");
            dataTable.Columns.Add("9");
            dataTable.Columns.Add("10");
            dataTable.Columns.Add("11");
            dataTable.Columns.Add("12");

            dataTable.Rows.Add(" ");
            dataTable.Rows.Add($"{selectedNomeAllegato} - {selectedBeneficio} - {AAsplit}");
            dataTable.Rows.Add(" ");
            dataTable.Rows.Add(
                "Num",
                "Codice Fiscale",
                "Num domanda",
                "Codice studente",
                "Nome",
                "Cognome",
                "Data di nascita",
                "Impegno Prima Rata",
                "Impegno Saldo",
                "Determinazione di assegnazione benefici",
                "Importo beneficio",
                "Liquidato",
                "Economia"
                );

            int sequential = 1;
            double totaleImporto = 0;
            foreach (KeyValuePair<Studente, List<string>> studenteKey in studentiDict)
            {
                dataTable.Rows.Add(
                    sequential.ToString(),
                    studenteKey.Key.codFiscale,
                    studenteKey.Key.numDomanda,
                    studenteKey.Key.codStudente,
                    studenteKey.Key.nome,
                    studenteKey.Key.cognome,
                    studenteKey.Key.dataNascita.ToString("dd/MM/yyyy"),
                    studenteKey.Value[0],
                    studenteKey.Value[1],
                    studenteKey.Value[2],
                    studenteKey.Value[3],
                    0,
                    studenteKey.Value[3]
                    );

                totaleImporto += double.Parse(studenteKey.Value[3]);
                sequential++;
            }
            dataTable.Rows.Add("Totale:", " ", " ", " ", " ", " ", " ", " ", " ", " ", Math.Round(totaleImporto, 2).ToString(), " ", Math.Round(totaleImporto, 2).ToString());
            dataTable.Rows.Add(" ");

            return dataTable;
        }
        private System.Data.DataTable AddRecuperoSomme()
        {
            System.Data.DataTable dataTable = new System.Data.DataTable();

            List<string> fiscalCodesList = new List<string>();
            foreach (Studente studente in studenti)
            {
                fiscalCodesList.Add(studente.codFiscale);
            }
            string fiscalCodes = string.Join(", ", fiscalCodesList.Select(cf => $"'{cf}'"));

            string dataQuery = $@"
                        WITH UltimoBeneficioNonZero AS (
                            SELECT
                                domanda.cod_fiscale,
		                        Domanda.Anno_accademico,
		                        Domanda.Num_domanda,
                                imp_beneficio,
                                Esiti_concorsi.data_validita,
                                cod_beneficio,
                                ROW_NUMBER() OVER (PARTITION BY domanda.cod_fiscale ORDER BY Esiti_concorsi.data_validita DESC) as rn
                            FROM
                                domanda inner join
		                        Esiti_concorsi on Domanda.Anno_accademico = Esiti_concorsi.Anno_accademico and Domanda.Num_domanda = Esiti_concorsi.Num_domanda
                            WHERE
                                Imp_beneficio != 0 and domanda.Anno_accademico = '{selectedAA}' and Domanda.Tipo_bando = '{selectedTipoBando}' and Esiti_concorsi.Cod_beneficio = '{selectedBeneficio}'
		                        and Cod_fiscale in ({fiscalCodes})
                        )
                        SELECT        
	                        UltimoBeneficioNonZero.Cod_fiscale, 
	                        Tipologie_pagam.Descrizione AS descr_pagam, 
	                        Pagamenti.Cod_mandato,
	                        Pagamenti.Ese_finanziario,
	                        specifiche_impegni.Tipo_fondo,
	                        specifiche_impegni.Determina_conferimento,
	                        specifiche_impegni.num_impegno_primaRata,
	                        specifiche_impegni.Esercizio_prima_rata,
	                        specifiche_impegni.num_impegno_saldo,
	                        specifiche_impegni.esercizio_saldo,
	                        Imp_beneficio,
	                        Pagamenti.Imp_pagato, 
	                        Sede_studi.Descrizione as descr_sede_studi
                        FROM            
	                        UltimoBeneficioNonZero INNER JOIN
	                        vIscrizioni ON UltimoBeneficioNonZero.Anno_accademico = vIscrizioni.Anno_accademico AND UltimoBeneficioNonZero.Cod_fiscale = vIscrizioni.Cod_fiscale INNER JOIN
	                        Pagamenti ON UltimoBeneficioNonZero.Anno_accademico = Pagamenti.Anno_accademico AND UltimoBeneficioNonZero.Num_domanda = Pagamenti.Num_domanda INNER JOIN
	                        Tipologie_pagam ON Pagamenti.Cod_tipo_pagam = Tipologie_pagam.Cod_tipo_pagam INNER JOIN
	                        Sede_studi ON vIscrizioni.Cod_sede_studi = Sede_studi.Cod_sede_studi INNER JOIN
	                        specifiche_impegni ON UltimoBeneficioNonZero.Anno_accademico = specifiche_impegni.Anno_accademico AND UltimoBeneficioNonZero.Cod_fiscale = specifiche_impegni.Cod_fiscale and UltimoBeneficioNonZero.Cod_beneficio = specifiche_impegni.Cod_beneficio
                        WHERE        
	                        (rn = 1) AND 
	                        (Pagamenti.Ritirato_azienda = 0) AND specifiche_impegni.data_fine_validita is null
                        ORDER BY 
	                        vIscrizioni.Cod_fiscale, Pagamenti.Data_validita

                        ";

            Dictionary<string, Dictionary<Studente, List<string>>> studentiDictDict = new Dictionary<string, Dictionary<Studente, List<string>>>();
            using (SqlCommand cmd = new(dataQuery, CONNECTION))
            {
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                    Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        string mandato = Utilities.SafeGetString(reader, "Cod_mandato");
                        List<string> datiStudente = new List<string>()
                        {
                            Utilities.SafeGetString(reader, "descr_pagam"),//0 OK
                            Utilities.SafeGetString(reader, "Cod_mandato"),//1 OK
                            Utilities.SafeGetString(reader, "Imp_beneficio"),//2 OK
                            Utilities.SafeGetString(reader, "Imp_pagato"),//3 OK
                            Utilities.SafeGetString(reader, "Ese_finanziario"),//4 OK 
                            Utilities.SafeGetString(reader, "Tipo_fondo"),//5 OK
                            Utilities.SafeGetString(reader, "Determina_conferimento"),//6 OK
                            Utilities.SafeGetString(reader, "num_impegno_primarata"),//7 OK
                            Utilities.SafeGetString(reader, "esercizio_prima_rata"),//8 OK 
                            Utilities.SafeGetString(reader, "num_impegno_saldo"),//9 OK
                            Utilities.SafeGetString(reader, "esercizio_saldo"),//10 OK
                            Utilities.SafeGetString(reader, "descr_sede_studi")//11 OK
                        };
                        studentiDictDict[mandato] = new Dictionary<Studente, List<string>>();
                        studentiDictDict[mandato][studente] = datiStudente;
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Studente con CF {codFiscale} non trovato");
                    }
                }
            }

            dataTable.Columns.Add("0");
            dataTable.Columns.Add("1");
            dataTable.Columns.Add("2");
            dataTable.Columns.Add("3");
            dataTable.Columns.Add("4");
            dataTable.Columns.Add("5");
            dataTable.Columns.Add("6");
            dataTable.Columns.Add("7");
            dataTable.Columns.Add("8");
            dataTable.Columns.Add("9");
            dataTable.Columns.Add("10");
            dataTable.Columns.Add("11");
            dataTable.Columns.Add("12");
            dataTable.Columns.Add("13");
            dataTable.Columns.Add("14");
            dataTable.Columns.Add("15");
            dataTable.Columns.Add("16");
            dataTable.Columns.Add("17");
            dataTable.Columns.Add("18");
            dataTable.Columns.Add("19");
            dataTable.Columns.Add("20");
            dataTable.Rows.Add(" ");
            dataTable.Rows.Add($"{selectedNomeAllegato} - {selectedBeneficio} - {AAsplit}");
            dataTable.Rows.Add(" ");
            dataTable.Rows.Add(
                "Num",
                "Università",
                "Codice Fiscale",
                "Num domanda",
                "Codice studente",
                "Nome",
                "Cognome",
                "Data di nascita",
                "Tipo Pagamento",
                "Mandato",
                "Esercizio finanziario MANDATO",
                "Tipo fondo",
                "Determinazione di assegnazione beneficio",
                "Impegno I rata",
                "Anno impegno I rata",
                "Impegno saldo",
                "Anno impegno saldo",
                "Importo beneficio",
                "Importo pagamento",
                "Totale pagamenti",
                "Economia"
                );

            int sequential = 1;
            double totaleImporto = 0;
            double totaliPagamenti = 0;
            double totaleEconomie = 0;
            List<Studente> studentiProcessati = new List<Studente>();
            foreach (KeyValuePair<string, Dictionary<Studente, List<string>>> outerEntry in studentiDictDict)
            {
                Dictionary<Studente, List<string>> studentiDict = outerEntry.Value;
                double totalePagamentiStudente = 0;
                foreach (KeyValuePair<Studente, List<string>> studenteKey in studentiDict)
                {

                    Studente studente = studenteKey.Key;

                    double importoBeneficio = double.Parse(studenteKey.Value[2]);
                    double importoPagamento = double.Parse(studenteKey.Value[3]);
                    totalePagamentiStudente += importoPagamento;
                    double economia = Math.Round(importoBeneficio - totalePagamentiStudente, 2);

                    dataTable.Rows.Add(
                        sequential.ToString(),
                        studenteKey.Value[11],
                        studente.codFiscale,
                        studente.numDomanda,
                        studente.codStudente,
                        studente.nome,
                        studente.cognome,
                        studente.dataNascita.ToString("dd/MM/yyyy"),
                        studenteKey.Value[0],
                        studenteKey.Value[1],
                        studenteKey.Value[4],
                        studenteKey.Value[5],
                        studenteKey.Value[6],
                        studenteKey.Value[7],
                        studenteKey.Value[8],
                        studenteKey.Value[9],
                        studenteKey.Value[10],
                        importoBeneficio,
                        importoPagamento,
                        totalePagamentiStudente,
                        economia
                        );

                    totaleImporto += importoBeneficio;
                    if (!studentiProcessati.Contains(studente))
                    {
                        studentiProcessati.Add(studente);
                        totaliPagamenti += totalePagamentiStudente;
                    }
                    else
                    {
                        totaliPagamenti += importoPagamento;
                    }
                    totaleEconomie += economia;
                    sequential++;
                }
            }
            dataTable.Rows.Add("Totale:", " ", " ", " ", " ", " ", " ", " ", " ", " ", " ", " ", " ", " ", " ", " ", " ", Math.Round(totaleImporto, 2).ToString(), " ", Math.Round(totaliPagamenti, 2).ToString(), Math.Round(totaleEconomie, 2).ToString());
            dataTable.Rows.Add(" ");

            return dataTable;
        }
        private System.Data.DataTable AddCambioAnnoCorso()
        {
            System.Data.DataTable dataTable = new System.Data.DataTable();

            List<string> fiscalCodesList = new List<string>();
            foreach (Studente studente in studenti)
            {
                fiscalCodesList.Add(studente.codFiscale);
            }
            string fiscalCodes = string.Join(", ", fiscalCodesList.Select(cf => $"'{cf}'"));


            string dataQuery = $@"
                SELECT 
                    Graduatorie.Cod_fiscale, 
                    Graduatorie.ImportoBeneficio AS importo_beneficio_grad, 
                    vEsiti_concorsi.Imp_beneficio AS importo_beneficio_ora, 
                    Graduatorie.Anno_corso AS Anno_corso_grad, 
                    vValori_calcolati.Anno_corso AS Anno_corso_ora
                FROM            
                    Graduatorie INNER JOIN
                    vEsiti_concorsi ON Graduatorie.Anno_accademico = vEsiti_concorsi.Anno_accademico AND Graduatorie.Num_domanda = vEsiti_concorsi.Num_domanda AND Graduatorie.Cod_beneficio = vEsiti_concorsi.Cod_beneficio INNER JOIN
                    vValori_calcolati ON Graduatorie.Num_domanda = vValori_calcolati.Num_domanda AND Graduatorie.Anno_accademico = vValori_calcolati.Anno_accademico
                WHERE
                    (Graduatorie.Cod_tipo_graduat = 1) AND
                    (Graduatorie.Cod_tipo_esito = 2) AND
                    (vEsiti_concorsi.Cod_tipo_esito = 2) AND
                    (Graduatorie.Anno_accademico = '{selectedAA}') AND
                    (Graduatorie.Cod_beneficio = '{selectedBeneficio}') AND
                    (vEsiti_concorsi.Cod_beneficio = '{selectedBeneficio}') AND
                    (Graduatorie.Cod_fiscale IN({fiscalCodes}))";

            Dictionary<Studente, List<string>> studentiDict = new Dictionary<Studente, List<string>>();
            using (SqlCommand cmd = new(dataQuery, CONNECTION))
            {
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                    Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        List<string> datiStudente = new List<string>()
                        {
                            Utilities.SafeGetString(reader, "Anno_corso_grad"),
                            Utilities.SafeGetString(reader, "Anno_corso_ora"),
                            Utilities.SafeGetString(reader, "importo_beneficio_grad"),
                            Utilities.SafeGetString(reader, "importo_beneficio_ora")
                        };
                        studentiDict[studente] = datiStudente;
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Studente con CF {codFiscale} non trovato");
                    }
                }
            }

            dataTable.Columns.Add("0");
            dataTable.Columns.Add("1");
            dataTable.Columns.Add("2");
            dataTable.Columns.Add("3");
            dataTable.Columns.Add("4");
            dataTable.Columns.Add("5");
            dataTable.Columns.Add("6");
            dataTable.Columns.Add("7");
            dataTable.Columns.Add("8");
            dataTable.Columns.Add("9");
            dataTable.Columns.Add("10");
            dataTable.Columns.Add("11");

            dataTable.Rows.Add(" ");
            dataTable.Rows.Add($"{selectedNomeAllegato} - {selectedBeneficio} - {AAsplit}");
            dataTable.Rows.Add(" ");
            dataTable.Rows.Add(
                "Num",
                "Codice Fiscale",
                "Num domanda",
                "Codice studente",
                "Nome",
                "Cognome",
                "Data di nascita",
                "Anno corso grad",
                "Anno corso attuale",
                "Importo grad",
                "Importo attuale",
                "Differenza");

            int sequential = 1;
            double totaleImportoGrad = 0;
            double totaleImportoAttuale = 0;
            double totaleDifferenza = 0;
            foreach (KeyValuePair<Studente, List<string>> studenteKey in studentiDict)
            {
                double gradValue = double.Parse(studenteKey.Value[2]);
                double nowValue = double.Parse(studenteKey.Value[3]);
                double difference = Math.Round(nowValue - gradValue, 2);

                dataTable.Rows.Add(
                    sequential.ToString(),
                    studenteKey.Key.codFiscale,
                    studenteKey.Key.numDomanda,
                    studenteKey.Key.codStudente,
                    studenteKey.Key.nome,
                    studenteKey.Key.cognome,
                    studenteKey.Key.dataNascita.ToString("dd/MM/yyyy"),
                    studenteKey.Value[0],
                    studenteKey.Value[1],
                    studenteKey.Value[2],
                    studenteKey.Value[3],
                    difference.ToString()
                    );

                totaleImportoGrad += gradValue;
                totaleImportoAttuale += nowValue;
                totaleDifferenza += difference;
                sequential++;
            }
            dataTable.Rows.Add("Totale:", " ", " ", " ", " ", " ", " ", " ", " ", Math.Round(totaleImportoGrad, 2).ToString(), Math.Round(totaleImportoAttuale, 2).ToString(), Math.Round(totaleDifferenza, 2).ToString());
            dataTable.Rows.Add(" ");

            return dataTable;
        }
    }
}