using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iText.Kernel.Pdf;
using iText.Kernel.Utils;

namespace ProcedureNet7
{
    internal class GenerazioneLettereRevoche
    {
        private readonly string _rootFolder;

        public GenerazioneLettereRevoche(string rootFolder)
        {
            _rootFolder = rootFolder;
        }

        // =====================================================
        // AVVIO
        // =====================================================
        public void RunProcedure()
        {
            if (string.IsNullOrWhiteSpace(_rootFolder))
                throw new Exception("Cartella non selezionata.");

            if (!Directory.Exists(_rootFolder))
                throw new Exception("Cartella inesistente.");

            Logger.LogInfo(50, "Avvio generazione lettere revoche...");

            var folders = Directory
                .GetDirectories(_rootFolder, "*", SearchOption.AllDirectories)
                .Where(IsValidRevocaFolder)
                .ToList();

            if (folders.Count == 0)
                throw new Exception("Nessuna cartella valida trovata.");

            using WordPdfService wordService =
                new WordPdfService();

            foreach (string folder in folders)
            {
                try
                {
                    ProcessFolder(
                        folder,
                        wordService);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        100,
                        $"Errore cartella {folder}: {ex.Message}");
                }
            }

            Logger.LogInfo(100, "Fine lavorazione.");
        }

