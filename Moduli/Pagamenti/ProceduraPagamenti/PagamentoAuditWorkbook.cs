using ClosedXML.Excel;
using System.Data;
using System.Globalization;

namespace ProcedureNet7;

internal sealed record ImportiAuditPagamento(double Lordo, double Netto);

internal static class PagamentoAuditWorkbook
{
    private const string InFlusso = "Inserito nel flusso";
    private const string Escluso = "Escluso dall'elaborazione";
    private const string NonInFlusso = "Non inserito nel flusso";
    private const string Interrotto = "Elaborazione interrotta";

    internal static XLWorkbook Create(DataTable dati,
        IEnumerable<StudenteEsclusoPagamento> esclusioni,
        IReadOnlyDictionary<string, List<string>> flussi,
        IReadOnlyDictionary<string, ImportiAuditPagamento> importi,
        ContestoEsclusioniPagamento contesto,
        IReadOnlyDictionary<string, MotivoEsclusionePagamento>? motiviNonFlusso = null)
    {
        var causePerStudente = esclusioni.Distinct()
            .ToLookup(e => e.CodFiscale, StringComparer.OrdinalIgnoreCase);
        var conteggi = new Dictionary<string, int>
        {
            [InFlusso] = 0, [Escluso] = 0, [NonInFlusso] = 0, [Interrotto] = 0
        };
        double totaleFlussi = 0;
        int flussiSenzaImporto = 0;
        var workbook = new XLWorkbook();
        var audit = workbook.AddWorksheet("Audit completo");
        Header(audit, "Audit completo del pagamento", contesto, 8);
        audit.Range(5, 1, 5, 8).Merge().Value =
            "Tutti gli studenti selezionati all'avvio dei controlli, anche senza impegno. Per gli esclusi: dati acquisiti fino all'esclusione. Importi lordo/netto vuoti = calcolo non eseguito.";
        audit.Row(5).Height = 32;

        // Retain every column of the per-impegno audit, with identity and outcome first.
        string[] identificativi = { "Cognome", "Nome", "CodFiscale", "NumDomanda", "Impegno" };
        string[] esiti = { "Esito elaborazione", "Motivo", "Dettaglio", "Fase di esclusione", "Calcolo importi", "File flusso" };
        var colonneDati = dati.Columns.Cast<DataColumn>()
            .Select(c => c.ColumnName).Except(identificativi).ToArray();
        var titoli = identificativi.Concat(esiti).Concat(colonneDati).ToArray();
        SetTitles(audit, titoli);
        var widths = titoli.Select(t => t switch
        {
            "Cognome" or "Nome" => 22d,
            "CodFiscale" => 22d,
            "NumDomanda" or "Impegno" => 18d,
            "Esito elaborazione" => 28d,
            "Motivo" => 40d,
            "Dettaglio" => 80d,
            "Fase di esclusione" or "File flusso" => 45d,
            _ when t.EndsWith("_Sintesi") => 60d,
            _ => 25d
        }).ToArray();
        int row = 8;
        foreach (DataRow s in dati.Rows.Cast<DataRow>()
                     .OrderBy(s => Text(s, "Cognome"), StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(s => Text(s, "Nome"), StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(s => Text(s, "CodFiscale"), StringComparer.OrdinalIgnoreCase))
        {
            string cf = Text(s, "CodFiscale");
            var esclusioniStudente = causePerStudente[cf].ToList();
            var cause = esclusioniStudente.Select(e => e.Causa).Distinct().ToList();
            bool inFlusso = flussi.TryGetValue(cf, out var files) && files.Count > 0;
            MotivoEsclusionePagamento? mancatoFlusso = null;
            bool motivoRegistrato = false;
            if (cause.Count == 0 && !inFlusso)
            {
                motivoRegistrato = motiviNonFlusso != null && motiviNonFlusso.TryGetValue(cf, out mancatoFlusso);
                mancatoFlusso ??= PagamentoFlussoRules.MancatoInserimento(contesto.Completata);
            }
            string esito = cause.Count > 0 ? Escluso : inFlusso ? InFlusso
                : contesto.Completata ? NonInFlusso : Interrotto;
            conteggi[esito]++;
            bool calcolato = importi.TryGetValue(cf, out var calcolo);
            if (esito == InFlusso)
            {
                if (calcolato) totaleFlussi += calcolo!.Netto;
                else flussiSenzaImporto++;
            }

            for (int col = 0; col < identificativi.Length; col++)
                SetText(audit.Cell(row, col + 1), Text(s, identificativi[col]));
            audit.Cell(row, 6).Value = esito;
            if (cause.Count > 0) audit.Cell(row, 7).Value = cause[0].Motivo;
            else if (mancatoFlusso != null) audit.Cell(row, 7).Value = mancatoFlusso.Motivo;
            audit.Cell(row, 8).Value = cause.Count > 0
                ? string.Join("\n", cause.Select((c, i) => cause.Count == 1
                    ? c.Dettaglio : $"{i + 1}. {c.Motivo}: {c.Dettaglio}"))
                : inFlusso ? "Lo studente è presente nei file di pagamento generati."
                : mancatoFlusso!.Dettaglio + (!contesto.Completata && motivoRegistrato
                    ? " L'elaborazione complessiva si è interrotta; la causa indicata era già stata rilevata durante la generazione dei flussi."
                    : "");
            if (esclusioniStudente.Count > 0)
                audit.Cell(row, 9).Value = string.Join("; ", esclusioniStudente.Select(e => e.Fase).Distinct());
            else if (motivoRegistrato) audit.Cell(row, 9).Value = "Generazione flussi";
            audit.Cell(row, 10).Value = calcolato ? "Eseguito" : "Non eseguito";
            if (inFlusso) audit.Cell(row, 11).Value = string.Join("\n", files!.Distinct());

            for (int col = 0; col < colonneDati.Length; col++)
            {
                string nome = colonneDati[col];
                var cell = audit.Cell(row, col + 12);
                if (nome is "ImportoLordo" or "ImportoNetto")
                {
                    if (calcolato) cell.Value = nome == "ImportoLordo" ? calcolo!.Lordo : calcolo!.Netto;
                    cell.Style.NumberFormat.Format = "#,##0.00;[Red]-#,##0.00;0.00";
                }
                else if ((nome.StartsWith("Importo") || nome.StartsWith("NumAssegnazioni")
                          || nome is "NumDetrazioni" or "NumReversali" or "NumPagamentiEffettuati")
                         && double.TryParse(Text(s, nome), NumberStyles.Number, CultureInfo.CurrentCulture, out double valore))
                {
                    cell.Value = valore;
                    cell.Style.NumberFormat.Format = nome.StartsWith("Importo") ? "#,##0.00;[Red]-#,##0.00;0.00" : "0";
                }
                else SetText(cell, Text(s, nome));
            }
            row++;
        }
        Finish(audit, row - 1, widths, 4);

        var riepilogo = workbook.AddWorksheet("Riepilogo");
        Header(riepilogo, "Riepilogo del pagamento", contesto, 2);
        riepilogo.Cell(5, 1).Value = "Studenti selezionati all'avvio dei controlli";
        riepilogo.Cell(5, 2).Value = dati.Rows.Count;
        SetTitles(riepilogo, new[] { "Esito elaborazione", "Numero studenti" });
        row = 8;
        foreach (var (esito, numero) in conteggi)
        {
            riepilogo.Cell(row, 1).Value = esito;
            riepilogo.Cell(row++, 2).Value = numero;
        }
        Finish(riepilogo, row - 1, new double[] { 76, 24 }, 0);
        riepilogo.Cell(13, 1).Value = "Importo netto degli studenti inseriti nei flussi (€)";
        if (flussiSenzaImporto == 0) riepilogo.Cell(13, 2).Value = Math.Round(totaleFlussi, 2);
        else riepilogo.Cell(13, 2).Value = "Non disponibile";
        riepilogo.Cell(13, 2).Style.NumberFormat.Format = "#,##0.00;[Red]-#,##0.00;0.00";
        riepilogo.Row(13).Height = 34;
        riepilogo.Range(15, 1, 15, 2).Merge().Value =
            "L'inserimento nel flusso indica la generazione del file; non attesta l'accredito allo studente o la conferma delle registrazioni nel database.";
        riepilogo.Row(15).Height = 44;
        return workbook;
    }

    private static string Text(DataRow row, string col) => Convert.ToString(row[col]) ?? "";

    private static void SetText(IXLCell cell, string value)
    {
        if (value.Length > 0) cell.Value = value;
        cell.Style.NumberFormat.Format = "@";
    }

    private static void Header(IXLWorksheet sheet, string title, ContestoEsclusioniPagamento c, int columns)
    {
        sheet.Style.Font.FontName = "Calibri";
        sheet.Style.Font.FontSize = 11;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Style.Alignment.WrapText = true;
        sheet.ShowGridLines = false;
        sheet.Range(1, 1, 1, columns).Merge().Value = title;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.FromHtml("#17365D"));
        sheet.Row(1).Height = 34;
        sheet.Range(2, 1, 2, columns).Merge().Value =
            $"A.A. {c.AnnoAccademico}. {c.Beneficio}. {c.Pagamento} ({c.CodPagamento}). Categoria {c.Categoria}.";
        sheet.Row(2).Height = 32;
        sheet.Range(3, 1, 3, columns).Merge().Value =
            $"Data di riferimento: {c.DataRiferimento}. Elaborato il {c.ElaboratoIl:dd/MM/yyyy HH:mm}.";
        sheet.Row(3).Height = 30;
        sheet.Range(4, 1, 4, columns).Merge().Value = c.Completata
            ? "Controlli completati. Una riga per ogni studente selezionato per questo pagamento."
            : "ELABORAZIONE INTERROTTA: tutti gli studenti selezionati, con i dati disponibili prima dell'interruzione.";
        sheet.Row(4).Height = 32;
        if (!c.Completata) sheet.Cell(4, 1).Style.Font.FontColor = XLColor.DarkRed;
    }

    private static void SetTitles(IXLWorksheet sheet, string[] titles)
    {
        for (int i = 0; i < titles.Length; i++) sheet.Cell(7, i + 1).Value = titles[i];
    }

    private static void Finish(IXLWorksheet sheet, int lastRow, double[] widths, int frozenColumns)
    {
        for (int i = 0; i < widths.Length; i++) sheet.Column(i + 1).Width = widths[i];
        sheet.Row(7).Height = 34;
        sheet.Range(7, 1, Math.Max(8, lastRow), widths.Length).CreateTable().Theme = XLTableTheme.TableStyleMedium2;
        sheet.SheetView.FreezeRows(7);
        if (frozenColumns > 0) sheet.SheetView.FreezeColumns(frozenColumns);
        for (int row = 8; row <= lastRow; row++)
        {
            int lines = Enumerable.Range(1, widths.Length).Max(col => sheet.Cell(row, col).GetString().Split('\n')
                .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (widths[col - 1] * 0.85)))));
            sheet.Row(row).Height = Math.Min(409, Math.Max(34, lines * 16 + 8));
        }
    }
}
