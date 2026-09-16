using ClosedXML.Excel;

namespace ProcedureNet7;

internal sealed record MotivoEsclusionePagamento(
    string Motivo, string Dettaglio, double? Importo = null);

// Snapshot captured before the student is removed or the next payment cycle starts.
internal sealed record StudenteEsclusoPagamento(
    string CodFiscale, string NumeroDomanda, string Cognome, string Nome,
    string CodEnte, string Impegno, string Fase, MotivoEsclusionePagamento Causa);

internal sealed record ContestoEsclusioniPagamento(
    string AnnoAccademico, string Beneficio, string Pagamento, string CodPagamento,
    string Categoria, string DataRiferimento, DateTime ElaboratoIl, bool Completata);

internal static class PagamentoEsclusioniWorkbook
{
    internal static XLWorkbook Create(IEnumerable<StudenteEsclusoPagamento> esclusioni,
        ContestoEsclusioniPagamento contesto)
    {
        var gruppi = esclusioni
            .Where(e => !string.IsNullOrWhiteSpace(e.CodFiscale))
            .Distinct()
            .GroupBy(e => e.CodFiscale, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.First().Cognome, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.First().Nome, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var workbook = new XLWorkbook();
        var elenco = workbook.AddWorksheet("Studenti esclusi");
        Header(elenco, "Studenti esclusi dall'elaborazione", contesto, 6);
        elenco.Cell(5, 1).Value = $"Studenti esclusi: {gruppi.Count}. Il motivo principale è il primo rilevato; il dettaglio riporta anche le altre cause accertate.";
        elenco.Range(5, 1, 5, 6).Merge();
        elenco.Row(5).Height = 30;
        string[] titoli = { "Cognome", "Nome", "Codice fiscale", "Numero domanda", "Motivo", "Dettaglio" };
        SetTitles(elenco, titoli);
        elenco.Columns(3, 4).Style.NumberFormat.Format = "@";
        int row = 8;
        foreach (var gruppo in gruppi)
        {
            var s = gruppo.First();
            var cause = gruppo.Select(e => e.Causa).Distinct().ToList();
            elenco.Cell(row, 1).Value = s.Cognome;
            elenco.Cell(row, 2).Value = s.Nome;
            elenco.Cell(row, 3).Value = s.CodFiscale;
            elenco.Cell(row, 4).Value = s.NumeroDomanda;
            elenco.Cell(row, 5).Value = s.Causa.Motivo;
            elenco.Cell(row, 6).Value = string.Join("\n", cause.Select((c, i) =>
                cause.Count == 1 ? c.Dettaglio : $"{i + 1}. {c.Motivo}: {c.Dettaglio}"));
            row++;
        }
        Finish(elenco, row - 1, new double[] { 22, 20, 21, 18, 36, 82 });

        var riepilogo = workbook.AddWorksheet("Riepilogo");
        Header(riepilogo, "Riepilogo delle esclusioni", contesto, 2);
        riepilogo.Cell(5, 1).Value = "Studenti esclusi (senza duplicati)";
        riepilogo.Cell(5, 2).Value = gruppi.Count;
        SetTitles(riepilogo, new[] { "Motivo principale", "Numero studenti" });
        row = 8;
        foreach (var motivo in gruppi.Select(g => g.First().Causa)
                     .GroupBy(c => c.Motivo).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            riepilogo.Cell(row, 1).Value = motivo.Key;
            riepilogo.Cell(row, 2).Value = motivo.Count();
            row++;
        }
        Finish(riepilogo, row - 1, new double[] { 76, 22 }, freezeIdentificativi: false);

        var tecnico = workbook.AddWorksheet("Dettagli tecnici");
        Header(tecnico, "Riferimenti per gli approfondimenti", contesto, 9);
        tecnico.Range(5, 1, 5, 9).Merge().Value =
            "Una riga per causa. Importo vuoto = non calcolato o non pertinente; zero = importo effettivamente rilevato. Sono riportati solo i controlli eseguiti.";
        tecnico.Row(5).Height = 30;
        SetTitles(tecnico, new[] { "Codice fiscale", "Numero domanda", "Codice ente", "Impegno", "Controllo", "Motivo", "Importo rilevato (€)", "Codice pagamento", "Categoria pagamento" });
        tecnico.Columns(1, 4).Style.NumberFormat.Format = "@";
        row = 8;
        foreach (var e in gruppi.SelectMany(g => g))
        {
            tecnico.Cell(row, 1).Value = e.CodFiscale;
            tecnico.Cell(row, 2).Value = e.NumeroDomanda;
            tecnico.Cell(row, 3).Value = e.CodEnte;
            tecnico.Cell(row, 4).Value = e.Impegno;
            tecnico.Cell(row, 5).Value = e.Fase;
            tecnico.Cell(row, 6).Value = e.Causa.Motivo;
            if (e.Causa.Importo.HasValue)
                tecnico.Cell(row, 7).Value = e.Causa.Importo.Value;
            tecnico.Cell(row, 8).Value = contesto.CodPagamento;
            tecnico.Cell(row, 9).Value = contesto.Categoria;
            row++;
        }
        tecnico.Column(7).Style.NumberFormat.Format = "#,##0.00;[Red]-#,##0.00;0.00";
        Finish(tecnico, row - 1, new double[] { 21, 18, 15, 18, 45, 42, 23, 20, 22 });
        return workbook;
    }

    private static void Header(IXLWorksheet sheet, string title, ContestoEsclusioniPagamento c, int columns)
    {
        sheet.Style.Font.FontName = "Calibri";
        sheet.Style.Font.FontSize = 11;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Style.Alignment.WrapText = true;
        sheet.ShowGridLines = false;
        sheet.Range(1, 1, 1, columns).Merge().Value = title;
        sheet.Range(1, 1, 1, columns).Style.Font.FontColor = XLColor.FromHtml("#17365D");
        sheet.Range(1, 1, 1, columns).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, columns).Style.Font.Bold = true;
        sheet.Row(1).Height = 34;
        sheet.Range(2, 1, 2, columns).Merge().Value = $"Anno accademico {c.AnnoAccademico}. {c.Beneficio}. {c.Pagamento}.";
        sheet.Row(2).Height = 30;
        sheet.Range(3, 1, 3, columns).Merge().Value = $"Data di riferimento: {c.DataRiferimento}. Elaborato il {c.ElaboratoIl:dd/MM/yyyy HH:mm}.";
        sheet.Range(4, 1, 4, columns).Merge().Value = c.Completata
            ? "Sono riportate le cause accertate dai controlli eseguiti per questo pagamento."
            : "ELABORAZIONE INTERROTTA: elenco parziale delle esclusioni rilevate prima dell'interruzione.";
        sheet.Row(4).Height = 30;
        if (!c.Completata) sheet.Cell(4, 1).Style.Font.FontColor = XLColor.DarkRed;
    }

    private static void SetTitles(IXLWorksheet sheet, string[] titles)
    {
        for (int i = 0; i < titles.Length; i++) sheet.Cell(7, i + 1).Value = titles[i];
    }

    private static void Finish(IXLWorksheet sheet, int lastRow, double[] widths, bool freezeIdentificativi = true)
    {
        for (int i = 0; i < widths.Length; i++) sheet.Column(i + 1).Width = widths[i];
        sheet.Row(7).Height = 32;
        sheet.Range(7, 1, Math.Max(8, lastRow), widths.Length).CreateTable().Theme = XLTableTheme.TableStyleMedium2;
        sheet.SheetView.FreezeRows(7);
        if (freezeIdentificativi) sheet.SheetView.FreezeColumns(2);
        // Estimate wrapped line count as well as explicit newlines; do not truncate explanations.
        for (int row = 8; row <= lastRow; row++)
        {
            int lines = Enumerable.Range(1, widths.Length).Max(col =>
                sheet.Cell(row, col).GetString().Split('\n')
                    .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (widths[col - 1] * 0.85)))));
            sheet.Row(row).Height = Math.Min(409, Math.Max(34, lines * 16 + 8));
        }
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(7, 7);
    }
}
