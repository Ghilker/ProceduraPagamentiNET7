using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Excel = Microsoft.Office.Interop.Excel;

namespace ProcedureNet7
{
    internal static class Utilities
    {
        public static DataTable ReadExcelToDataTable(string filePath)
        {
            DataTable dataTable = new DataTable();
            Excel.Application excelApp = null;
            Excel.Workbooks workbooks = null;
            Excel.Workbook workbook = null;
            Excel.Worksheet worksheet = null;
            Excel.Range range = null;

            try
            {
                excelApp = new Excel.Application();
                workbooks = excelApp.Workbooks;
                workbook = workbooks.Open(filePath);
                worksheet = workbook.Sheets[1];
                range = worksheet.UsedRange;

                object[,] data = range.Value2;
                if (data == null) return dataTable; // Return an empty dataTable if no data

                int columnsCount = range.Columns.Count;
                int rowsCount = range.Rows.Count;

                Dictionary<string, int> columnNames = new Dictionary<string, int>();
                for (int col = 1; col <= columnsCount; col++)
                {
                    string originalColumnName = data[1, col]?.ToString() ?? $"Column{col}";
                    string sanitizedColumnName = SanitizeColumnName(originalColumnName);

                    if (columnNames.ContainsKey(sanitizedColumnName))
                    {
                        columnNames[sanitizedColumnName]++;
                        sanitizedColumnName += $"_{columnNames[sanitizedColumnName]}";
                    }
                    else
                    {
                        columnNames[sanitizedColumnName] = 0;
                    }

                    dataTable.Columns.Add(sanitizedColumnName);
                }

                for (int row = 2; row <= rowsCount; row++)
                {
                    var dataRow = dataTable.NewRow();
                    for (int col = 1; col <= columnsCount; col++)
                    {
                        dataRow[col - 1] = data[row, col]?.ToString() ?? string.Empty;
                    }
                    dataTable.Rows.Add(dataRow);
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions or log errors
                throw;
            }
            finally
            {
                // Cleanup
                if (range != null) Marshal.ReleaseComObject(range);
                if (worksheet != null) Marshal.ReleaseComObject(worksheet);
                if (workbook != null)
                {
                    workbook.Close(false);
                    Marshal.ReleaseComObject(workbook);
                }
                if (workbooks != null)
                {
                    workbooks.Close();
                    Marshal.ReleaseComObject(workbooks);
                }
                if (excelApp != null)
                {
                    excelApp.Quit();
                    Marshal.ReleaseComObject(excelApp);
                }
            }

            return dataTable;
        }
        public static string SanitizeColumnName(string columnName)
        {
            // Regular expression to remove any special characters but preserve spaces
            return Regex.Replace(columnName, "[^a-zA-Z0-9_ ]", "");
        }

        // Method to check database connection
        public static bool CanConnectToDatabase(string connectionString)
        {
            try
            {
                using SqlConnection connection = new(connectionString);
                connection.Open(); // Attempt to open the connection
                return true; // Connection successful
            }
            catch
            {
                return false; // Connection failed
            }


        }

        public static string GetCheckBoxSelectedCodes(ToolStripItemCollection items)
        {
            List<string> selectedCodes = new List<string>();
            foreach (ToolStripMenuItem item in items)
            {
                if (item.Checked)
                {
                    string code = item.Text.Split('-')[0].Trim();
                    selectedCodes.Add("'" + code.Replace("'", "''") + "'");
                }
            }
            return selectedCodes.Count == 0 ? "''" : string.Join(", ", selectedCodes);
        }

        public static void ChooseFileAndSetPath(Label filePathLabel, FileDialog fileDialogToOpen, ref string varToSave, bool writeOnlySelected = false)
        {
            // Show the OpenFileDialog.
            DialogResult result = fileDialogToOpen.ShowDialog();

            // Check if the user selected a file.
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fileDialogToOpen.FileName))
            {
                varToSave = fileDialogToOpen.FileName;
                if (writeOnlySelected)
                {
                    filePathLabel.Text = "Selezionato";
                    return;
                }
                filePathLabel.Text = varToSave;
            }
        }

