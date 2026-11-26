using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal static partial class ControlloDomicilioPdfAnalyzer
    {
        private const string PropertySection = @"\b(?:dati\s+(?:catastali|degli\s+immobili|dell['’]immobile)|identificativi\s+catastali)\b";
        private const string PropertySectionContent =
            @"(?:(?!\b(?:ufficio|soggetti|residenza|domicilio\s+fiscale|richiedente|locatore|conduttore|dati\s+del\s+contratto)\b)[\s\S]){0,600}";
        private const string PropertyCityLabel =
            @"(?:\bcomune\s+(?:(?:dell['’]?|di)\s*)?immobile\b" +
            @"|\bimmobile\s+(?:sito|situato|ubicato)\s+nel\s+comune\s+di\b" +
            @"|(?<address>\bimmobile\s+(?:sito|situato|ubicato)\s+(?:in|a)\b)(?!\s+(?:via|viale|piazza|strada|vicolo|corso|localit[aà])\b)" +
            @"|\btipo\s+catasto\s*[-,:;]?\s*(?:urbano|terreni)\s+ubicato\s+nel\s+comune\s+di\b" +
            @"|" + PropertySection + @"\s*[-,:;]?\s*comune\b(?:\s+di\b)?(?!\s+catastale\b)" +
            @"|" + PropertySection + PropertySectionContent + @"?\bubicato\s+nel\s+comune\s+di\b)";
        private const string PropertyCityValue =
            @"\s*[:=-]?\s*(?<city>[\p{L}][\p{L}\p{M}.'’ \t-]{0,85}?)" +
            @"(?=[ \t]*(?:[\r\n,;:(|]|$|\b(?:prov(?:incia)?\.?|foglio|indirizzo|via|viale|piazza|particella|catasto|categoria|c\.?a\.?p\.?)\b))";
        private const string PropertyCodeLabel = @"\b(?:cod(?:ice|\.)\s+(?:del\s+)?comune(?:\s+catastale)?|codice\s+catastale(?:\s+del\s+comune)?|comune\s+catastale)\b";
        private const string CadastralColumnHeading = @"^[\s,:;|]*(?:T\s*/\s*U|I\s*/\s*P|comune|categoria|sezione|foglio|particella|subalterno)\b";

        // null = no property field, false = a conflicting or unverifiable field.
        private static (bool? Matches, int Page) MatchPropertyMunicipality(IEnumerable<PdfPageText> pages, DomicilioPdfExpectation expectation)
        {
            int firstPage = 0;
            bool hasUnresolvedColumn = false;
            foreach (var page in pages)
            {
                foreach (Match match in Regex.Matches(page.Text, PropertyCityLabel + "(?:" + PropertyCityValue + ")?", ReceiptOptions))
                {
                    if (!match.Groups["city"].Success)
                    {
                        // A street address or damaged free-text clause can omit the town.
                        // It supplies no city to compare with the explicit cadastral field.
                        if (match.Groups["address"].Success) continue;
                        return (false, page.Page);
                    }
                    var city = NormalizeSpaces(match.Groups["city"].Value.Trim());
                    if (!string.Equals(city, NormalizeSpaces(expectation.ComuneDomicilio.Trim()), StringComparison.OrdinalIgnoreCase))
                        return (false, page.Page);
                    if (firstPage == 0) firstPage = page.Page;
                }

                // Use a code only inside the property section on this page. A second
                // value in the same section must agree, including when names also appear.
                foreach (Match section in Regex.Matches(page.Text, PropertySection + PropertySectionContent, ReceiptOptions))
                {
                    foreach (Match field in Regex.Matches(section.Value, PropertyCodeLabel, ReceiptOptions))
                    {
                        var start = section.Index + field.Index + field.Length;
                        var following = page.Text[start..];
                        // On some forms the four boxes print as "H 5 01". Join them
                        // only on one line, with an identifiable end to that field.
                        var code = Regex.Match(following, @"^\s*[:=-]?\s*(?:(?<code>[A-Z]\d{3})\b|" +
                            @"(?<code>[A-Z](?:[ \t]*\d){3})(?=[ \t]*(?:[\r\n|,;]|$)))", ReceiptOptions);
                        if (!code.Success)
                        {
                            if (!Regex.IsMatch(following, CadastralColumnHeading, ReceiptOptions) &&
                                Regex.IsMatch(following, @"^\s*(?:[:=-]|[A-Z][ \t]*\d)", ReceiptOptions))
                                return (false, page.Page);
                            hasUnresolvedColumn = true;
                            continue;
                        }
                        var printedCode = Regex.Replace(code.Groups["code"].Value, @"[ \t]", "");
                        if (!string.Equals(printedCode, expectation.CodComuneDomicilio.Trim(), StringComparison.OrdinalIgnoreCase))
                            return (false, page.Page);
                        if (firstPage == 0) firstPage = page.Page;
                    }
                }
            }
            return firstPage > 0 ? (true, firstPage) : (hasUnresolvedColumn ? false : null, 0);
        }

        private static void ReadReceiptProperty(DomicilioPdfAnalysis analysis, PdfExtractedData document, DomicilioPdfExpectation expectation)
        {
            if (!expectation.ReadAllPages || !analysis.TypeRecognized || document.FailedPages.Count != 0 ||
                document.TextlessPages.Count != 0 || string.IsNullOrWhiteSpace(expectation.ComuneDomicilio) ||
                document.OfficialEvidence is not { Unambiguous: true, Primary: { } receipt } evidence) return;
            if (evidence.Registration is { } registration &&
                (receipt.Key != registration.Key || receipt.Office != registration.Office)) return;

            var property = MatchPropertyMunicipality(receipt.Pages, expectation);
            if (MatchPropertyMunicipality(document.PageTexts, expectation).Matches == false)
                property = (false, 0);
            analysis.ReceiptProperty = new(receipt.Office + "|" + receipt.Key, receipt.Identifier, property.Matches, property.Page);
        }

        internal static void ApplyLinkedMunicipalities(IEnumerable<(int Request, decimal Attachment,
            DomicilioPdfExpectation Expectation, DomicilioPdfAnalysis Analysis)> documents)
        {
            var groups = documents.Where(d => d.Analysis.ReceiptProperty != null)
                .GroupBy(d => (d.Request, d.Analysis.ReceiptProperty!.ContractKey));
            foreach (var group in groups)
            {
                if (group.Any(d => d.Analysis.ReceiptProperty!.Matches == false) ||
                    group.Select(d => d.Expectation.ComuneDomicilio.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
                    group.Select(d => d.Analysis.ReceiptProperty!.ContractIdentifier).Where(id => id.Length > 0).Distinct().Count() > 1)
                    continue;
                var source = group.Where(d => d.Analysis.ReceiptProperty!.Matches == true && d.Analysis.DataCoherent == true)
                    .OrderBy(d => d.Attachment).FirstOrDefault();
                if (source.Analysis == null) continue;
                foreach (var target in group.Where(d => d.Attachment != source.Attachment && d.Analysis.MunicipalityAbsent &&
                    d.Analysis.State.Findings.Any(f => f.Code == "PDF_024")))
                {
                    target.Analysis.State.ResolveMissingMunicipality();
                    target.Analysis.ExtractedMunicipality = $"{target.Expectation.ComuneDomicilio} (allegato {source.Attachment:0}, pagina {source.Analysis.ReceiptProperty!.Page})";
                    SetDataCoherence(target.Analysis);
                }
            }
        }
    }
}
