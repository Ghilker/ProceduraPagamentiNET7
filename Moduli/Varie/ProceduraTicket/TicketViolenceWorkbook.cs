using ClosedXML.Excel;
using System;
using System.Data;
using System.IO;
using System.Linq;

namespace ProcedureNet7
{
    internal static class TicketViolenceWorkbook
    {
        public static string Export(DataTable data, string folderPath)
        {
            Directory.CreateDirectory(folderPath);
            string path = Path.Combine(folderPath, $"Ticket_Segnalazioni_Violenza_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            using var workbook = new XLWorkbook();
            AddSheet(workbook, data);
            workbook.SaveAs(path);
            return path;
        }

        private static void AddSheet(XLWorkbook workbook, DataTable data)
        {
            var sheet = workbook.Worksheets.Add("Controllo violenza");
            for (int column = 0; column < data.Columns.Count; column++)
                sheet.Cell(1, column + 1).Value = data.Columns[column].ColumnName;
            if (data.Rows.Count > 0)
                sheet.Cell(2, 1).InsertData(data.AsEnumerable());
            sheet.Range(1, 1, Math.Max(1, data.Rows.Count + 1), data.Columns.Count).CreateTable();
            sheet.ShowGridLines = false;
            sheet.SheetView.FreezeRows(1);
            sheet.SheetView.FreezeColumns(2);
            sheet.Style.Font.FontName = "Arial";
            sheet.Style.Font.FontSize = 10;
            sheet.Columns().Width = 20;
            sheet.Row(1).Height = 32;
            sheet.Row(1).Style.Alignment.WrapText = true;
            foreach (string column in new[] { "OGGETTO", "PRIMO_MSG_STUDENTE" })
            {
                if (!data.Columns.Contains(column)) continue;
                var excelColumn = sheet.Column(data.Columns[column]!.Ordinal + 1);
                excelColumn.Width = column == "OGGETTO" ? 45 : 85;
                excelColumn.Style.Alignment.WrapText = true;
            }
            if (data.Columns.Contains("DATA_CREAZIONE"))
                sheet.Column(data.Columns["DATA_CREAZIONE"]!.Ordinal + 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            if (data.Rows.Count > 0)
            {
                sheet.Rows(2, data.Rows.Count + 1).Height = 64;
                sheet.Rows(2, data.Rows.Count + 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            }
            Highlight(sheet, data);
        }

        public static void Highlight(IXLWorksheet sheet, DataTable data)
        {
            if (!data.Columns.Contains(TicketViolenceCheck.ColumnName)) return;
            int column = data.Columns[TicketViolenceCheck.ColumnName]!.Ordinal + 1;
            sheet.Column(column).Width = 24;
            for (int row = 0; row < data.Rows.Count; row++)
            {
                if (!string.Equals(Convert.ToString(data.Rows[row][column - 1]), "SI", StringComparison.Ordinal)) continue;
                var cell = sheet.Cell(row + 2, column);
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFA500");
                cell.Style.Font.FontColor = XLColor.Black;
                cell.Style.Font.Bold = true;
            }
        }
    }
}
