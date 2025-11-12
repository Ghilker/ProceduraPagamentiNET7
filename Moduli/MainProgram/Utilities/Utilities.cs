using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;
using OfficeOpenXml;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Excel = Microsoft.Office.Interop.Excel;

using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
namespace ProcedureNet7
{
    public static class Utilities
    {
        /// <summary>
        /// Reads data from an Excel file into a DataTable.
        /// Optionally uses the first row as data instead of headers.
        /// </summary>
        /// <param name="filePath">Path to the Excel file</param>
        /// <param name="useFirstRowAsData">If true, the first row is treated as data rather than headers.</param>
        /// <returns>A populated DataTable.</returns>
        public static DataTable ReadExcelToDataTable(string filePath, bool useFirstRowAsData = false)
        {
            Logger.LogDebug(null, $"Starting to read Excel file: '{filePath}' (useFirstRowAsData={useFirstRowAsData}).");

            DataTable returnDataTable = new DataTable();

            try
            {
                using (XLWorkbook workbook = new XLWorkbook(filePath))
                {
                    var worksheet = workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        Logger.LogError(null, "No worksheet found in the Excel file.");
                        throw new Exception("No worksheet found in the Excel file.");
                    }

                    // Get the used range
                    var range = worksheet.RangeUsed();
                    int lastColumnNumber = range.LastColumnUsed().ColumnNumber();
                    int lastRowNumber = range.LastRowUsed().RowNumber();

                    // Iterate row by row
                    for (int rowIdx = 1; rowIdx <= lastRowNumber; rowIdx++)
                    {
                        var row = worksheet.Row(rowIdx);

                        // If first row is headers (unless useFirstRowAsData == true)
                        if (rowIdx == 1 && !useFirstRowAsData)
                        {
                            for (int colIdx = 1; colIdx <= lastColumnNumber; colIdx++)
                            {
                                string originalHeader = row.Cell(colIdx).GetValue<string>();
                                string sanitizedHeader = SanitizeColumnName(originalHeader);
                                returnDataTable.Columns.Add(sanitizedHeader);
                            }
                        }
                        else
                        {
                            // If the DataTable has no columns, create them
                            if (returnDataTable.Columns.Count == 0)
                            {
                                for (int colIdx = 1; colIdx <= lastColumnNumber; colIdx++)
                                {
                                    returnDataTable.Columns.Add("Column" + colIdx);
                                }
                            }

                            DataRow dataRow = returnDataTable.NewRow();
                            for (int colIdx = 1; colIdx <= lastColumnNumber; colIdx++)
                            {
                                dataRow[colIdx - 1] = row.Cell(colIdx).GetValue<string>() ?? string.Empty;
                            }
                            returnDataTable.Rows.Add(dataRow);
                        }
                    }
                }

                Logger.LogInfo(null,
                    $"Successfully read Excel file: '{filePath}'. Rows={returnDataTable.Rows.Count}, Cols={returnDataTable.Columns.Count}");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error reading Excel file '{filePath}': {ex.Message}");
                throw; // re-throw for higher-level handling
            }

            return returnDataTable;
        }

        /// <summary>
        /// Reads a CSV file into a DataTable. Assumes the first row is a header row.
        /// </summary>
        public static DataTable CsvToDataTable(string csvFilePath)
        {
            Logger.LogDebug(null, $"Starting to read CSV file: '{csvFilePath}'.");

            DataTable returnDataTable = new DataTable();

            try
            {
                using (TextFieldParser parser = new TextFieldParser(csvFilePath))
                {
                    parser.Delimiters = new[] { ";" };
                    parser.HasFieldsEnclosedInQuotes = true;

                    // Assume first row is header
                    string[] headers = parser.ReadFields();
                    if (headers != null)
                    {
                        foreach (string header in headers)
                        {
                            string sanitizedHeader = SanitizeColumnName(header);
                            returnDataTable.Columns.Add(sanitizedHeader);
                        }
                    }

                    // Read remaining rows
                    while (!parser.EndOfData)
                    {
                        string[] fields = parser.ReadFields();
                        // Ensure the row has the same number of columns
                        // (in case CSV lines have varying columns)
                        while (fields.Length < returnDataTable.Columns.Count)
                        {
                            // Add empty values for missing columns
                            fields = fields.Concat(new[] { string.Empty }).ToArray();
                        }

                        returnDataTable.Rows.Add(fields);
                    }
                }

                Logger.LogInfo(null,
                    $"Successfully read CSV file: '{csvFilePath}'. Rows={returnDataTable.Rows.Count}, Cols={returnDataTable.Columns.Count}");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error reading CSV file '{csvFilePath}': {ex.Message}");
                throw;
            }

            return returnDataTable;
        }

