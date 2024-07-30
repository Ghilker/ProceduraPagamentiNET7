using ClosedXML.Excel;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Excel = Microsoft.Office.Interop.Excel;


namespace ProcedureNet7
{
    internal static class Utilities
    {
        public static DataTable ReadExcelToDataTable(string filePath, bool firstRowAsData = false)
        {
            DataTable dataTable = new();
            Excel.Application? excelApp = null;
            Excel.Workbooks? workbooks = null;
            Excel.Workbook? workbook = null;
            Excel.Worksheet? worksheet = null;
            Excel.Range? range = null;

            Logger.LogDebug(null, "Initializing Excel application.");

            try
            {
                excelApp = new Excel.Application();
                if (excelApp == null)
                {
                    Logger.LogError(null, "Excel application could not be started.");
                    return new DataTable();
                }

                workbooks = excelApp.Workbooks;
                workbook = workbooks.Open(filePath);
                if (workbook == null)
                {
                    Logger.LogError(null, "Failed to open workbook.");
                    return new DataTable();
                }

                worksheet = workbook.Sheets[1];
                if (worksheet == null)
                {
                    Logger.LogError(null, "Failed to get the first worksheet.");
                    return new DataTable();
                }

                range = worksheet.UsedRange;
                if (range == null)
                {
                    Logger.LogError(null, "Failed to get used range of worksheet.");
                    return new DataTable();
                }

                object[,] data = range.Value2;
                if (data == null)
                {
                    Logger.LogWarning(null, "No data found in Excel range.");
                    return new DataTable(); // Return an empty dataTable if no data
                }

                int columnsCount = range.Columns.Count;
                int rowsCount = range.Rows.Count;
                Logger.LogDebug(null, $"Preparing to process {columnsCount} columns and {rowsCount - (firstRowAsData ? 0 : 1)} rows.");

                Dictionary<string, int> columnNames = new();

                // Adjust start row based on whether the first row is treated as data
                int startRow = firstRowAsData ? 1 : 2;

                // Process column names
                for (int col = 1; col <= columnsCount; col++)
                {
                    string columnName;
                    if (firstRowAsData)
                    {
                        // Use default column names if the first row is treated as data
                        columnName = $"Column{col}";
                    }
                    else
                    {
                        // Use the first row for column names if not treated as data
                        string originalColumnName = data[1, col]?.ToString() ?? $"Column{col}";
                        columnName = SanitizeColumnName(originalColumnName);

                        if (columnNames.TryGetValue(columnName, out int value))
                        {
                            columnNames[columnName] = ++value;
                            columnName += $"_{value}";
                        }
                        else
                        {
                            columnNames[columnName] = 0;
                        }
                    }
                    dataTable.Columns.Add(columnName);
                    Logger.LogDebug(null, $"Column added: {columnName}");
                }

                // Process data rows
                for (int row = startRow; row <= rowsCount; row++)
                {
                    var dataRow = dataTable.NewRow();
                    for (int col = 1; col <= columnsCount; col++)
                    {
                        dataRow[col - 1] = data[row, col]?.ToString() ?? string.Empty;
                    }
                    dataTable.Rows.Add(dataRow);
                }
                Logger.LogDebug(null, "Data rows processed successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"An error occurred: {ex.Message}");
                throw; // Re-throwing the exception after logging
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
                Logger.LogDebug(null, "Excel resources cleaned up successfully.");
            }

            return dataTable;
        }

        public static string SanitizeColumnName(string columnName)
        {
            // Regular expression to remove any special characters but preserve spaces
            return Regex.Replace(columnName, "[^a-zA-Z0-9_ ]", "");
        }

        // Method to check database connection
        public static bool CanConnectToDatabase(string connectionString, out SqlConnection? outConnection)
        {
            try
            {
                SqlConnection connection = new(connectionString);
                connection.Open(); // Attempt to open the connection
                outConnection = connection;
                return true; // Connection successful
            }
            catch
            {
                outConnection = null;
                return false; // Connection failed
            }
        }

        public static bool CanConnectToDatabase(SqlConnection connection)
        {
            if (connection == null)
            {
                return false;
            }
            if (connection.State == System.Data.ConnectionState.Open)
            {
                return true;
            }
            return false;

        }

        public static string GetCheckBoxSelectedCodes(ToolStripItemCollection? items)
        {
            if (items == null)
            {
                return "''";
            }
            List<string> selectedCodes = new();
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

        public static string ExportDataTableToExcel(DataTable dataTable, string folderPath, bool includeHeaders = true, string fileName = "")
        {
            if (dataTable == null || dataTable.Rows.Count == 0 || string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Invalid input parameters.");

            fileName = GenerateFileName(fileName);
            string fullPath = Path.Combine(folderPath, fileName);
            Directory.CreateDirectory(folderPath); // Ensure the directory exists

            Excel.Application? excelApp = new();
            Excel.Workbooks? workbooks = null;
            Excel._Workbook? workbook = null;
            Excel._Worksheet? worksheet = null;

            try
            {
                workbooks = excelApp.Workbooks;
                workbook = workbooks.Add();
                worksheet = (Excel._Worksheet)workbook.ActiveSheet!;

                // Optionally, add column headers
                if (includeHeaders && worksheet != null)
                    AddHeaders(worksheet, dataTable);

                // Add data rows
                if (worksheet != null)
                    AddData(worksheet, dataTable, includeHeaders);

                // Save the Excel file
                workbook.SaveAs(fullPath);
                return fullPath;
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"An error occurred while exporting data to Excel: {ex.Message}");
                throw; // Re-throwing the exception after logging
            }
            finally
            {
                CleanUp(worksheet, workbook, workbooks, excelApp);
            }
        }

        private static void CleanUp(Excel._Worksheet? worksheet, Excel._Workbook? workbook, Excel.Workbooks? workbooks, Excel.Application? excelApp)
        {
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

        private static string GenerateFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) ? $"{fileName}.xlsx" : $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        }

        private static void AddHeaders(Excel._Worksheet worksheet, DataTable dataTable)
        {
            for (int i = 0; i < dataTable.Columns.Count; i++)
                worksheet.Cells[1, i + 1] = dataTable.Columns[i].ColumnName;
        }

        private static void AddData(Excel._Worksheet worksheet, DataTable dataTable, bool includeHeaders)
        {
            int rowOffset = includeHeaders ? 2 : 1;
            int rowCount = dataTable.Rows.Count;
            int columnCount = dataTable.Columns.Count;

            object[,] values = new object[rowCount, columnCount];

            for (int i = 0; i < rowCount; i++)
                for (int j = 0; j < columnCount; j++)
                    values[i, j] = dataTable.Rows[i][j];

            Excel.Range range = worksheet.Cells[rowOffset, 1];
            range = range.Resize[rowCount, columnCount];
            range.Value = values; // Set the entire block of values in one shot
        }

        private static void AddDataInBulk(Excel._Worksheet worksheet, DataTable dataTable, bool includeHeaders)
        {
            int columnsCount = dataTable.Columns.Count;
            object[,] values = new object[dataTable.Rows.Count, columnsCount];

            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                for (int j = 0; j < columnsCount; j++)
                {
                    values[i, j] = dataTable.Rows[i][j];
                }
            }

            int rowOffset = includeHeaders ? 2 : 1;
            string excelRange = $"A{rowOffset}:" + GetExcelColumnName(columnsCount) + $"{rowOffset + dataTable.Rows.Count - 1}";
            Excel.Range range = worksheet.Range[excelRange];
            range.Value = values;
        }

