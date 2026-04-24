using System;
using System.Runtime.InteropServices;
using Word = Microsoft.Office.Interop.Word;

namespace ProcedureNet7
{
    internal sealed class WordPdfService : IDisposable
    {
        private Word.Application _app;

        public WordPdfService()
        {
            _app = new Word.Application();
            _app.Visible = false;
            _app.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
            _app.ScreenUpdating = false;
        }

        // =====================================================
        // CONVERSIONE DOCX -> PDF
        // =====================================================
        public void ConvertToPdf(
            string docxPath,
            string pdfPath)
        {
            Word.Document doc = null;

            try
            {
                doc = _app.Documents.Open(
                    FileName: docxPath,
                    ReadOnly: true,
                    Visible: false);

                doc.ExportAsFixedFormat(
                    OutputFileName: pdfPath,
                    ExportFormat:
                        Word.WdExportFormat
                            .wdExportFormatPDF);
            }
            finally
            {
                if (doc != null)
                {
                    doc.Close(false);
                    Marshal.ReleaseComObject(doc);
                }
            }
        }

        // =====================================================
        // CHIUSURA
        // =====================================================
        public void Dispose()
        {
            try
            {
                if (_app != null)
                {
                    _app.Quit(false);
                    Marshal.ReleaseComObject(_app);
                    _app = null;
                }
            }
            catch
            {
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}