        /// <summary>
        /// Sanitizes a column name by removing special characters (except spaces).
        /// </summary>
        public static string SanitizeColumnName(string columnName)
        {
            return Regex.Replace(columnName, "[^a-zA-Z0-9_ ]", "");
        }

        /// <summary>
        /// Checks if the application can connect to a database using the supplied connection string.
        /// </summary>
        public static bool CanConnectToDatabase(string connectionString, out SqlConnection? outConnection)
        {
            Logger.LogDebug(null, "Attempting to open a SQL connection.");
            outConnection = null;

            try
            {
                SqlConnection connection = new(connectionString);
                connection.Open();
                outConnection = connection;
                Logger.LogInfo(null, "SQL connection opened successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Failed to open SQL connection: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the existing SqlConnection is valid and open.
        /// </summary>
        public static bool CanConnectToDatabase(SqlConnection connection)
        {
            if (connection == null)
                return false;

            return connection.State == ConnectionState.Open;
        }

        /// <summary>
        /// Returns a comma-separated list of selected codes from a set of ToolStripMenuItems (checked).
        /// </summary>
        public static string GetCheckBoxSelectedCodes(ToolStripItemCollection? items)
        {
            if (items == null)
            {
                Logger.LogWarning(null, "No ToolStrip items provided. Returning empty codes.");
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

            string result = selectedCodes.Count == 0 ? "''" : string.Join(", ", selectedCodes);
            Logger.LogDebug(null, $"Selected codes: {result}");
            return result;
        }

        /// <summary>
        /// Prompts the user to pick a file, sets the path label and variable, optionally only writing 'Selezionato'.
        /// </summary>
        public static void ChooseFileAndSetPath(Label filePathLabel, FileDialog fileDialogToOpen, ref string varToSave, bool writeOnlySelected = false)
        {
            try
            {
                Logger.LogDebug(null, "Opening file dialog...");
                DialogResult result = fileDialogToOpen.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fileDialogToOpen.FileName))
                {
                    varToSave = fileDialogToOpen.FileName;
                    if (writeOnlySelected)
                    {
                        filePathLabel.Text = "Selezionato";
                    }
                    else
                    {
                        filePathLabel.Text = varToSave;
                    }
                    Logger.LogInfo(null, $"File selected: {varToSave}");
                }
                else
                {
                    Logger.LogWarning(null, "No file was selected or file path is empty.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error in ChooseFileAndSetPath: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Prompts the user to pick a folder, sets the label and variable accordingly.
        /// </summary>
        public static void ChooseFolder(Label folderPathLabel, FolderBrowserDialog chosenFolderDialog, ref string varToSaveTo)
        {
            try
            {
                Logger.LogDebug(null, "Opening folder dialog...");
                DialogResult result = chosenFolderDialog.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(chosenFolderDialog.SelectedPath))
                {
                    varToSaveTo = chosenFolderDialog.SelectedPath;
                    folderPathLabel.Text = varToSaveTo;
                    Logger.LogInfo(null, $"Folder selected: {varToSaveTo}");
                }
                else
                {
                    Logger.LogWarning(null, "No folder was selected or path is empty.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error in ChooseFolder: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Exports a DataTable to an Excel file using ClosedXML.
        /// </summary>
        public static string ExportDataTableToExcel(DataTable dataTable, string folderPath, bool includeHeaders = true, string fileName = "")
        {
            Logger.LogDebug(null, "Starting ExportDataTableToExcel...");

            if (dataTable == null || dataTable.Rows.Count == 0)
            {
                Logger.LogWarning(null, "DataTable is null or empty, no Excel file will be created.");
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                Logger.LogError(null, "Folder path is invalid or empty.");
                throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
            }

            try
            {
                fileName = GenerateFileName(fileName);
                string fullPath = Path.Combine(folderPath, fileName);

                // Ensure directory
                Directory.CreateDirectory(folderPath);

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Sheet1");

                    int currentRow = 1;
                    int totalColumns = dataTable.Columns.Count;

                    // If we want column headers
                    if (includeHeaders)
                    {
                        for (int col = 0; col < totalColumns; col++)
                        {
                            worksheet.Cell(currentRow, col + 1).Value = dataTable.Columns[col].ColumnName;
                        }
                        currentRow++;
                    }

                    // Populate rows
                    foreach (DataRow row in dataTable.Rows)
                    {
                        for (int col = 0; col < totalColumns; col++)
                        {
                            worksheet.Cell(currentRow, col + 1).Value = row[col]?.ToString() ?? string.Empty;
                        }
                        currentRow++;
                    }

                    workbook.SaveAs(fullPath);
                }

                Logger.LogInfo(null, $"Excel file created successfully at '{folderPath}\\{fileName}'.");
                return Path.Combine(folderPath, fileName);
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error exporting DataTable to Excel: {ex.Message}");
                throw;
            }
        }



        private static string GenerateFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName)
                ? $"{fileName}.xlsx"
                : $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        }

        /// <summary>
        /// Writes a DataTable to a text file. Each row is semicolon-separated.
        /// </summary>
        public static void WriteDataTableToTextFile(DataTable dataTable, string directoryPath, string fileName)
        {
            Logger.LogDebug(null, "Starting WriteDataTableToTextFile...");

            if (dataTable == null || dataTable.Rows.Count == 0)
            {
                Logger.LogWarning(null, "DataTable is null or empty, no text file will be created.");
                return;
            }

            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                Logger.LogError(null, "Directory path is invalid or empty.");
                throw new ArgumentException("Directory path cannot be empty.", nameof(directoryPath));
            }

            try
            {
                fileName = Path.ChangeExtension(fileName, ".txt");
                string filePath = Path.Combine(directoryPath, fileName);

                Directory.CreateDirectory(directoryPath); // ensure directory

                StringBuilder sb = new();
                foreach (DataRow row in dataTable.Rows)
                {
                    string[] array = new string[dataTable.Columns.Count];
                    for (int i = 0; i < dataTable.Columns.Count; i++)
                    {
                        string? nullableRowContent = row[i].ToString();
                        array[i] = nullableRowContent ?? string.Empty;
                    }
                    sb.AppendLine(string.Join(";", array));
                }

                File.WriteAllText(filePath, sb.ToString());
                Logger.LogInfo(null, $"Text file created successfully at '{filePath}'.");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error writing DataTable to text file: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates a DataGridView for a given DataTable and adds it to a panel in a form.
        /// </summary>
        public static DataGridView CreateDataGridView(
            DataTable dataTable,
            Form mainForm,
            Panel panelToAddTo,
            DataGridViewCellEventHandler? mouseClick_Handler = null,
            DataGridViewCellMouseEventHandler? columnHeaderMouseClick_Handler = null,
            string keyTag = "")
        {
            Logger.LogDebug(null, "Starting CreateDataGridView...");

            if (dataTable == null)
            {
                Logger.LogWarning(null, "DataTable is null. Creating an empty DataGridView.");
                dataTable = new DataTable();
            }

            DataGridView newDataGridView = new();

            try
            {
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
                        newDataGridView.CellClick += mouseClick_Handler;

                    if (columnHeaderMouseClick_Handler != null)
                        newDataGridView.ColumnHeaderMouseClick += columnHeaderMouseClick_Handler;

                    panelToAddTo.Controls.Add(newDataGridView);
                    newDataGridView.BringToFront();

                    foreach (DataGridViewColumn column in newDataGridView.Columns)
                        column.SortMode = DataGridViewColumnSortMode.NotSortable;

                    newDataGridView.DataSource = dataTable;
                    newDataGridView.Cursor = Cursors.Hand;
                });

                Logger.LogInfo(null, "DataGridView created and added to panel successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error creating DataGridView: {ex.Message}");
                throw;
            }

            return newDataGridView;
        }

        /// <summary>
        /// Creates a dropdown menu (ContextMenuStrip) for a dictionary of items and attaches it to a button.
        /// </summary>
        public static void CreateDropDownMenu(
            ref Button buttonToUse,
            ref ContextMenuStrip? contextMenuStrip,
            Dictionary<string, string> dictionaryToUse,
            bool allActive = false,
            int visibleItemCount = 30,
            bool clean = false)
        {
            Logger.LogDebug(null, $"Starting CreateDropDownMenu for button '{buttonToUse.Name}'...");

            try
            {
                contextMenuStrip = new ContextMenuStrip();
                Button localButtonToUse = buttonToUse;

                List<ToolStripMenuItem> allMenuItems = new();
                List<string> selectedItems = new();

                foreach (KeyValuePair<string, string> item in dictionaryToUse)
                {
                    string text = clean
                        ? $"{item.Value}"
                        : $"{item.Key} - {item.Value}";

                    ToolStripMenuItem menuItem = new ToolStripMenuItem(text)
                    {
                        CheckOnClick = true,
                        Checked = allActive,
                        Font = new Font("Segoe UI", 10, FontStyle.Regular),
                        Tag = item.Key
                    };

                    menuItem.Click += (sender, e) =>
                    {
                        if (sender is ToolStripMenuItem clickedItem)
                        {
                            string? nullTag = clickedItem.Tag?.ToString();
                            string tag = nullTag ?? string.Empty;

                            if (clickedItem.Checked)
                            {
                                if (!string.IsNullOrWhiteSpace(tag))
                                    selectedItems.Add(tag);
                            }
                            else
                            {
                                if (!string.IsNullOrWhiteSpace(tag) && selectedItems.Contains(tag))
                                    selectedItems.Remove(tag);
                            }

                            localButtonToUse.Text = string.Join(", ", selectedItems);

                            // Adjust the font if there are selected items
                            if (selectedItems.Count > 0)
                                AdjustFontSizeToFit(localButtonToUse, localButtonToUse.Text);
                            else
                                localButtonToUse.Font = new Font(localButtonToUse.Font.FontFamily, 10, FontStyle.Regular);
                        }
                    };

                    allMenuItems.Add(menuItem);
                }

                int scrollPosition = 0;
                ContextMenuStrip localMenuStrip = contextMenuStrip;

                void InitialPopulation()
                {
                    localMenuStrip.Items.Clear();
                    for (int i = 0; i < Math.Min(visibleItemCount, allMenuItems.Count); i++)
                    {
                        localMenuStrip.Items.Add(allMenuItems[i]);
                    }
                }

                buttonToUse.Click += (sender, e) =>
                {
                    InitialPopulation();
                    localMenuStrip.Show(localButtonToUse, new Point(0, localButtonToUse.Height));
                };

                contextMenuStrip.MouseWheel += (sender, e) =>
                {
                    int previousScrollPosition = scrollPosition;
                    scrollPosition -= Math.Sign(e.Delta) * 3; // Smoother scroll
                    scrollPosition = Math.Max(0, Math.Min(scrollPosition, allMenuItems.Count - visibleItemCount));

                    int delta = scrollPosition - previousScrollPosition;
                    UpdateVisibleItems(scrollPosition, delta);
                };

                void UpdateVisibleItems(int newScrollPosition, int delta)
                {
                    if (delta > 0) // scrolling down
                    {
                        for (int i = 0; i < delta; i++)
                        {
                            if (localMenuStrip.Items.Count >= visibleItemCount)
                                localMenuStrip.Items.RemoveAt(0);

                            if (newScrollPosition + visibleItemCount - 1 + i < allMenuItems.Count)
                                localMenuStrip.Items.Add(allMenuItems[newScrollPosition + visibleItemCount - 1 + i]);
                        }
                    }
                    else if (delta < 0) // scrolling up
                    {
                        for (int i = delta; i < 0; i++)
                        {
                            if (localMenuStrip.Items.Count >= visibleItemCount)
                                localMenuStrip.Items.RemoveAt(localMenuStrip.Items.Count - 1);

                            if (newScrollPosition - i < allMenuItems.Count)
                                localMenuStrip.Items.Insert(0, allMenuItems[newScrollPosition - i]);
                        }
                    }
                }

                void AdjustFontSizeToFit(Button button, string text)
                {
                    using Graphics g = button.CreateGraphics();
                    float fontSize = button.Font.Size;
                    SizeF stringSize = g.MeasureString(text, new Font(button.Font.FontFamily, fontSize));

                    // Increase font size while it fits
                    while (true)
                    {
                        SizeF newSize = g.MeasureString(text, new Font(button.Font.FontFamily, fontSize + 0.2f));
                        if (newSize.Width > button.Width - 10 || newSize.Height > button.Height - 10)
                        {
                            break;
                        }
                        fontSize += 0.2f;
                        stringSize = newSize;
                    }

                    // Decrease font size if too large
                    while ((stringSize.Width > button.Width - 10 || stringSize.Height > button.Height - 10) && fontSize > 1)
                    {
                        fontSize -= 0.2f;
                        stringSize = g.MeasureString(text, new Font(button.Font.FontFamily, fontSize));
                    }

                    button.Font = new Font(button.Font.FontFamily, fontSize, button.Font.Style);
                }

                Logger.LogInfo(null, $"CreateDropDownMenu completed for button '{buttonToUse.Name}'.");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error creating dropdown menu: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Ensures that a directory path exists. If not, it is created.
        /// </summary>
        public static string EnsureDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                Logger.LogDebug(null, $"Directory ensured: {path}");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error ensuring directory '{path}': {ex.Message}");
                throw;
            }
            return path;
        }

        public static string RemoveAllSpaces(string strimToTrim)
        {
            return strimToTrim.Replace(" ", "");
        }

        public static string RemoveNonAlphanumeric(string input)
        {
            return Regex.Replace(input, "[^a-zA-Z0-9]", "");
        }

        public static string RemoveNonNumeric(string input)
        {
            return Regex.Replace(input, "[^0-9]", "");
        }

        public static string RemoveNonAlphanumericAndKeepSpaces(string input)
        {
            return Regex.Replace(input, "[^a-zA-Z0-9 ]", "");
        }

        public static string SafeGetString(this IDataRecord record, string fieldName)
        {
            if (record[fieldName] is DBNull or null)
                return string.Empty;

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

        public static int SafeGetInt(this IDataRecord record, int index, int defaultValue = 0)
        {
            if (record[index] is DBNull or null)
                return defaultValue;

            if (int.TryParse(record[index].ToString(), out int result))
                return result;

            return defaultValue;
        }

        public static DateTime SafeGetDateTime(this IDataRecord record, string fieldName, DateTime? defaultValue = null)
        {
            if (record[fieldName] is DBNull or null)
                return defaultValue ?? DateTime.MinValue;

            if (DateTime.TryParse(record[fieldName].ToString(), out DateTime result))
                return result;

            return defaultValue ?? DateTime.MinValue;
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

            return record[index].ToString()!;
        }

        /// <summary>
        /// Converts a generic List of objects (with public properties) to a DataTable.
        /// </summary>
        public static DataTable ConvertListToDataTable<T>(List<T> items)
        {
            Logger.LogDebug(null, $"Converting list of {typeof(T).Name} to DataTable...");

            DataTable dataTable = new DataTable(typeof(T).Name);

            try
            {
                PropertyInfo[] props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (PropertyInfo prop in props)
                {
                    dataTable.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
                }

                foreach (T item in items)
                {
                    var values = new object[props.Length];
                    for (int i = 0; i < props.Length; i++)
                    {
                        values[i] = props[i].GetValue(item, null) ?? DBNull.Value;
                    }
                    dataTable.Rows.Add(values);
                }

                Logger.LogInfo(null, $"Conversion successful. Rows={dataTable.Rows.Count}, Cols={dataTable.Columns.Count}.");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Error converting list to DataTable: {ex.Message}");
                throw;
            }

            return dataTable;
        }

        public static readonly HashSet<string> _reservedWinNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
            "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
        };

        public static string MakeSafePathSegment(string name, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(name)) return "untitled";

        // replace invalid chars
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(invalid.Contains(ch) ? '_' : ch);

        // collapse whitespace, trim, drop trailing dots
        string cleaned = Regex.Replace(sb.ToString(), @"\s+", " ").Trim().TrimEnd('.');
        if (cleaned.Length == 0) cleaned = "untitled";

        // avoid reserved names
        if (_reservedWinNames.Contains(cleaned))
            cleaned = "_" + cleaned;

        if (cleaned.Length <= maxLen) return cleaned;

        // shorten with stable hash suffix
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(cleaned))).Substring(0, 8);
        int keep = Math.Max(1, maxLen - (1 + hash.Length)); // "~" + 8
        return cleaned.Substring(0, keep) + "~" + hash;
    }

        /// <summary>
        /// Ensures the *full path* (folder + "\\" + base + ext) stays under a conservative limit (default 200).
        /// Returns a safe base name (without extension) respecting both per-segment and full-path constraints.
        /// </summary>
        public static string MakeSafeFileBaseForFolder(
            string baseNameNoExt,
            string folderPath,
            string extensionWithDot,
            int maxTotalPath = 200,
            int hardMaxBaseLen = 120)
        {
        baseNameNoExt ??= "file";
        extensionWithDot ??= ".dat";

        string sanitized = MakeSafePathSegment(baseNameNoExt, hardMaxBaseLen);
        string full = Path.Combine(folderPath, sanitized + extensionWithDot);

        if (full.Length <= maxTotalPath) return sanitized;

        int allowedBaseLen = Math.Max(10, maxTotalPath - folderPath.Length - extensionWithDot.Length - 1);
        return MakeSafePathSegment(sanitized, allowedBaseLen);
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