        private static string GetExcelColumnName(int columnNumber)
        {
            int dividend = columnNumber;
            string columnName = String.Empty;
            int modulo;

            while (dividend > 0)
            {
                modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modulo).ToString() + columnName;
                dividend = (dividend - modulo) / 26;
            }

            return columnName;
        }

        public static void WriteDataTableToTextFile(DataTable dataTable, string directoryPath, string fileName)
        {
            fileName = Path.ChangeExtension(fileName, ".txt");
            string filePath = Path.Combine(directoryPath, fileName);

            StringBuilder sb = new();

            // Iterate through all rows in the DataTable
            foreach (DataRow row in dataTable.Rows)
            {
                // Array to hold the cells in the current row
                string[] array = new string[dataTable.Columns.Count];

                // Fill the array with the cell values
                for (int i = 0; i < dataTable.Columns.Count; i++)
                {
                    string? nullableRowContent = row[i].ToString();
                    string rowContent = "";
                    if (nullableRowContent != null)
                    {
                        rowContent = nullableRowContent;
                    }
                    // Use .ToString() to ensure that even null values become empty strings
                    array[i] = rowContent;
                }

                // Join the array elements with a semicolon and append to the StringBuilder
                sb.AppendLine(string.Join(";", array));
            }

            // Write the StringBuilder contents to the text file at the specified path
            File.WriteAllText(filePath, sb.ToString());
        }