        // =====================================================
        // CARTELLA
        // =====================================================
        private void ProcessFolder(
            string folder,
            WordPdfService wordService)
        {
            string excel = GetMainExcel(folder);
            string template = GetTemplate(folder);
            string pagoPaFolder = Path.Combine(folder, "PAGOPA");

            DataTable dt = ReadExcel(excel);

            Logger.LogInfo(30, $"Elaboro: {folder}");

            foreach (DataRow row in dt.Rows)
            {
                try
                {
                    ProcessStudent(
                        row,
                        folder,
                        template,
                        pagoPaFolder,
                        wordService);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        100,
                        $"Errore studente: {ex.Message}");
                }
            }
        }

        // =====================================================
        // STUDENTE
        // =====================================================
        private void ProcessStudent(
            DataRow row,
            string currentFolder,
            string template,
            string pagoPaFolder,
            WordPdfService wordService)
        {
            string nome = S(row, "Nome");
            string cognome = S(row, "Cognome");
            string cf = S(row, "Cod_fiscale")
                .Trim()
                .ToUpper();

            if (cf == "")
                return;

            string pec = S(row, "Indirizzo_PEC");

            bool isPec =
                !string.IsNullOrWhiteSpace(pec);

            string macroFolder =
                isPec ? "PEC" : "Raccomandate";

            string studentFolder = Path.Combine(
                currentFolder,
                "Lettere",
                macroFolder,
                Safe($"{cognome} {nome}"));

            Directory.CreateDirectory(studentFolder);

            string tempDocx = Path.Combine(
                studentFolder,
                "temp.docx");

            string tempPdf = Path.Combine(
                studentFolder,
                "temp.pdf");

            string finalPdf = Path.Combine(
                studentFolder,
                $"Revoca {cognome} {nome}.pdf");

            File.Copy(template, tempDocx, true);

            ReplaceInWord(
                tempDocx,
                row,
                isPec);

            wordService.ConvertToPdf(
                tempDocx,
                tempPdf);

            // ============================================
            // TROVA TUTTI I BOLLETTINI
            // ============================================
            List<string> bollettini =
                FindPagoPaByCF(
                    pagoPaFolder,
                    cf);

            // ============================================
            // MERGE
            // ============================================
            if (bollettini.Count > 0)
            {
                MergePdfMultiplo(
                    tempPdf,
                    bollettini,
                    finalPdf);
            }
            else
            {
                File.Copy(
                    tempPdf,
                    finalPdf,
                    true);
            }

            DeleteIfExists(tempDocx);
            DeleteIfExists(tempPdf);

            Logger.LogInfo(
                10,
                $"Creato PDF {cognome} {nome}");
        }

        // =====================================================
        // WORD MERGE
        // =====================================================
        private void ReplaceInWord(
            string file,
            DataRow row,
            bool isPec)
        {
            using WordprocessingDocument doc =
                WordprocessingDocument.Open(
                    file,
                    true);

            var texts =
                doc.MainDocumentPart
                   .Document
                   .Descendants<Text>();

            foreach (var t in texts)
            {
                Replace(t, "«Nome»", S(row, "Nome"));
                Replace(t, "«Cognome»", S(row, "Cognome"));
                Replace(t, "«Cod_fiscale»", S(row, "Cod_fiscale"));

                Replace(t,
                    "«Recupero_borsa_di_studio»",
                    Euro(S(row,
                        "Recupero_borsa_di_studio")));

                Replace(t,
                    "«Recupero_servizio_abitativo»",
                    Euro(S(row,
                        "Recupero_servizio_abitativo")));

                if (isPec)
                {
                    Replace(t, "«Indirizzo_residenza»", "");
                    Replace(t, "«Civico_residenza»", "");
                    Replace(t, "«CAP_residenza»", "");
                    Replace(t, "«comune_residenza»", "");
                    Replace(t, "«provincia_residenza»", "");

                    Replace(t,
                        "«Indirizzo_PEC»",
                        S(row, "Indirizzo_PEC"));
                }
                else
                {
                    Replace(t,
                        "«Indirizzo_residenza»",
                        S(row, "Indirizzo_residenza"));

                    Replace(t,
                        "«Civico_residenza»",
                        S(row, "Civico_residenza"));

                    Replace(t,
                        "«CAP_residenza»",
                        S(row, "CAP_residenza"));

                    Replace(t,
                        "«comune_residenza»",
                        S(row, "comune_residenza"));

                    Replace(t,
                        "«provincia_residenza»",
                        S(row, "provincia_residenza"));

                    Replace(t, "«Indirizzo_PEC»", "");
                }
            }

            doc.MainDocumentPart.Document.Save();
        }

        private void Replace(
            Text t,
            string tag,
            string value)
        {
            if (t.Text.Contains(tag))
                t.Text = t.Text.Replace(tag, value);
        }

        // =====================================================
        // MERGE PDF MULTIPLO
        // =====================================================
        private void MergePdfMultiplo(
            string firstPdf,
            List<string> allegati,
            string output)
        {
            using PdfDocument dest =
                new PdfDocument(
                    new PdfWriter(output));

            PdfMerger merger =
                new PdfMerger(dest);

            // ============================================
            // LETTERA PRINCIPALE
            // ============================================
            using (PdfDocument src =
                new PdfDocument(
                    new PdfReader(firstPdf)))
            {
                merger.Merge(
                    src,
                    1,
                    src.GetNumberOfPages());
            }

            // ============================================
            // ALLEGATI PAGOPA
            // ============================================
            foreach (string file in allegati)
            {
                using PdfDocument src =
                    new PdfDocument(
                        new PdfReader(file));

                merger.Merge(
                    src,
                    1,
                    src.GetNumberOfPages());
            }
        }

        // =====================================================
        // CERCA TUTTI I PAGOPA DEL CF
        // =====================================================
        private List<string> FindPagoPaByCF(
            string folder,
            string cf)
        {
            return Directory
                .GetFiles(folder, "*.pdf")
                .Where(x =>
                    Path.GetFileName(x)
                        .ToUpper()
                        .Contains(cf))
                .OrderBy(x => x)
                .ToList();
        }

        // =====================================================
        // EXCEL
        // =====================================================
        private DataTable ReadExcel(
            string path)
        {
            DataTable dt = new();

            using var wb =
                new XLWorkbook(path);

            var ws =
                wb.Worksheet(1);

            bool first = true;

            foreach (var row in ws.RowsUsed())
            {
                if (first)
                {
                    foreach (var c in row.Cells())
                        dt.Columns.Add(
                            c.GetString());

                    first = false;
                }
                else
                {
                    DataRow dr =
                        dt.NewRow();

                    for (int i = 0;
                        i < dt.Columns.Count;
                        i++)
                    {
                        dr[i] =
                            row.Cell(i + 1)
                               .Value
                               .ToString();
                    }

                    dt.Rows.Add(dr);
                }
            }

            return dt;
        }

        // =====================================================
        // HELPERS
        // =====================================================
        private bool IsValidRevocaFolder(
            string folder)
        {
            return GetMainExcel(folder) != ""
                && GetTemplate(folder) != ""
                && Directory.Exists(
                    Path.Combine(folder, "PAGOPA"));
        }

        private string GetMainExcel(string folder)
        {
            return Directory
                .GetFiles(folder, "*.xlsx")
                .FirstOrDefault(x =>
                {
                    var fileName = Path.GetFileName(x);

                    return !fileName.Contains(
                               "allegato",
                               StringComparison.OrdinalIgnoreCase)
                        && !fileName.Contains(
                               "tracciato",
                               StringComparison.OrdinalIgnoreCase);
                })
                ?? "";
        }

        private string GetTemplate(string folder)
        {
            return Directory
                .GetFiles(folder, "*.docx")
                .FirstOrDefault(x =>
                    Path.GetFileName(x)
                        .Contains(
                            "SCHEMA",
                            StringComparison.OrdinalIgnoreCase))
                ?? "";
        }

        private string S(
            DataRow r,
            string col)
        {
            if (!r.Table.Columns.Contains(col))
                return "";

            if (r[col] == DBNull.Value)
                return "";

            return r[col]
                ?.ToString()
                ?.Trim()
                ?? "";
        }

        private string Safe(
            string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            return s.Trim();
        }

        private string Euro(
            string val)
        {
            if (decimal.TryParse(
                val,
                NumberStyles.Any,
                CultureInfo.GetCultureInfo("it-IT"),
                out decimal d))
            {
                return d.ToString(
                    "C",
                    CultureInfo.GetCultureInfo("it-IT"));
            }

            return val;
        }

        private void DeleteIfExists(
            string file)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}