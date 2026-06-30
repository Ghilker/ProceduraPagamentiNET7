using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ProcedureNet7
{
    internal static class TicketWorkbookExporter
    {
        private static readonly string[] MailAttachmentColumns =
        {
            "ID_TICKET",
            "CODSTUD",
            "COGNOME",
            "NOME",
            "CODFISC",
            "OGGETTO",
            "CATEGORIA",
            "SOTTOCATEGORIA",
            "ANNO_ACCADEMICO_RICHIESTA",
            "STATO",
            "DATA_CREAZIONE",
            "PRIMO_MSG_STUDENTE",
            "DATA_ULTIMO_MESSAGGIO",
            "DOMANDA_20242025",
            "BLOCCHI_20242025",
            "ESITO_BS_20242025",
            "ESITO_PA_20242025",
            "SEDE_DESCR_20242025",
            "HA_PRIMA_RATA_20242025",
            "HA_SALDO_20242025",
            "HA_RIMBORSO_20242025",
            "DOMANDA_20252026",
            "BLOCCHI_20252026",
            "ESITO_BS_20252026",
            "ESITO_PA_20252026",
            "SEDE_DESCR_20252026",
            "HA_PRIMA_RATA_20252026",
            "HA_SALDO_20252026",
            "HA_RIMBORSO_20252026",
            "ARGOMENTO_PRIMARIO",
            "ARGOMENTO_SECONDARIO",
            "PUNTEGGIO_CONFIDENZA",
            "MOTIVO_VERIFICA",
            "CLASSIFICAZIONE_DA_VERIFICARE",
            "UFFICIO_DESTINATARIO",
            "STATO_REVISIONE",
            "CLASSIFICAZIONE_CORRETTA",
            "RIFERITO_A_BLOCCHI",
            "PS_DOCUMENTI_LAVORATI",
            "ISTANZA_DOMICILIO_APERTA",
            "ISTANZA_DOMICILIO_LAVORATA"
        };

        public static string ExportMailAttachment(
            DataTable summary,
            string folderPath)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("La cartella di esportazione non è valida.", nameof(folderPath));

            Directory.CreateDirectory(folderPath);
            string outputPath = Path.Combine(
                folderPath,
                $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            DataTable mailData = CreateMailAttachmentTable(summary);
            Logger.LogInfo(
                null,
                $"Creazione file Excel per invio mail. Righe: {mailData.Rows.Count}; colonne: {mailData.Columns.Count}");

            using var workbook = new XLWorkbook();
            IXLWorksheet worksheet = AddWorksheet(workbook, "Sheet1", mailData);
            ConfigureMailAttachment(worksheet, mailData);

            Logger.LogInfo(null, $"Salvataggio file mail ticket in corso: {outputPath}");
            workbook.SaveAs(outputPath);
            Logger.LogInfo(null, $"File mail ticket creato: {outputPath}");
            return outputPath;
        }

        public static string Export(
            DataTable summary,
            TicketTopicSheetContext context,
            IEnumerable<ITicketTopicSheet> topicSheets,
            string folderPath)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("La cartella di esportazione non è valida.", nameof(folderPath));

            Directory.CreateDirectory(folderPath);
            string outputPath = Path.Combine(
                folderPath,
                $"Ticket_Argomenti_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            List<ITicketTopicSheet> orderedSheets = topicSheets
                .OrderBy(GetOperationalTopicOrder)
                .ThenBy(sheet => sheet.SheetName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Logger.LogInfo(
                null,
                $"Creazione workbook ticket. Ticket nel riepilogo: {summary.Rows.Count}; fogli tematici: {orderedSheets.Count}");

            using var workbook = new XLWorkbook();

            // Il primo foglio mantiene nome, posizione e contenuto operativo originari.
            // Le keyword tecniche restano presenti per compatibilità, ma vengono nascoste.
            Logger.LogInfo(null, $"Creazione foglio 'Riepilogo ticket' con {summary.Rows.Count} righe");
            IXLWorksheet summaryWorksheet = AddWorksheet(workbook, "Riepilogo ticket", summary);
            ConfigureSummaryClassification(summaryWorksheet, summary);

            // Fogli operativi aggiunti dopo il riepilogo originario.
            Logger.LogInfo(null, "Preparazione fogli operativi della coda ticket");
            DataTable operationalQueue = TicketOperationalWorkbook.BuildOperationalQueue(context);
            DataTable dashboard = TicketOperationalWorkbook.BuildDashboard(operationalQueue);
            DataTable rules = TicketOperationalWorkbook.BuildRulesAndResponseCodes();
            DataTable ticketsToVerify = TicketOperationalWorkbook.BuildTicketsToVerify(operationalQueue);
            DataTable classificationReview = BuildClassificationReview(summary);
            DataTable classificationControl = BuildClassificationControl(summary);
            DataTable paymentsByAcademicYear = TicketPaymentYearWorkbook.Build(context, operationalQueue);

            IXLWorksheet queueWorksheet = AddWorksheet(workbook, "00_Coda operativa", operationalQueue);
            ConfigureOperationalQueue(queueWorksheet, operationalQueue);

            IXLWorksheet dashboardWorksheet = AddWorksheet(workbook, "01_Cruscotto", dashboard);
            ConfigureDashboard(dashboardWorksheet, dashboard);

            IXLWorksheet rulesWorksheet = AddWorksheet(workbook, "02_Regole e codici risposta", rules);
            ConfigureRules(rulesWorksheet, rules);

            IXLWorksheet verifyWorksheet = AddWorksheet(workbook, "03_Ticket da verificare", ticketsToVerify);
            ConfigureOperationalQueue(verifyWorksheet, ticketsToVerify);

            IXLWorksheet classificationReviewWorksheet = AddWorksheet(
                workbook,
                "03A_Revisione classificazione",
                classificationReview);
            ConfigureClassificationReview(classificationReviewWorksheet, classificationReview);

            IXLWorksheet classificationControlWorksheet = AddWorksheet(
                workbook,
                "03B_Controllo classificazione",
                classificationControl);
            ConfigureClassificationControl(classificationControlWorksheet, classificationControl);

            string onlyCurrentCategory = context.GetStudentApplicationCategoryLabel(
                StudentApplicationCategory.SOLO_DOMANDA_ANNO_CORRENTE);
            string onlyPreviousCategory = context.GetStudentApplicationCategoryLabel(
                StudentApplicationCategory.SOLO_DOMANDE_PRECEDENTI);
            string currentAndPreviousCategory = context.GetStudentApplicationCategoryLabel(
                StudentApplicationCategory.DOMANDA_ANNO_CORRENTE_E_PRECEDENTI);

            DataTable onlyCurrentStudents = TicketOperationalWorkbook.BuildStudentCategorySummary(
                operationalQueue,
                onlyCurrentCategory);
            DataTable onlyPreviousStudents = TicketOperationalWorkbook.BuildStudentCategorySummary(
                operationalQueue,
                onlyPreviousCategory);
            DataTable currentAndPreviousStudents = TicketOperationalWorkbook.BuildStudentCategorySummary(
                operationalQueue,
                currentAndPreviousCategory);

            IXLWorksheet onlyCurrentStudentsWorksheet = AddWorksheet(
                workbook,
                "04_Studenti solo 2026-2027",
                onlyCurrentStudents);
            ConfigureStudentCategorySummary(onlyCurrentStudentsWorksheet, onlyCurrentStudents);

            IXLWorksheet onlyPreviousStudentsWorksheet = AddWorksheet(
                workbook,
                "05_Studenti solo precedenti",
                onlyPreviousStudents);
            ConfigureStudentCategorySummary(onlyPreviousStudentsWorksheet, onlyPreviousStudents);

            IXLWorksheet currentAndPreviousStudentsWorksheet = AddWorksheet(
                workbook,
                "06_Studenti entrambe",
                currentAndPreviousStudents);
            ConfigureStudentCategorySummary(currentAndPreviousStudentsWorksheet, currentAndPreviousStudents);

            var createdTopicSheets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ITicketTopicSheet topicSheet in orderedSheets)
            {
                string operationalSheetName = GetOperationalTopicSheetName(topicSheet.SheetName);
                Logger.LogInfo(null, $"Preparazione foglio tematico '{operationalSheetName}'");
                DataTable topicData = topicSheet.BuildSheet(context);
                TicketOperationalWorkbook.EnrichTopicSheet(topicData, operationalQueue);
                Logger.LogInfo(
                    null,
                    $"Popolamento foglio '{operationalSheetName}' con {topicData.Rows.Count} righe");
                IXLWorksheet topicWorksheet = AddWorksheet(workbook, operationalSheetName, topicData);
                createdTopicSheets[operationalSheetName] = topicWorksheet.Name;

                if (string.Equals(topicSheet.SheetName, "Pagamenti", StringComparison.OrdinalIgnoreCase))
                {
                    ConfigurePayments(topicWorksheet, topicData);
                    IXLWorksheet annualPaymentsWorksheet = AddWorksheet(
                        workbook,
                        "10A_Pagamenti per AA",
                        paymentsByAcademicYear);
                    ConfigureAnnualPayments(annualPaymentsWorksheet, paymentsByAcademicYear);
                }
            }

            // In tutti i riepiloghi, "Foglio dettaglio" apre direttamente la riga del ticket.
            ConfigureDetailLinks(queueWorksheet, operationalQueue, createdTopicSheets, "ID_TICKET");
            ConfigureDetailLinks(verifyWorksheet, ticketsToVerify, createdTopicSheets, "ID_TICKET");
            ConfigureDetailLinks(
                onlyCurrentStudentsWorksheet,
                onlyCurrentStudents,
                createdTopicSheets,
                "ID_TICKET_PRIORITARIO");
            ConfigureDetailLinks(
                onlyPreviousStudentsWorksheet,
                onlyPreviousStudents,
                createdTopicSheets,
                "ID_TICKET_PRIORITARIO");
            ConfigureDetailLinks(
                currentAndPreviousStudentsWorksheet,
                currentAndPreviousStudents,
                createdTopicSheets,
                "ID_TICKET_PRIORITARIO");

            Logger.LogInfo(null, $"Salvataggio workbook ticket in corso: {outputPath}");
            workbook.SaveAs(outputPath);
            Logger.LogInfo(null, $"File ticket multi-foglio creato: {outputPath}");
            return outputPath;
        }

        private static DataTable CreateMailAttachmentTable(DataTable summary)
        {
            var result = new DataTable();
            foreach (string columnName in MailAttachmentColumns)
            {
                Type columnType = summary.Columns.Contains(columnName)
                    ? summary.Columns[columnName]!.DataType
                    : typeof(string);
                result.Columns.Add(columnName, columnType);
            }

            foreach (DataRow sourceRow in summary.Rows)
            {
                DataRow targetRow = result.NewRow();
                foreach (string columnName in MailAttachmentColumns)
                {
                    targetRow[columnName] = summary.Columns.Contains(columnName)
                        ? sourceRow[columnName]
                        : string.Empty;
                }
                result.Rows.Add(targetRow);
            }

            return result;
        }

        private static DataTable BuildClassificationReview(DataTable summary)
        {
            var result = new DataTable();
            result.Columns.Add("ID_TICKET", typeof(string));
            result.Columns.Add("OGGETTO", typeof(string));
            result.Columns.Add("PRIMO_MSG_STUDENTE", typeof(string));
            result.Columns.Add("CLASSIFICAZIONE_PROPOSTA", typeof(string));
            result.Columns.Add("PUNTEGGIO_CONFIDENZA", typeof(int));
            result.Columns.Add("MOTIVO_VERIFICA", typeof(string));
            result.Columns.Add("UFFICIO_DESTINATARIO", typeof(string));
            result.Columns.Add("CLASSIFICAZIONE_CORRETTA", typeof(string));
            result.Columns.Add("MOTIVAZIONE_CORREZIONE", typeof(string));
            result.Columns.Add("KEYWORD_DA_AGGIUNGERE", typeof(string));
            result.Columns.Add("KEYWORD_DA_ESCLUDERE", typeof(string));
            result.Columns.Add("STATO_REVISIONE", typeof(string));

            foreach (DataRow sourceRow in summary.Rows)
            {
                if (!string.Equals(
                        ReadString(sourceRow, "CLASSIFICAZIONE_DA_VERIFICARE"),
                        "SI",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DataRow targetRow = result.NewRow();
                targetRow["ID_TICKET"] = ReadString(sourceRow, "ID_TICKET");
                targetRow["OGGETTO"] = ReadString(sourceRow, "OGGETTO");
                targetRow["PRIMO_MSG_STUDENTE"] = ReadString(sourceRow, "PRIMO_MSG_STUDENTE");
                targetRow["CLASSIFICAZIONE_PROPOSTA"] = BuildProposedClassification(sourceRow);
                targetRow["PUNTEGGIO_CONFIDENZA"] = ReadInt(sourceRow, "PUNTEGGIO_CONFIDENZA");
                targetRow["MOTIVO_VERIFICA"] = ReadString(sourceRow, "MOTIVO_VERIFICA");
                targetRow["UFFICIO_DESTINATARIO"] = ReadString(sourceRow, "UFFICIO_DESTINATARIO");
                targetRow["CLASSIFICAZIONE_CORRETTA"] = ReadString(sourceRow, "CLASSIFICAZIONE_CORRETTA");
                targetRow["MOTIVAZIONE_CORREZIONE"] = string.Empty;
                targetRow["KEYWORD_DA_AGGIUNGERE"] = string.Empty;
                targetRow["KEYWORD_DA_ESCLUDERE"] = string.Empty;
                targetRow["STATO_REVISIONE"] = string.IsNullOrWhiteSpace(ReadString(sourceRow, "STATO_REVISIONE"))
                    ? "DA_REVISIONARE"
                    : ReadString(sourceRow, "STATO_REVISIONE");
                result.Rows.Add(targetRow);
            }

            return result;
        }

        private static DataTable BuildClassificationControl(DataTable summary)
        {
            var result = new DataTable();
            result.Columns.Add("ID_TICKET", typeof(string));
            result.Columns.Add("ARGOMENTO_PRIMARIO", typeof(string));
            result.Columns.Add("ARGOMENTO_SECONDARIO", typeof(string));
            result.Columns.Add("PUNTEGGIO_CONFIDENZA", typeof(int));
            result.Columns.Add("CLASSIFICAZIONE_DA_VERIFICARE", typeof(string));
            result.Columns.Add("MOTIVO_VERIFICA", typeof(string));
            result.Columns.Add("KEYWORD_ARGOMENTO_PRIMARIO", typeof(string));
            result.Columns.Add("KEYWORD_ARGOMENTO_SECONDARIO", typeof(string));
            result.Columns.Add("OGGETTO", typeof(string));
            result.Columns.Add("PRIMO_MSG_STUDENTE", typeof(string));

            foreach (DataRow sourceRow in summary.Rows)
            {
                DataRow targetRow = result.NewRow();
                foreach (DataColumn column in result.Columns)
                {
                    targetRow[column.ColumnName] = column.DataType == typeof(int)
                        ? ReadInt(sourceRow, column.ColumnName)
                        : ReadString(sourceRow, column.ColumnName);
                }
                result.Rows.Add(targetRow);
            }

            return result;
        }

        private static string BuildProposedClassification(DataRow row)
        {
            string primary = ReadString(row, "ARGOMENTO_PRIMARIO");
            string secondary = ReadString(row, "ARGOMENTO_SECONDARIO");
            return string.IsNullOrWhiteSpace(secondary)
                ? primary
                : string.Concat(primary, " / ", secondary);
        }

        private static int ReadInt(DataRow row, string columnName)
        {
            if (row == null || row.Table == null || !row.Table.Columns.Contains(columnName))
                return 0;

            object value = row[columnName];
            if (value == null || value == DBNull.Value)
                return 0;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return 0;
            }
            catch (InvalidCastException)
            {
                return 0;
            }
            catch (OverflowException)
            {
                return 0;
            }
        }

        private static IXLWorksheet AddWorksheet(
            XLWorkbook workbook,
            string requestedName,
            DataTable data)
        {
            string sheetName = GetUniqueSheetName(workbook, requestedName);
            IXLWorksheet worksheet = workbook.Worksheets.Add(sheetName);

            int columnCount = data.Columns.Count;
            if (columnCount == 0)
            {
                worksheet.Cell(1, 1).Value = "Nessuna colonna disponibile";
                return worksheet;
            }

            if (data.Rows.Count == 0)
            {
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                    worksheet.Cell(1, columnIndex + 1).Value = data.Columns[columnIndex].ColumnName;
            }
            else
            {
                // InsertTable evita il popolamento cella-per-cella e applica una struttura Excel nativa.
                string tableName = $"TicketData{workbook.Worksheets.Count}";
                IXLTable table = worksheet.Cell(1, 1).InsertTable(data, tableName, true);
                table.Theme = XLTableTheme.TableStyleMedium2;
            }

            IXLRange header = worksheet.Range(1, 1, 1, columnCount);
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            worksheet.SheetView.FreezeRows(1);
            worksheet.Columns().AdjustToContents();
            return worksheet;
        }

        private static void ConfigureSummaryClassification(IXLWorksheet worksheet, DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            int lastRow = data.Rows.Count + 1;
            worksheet.SheetView.FreezeRows(1);
            worksheet.Row(1).Height = 30;

            SetWidth(worksheet, data, "ARGOMENTO_PRIMARIO", 26);
            SetWidth(worksheet, data, "ARGOMENTO_SECONDARIO", 34);
            SetWidth(worksheet, data, "PUNTEGGIO_CONFIDENZA", 18);
            SetWidth(worksheet, data, "MOTIVO_VERIFICA", 40);
            SetWidth(worksheet, data, "UFFICIO_DESTINATARIO", 24);
            SetWidth(worksheet, data, "STATO_REVISIONE", 20);
            SetWidth(worksheet, data, "CLASSIFICAZIONE_CORRETTA", 38);

            HideColumn(worksheet, data, "KEYWORD_ARGOMENTO_PRIMARIO");
            HideColumn(worksheet, data, "KEYWORD_ARGOMENTO_SECONDARIO");

            ApplyEditableColumnStyle(worksheet, data, lastRow, "UFFICIO_DESTINATARIO");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "STATO_REVISIONE");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "CLASSIFICAZIONE_CORRETTA");
            ApplyListValidation(
                worksheet,
                data,
                lastRow,
                "STATO_REVISIONE",
                "DA_REVISIONARE,IN_REVISIONE,COMPLETATA,NON_RICHIESTA");

            int confidenceColumn = GetColumnNumber(data, "PUNTEGGIO_CONFIDENZA");
            int verificationColumn = GetColumnNumber(data, "CLASSIFICAZIONE_DA_VERIFICARE");
            int reasonColumn = GetColumnNumber(data, "MOTIVO_VERIFICA");
            if (data.Rows.Count == 0 || confidenceColumn == 0)
                return;

            for (int rowIndex = 2; rowIndex <= lastRow; rowIndex++)
            {
                DataRow sourceRow = data.Rows[rowIndex - 2];
                int confidence = ReadInt(sourceRow, "PUNTEGGIO_CONFIDENZA");
                bool needsReview = string.Equals(
                    ReadString(sourceRow, "CLASSIFICAZIONE_DA_VERIFICARE"),
                    "SI",
                    StringComparison.OrdinalIgnoreCase);

                if (needsReview || confidence < 60)
                {
                    worksheet.Cell(rowIndex, confidenceColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE5CD");
                    worksheet.Cell(rowIndex, confidenceColumn).Style.Font.Bold = true;
                }
                if (needsReview && verificationColumn > 0)
                {
                    worksheet.Cell(rowIndex, verificationColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4CCCC");
                    worksheet.Cell(rowIndex, verificationColumn).Style.Font.Bold = true;
                }
                if (needsReview && reasonColumn > 0)
                {
                    worksheet.Cell(rowIndex, reasonColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
                }
            }
        }

        private static void ConfigureClassificationReview(IXLWorksheet worksheet, DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            int lastRow = data.Rows.Count + 1;
            worksheet.SheetView.FreezeRows(1);
            worksheet.SheetView.FreezeColumns(2);
            worksheet.Row(1).Height = 34;
            if (data.Rows.Count > 0)
            {
                worksheet.Range(2, 1, lastRow, data.Columns.Count)
                    .Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                worksheet.Range(2, 1, lastRow, data.Columns.Count)
                    .Style.Alignment.WrapText = true;
            }

            SetWidth(worksheet, data, "ID_TICKET", 20);
            SetWidth(worksheet, data, "OGGETTO", 38);
            SetWidth(worksheet, data, "PRIMO_MSG_STUDENTE", 62);
            SetWidth(worksheet, data, "CLASSIFICAZIONE_PROPOSTA", 42);
            SetWidth(worksheet, data, "PUNTEGGIO_CONFIDENZA", 18);
            SetWidth(worksheet, data, "MOTIVO_VERIFICA", 38);
            SetWidth(worksheet, data, "UFFICIO_DESTINATARIO", 24);
            SetWidth(worksheet, data, "CLASSIFICAZIONE_CORRETTA", 42);
            SetWidth(worksheet, data, "MOTIVAZIONE_CORREZIONE", 48);
            SetWidth(worksheet, data, "KEYWORD_DA_AGGIUNGERE", 34);
            SetWidth(worksheet, data, "KEYWORD_DA_ESCLUDERE", 34);
            SetWidth(worksheet, data, "STATO_REVISIONE", 20);

            ApplyEditableColumnStyle(worksheet, data, lastRow, "UFFICIO_DESTINATARIO");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "CLASSIFICAZIONE_CORRETTA");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "MOTIVAZIONE_CORREZIONE");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "KEYWORD_DA_AGGIUNGERE");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "KEYWORD_DA_ESCLUDERE");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "STATO_REVISIONE");
            ApplyListValidation(
                worksheet,
                data,
                lastRow,
                "STATO_REVISIONE",
                "DA_REVISIONARE,IN_REVISIONE,COMPLETATA,NON_RICHIESTA");

            int confidenceColumn = GetColumnNumber(data, "PUNTEGGIO_CONFIDENZA");
            if (confidenceColumn > 0 && data.Rows.Count > 0)
            {
                worksheet.Range(2, confidenceColumn, lastRow, confidenceColumn)
                    .Style.Fill.BackgroundColor = XLColor.FromHtml("#F4CCCC");
                worksheet.Range(2, confidenceColumn, lastRow, confidenceColumn)
                    .Style.Font.Bold = true;
            }
        }

        private static void ConfigureClassificationControl(IXLWorksheet worksheet, DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            int lastRow = data.Rows.Count + 1;
            worksheet.SheetView.FreezeRows(1);
            worksheet.SheetView.FreezeColumns(4);
            worksheet.Row(1).Height = 34;
            if (data.Rows.Count > 0)
            {
                worksheet.Range(2, 1, lastRow, data.Columns.Count)
                    .Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                worksheet.Range(2, 1, lastRow, data.Columns.Count)
                    .Style.Alignment.WrapText = true;
            }

            SetWidth(worksheet, data, "ID_TICKET", 20);
            SetWidth(worksheet, data, "ARGOMENTO_PRIMARIO", 26);
            SetWidth(worksheet, data, "ARGOMENTO_SECONDARIO", 34);
            SetWidth(worksheet, data, "PUNTEGGIO_CONFIDENZA", 18);
            SetWidth(worksheet, data, "CLASSIFICAZIONE_DA_VERIFICARE", 22);
            SetWidth(worksheet, data, "MOTIVO_VERIFICA", 40);
            SetWidth(worksheet, data, "KEYWORD_ARGOMENTO_PRIMARIO", 48);
            SetWidth(worksheet, data, "KEYWORD_ARGOMENTO_SECONDARIO", 48);
            SetWidth(worksheet, data, "OGGETTO", 42);
            SetWidth(worksheet, data, "PRIMO_MSG_STUDENTE", 68);
        }

        private static void ConfigureMailAttachment(
            IXLWorksheet worksheet,
            DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            int lastRow = data.Rows.Count + 1;
            worksheet.SheetView.FreezeRows(1);
            worksheet.SheetView.FreezeColumns(5);
            worksheet.Row(1).Height = 30;

            if (data.Rows.Count > 0)
            {
                IXLRange rows = worksheet.Range(2, 1, lastRow, data.Columns.Count);
                rows.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                rows.Style.Alignment.WrapText = true;
            }

            SetWidth(worksheet, data, "ID_TICKET", 20);
            SetWidth(worksheet, data, "CODSTUD", 14);
            SetWidth(worksheet, data, "COGNOME", 18);
            SetWidth(worksheet, data, "NOME", 18);
            SetWidth(worksheet, data, "CODFISC", 18);
            SetWidth(worksheet, data, "OGGETTO", 42);
            SetWidth(worksheet, data, "CATEGORIA", 24);
            SetWidth(worksheet, data, "SOTTOCATEGORIA", 24);
            SetWidth(worksheet, data, "ANNO_ACCADEMICO_RICHIESTA", 22);
            SetWidth(worksheet, data, "STATO", 14);
            SetWidth(worksheet, data, "PRIMO_MSG_STUDENTE", 60);
            SetWidth(worksheet, data, "ARGOMENTO_PRIMARIO", 26);
            SetWidth(worksheet, data, "ARGOMENTO_SECONDARIO", 34);
            SetWidth(worksheet, data, "PUNTEGGIO_CONFIDENZA", 18);
            SetWidth(worksheet, data, "MOTIVO_VERIFICA", 40);
            SetWidth(worksheet, data, "CLASSIFICAZIONE_DA_VERIFICARE", 22);
            SetWidth(worksheet, data, "UFFICIO_DESTINATARIO", 24);
            SetWidth(worksheet, data, "STATO_REVISIONE", 20);
            SetWidth(worksheet, data, "CLASSIFICAZIONE_CORRETTA", 38);

            ApplyDateFormat(worksheet, data, lastRow, "DATA_CREAZIONE");
            ApplyDateFormat(worksheet, data, lastRow, "DATA_ULTIMO_MESSAGGIO");
        }

        private static void ConfigureOperationalQueue(IXLWorksheet worksheet, DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            int lastRow = data.Rows.Count + 1;
            int lastColumn = data.Columns.Count;
            worksheet.SheetView.FreezeRows(1);
            worksheet.SheetView.FreezeColumns(10);
            worksheet.Row(1).Height = 30;

            if (data.Rows.Count > 0)
            {
                IXLRange rows = worksheet.Range(2, 1, lastRow, lastColumn);
                rows.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                rows.Style.Alignment.WrapText = true;
            }

            SetWidth(worksheet, data, "CHIUDIBILE", 14);
            SetWidth(worksheet, data, "PRIORITA", 11);
            SetWidth(worksheet, data, "PROSSIMO_CONTROLLO", 46);
            SetWidth(worksheet, data, "GRUPPO_VERIFICA", 26);
            SetWidth(worksheet, data, "AZIONE_RICHIESTA", 46);
            SetWidth(worksheet, data, "CODICE_RISPOSTA", 15);
            SetWidth(worksheet, data, "TESTO_RISCONTRO_BASE", 54);
            SetWidth(worksheet, data, "EVIDENZE_CHIAVE", 54);
            SetWidth(worksheet, data, "MOTIVO_NON_CHIUSURA", 54);
            SetWidth(worksheet, data, "FOGLIO_DETTAGLIO", 26);
            SetWidth(worksheet, data, "ID_TICKET", 20);
            SetWidth(worksheet, data, "ETA_TICKET_GIORNI", 14);
            SetWidth(worksheet, data, "DATA_APERTURA", 16);
            SetWidth(worksheet, data, "DATA_ULTIMO_MESSAGGIO", 18);
            SetWidth(worksheet, data, "ARGOMENTO", 24);
            SetWidth(worksheet, data, "OGGETTO", 38);
            SetWidth(worksheet, data, "PRIMO_MSG_STUDENTE", 60);
            SetWidth(worksheet, data, "STATO_CHIUDIBILITA", 36);
            SetWidth(worksheet, data, "OGGETTO_RICHIESTO", 34);
            SetWidth(worksheet, data, "AA_COERENZA", 26);
            SetWidth(worksheet, data, "AMBITI_RICHIESTI", 42);
            SetWidth(worksheet, data, "ESITO_CONTROLLI_CHIUSURA", 80);
            SetWidth(worksheet, data, "CONDIZIONE_RISOLTA", 38);
            SetWidth(worksheet, data, "MOTIVO_PRIORITA", 36);
            SetWidth(worksheet, data, "DECISIONE_PROPOSTA", 34);
            SetWidth(worksheet, data, "MOTIVO_DECISIONE", 42);
            SetWidth(worksheet, data, "TICKET_RIGUARDA_BLOCCHI", 20);
            SetWidth(worksheet, data, "STUDENTE_HA_BLOCCHI", 20);
            SetWidth(worksheet, data, "BLOCCHI_RILEVANTI", 38);
            SetWidth(worksheet, data, "BLOCCHI_NON_PERTINENTI", 38);
            SetWidth(worksheet, data, "CONTRADDIZIONI_RILEVATE", 56);
            SetWidth(worksheet, data, "ARGOMENTI_SECONDARI", 32);
            SetWidth(worksheet, data, "INTENTO_RICHIESTA", 24);
            SetWidth(worksheet, data, "SEGNALI_INTENTO", 34);
            SetWidth(worksheet, data, "ANNO_ACCADEMICO_RICHIESTA", 22);
            SetWidth(worksheet, data, "CATEGORIA_DOMANDE_STUDENTE", 34);
            SetWidth(worksheet, data, "ANNI_DOMANDE_RILEVATI", 30);
            SetWidth(worksheet, data, "AA_RECORD_OPERATIVO", 20);
            SetWidth(worksheet, data, "CRITERIO_SELEZIONE_RECORD", 28);
            SetWidth(worksheet, data, "STATO_DOMANDA_INTERPRETATO", 30);
            SetWidth(worksheet, data, "CONFIDENZA_ARGOMENTO", 20);
            SetWidth(worksheet, data, "CONFIDENZA_INTENTO", 18);
            SetWidth(worksheet, data, "CONFIDENZA_DATI_OPERATIVI", 24);
            SetWidth(worksheet, data, "CONFIDENZA_DECISIONE", 20);
            SetWidth(worksheet, data, "CODFISC", 18);
            SetWidth(worksheet, data, "CODSTUD", 14);
            SetWidth(worksheet, data, "STATO_LAVORAZIONE", 22);
            SetWidth(worksheet, data, "DECISIONE_OPERATORE", 32);
            SetWidth(worksheet, data, "ASSEGNATARIO", 22);
            SetWidth(worksheet, data, "NOTA_OPERATORE", 50);
            SetWidth(worksheet, data, "DATA_LAVORAZIONE", 18);
            SetWidth(worksheet, data, "ESITO_INVIATO", 18);

            ApplyEditableColumnStyle(worksheet, data, lastRow, "STATO_LAVORAZIONE");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "DECISIONE_OPERATORE");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "ASSEGNATARIO");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "NOTA_OPERATORE");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "DATA_LAVORAZIONE");
            ApplyEditableColumnStyle(worksheet, data, lastRow, "ESITO_INVIATO");
            ApplyListValidation(
                worksheet,
                data,
                lastRow,
                "STATO_LAVORAZIONE",
                "DA_LAVORARE,IN_LAVORAZIONE,IN_ATTESA_STUDENTE,IN_ATTESA_UFFICIO,CHIUSO");
            ApplyListValidation(
                worksheet,
                data,
                lastRow,
                "DECISIONE_OPERATORE",
                "CONFERMA_CHIUSURA,NON_CHIUDIBILE,DA_VERIFICARE");
            ApplyListValidation(worksheet, data, lastRow, "ESITO_INVIATO", "SI,NO");

            ApplyClosureStyle(worksheet, data, lastRow);
            ApplyPriorityStyle(worksheet, data, lastRow);
            ApplyDecisionConfidenceStyle(worksheet, data, lastRow);
            ApplyDateFormat(worksheet, data, lastRow, "DATA_APERTURA");
            ApplyDateFormat(worksheet, data, lastRow, "DATA_ULTIMO_MESSAGGIO");
            ApplyDateFormat(worksheet, data, lastRow, "DATA_LAVORAZIONE");

            HideColumn(worksheet, data, "MOTIVO_PRIORITA");
            HideColumn(worksheet, data, "MOTIVO_DECISIONE");
            HideColumn(worksheet, data, "SEGNALI_INTENTO");
            HideColumn(worksheet, data, "CRITERIO_SELEZIONE_RECORD");
            HideColumn(worksheet, data, "CONFIDENZA_ARGOMENTO");
            HideColumn(worksheet, data, "CONFIDENZA_INTENTO");
            HideColumn(worksheet, data, "CONFIDENZA_DATI_OPERATIVI");
        }

        private static void ConfigureStudentCategorySummary(
            IXLWorksheet worksheet,
            DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            worksheet.SheetView.FreezeRows(1);
            worksheet.SheetView.FreezeColumns(4);
            worksheet.Row(1).Height = 30;
            if (data.Rows.Count > 0)
            {
                IXLRange rows = worksheet.Range(2, 1, data.Rows.Count + 1, data.Columns.Count);
                rows.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                rows.Style.Alignment.WrapText = true;
            }

            SetWidth(worksheet, data, "CATEGORIA_DOMANDE_STUDENTE", 34);
            SetWidth(worksheet, data, "CODFISC", 18);
            SetWidth(worksheet, data, "CODSTUD", 14);
            SetWidth(worksheet, data, "ANNI_DOMANDE_RILEVATI", 28);
            SetWidth(worksheet, data, "NUMERO_TICKET", 14);
            SetWidth(worksheet, data, "TICKET_P1", 12);
            SetWidth(worksheet, data, "TICKET_CHIUDIBILI", 18);
            SetWidth(worksheet, data, "TICKET_DA_VERIFICARE", 20);
            SetWidth(worksheet, data, "TICKET_AZIONE_OPERATIVA", 22);
            SetWidth(worksheet, data, "ALMENO_UN_TICKET_RIGUARDA_BLOCCHI", 28);
            SetWidth(worksheet, data, "STUDENTE_HA_BLOCCHI", 20);
            SetWidth(worksheet, data, "PRIORITA_MASSIMA", 18);
            SetWidth(worksheet, data, "ID_TICKET_PRIORITARIO", 22);
            SetWidth(worksheet, data, "OGGETTO_TICKET_PRIORITARIO", 48);
            SetWidth(worksheet, data, "DECISIONE_PROPOSTA_TICKET_PRIORITARIO", 38);
            SetWidth(worksheet, data, "FOGLIO_DETTAGLIO", 26);
            SetWidth(worksheet, data, "DATA_APERTURA_PIU_VECCHIA", 20);
            ApplyDateFormat(worksheet, data, data.Rows.Count + 1, "DATA_APERTURA_PIU_VECCHIA");
        }

        private static void ConfigurePayments(
            IXLWorksheet worksheet,
            DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            int lastRow = data.Rows.Count + 1;
            worksheet.SheetView.FreezeRows(1);
            worksheet.SheetView.FreezeColumns(6);
            worksheet.Row(1).Height = 34;
            if (data.Rows.Count > 0)
            {
                IXLRange rows = worksheet.Range(2, 1, lastRow, data.Columns.Count);
                rows.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                rows.Style.Alignment.WrapText = true;
            }

            SetWidth(worksheet, data, "ID_TICKET", 20);
            SetWidth(worksheet, data, "CODSTUD", 14);
            SetWidth(worksheet, data, "CODFISC", 18);
            SetWidth(worksheet, data, "PRIMO_MSG_STUDENTE", 54);
            SetWidth(worksheet, data, "AA_RECORD_OPERATIVO", 20);
            SetWidth(worksheet, data, "CRITERIO_SELEZIONE_RECORD", 28);
            SetWidth(worksheet, data, "ESITO_BS", 14);
            SetWidth(worksheet, data, "IMPORTO_ASSEGNATO", 18);
            SetWidth(worksheet, data, "IMPORTO_SPECIFICHE_IMPEGNI", 24);
            SetWidth(worksheet, data, "IMPORTO_PAGATO_BS_VALIDO", 22);
            SetWidth(worksheet, data, "IMPORTO_PRIMA_RATA_BS", 22);
            SetWidth(worksheet, data, "IMPORTO_SALDO_BS", 20);
            SetWidth(worksheet, data, "IMPORTO_INTEGRAZIONI_BS", 24);
            SetWidth(worksheet, data, "IMPORTO_PAGAMENTI_BS_STORNATI", 26);
            SetWidth(worksheet, data, "CODICI_PAGAMENTI_BS_STORNATI", 30);
            SetWidth(worksheet, data, "MANDATI_BS_STORNATI", 26);
            SetWidth(worksheet, data, "IMPORTO_BS_CODICI_NON_CLASSIFICATI", 28);
            SetWidth(worksheet, data, "CODICI_BS_NON_CLASSIFICATI", 28);
            SetWidth(worksheet, data, "IMPORTO_REVERSALI_BS", 20);
            SetWidth(worksheet, data, "IMPORTO_DETRAZIONI", 20);
            SetWidth(worksheet, data, "RESIDUO_BS_STIMATO", 20);
            SetWidth(worksheet, data, "TIPI_PAGAMENTO_BS", 28);
            SetWidth(worksheet, data, "MANDATI_BS", 26);
            SetWidth(worksheet, data, "MANDATI_PRIMA_RATA_BS", 26);
            SetWidth(worksheet, data, "MANDATI_SALDO_BS", 26);
            SetWidth(worksheet, data, "MANDATI_INTEGRAZIONI_BS", 30);
            SetWidth(worksheet, data, "BLOCCHI", 42);
            SetWidth(worksheet, data, "INDICAZIONE_OPERATORE", 58);
            SetWidth(worksheet, data, "TICKET_RIGUARDA_BLOCCHI", 20);
            SetWidth(worksheet, data, "STUDENTE_HA_BLOCCHI", 20);

            ApplyCurrencyFormat(worksheet, data, lastRow);
            HighlightStornedPayments(worksheet, data, lastRow);
            HighlightUnclassifiedPaymentCodes(worksheet, data, lastRow);
        }

        private static void ConfigureAnnualPayments(
            IXLWorksheet worksheet,
            DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            int lastRow = data.Rows.Count + 1;
            worksheet.SheetView.FreezeRows(1);
            worksheet.SheetView.FreezeColumns(8);
            worksheet.Row(1).Height = 34;

            if (data.Rows.Count > 0)
            {
                IXLRange rows = worksheet.Range(2, 1, lastRow, data.Columns.Count);
                rows.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                rows.Style.Alignment.WrapText = true;
            }

            SetWidth(worksheet, data, "ID_TICKET", 20);
            SetWidth(worksheet, data, "CODSTUD", 14);
            SetWidth(worksheet, data, "CODFISC", 18);
            SetWidth(worksheet, data, "OGGETTO", 42);
            SetWidth(worksheet, data, "PRIORITA", 10);
            SetWidth(worksheet, data, "DECISIONE_PROPOSTA", 34);
            SetWidth(worksheet, data, "TICKET_RIGUARDA_BLOCCHI", 20);
            SetWidth(worksheet, data, "STUDENTE_HA_BLOCCHI", 20);
            SetWidth(worksheet, data, "ANNO_ACCADEMICO_PAGAMENTO", 22);
            SetWidth(worksheet, data, "NUM_DOMANDA", 18);
            SetWidth(worksheet, data, "ESITO_BS", 14);
            SetWidth(worksheet, data, "IMPORTO_ASSEGNATO", 18);
            SetWidth(worksheet, data, "IMPORTO_SPECIFICHE_IMPEGNI", 24);
            SetWidth(worksheet, data, "IMPORTO_BSP0_PRIMA_RATA", 22);
            SetWidth(worksheet, data, "IMPORTO_BSP_RIEMISSIONI_PRIMA_RATA", 28);
            SetWidth(worksheet, data, "IMPORTO_BSS0_SALDO", 20);
            SetWidth(worksheet, data, "IMPORTO_BSS_RIEMISSIONI_SALDO", 26);
            SetWidth(worksheet, data, "IMPORTO_BSI0_INTEGRAZIONE_PRIMA_RATA", 30);
            SetWidth(worksheet, data, "IMPORTO_BSI_RIEMISSIONI_INTEGRAZIONE_PRIMA_RATA", 38);
            SetWidth(worksheet, data, "IMPORTO_BSI9_INTEGRAZIONE_SALDO", 28);
            SetWidth(worksheet, data, "IMPORTO_BSI_RIEMISSIONI_INTEGRAZIONE_SALDO", 36);
            SetWidth(worksheet, data, "IMPORTO_PAGATO_BS_VALIDO", 22);
            SetWidth(worksheet, data, "IMPORTO_PAGAMENTI_BS_STORNATI", 26);
            SetWidth(worksheet, data, "CODICI_PAGAMENTI_BS_STORNATI", 30);
            SetWidth(worksheet, data, "MANDATI_BS_STORNATI", 26);
            SetWidth(worksheet, data, "IMPORTO_BS_CODICI_NON_CLASSIFICATI", 28);
            SetWidth(worksheet, data, "CODICI_BS_NON_CLASSIFICATI", 28);
            SetWidth(worksheet, data, "IMPORTO_REVERSALI_BS", 20);
            SetWidth(worksheet, data, "IMPORTO_DETRAZIONI", 20);
            SetWidth(worksheet, data, "RESIDUO_BS_STIMATO", 20);
            SetWidth(worksheet, data, "CODICI_PAGAMENTO_BS_VALIDI", 30);
            SetWidth(worksheet, data, "MANDATI_BS_VALIDI", 28);
            SetWidth(worksheet, data, "ESERCIZI_FINANZIARI_BS", 24);
            SetWidth(worksheet, data, "BLOCCHI", 42);

            ApplyPriorityStyle(worksheet, data, lastRow);
            ApplyCurrencyFormat(worksheet, data, lastRow);
            HighlightStornedPayments(worksheet, data, lastRow);
        }

        private static void ApplyCurrencyFormat(IXLWorksheet worksheet, DataTable data, int lastRow)
        {
            if (lastRow < 2)
                return;

            foreach (DataColumn column in data.Columns)
            {
                if (column.DataType != typeof(decimal))
                    continue;

                int columnNumber = column.Ordinal + 1;
                worksheet.Range(2, columnNumber, lastRow, columnNumber)
                    .Style.NumberFormat.Format = "#,##0.00";
            }
        }

        private static void HighlightStornedPayments(IXLWorksheet worksheet, DataTable data, int lastRow)
        {
            int stornedColumn = GetColumnNumber(data, "IMPORTO_PAGAMENTI_BS_STORNATI");
            if (stornedColumn == 0 || lastRow < 2)
                return;

            // Legge il DataTable, non la cella Excel: le righe senza record operativo
            // contengono DBNull e vengono esportate come celle vuote.
            for (int rowIndex = 2; rowIndex <= lastRow; rowIndex++)
            {
                DataRow sourceRow = data.Rows[rowIndex - 2];
                decimal amount = ReadDecimal(sourceRow, "IMPORTO_PAGAMENTI_BS_STORNATI");
                if (amount == 0m)
                    continue;

                worksheet.Cell(rowIndex, stornedColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4CCCC");
                worksheet.Cell(rowIndex, stornedColumn).Style.Font.Bold = true;
            }
        }

        private static void HighlightUnclassifiedPaymentCodes(IXLWorksheet worksheet, DataTable data, int lastRow)
        {
            int amountColumn = GetColumnNumber(data, "IMPORTO_BS_CODICI_NON_CLASSIFICATI");
            int codesColumn = GetColumnNumber(data, "CODICI_BS_NON_CLASSIFICATI");
            if (amountColumn == 0 || lastRow < 2)
                return;

            for (int rowIndex = 2; rowIndex <= lastRow; rowIndex++)
            {
                DataRow sourceRow = data.Rows[rowIndex - 2];
                decimal amount = ReadDecimal(sourceRow, "IMPORTO_BS_CODICI_NON_CLASSIFICATI");
                string codes = codesColumn == 0
                    ? string.Empty
                    : ReadString(sourceRow, "CODICI_BS_NON_CLASSIFICATI");
                if (amount == 0m && string.IsNullOrWhiteSpace(codes))
                    continue;

                worksheet.Cell(rowIndex, amountColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE5CD");
                worksheet.Cell(rowIndex, amountColumn).Style.Font.Bold = true;
                if (codesColumn > 0)
                    worksheet.Cell(rowIndex, codesColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE5CD");
            }
        }

        private static decimal ReadDecimal(DataRow row, string columnName)
        {
            if (row == null || row.Table == null || !row.Table.Columns.Contains(columnName))
                return 0m;

            object value = row[columnName];
            if (value == null || value == DBNull.Value)
                return 0m;

            try
            {
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return 0m;
            }
            catch (InvalidCastException)
            {
                return 0m;
            }
            catch (OverflowException)
            {
                return 0m;
            }
        }

        private static string ReadString(DataRow row, string columnName)
        {
            if (row == null || row.Table == null || !row.Table.Columns.Contains(columnName))
                return string.Empty;

            object value = row[columnName];
            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        private static void ConfigureDashboard(
            IXLWorksheet worksheet,
            DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            SetWidth(worksheet, data, "SEZIONE", 18);
            SetWidth(worksheet, data, "INDICATORE", 48);
            SetWidth(worksheet, data, "VALORE", 14);
            if (data.Rows.Count > 0)
            {
                worksheet.Range(2, 1, data.Rows.Count + 1, data.Columns.Count)
                    .Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            }

            // I valori sono calcolati durante la generazione, così il cruscotto è
            // immediatamente leggibile anche senza il ricalcolo delle formule di Excel.
        }

        private static void ApplyDashboardFormulas(
            IXLWorksheet dashboard,
            DataTable dashboardData,
            DataTable queue)
        {
            if (queue.Rows.Count == 0 || dashboardData.Rows.Count == 0)
                return;

            const string queueSheetName = "00_Coda operativa";
            int lastQueueRow = queue.Rows.Count + 1;
            int valueColumn = GetColumnNumber(dashboardData, "VALORE");
            if (valueColumn == 0)
                return;

            for (int rowIndex = 0; rowIndex < dashboardData.Rows.Count; rowIndex++)
            {
                DataRow metric = dashboardData.Rows[rowIndex];
                string section = metric["SEZIONE"]?.ToString()?.Trim() ?? string.Empty;
                string indicator = metric["INDICATORE"]?.ToString()?.Trim() ?? string.Empty;
                string? formula = GetDashboardFormula(queueSheetName, lastQueueRow, queue, section, indicator);
                if (!string.IsNullOrWhiteSpace(formula))
                    dashboard.Cell(rowIndex + 2, valueColumn).FormulaA1 = formula;
            }
        }

        private static string? GetDashboardFormula(
            string queueSheetName,
            int lastQueueRow,
            DataTable queue,
            string section,
            string indicator)
        {
            string? range(string columnName)
            {
                int columnNumber = GetColumnNumber(queue, columnName);
                if (columnNumber == 0)
                    return null;

                string letter = GetColumnLetter(columnNumber);
                return $"'{queueSheetName}'!${letter}$2:${letter}${lastQueueRow}";
            }

            string? decision = range("DECISIONE_PROPOSTA");
            string? priority = range("PRIORITA");
            string? status = range("STATO_LAVORAZIONE");
            string? contradiction = range("CONTRADDIZIONI_RILEVATE");
            string? confidence = range("CONFIDENZA_DECISIONE");
            string? ticketId = range("ID_TICKET");
            string? topic = range("ARGOMENTO");
            string? ticketMentionsBlocks = range("TICKET_RIGUARDA_BLOCCHI");
            string? studentHasBlocks = range("STUDENTE_HA_BLOCCHI");

            if (indicator == "Ticket totali" && ticketId != null)
                return $"COUNTA({ticketId})";
            if (indicator == "Chiudibili con risposta standard" && decision != null)
                return $"COUNTIF({decision},\"CHIUDIBILE_CON_RISPOSTA_STANDARD*\")";
            if (indicator == "Chiudibili con riscontro personalizzato" && decision != null)
                return $"COUNTIF({decision},\"CHIUDIBILE_CON_RISCONTRO_PERSONALIZZATO*\")";
            if (indicator == "Non chiudibili - azione operativa" && decision != null)
                return $"COUNTIF({decision},\"NON_CHIUDIBILE_AZIONE_OPERATIVA_NECESSARIA*\")";
            if (indicator == "Non chiudibili - documento o dato mancante" && decision != null)
                return $"COUNTIF({decision},\"NON_CHIUDIBILE_DOCUMENTO_O_DATO_MANCANTE*\")";
            if (indicator == "Da verificare" && decision != null)
                return $"COUNTIF({decision},\"DA_VERIFICARE_DATI_INSUFFICIENTI*\")";
            if ((indicator == "P1" || indicator == "P2" || indicator == "P3") && priority != null)
                return $"COUNTIF({priority},\"{indicator}\")";
            if (indicator == "Contraddizioni rilevate" && contradiction != null)
                return $"COUNTIF({contradiction},\"<>\")";
            if (indicator == "Decisioni a confidenza bassa" && confidence != null)
                return $"COUNTIF({confidence},\"BASSA\")";
            if (indicator == "Ticket che riguardano blocchi" && ticketMentionsBlocks != null)
                return $"COUNTIF({ticketMentionsBlocks},\"SI\")";
            if (indicator == "Ticket con studente che ha blocchi" && studentHasBlocks != null)
                return $"COUNTIF({studentHasBlocks},\"SI\")";
            if (section == "Lavorazione" && status != null)
            {
                string expectedStatus = indicator switch
                {
                    "Da lavorare" => "DA_LAVORARE",
                    "In lavorazione" => "IN_LAVORAZIONE",
                    "In attesa studente" => "IN_ATTESA_STUDENTE",
                    "In attesa ufficio" => "IN_ATTESA_UFFICIO",
                    "Chiusi" => "CHIUSO",
                    _ => indicator.ToUpperInvariant().Replace(" ", "_")
                };
                return $"COUNTIF({status},\"{EscapeExcelFormulaValue(expectedStatus)}\")";
            }
            if (section == "Argomento" && topic != null)
                return $"COUNTIF({topic},\"{EscapeExcelFormulaValue(indicator)}\")";

            return null;
        }

        private static void ConfigureDetailLinks(
            IXLWorksheet worksheet,
            DataTable data,
            IReadOnlyDictionary<string, string> topicSheets,
            string ticketIdColumnName)
        {
            if (worksheet == null)
                throw new ArgumentNullException(nameof(worksheet));
            if (data == null || data.Rows.Count == 0)
                return;
            if (topicSheets == null || topicSheets.Count == 0)
                return;
            if (string.IsNullOrWhiteSpace(ticketIdColumnName))
                throw new ArgumentException("La colonna dell'ID ticket non è valida.", nameof(ticketIdColumnName));

            int detailColumn = GetColumnNumber(data, "FOGLIO_DETTAGLIO");
            int ticketColumn = GetColumnNumber(data, ticketIdColumnName);
            if (detailColumn == 0 || ticketColumn == 0)
                return;

            // I fogli tematici hanno ID_TICKET nella prima colonna (vedi TicketTopicSheetBase.CoreColumns).
            const string targetTicketColumn = "A";
            string ticketColumnLetter = GetColumnLetter(ticketColumn);
            for (int rowIndex = 2; rowIndex <= data.Rows.Count + 1; rowIndex++)
            {
                string requestedSheetName = worksheet.Cell(rowIndex, detailColumn).Value.ToString().Trim();
                string ticketId = worksheet.Cell(rowIndex, ticketColumn).Value.ToString().Trim();
                if (string.IsNullOrWhiteSpace(requestedSheetName) ||
                    string.IsNullOrWhiteSpace(ticketId) ||
                    !topicSheets.TryGetValue(requestedSheetName, out string? actualSheetName))
                {
                    continue;
                }

                string escapedSheetName = actualSheetName.Replace("'", "''");
                string label = EscapeExcelFormulaValue(requestedSheetName);
                string ticketCell = $"${ticketColumnLetter}{rowIndex}";

                // MATCH individua la riga effettiva dell'ID nel foglio tematico: il link non porta
                // soltanto al foglio, ma al ticket specifico, anche se l'ordinamento del foglio cambia.
                worksheet.Cell(rowIndex, detailColumn).FormulaA1 =
                    $"IFERROR(HYPERLINK(\"#'{escapedSheetName}'!{targetTicketColumn}\"&MATCH({ticketCell},'{escapedSheetName}'!${targetTicketColumn}:${targetTicketColumn},0),\"{label}\"),\"{label}\")";
                worksheet.Cell(rowIndex, detailColumn).Style.Font.Underline = XLFontUnderlineValues.Single;
                worksheet.Cell(rowIndex, detailColumn).Style.Font.FontColor = XLColor.FromHtml("#0563C1");
            }
        }

        private static void ConfigureRules(IXLWorksheet worksheet, DataTable data)
        {
            if (data.Columns.Count == 0)
                return;

            SetWidth(worksheet, data, "CODICE_RISPOSTA", 18);
            SetWidth(worksheet, data, "CASO", 36);
            SetWidth(worksheet, data, "DECISIONE_PROPOSTA", 34);
            SetWidth(worksheet, data, "AZIONE_RICHIESTA", 48);
            SetWidth(worksheet, data, "RISCONTRO_BASE", 70);
            if (data.Rows.Count > 0)
            {
                IXLRange rows = worksheet.Range(2, 1, data.Rows.Count + 1, data.Columns.Count);
                rows.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                rows.Style.Alignment.WrapText = true;
            }
        }

        private static void ApplyEditableColumnStyle(
            IXLWorksheet worksheet,
            DataTable data,
            int lastRow,
            string columnName)
        {
            int columnNumber = GetColumnNumber(data, columnName);
            if (columnNumber == 0 || lastRow < 2)
                return;

            IXLRange range = worksheet.Range(2, columnNumber, lastRow, columnNumber);
            range.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
            range.Style.Font.FontColor = XLColor.FromHtml("#7F6000");
        }

        private static void ApplyListValidation(
            IXLWorksheet worksheet,
            DataTable data,
            int lastRow,
            string columnName,
            string values)
        {
            int columnNumber = GetColumnNumber(data, columnName);
            if (columnNumber == 0 || lastRow < 2)
                return;

            IXLDataValidation validation = worksheet
                .Range(2, columnNumber, lastRow, columnNumber)
                .CreateDataValidation();
            validation.List($"\"{values}\"", true);
        }

        private static void ApplyPriorityStyle(IXLWorksheet worksheet, DataTable data, int lastRow)
        {
            int priorityColumn = GetColumnNumber(data, "PRIORITA");
            int decisionColumn = GetColumnNumber(data, "DECISIONE_PROPOSTA");
            if (priorityColumn == 0 || lastRow < 2)
                return;

            for (int rowIndex = 2; rowIndex <= lastRow; rowIndex++)
            {
                string priority = worksheet.Cell(rowIndex, priorityColumn).Value.ToString().Trim();
                XLColor color = priority switch
                {
                    "P1" => XLColor.FromHtml("#B6D7A8"),
                    "P2" => XLColor.FromHtml("#D9EAD3"),
                    "P3" => XLColor.FromHtml("#FCE5CD"),
                    _ => XLColor.FromHtml("#D9D9D9")
                };

                worksheet.Cell(rowIndex, priorityColumn).Style.Fill.BackgroundColor = color;
                worksheet.Cell(rowIndex, priorityColumn).Style.Font.Bold = true;
                if (decisionColumn > 0)
                    worksheet.Cell(rowIndex, decisionColumn).Style.Fill.BackgroundColor = color;
            }
        }

        private static void ApplyClosureStyle(IXLWorksheet worksheet, DataTable data, int lastRow)
        {
            int closableColumn = GetColumnNumber(data, "CHIUDIBILE");
            int closureStatusColumn = GetColumnNumber(data, "STATO_CHIUDIBILITA");
            int conditionColumn = GetColumnNumber(data, "CONDIZIONE_RISOLTA");
            if (closableColumn == 0 || lastRow < 2)
                return;

            for (int rowIndex = 2; rowIndex <= lastRow; rowIndex++)
            {
                string closable = worksheet.Cell(rowIndex, closableColumn).Value.ToString().Trim();
                XLColor color = closable switch
                {
                    "SI" => XLColor.FromHtml("#93C47D"),
                    "PROBABILE" => XLColor.FromHtml("#D9EAD3"),
                    "NO" => XLColor.FromHtml("#FCE5CD"),
                    _ => XLColor.FromHtml("#D9D9D9")
                };

                worksheet.Cell(rowIndex, closableColumn).Style.Fill.BackgroundColor = color;
                worksheet.Cell(rowIndex, closableColumn).Style.Font.Bold = true;
                if (closureStatusColumn > 0)
                {
                    worksheet.Cell(rowIndex, closureStatusColumn).Style.Fill.BackgroundColor = color;
                    worksheet.Cell(rowIndex, closureStatusColumn).Style.Font.Bold = true;
                }
                if (conditionColumn > 0 && !string.IsNullOrWhiteSpace(
                        worksheet.Cell(rowIndex, conditionColumn).Value.ToString()))
                {
                    worksheet.Cell(rowIndex, conditionColumn).Style.Fill.BackgroundColor = color;
                }
            }
        }

        private static void ApplyDecisionConfidenceStyle(IXLWorksheet worksheet, DataTable data, int lastRow)
        {
            int confidenceColumn = GetColumnNumber(data, "CONFIDENZA_DECISIONE");
            int contradictionColumn = GetColumnNumber(data, "CONTRADDIZIONI_RILEVATE");
            if (lastRow < 2)
                return;

            for (int rowIndex = 2; rowIndex <= lastRow; rowIndex++)
            {
                if (confidenceColumn > 0 &&
                    string.Equals(worksheet.Cell(rowIndex, confidenceColumn).Value.ToString().Trim(), "BASSA", StringComparison.OrdinalIgnoreCase))
                {
                    worksheet.Cell(rowIndex, confidenceColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4CCCC");
                    worksheet.Cell(rowIndex, confidenceColumn).Style.Font.Bold = true;
                }

                if (contradictionColumn > 0 &&
                    !string.IsNullOrWhiteSpace(worksheet.Cell(rowIndex, contradictionColumn).Value.ToString()))
                {
                    worksheet.Cell(rowIndex, contradictionColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
                }
            }
        }

        private static void ApplyDateFormat(
            IXLWorksheet worksheet,
            DataTable data,
            int lastRow,
            string columnName)
        {
            int columnNumber = GetColumnNumber(data, columnName);
            if (columnNumber == 0 || lastRow < 2)
                return;

            worksheet.Range(2, columnNumber, lastRow, columnNumber)
                .Style.DateFormat.Format = "dd/MM/yyyy";
        }

        private static void SetWidth(IXLWorksheet worksheet, DataTable data, string columnName, double width)
        {
            int columnNumber = GetColumnNumber(data, columnName);
            if (columnNumber > 0)
                worksheet.Column(columnNumber).Width = width;
        }

        private static void HideColumn(IXLWorksheet worksheet, DataTable data, string columnName)
        {
            int columnNumber = GetColumnNumber(data, columnName);
            if (columnNumber > 0)
                worksheet.Column(columnNumber).Hide();
        }

        private static int GetColumnNumber(DataTable data, string columnName)
        {
            return data.Columns.Contains(columnName)
                ? data.Columns[columnName]!.Ordinal + 1
                : 0;
        }

        private static string GetColumnLetter(int columnNumber)
        {
            string result = string.Empty;
            while (columnNumber > 0)
            {
                columnNumber--;
                result = (char)('A' + columnNumber % 26) + result;
                columnNumber /= 26;
            }
            return result;
        }

        private static string EscapeExcelFormulaValue(string value) =>
            (value ?? string.Empty).Replace("\"", "\"\"");

        private static int GetOperationalTopicOrder(ITicketTopicSheet sheet) => sheet.SheetName switch
        {
            "Pagamenti" => 10,
            "Benefici e importi" => 11,
            "Alloggio" => 12,
            "Mensa" => 13,
            "Documenti e permessi" => 14,
            "Carriere" => 15,
            "Graduatorie" => 16,
            "Mobilita" => 17,
            "IBAN" => 18,
            "Portale e accesso" => 19,
            "Altro da verificare" => 20,
            _ => 99
        };

        private static string GetOperationalTopicSheetName(string sheetName) => sheetName switch
        {
            "Pagamenti" => "10_Pagamenti",
            "Benefici e importi" => "11_Benefici e importi",
            "Alloggio" => "12_Alloggio",
            "Mensa" => "13_Mensa",
            "Documenti e permessi" => "14_Documenti e permessi",
            "Carriere" => "15_Carriere",
            "Graduatorie" => "16_Graduatorie",
            "Mobilita" => "17_Mobilita",
            "IBAN" => "18_IBAN",
            "Portale e accesso" => "19_Portale e accesso",
            "Altro da verificare" => "20_Altro da verificare",
            _ => sheetName
        };

        private static string GetUniqueSheetName(XLWorkbook workbook, string requestedName)
        {
            string sanitized = string.Concat(
                (requestedName ?? "Foglio")
                .Where(character => !"[]:*?/\\".Contains(character)));

            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "Foglio";
            if (sanitized.Length > 31)
                sanitized = sanitized[..31];

            string candidate = sanitized;
            int suffix = 2;
            while (workbook.Worksheets.Any(sheet =>
                       string.Equals(sheet.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                string suffixText = $" {suffix++}";
                int maxBaseLength = 31 - suffixText.Length;
                candidate = sanitized[..Math.Min(sanitized.Length, maxBaseLength)] + suffixText;
            }

            return candidate;
        }
    }
}
