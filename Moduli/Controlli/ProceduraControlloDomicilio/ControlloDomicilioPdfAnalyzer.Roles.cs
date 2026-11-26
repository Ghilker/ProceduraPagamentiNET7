using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace ProcedureNet7
{
    internal static partial class ControlloDomicilioPdfAnalyzer
    {
        private const string FiscalCodePattern = @"[A-Z]{6}[0-9LMNPQRSTUV]{2}[ABCDEHLMPRST][0-9LMNPQRSTUV]{2}[A-Z][0-9LMNPQRSTUV]{3}[A-Z]";

        private static bool IsRliTaxpayerStatement(string text) =>
            Regex.IsMatch(text, RliFormHeading, ReceiptOptions) &&
            Regex.IsMatch(text, @"\b(?:utente|del\s+contribuente|elenco\s+delle\s+controparti)\b", ReceiptOptions);

        private static List<FiscalCodeRole> ReadRliTaxpayerRole(PdfPage page, int pageCount, DomicilioPdfDocumentType expectedType)
        {
            var roles = new List<FiscalCodeRole>();
            if (pageCount != 1 || expectedType != DomicilioPdfDocumentType.Contratto) return roles;
            // This statement prints UTENTE (portal user), DEL CONTRIBUENTE (the subject of
            // the statement), and separate counterparties. The PDF paints its header last.
            var visualText = PdfTextExtractor.GetTextFromPage(page, new LocationTextExtractionStrategy());
            var forms = ReceiptMatches(visualText, RliFormHeading);
            var taxpayers = ReceiptMatches(visualText, @"\bDEL[ \t]+CONTRIBUENTE\b");
            var roleLabels = ReceiptMatches(visualText, @"\bRUOLO\b");
            var counterparties = ReceiptMatches(visualText, @"\bELENCO[ \t]+DELLE[ \t]+CONTROPARTI\b");
            if (forms.Count != 1 || taxpayers.Count != 1 || roleLabels.Count != 1 || counterparties.Count != 1 ||
                taxpayers[0].Index >= forms[0].Index || roleLabels[0].Index <= forms[0].Index ||
                roleLabels[0].Index >= counterparties[0].Index || !IsRegisteredLeaseForm(visualText))
                return roles;
            var taxpayer = Regex.Match(visualText, @"(?m)^[ \t]*DEL[ \t]+CONTRIBUENTE[ \t]+(" +
                FiscalCodePattern + @")\b[^\r\n]*", ReceiptOptions);
            var role = Regex.Match(visualText, @"(?m)^[ \t]*NEGOZIO[ \t]+LOCAZIONE\b[^\r\n]*?\bRUOLO[ \t]+(CONDUTTORE|LOCATORE)\b", ReceiptOptions);
            if (taxpayer.Success && role.Success && ReceiptMatches(taxpayer.Value, FiscalCodePattern).Count == 1)
                roles.Add(new(taxpayer.Groups[1].Value.ToUpperInvariant(),
                    role.Groups[1].Value.Equals("CONDUTTORE", StringComparison.OrdinalIgnoreCase) ? "B" : "A"));
            return roles;
        }

        private static bool HasNumberedRoleHeadings(string text) =>
            text.Contains("RUOLO", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("FISCALE", StringComparison.OrdinalIgnoreCase) || text.Contains("L!'ISCALE", StringComparison.OrdinalIgnoreCase)) &&
            (Regex.IsMatch(text, @"\bN[.°º]?\s+CODICE\s+FISCALE\s+RUOLO\b", ReceiptOptions) ||
             Regex.Matches(text, @"\bRUOLO\b", ReceiptOptions).Count >= 2 &&
             Regex.IsMatch(text, @"\bN[.°º]|\bN\s+CODICE", ReceiptOptions));

        private sealed record TableWord(string Text, float Left, float Right, float MiddleY, float Height, bool DamagedFiscalHeading)
        {
            public float MiddleX => (Left + Right) / 2;
        }

        private static TableWord[] AlignDoubleHeader(TableWord[] words)
        {
            var role = words.Where(word => word.Text == "RUOLO").OrderBy(word => word.Left).ToArray();
            if (role.Length != 2 || role[1].Left <= role[0].Right) return words;
            var margin = Math.Min(role[0].Height, role[1].Height);
            var headers = words.Where(word => word.MiddleY >= Math.Min(role[0].MiddleY, role[1].MiddleY) - margin &&
                word.MiddleY <= Math.Max(role[0].MiddleY, role[1].MiddleY) + margin &&
                Regex.IsMatch(word.Text, @"^(?:N[.°º]?|CODICE|FISCALE|RUOLO)$")).OrderBy(word => word.Left).ToArray();
            var labels = string.Join(" ", headers.Select(word => word.Text));
            if (!Regex.IsMatch(labels, @"^N[.°º]? CODICE FISCALE RUOLO N[.°º]? CODICE FISCALE RUOLO$")) return words;
            // The two complete sets of headers independently trace the baseline of the scanned
            // table. Each consecutive segment must remain within a slight tilt. A role or a
            // fiscal code from a data row never contributes to this measurement.
            for (var i = 1; i < headers.Length; i++)
            {
                var dx = headers[i].MiddleX - headers[i - 1].MiddleX;
                if (dx <= 0 || Math.Abs(headers[i].MiddleY - headers[i - 1].MiddleY) > dx * 0.1f) return words;
            }
            float HeaderY(float x)
            {
                if (x <= headers[0].MiddleX) return headers[0].MiddleY;
                for (var i = 1; i < headers.Length; i++)
                {
                    var right = headers[i];
                    if (x > right.MiddleX) continue;
                    var left = headers[i - 1];
                    return left.MiddleY + (right.MiddleY - left.MiddleY) * (x - left.MiddleX) / (right.MiddleX - left.MiddleX);
                }
                return headers[^1].MiddleY;
            }
            return words.Select(word => word with { MiddleY = word.MiddleY - HeaderY(word.MiddleX) }).ToArray();
        }

        private static List<FiscalCodeRole> ReadPositionedRoles(PdfPage page)
        {
            // iText orders the glyphs on the page even when a PDF draws the role before its fiscal code.
            var extraction = new RegexBasedLocationExtractionStrategy(
                @"(?i:N[.°º]|CODICE|FISCALE|RUOLO|" + FiscalCodePattern + @"|[ABCD](?=\d{3})|\d{3}(?=\d{11}(?!\d)|[A-Z]{6}\d{2})|\S+)");
            new PdfCanvasProcessor(extraction).ProcessPageContent(page);
            var words = extraction.GetResultantLocations().Select(location =>
            {
                var bounds = location.GetRectangle();
                var text = location.GetText().ToUpperInvariant();
                // In some native OCR layers the printed F of the fiscal-code heading is split into three glyphs.
                var damaged = text == "L!'ISCALE";
                return new TableWord(damaged ? "FISCALE" : text, bounds.GetLeft(), bounds.GetRight(),
                    bounds.GetBottom() + bounds.GetHeight() / 2, bounds.GetHeight(), damaged);
            }).ToArray();

            var rows = new List<List<TableWord>>();
            foreach (var word in AlignDoubleHeader(words).OrderByDescending(word => word.MiddleY))
            {
                var previous = rows.LastOrDefault()?.First();
                if (previous == null || previous.MiddleY - word.MiddleY > Math.Min(previous.Height, word.Height) * 0.4f)
                    rows.Add(new List<TableWord>());
                rows[^1].Add(word);
            }

            var roles = new List<FiscalCodeRole>();
            TableWord[]? header = null;
            var hasDescription = false;
            TableWord? previousRow = null;
            foreach (var row in rows)
            {
                var cells = row.OrderBy(word => word.Left).ToArray();
                var labels = string.Join(" ", cells.Select(word => word.Text));
                if (Regex.IsMatch(labels, @"^N[.°º]? CODICE FISCALE RUOLO(?: N[.°º]? CODICE FISCALE RUOLO| DESCRIZIONE RUOLO)?$"))
                {
                    if (cells.Any(cell => cell.DamagedFiscalHeading) &&
                        (cells.Length != 8 || !cells[2].DamagedFiscalHeading || !cells[6].DamagedFiscalHeading))
                    {
                        header = null;
                        continue;
                    }
                    hasDescription = labels.EndsWith(" DESCRIZIONE RUOLO", StringComparison.Ordinal);
                    header = hasDescription ? cells[..4] : cells;
                    previousRow = cells[0];
                    continue;
                }
                if (header == null) continue;
                const string triplet = @"\d{3} " + FiscalCodePattern + " [ABCD]";
                if (header[2].DamagedFiscalHeading && !Regex.IsMatch(labels, "^" + triplet + " " + triplet + "$"))
                {
                    header = null;
                    continue;
                }
                if (hasDescription && cells.Length == 4 && cells[3].Text is "LOCATORE" or "CONDUTTORE")
                {
                    var expected = cells[3].Text == "LOCATORE" ? "A" : "B";
                    if (cells[2].Text != expected) continue;
                    cells = cells[..3];
                }
                // The first data row can be spaced farther from a double heading than
                // successive rows are from one another. An intervening section still ends the table.
                var maxGap = previousRow!.Height * (header.Length == 8 && previousRow == header[0] ? 4 : 3);
                if (previousRow.MiddleY - cells[0].MiddleY > maxGap ||
                    !cells.Any(cell => Regex.IsMatch(cell.Text, @"^\d{3}$")) ||
                    !cells.Any(cell => Regex.IsMatch(cell.Text, @"^(?:\d{11}|[A-Z0-9]{16})$")) ||
                    cells.Any(cell => !Regex.IsMatch(cell.Text, @"^(?:\d{3}|\d{11}|[A-Z0-9]{16}|\S)$")))
                {
                    header = null;
                    continue;
                }
                previousRow = cells[0];
                // Each triplet must share a visual row and match the fiscal-code and role columns
                // of the same group. An empty role cell cannot borrow a letter from its neighbour.
                for (var column = 0; column < header.Length; column += 4)
                {
                    var fiscalHeading = header[column + 1];
                    var fiscalEnd = header[column + 2].Right;
                    var roleHeading = header[column + 3];
                    var roleMargin = roleHeading.Right - roleHeading.Left;
                    var codes = cells.Where(cell => cell.Right >= fiscalHeading.Left && cell.Left <= fiscalEnd &&
                        Regex.IsMatch(cell.Text, "^" + FiscalCodePattern + "$")).ToArray();
                    var letters = cells.Where(cell => cell.Left >= Math.Max(fiscalEnd, roleHeading.Left - roleMargin) &&
                        cell.Left <= roleHeading.Right + roleMargin && Regex.IsMatch(cell.Text, @"^[ABCD]$")).ToArray();
                    if (codes.Length != 1 || letters.Length != 1) continue;
                    var index = Array.IndexOf(cells, codes[0]);
                    if (index > 0 && index + 1 < cells.Length && cells[index + 1] == letters[0] &&
                        Regex.IsMatch(cells[index - 1].Text, @"^\d{3}$"))
                        roles.Add(new FiscalCodeRole(codes[0].Text, letters[0].Text));
                }
            }
            return roles.Distinct().ToList();
        }
    }
}