        public static void ChooseFolder(Label folderPathLabel, FolderBrowserDialog chosenFolderDialog, ref string varToSaveTo)
        {
            // Show the FolderBrowserDialog.
            DialogResult result = chosenFolderDialog.ShowDialog();

            // Check if the user selected a folder.
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(chosenFolderDialog.SelectedPath))
            {
                varToSaveTo = chosenFolderDialog.SelectedPath;
                folderPathLabel.Text = varToSaveTo;
            }
        }

        public static void ExportDataTableToExcel(DataTable dataTable, string folderPath, bool includeHeaders = true, string fileName = "")
        {
            if (dataTable == null || dataTable.Rows.Count == 0)
            {
                return;
            }

            Excel.Application excelApp = null;
            Excel.Workbooks workbooks = null;
            Excel._Workbook workbook = null;
            Excel._Worksheet worksheet = null;
            try
            {
                excelApp = new Excel.Application();
                workbooks = excelApp.Workbooks;
                workbook = workbooks.Add();
                worksheet = (Excel._Worksheet)workbook.ActiveSheet;

                int rowCount = dataTable.Rows.Count;
                int columnCount = dataTable.Columns.Count;

                // Adjust the array size based on whether headers are included
                int arrayRows = includeHeaders ? rowCount + 1 : rowCount;
                object[,] values = new object[arrayRows, columnCount];

                int rowIndex = 0;

                // Include headers if requested
                if (includeHeaders)
                {
                    for (int i = 0; i < columnCount; i++)
                    {
                        values[rowIndex, i] = dataTable.Columns[i].ColumnName;
                    }
                    rowIndex = 1; // Start data entry from the second row
                }

                // Rows
                for (int i = 0; i < rowCount; i++)
                {
                    for (int j = 0; j < columnCount; j++)
                    {
                        values[i + rowIndex, j] = dataTable.Rows[i][j]; // Adjust index based on header inclusion
                    }
                }

                // Set the range value to the array
                Excel.Range startCell = (Excel.Range)worksheet.Cells[includeHeaders ? 1 : 2, 1]; // Adjust starting cell based on header inclusion
                Excel.Range endCell = (Excel.Range)worksheet.Cells[rowCount + (includeHeaders ? 1 : 0), columnCount]; // Adjust end cell based on header inclusion
                Excel.Range writeRange = worksheet.Range[startCell, endCell];
                writeRange.Value2 = values;

                // Generate a unique file name and save the file
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = "Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx";
                }
                string excelFilePath = Path.Combine(folderPath, fileName);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
                workbook.SaveAs(excelFilePath);
            }
            finally
            {
                // Release COM objects
                if (worksheet != null) Marshal.ReleaseComObject(worksheet);
                if (workbook != null)
                {
                    workbook.Close(false);
                    Marshal.ReleaseComObject(workbook);
                }
                if (workbooks != null) Marshal.ReleaseComObject(workbooks);
                if (excelApp != null)
                {
                    excelApp.Quit();
                    Marshal.ReleaseComObject(excelApp);
                }
            }
        }

        public static void WriteDataTableToTextFile(DataTable dataTable, string directoryPath, string fileName)
        {
            fileName = Path.ChangeExtension(fileName, ".txt");
            string filePath = Path.Combine(directoryPath, fileName);

            StringBuilder sb = new StringBuilder();

            // Iterate through all rows in the DataTable
            foreach (DataRow row in dataTable.Rows)
            {
                // Array to hold the cells in the current row
                string[] array = new string[dataTable.Columns.Count];

                // Fill the array with the cell values
                for (int i = 0; i < dataTable.Columns.Count; i++)
                {
                    // Use .ToString() to ensure that even null values become empty strings
                    array[i] = row[i].ToString();
                }

                // Join the array elements with a semicolon and append to the StringBuilder
                sb.AppendLine(string.Join(";", array));
            }

            // Write the StringBuilder contents to the text file at the specified path
            File.WriteAllText(filePath, sb.ToString());
        }

        public static DataGridView CreateDataGridView(
            DataTable dataTable,
            MainUI mainForm,
            Panel panelToAddTo = null,
            DataGridViewCellEventHandler? mouseClick_Handler = null,
            DataGridViewCellMouseEventHandler? columnHeaderMouseClick_Handler = null,
            string keyTag = "")
        {
            DataGridView newDataGridView = new();

            // Execute the following code on the UI thread
            mainForm.Invoke((MethodInvoker)delegate
            {
                newDataGridView = new DataGridView
                {
                    Name = "dataGridView" + (panelToAddTo.Controls.Count + 1).ToString(),
                    Size = mainForm.ClientSize,
                    Dock = DockStyle.Fill,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                    ScrollBars = ScrollBars.Both,
                    Tag = keyTag,
                };

                if (mouseClick_Handler != null)
                {
                    newDataGridView.CellClick += new DataGridViewCellEventHandler(mouseClick_Handler);
                }

                if (columnHeaderMouseClick_Handler != null)
                {
                    newDataGridView.ColumnHeaderMouseClick += new DataGridViewCellMouseEventHandler(columnHeaderMouseClick_Handler);
                }

                panelToAddTo.Controls.Add(newDataGridView);
                newDataGridView.BringToFront();

                // Disable sorting for each column
                foreach (DataGridViewColumn column in newDataGridView.Columns)
                {
                    column.SortMode = DataGridViewColumnSortMode.NotSortable;
                }

                newDataGridView.DataSource = dataTable;
                newDataGridView.Cursor = Cursors.Hand;
            });

            return newDataGridView;
        }

        public static void CreateDropDownMenu(ref Button buttonToUse, ref ContextMenuStrip contextMenuStrip, Dictionary<string, string> dictionaryToUse, bool allActive = false)
        {
            contextMenuStrip = new ContextMenuStrip();
            foreach (KeyValuePair<string, string> item in dictionaryToUse)
            {
                ToolStripMenuItem menuItem = new($"{item.Key} - {item.Value}")
                {
                    CheckOnClick = true,
                    Checked = allActive
                };
                _ = contextMenuStrip.Items.Add(menuItem);
            }

            ContextMenuStrip localMenuStrip = contextMenuStrip;
            Button localButtonToUse = buttonToUse;
            // Use a Button click event to show the menu
            buttonToUse.Click += (sender, e) =>
            {
                localMenuStrip.Show(localButtonToUse, new Point(0, localButtonToUse.Height));
            };
        }

        public static void RemoveDropDownMenu(ref Button buttonToUse, ref ContextMenuStrip contextMenuStrip)
        {
            if (buttonToUse != null && contextMenuStrip != null)
            {
                // Dispose of the context menu
                contextMenuStrip.Dispose();
                contextMenuStrip = null;
            }
        }

        public static string EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        public static string RemoveAllSpaces(string strimToTrim)
        {
            return strimToTrim.Replace(" ", "");
        }
    }



    public class ProgressUpdater
    {
        private IProgress<(int, string)> _progress;
        private int _updateCount = 0;
        private int maxDashes = 40;
        private bool _inProcedure = false;
        private int _currentProgress;

        public ProgressUpdater(IProgress<(int, string)> progress, int currentProgress)
        {
            _progress = progress;
            _inProcedure = true;
            _currentProgress = currentProgress;
        }

        public void StartUpdating()
        {
            _updateCount = 0;
            Task.Run(() => UpdateProgress());
        }

        public void StopUpdating()
        {
            _inProcedure = false;
            _progress.Report((_currentProgress, "UPDATE:" + new String('-', maxDashes)));
        }

        private void UpdateProgress()
        {
            while (_inProcedure)
            {
                while (_updateCount < maxDashes)
                {
                    if (!_inProcedure) { break; }
                    Thread.Sleep(250);
                    if (!_inProcedure) { break; }
                    string updateMessage = new String('-', _updateCount + 1);
                    _progress.Report((_currentProgress, $"UPDATE:{updateMessage}"));
                    _updateCount++;
                }
                _updateCount = 0;
            }
        }
    }
}
