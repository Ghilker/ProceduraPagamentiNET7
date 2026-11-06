using ClosedXML.Excel;
using System;
using System.Data;
using System.IO;
using System.Linq;

namespace ProcedureNet7
{
    internal static class TicketClosureWorkbook
    {
        public static string Export(DataTable decisions, string folderPath)
        {
            Directory.CreateDirectory(folderPath);
            string path = Path.Combine(folderPath, $"Ticket_Chiusure_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            using var workbook = new XLWorkbook();
            AddSheet(workbook, decisions, "Ticket chiudibili", "CHIUDERE");
            AddSheet(workbook, decisions, "Da verificare", "VERIFICARE");
            workbook.SaveAs(path);
            return path;
        }

        private static void AddSheet(XLWorkbook workbook, DataTable source, string name, string action)
        {
            DataTable data = source.Clone();
            foreach (DataRow row in source.Rows)
                if (TicketClosureReview.Read(row, "AZIONE_PROPOSTA") == action) data.ImportRow(row);
            var sheet = workbook.Worksheets.Add(name);
            sheet.ShowGridLines = false;
            for (int c = 0; c < data.Columns.Count; c++)
                sheet.Cell(1, c + 1).Value = data.Columns[c].ColumnName;
            if (data.Rows.Count > 0)
                sheet.Cell(2, 1).InsertData(data.AsEnumerable());
            sheet.Range(1, 1, Math.Max(1, data.Rows.Count + 1), data.Columns.Count).CreateTable();
            sheet.SheetView.FreezeRows(1);
            sheet.SheetView.FreezeColumns(1);
            sheet.Style.Font.FontName = "Arial";
            sheet.Style.Font.FontSize = 10;
            sheet.Columns().Width = 20;
            foreach (string column in new[] { "REGOLA", "MOTIVO", "EVIDENZE", "OGGETTO", "PRIMO_MSG_STUDENTE", "RICHIESTE_RICONOSCIUTE", "CODICI_BLOCCHI_ATTIVI", "DETTAGLIO_PAGAMENTI" })
            {
                int index = data.Columns[column]!.Ordinal + 1;
                sheet.Column(index).Width = column == "EVIDENZE" ? 85 : column == "PRIMO_MSG_STUDENTE" ? 70 : 45;
                sheet.Column(index).Style.Alignment.WrapText = true;
            }
            sheet.Column(data.Columns["DATA_CREAZIONE"]!.Ordinal + 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            sheet.Column(data.Columns["DATA_ESTRAZIONE"]!.Ordinal + 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            foreach (string column in new[] { "IMPORTO_BORSA_LORDO", "PAGATO_BORSA_LORDO", "PAGATO_RIMBORSO_TASSA", "RESIDUO_BORSA_LORDO" })
            {
                sheet.Column(data.Columns[column]!.Ordinal + 1).Style.NumberFormat.Format = "#,##0.00";
                for (int r = 0; r < data.Rows.Count; r++)
                    if (data.Rows[r].IsNull(column)) sheet.Cell(r + 2, data.Columns[column]!.Ordinal + 1).Clear(XLClearOptions.Contents);
            }
            sheet.Row(1).Height = 32;
            sheet.Row(1).Style.Alignment.WrapText = true;
            if (data.Rows.Count > 0)
            {
                sheet.Rows(2, data.Rows.Count + 1).Height = 64;
                sheet.Rows(2, data.Rows.Count + 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            }
        }
    }
}