        public static DataGridView CreateDataGridView(
            DataTable dataTable,
            Form mainForm,
            Panel panelToAddTo,
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

        public static void CreateDropDownMenu(ref Button buttonToUse, ref ContextMenuStrip? contextMenuStrip, Dictionary<string, string> dictionaryToUse, bool allActive = false, int visibleItemCount = 30, bool clean = false)
        {
            contextMenuStrip = new ContextMenuStrip();
            Button localButtonToUse = buttonToUse;
            // List to hold all menu items
            List<ToolStripMenuItem> allMenuItems = new();
            List<string> selectedItems = new();
            foreach (KeyValuePair<string, string> item in dictionaryToUse)
            {
                ToolStripMenuItem menuItem;
                if (clean)
                {
                    menuItem = new ToolStripMenuItem($"{item.Value}")
                    {
                        CheckOnClick = true,
                        Checked = allActive,
                        Font = new Font("Segoe UI", 10, FontStyle.Regular),
                        Tag = item.Key
                    };
                }
                else
                {
                    menuItem = new ToolStripMenuItem($"{item.Key} - {item.Value}")
                    {
                        CheckOnClick = true,
                        Checked = allActive,
                        Font = new Font("Segoe UI", 10, FontStyle.Regular),
                        Tag = item.Key
                    };
                }

                menuItem.Click += (sender, e) =>
                {
                    if (sender is ToolStripMenuItem clickedItem)
                    {
                        string? nullTag = clickedItem.Tag.ToString();
                        string tag = "";
                        if (nullTag != null)
                        {
                            tag = nullTag;
                        }
                        else
                        {
                            Logger.LogWarning(null, $"Tag vuota nel CreateDropDownMenu del {menuItem}");
                        }
                        // Check if the item is being selected or deselected
                        if (clickedItem.Checked)
                        {
                            if (!string.IsNullOrWhiteSpace(tag))
                            {
                                // Add the item's key to the list of selected items
                                selectedItems.Add(tag);
                            }
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(tag) && selectedItems.Contains(tag))
                            {
                                // Remove the item's key from the list of selected items
                                selectedItems.Remove(tag);
                            }
                        }

                        // Update the button's text with all selected item keys, joined by commas
                        localButtonToUse.Text = string.Join(", ", selectedItems);

                        if (selectedItems.Count > 0)
                        {
                            AdjustFontSizeToFit(localButtonToUse, localButtonToUse.Text);
                        }
                        else
                        {
                            // Reset font size to default if no items are selected
                            localButtonToUse.Font = new Font(localButtonToUse.Font.FontFamily, 10, FontStyle.Regular);
                        }
                    }
                };

                allMenuItems.Add(menuItem);
            }

            int scrollPosition = 0; // Track the current scroll position

            ContextMenuStrip localMenuStrip = contextMenuStrip;

            void InitialPopulation()
            {
                localMenuStrip.Items.Clear();
                for (int i = 0; i < Math.Min(visibleItemCount, allMenuItems.Count); i++)
                {
                    localMenuStrip.Items.Add(allMenuItems[i]);
                }
            }

            void AdjustFontSizeToFit(Button button, string text)
            {
                using Graphics g = button.CreateGraphics();
                float fontSize = button.Font.Size;
                SizeF stringSize = g.MeasureString(text, new Font(button.Font.FontFamily, fontSize));

                // Try increasing font size until the text size exceeds the button's dimensions
                while (true)
                {
                    SizeF newSize = g.MeasureString(text, new Font(button.Font.FontFamily, fontSize + 0.2f));

                    // Check if increasing makes the text too large for the button's width or height
                    if (newSize.Width > button.Width - 10 || newSize.Height > button.Height - 10) // 10 is a buffer to avoid text touching the button edges
                    {
                        break; // Stop if the next size would exceed the button's dimensions
                    }
                    else
                    {
                        fontSize += 0.2f; // Increase font size by small increments
                        stringSize = newSize;
                    }
                }

                // Decrease font size if the text size exceeds the button's width or height
                while ((stringSize.Width > button.Width - 10 || stringSize.Height > button.Height - 10) && fontSize > 1)
                {
                    fontSize -= 0.2f; // Decrease font size by small increments
                    stringSize = g.MeasureString(text, new Font(button.Font.FontFamily, fontSize));
                }

                // Apply the calculated font size to the button
                button.Font = new Font(button.Font.FontFamily, fontSize, button.Font.Style);
            }

            void UpdateVisibleItems(int newScrollPosition, int delta)
            {
                if (delta > 0) // Scrolling down
                {
                    for (int i = 0; i < delta; i++)
                    {
                        if (localMenuStrip.Items.Count >= visibleItemCount)
                        {
                            localMenuStrip.Items.RemoveAt(0);
                        }
                        if (newScrollPosition + visibleItemCount - 1 + i < allMenuItems.Count)
                        {
                            localMenuStrip.Items.Add(allMenuItems[newScrollPosition + visibleItemCount - 1 + i]);
                        }
                    }
                }
                else if (delta < 0) // Scrolling up
                {
                    for (int i = delta; i < 0; i++)
                    {
                        if (localMenuStrip.Items.Count >= visibleItemCount)
                        {
                            localMenuStrip.Items.RemoveAt(localMenuStrip.Items.Count - 1);
                        }
                        if (newScrollPosition - i < allMenuItems.Count)
                        {
                            localMenuStrip.Items.Insert(0, allMenuItems[newScrollPosition - i]);
                        }
                    }
                }
            }

            buttonToUse.Click += (sender, e) =>
            {
                InitialPopulation(); // Populate the initial set of items
                localMenuStrip.Show(localButtonToUse, new Point(0, localButtonToUse.Height));
            };

            contextMenuStrip.MouseWheel += (sender, e) =>
            {
                int previousScrollPosition = scrollPosition;
                scrollPosition -= Math.Sign(e.Delta) * 3; // Smoother scroll with smaller step
                scrollPosition = Math.Max(0, Math.Min(scrollPosition, allMenuItems.Count - visibleItemCount));

                int delta = scrollPosition - previousScrollPosition;

                UpdateVisibleItems(scrollPosition, delta); // Update visible items based on scroll
            };
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

        public static string SafeGetString(this IDataRecord record, string fieldName)
        {
            if (record[fieldName] is DBNull or null)
                return string.Empty;

            // At this point, record[fieldName] is neither DBNull nor null.
            // You can now safely call ToString() on it.
            return record[fieldName].ToString()!;
        }

        public static int SafeGetInt(this IDataRecord record, string fieldName, int defaultValue = 0)
        {
            if (record[fieldName] is DBNull or null)
                return defaultValue;

            if (int.TryParse(record[fieldName].ToString(), out int result))
                return result;

            return defaultValue;
        }

        public static double SafeGetDouble(this IDataRecord record, string fieldName, double defaultValue = 0.0)
        {
            if (record[fieldName] is DBNull or null)
                return defaultValue;

            if (double.TryParse(record[fieldName].ToString(), out double result))
                return result;

            return defaultValue;
        }

        public static string SafeGetString(this IDataRecord record, int index)
        {
            if (record[index] is DBNull or null)
                return string.Empty;

            // At this point, record[fieldName] is neither DBNull nor null.
            // You can now safely call ToString() on it.
            return record[index].ToString()!;
        }
        public static DataTable ConvertListToDataTable<T>(List<T> items)
        {
            DataTable dataTable = new DataTable(typeof(T).Name);

            // Get all the properties
            PropertyInfo[] Props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo prop in Props)
            {
                // Setting column names as Property names
                dataTable.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
            }
            foreach (T item in items)
            {
                var values = new object[Props.Length];
                for (int i = 0; i < Props.Length; i++)
                {
                    // Inserting property values to DataTable rows
                    values[i] = Props[i].GetValue(item, null);
                }
                dataTable.Rows.Add(values);
            }
            return dataTable;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    sealed class ProcedureCategoryAttribute : Attribute
    {
        public string Category { get; }
        public string Tier { get; }

        public ProcedureCategoryAttribute(string category, string tier)
        {
            Category = category;
            Tier = tier;
        }
    }
}
