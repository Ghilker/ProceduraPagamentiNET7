using System.Globalization;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal static partial class ControlloDomicilioPdfAnalyzer
    {
        private const string ReceiptIdentifierPattern = @"\bCodice\s+identificativo\s+(?:del\s+)?contratto(?:\s+registrato)?\s*[:\-]?\s*((?=[A-Z0-9]*\d)(?=[A-Z0-9]*[A-Z])[A-Z0-9]{10,60})\b";
        private const string ReceiptNumberSeriesPattern = @"\b(?:(?:al\s+)?n\.?\s*(?<number>\d+)\s*[-–—/]?\s*serie\s*(?<series>[A-Z0-9]+)|serie\s*(?<series>[A-Z0-9]+)\s*[,;/-]?\s*numero\s*(?<number>\d+))\b";
        private const string CompactRegistrationPattern = @"^(?:3T[\s/-]*(?<number>\d+)|N?\s*(?<number>\d+)[\s/-]*3T)$";
        private const string RliFormHeading = @"\bmodello\s+RLI\s+telematico\b";
        private const string RegistrationReceiptHeading = @"(?m)^[ \t]*ricevuta\s+di\s+avvenuta\s+registrazione\b";
        private const string RegistrationReceiptTitle = RegistrationReceiptHeading +
            @"(?:\s+(?:(?:di|dei|del|per)\s+)?contratt[oi](?:\s+di\s+locazione)?)?" +
            @"(?:[ \t]*[-(]?[ \t]*(?:mod(?:ello)?\.?[ \t]*)?RLI[ \t]*\d{0,2}[ \t]*\)?)?[ \t]*[.:]?[ \t]*\r?$";
        private const string RliFormOffice = @"\bufficio[ \t]+registrazione(?:[ \t]*:[ \t]*|[ \t]+)([^\s:][^\r\n]*?)(?=[ \t]+data[ \t]+(?:di[ \t]+)?registrazione\b|[\r\n]|$)";
        private const string ReceiptRegistrationPattern = @"registrat[oa]\s+il\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})\s+al\s+n\.?\s*(\d+)\s*[-–—/]?\s*serie\s*([A-Z0-9]+)";
        private const string CertifiedContractAction = @"\s+contratto\s+(?:è|e['’])\s+stato\s+" + ReceiptRegistrationPattern;
        private const string ReceiptRegistrationLabelPattern = @"\b(?:data\s+(?:di\s+)?registrazione|registrazione\s+in\s+data)\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})\b";
        private const string ReceiptRegistrationOfficePattern = @"(?m)^[ \t]*Il\s+(\d{1,2}[/.-]\d{1,2}[/.-]\d{4})\s*,?\s+presso\s+questo\s+ufficio\s*,?\s+(?:è|e['’])\s+stato\s+registrato\s+il\s+contratto\b";
        private const string ReceiptPresentationDatePattern = @"\bIn\s+data\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})[\s\S]{0,220}?acquisito\s+con\s+protocollo";
        private const string ReceiptPeriodPattern = @"(?:Durata|Periodo)\s*[:\-]?\s*dal\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})\s*al\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})";
        private const string ReceiptLeaseStartLabelPattern = @"\bdata\s+(?:di\s+)?inizio\s+(?:della\s+)?locazione\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})\b";
        private const string ReceiptLeaseEndLabelPattern = @"\bdata\s+(?:di\s+)?fine\s+(?:della\s+)?locazione\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})\b";
        private const string ReceiptLinkPattern = @"\banno\s*(\d{4})\s*[,;/\-]?\s*serie\s*([A-Z0-9]+)\s*[,;/\-]?\s*n(?:umero)?\.?\s*0*([0-9]+)";
        private const string ReceiptExtensionEndPattern = @"proroga\s+di\s+un\s+contratto\s*di\s+locazione\s+fino\s+(?:alla\s+data|al)\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})";
        private const string ReceiptExtensionEndLabelPattern = @"(?:scadenza|fine)\s+(?:della\s+)?proroga\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})";
        private const RegexOptions ReceiptOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private sealed record PdfPageText(int Page, string Text, List<FiscalCodeRole> Roles, bool FromOcr);
        private sealed class OfficialReceipt
        {
            public DomicilioPdfDocumentType Kind { get; init; }
            public List<PdfPageText> Pages { get; } = new();
            public string Text => NormalizeSpaces(string.Join("\n", Pages.Select(p => p.Text)));
            public string RawText => string.Join("\n", Pages.Select(p => p.Text));
            public string Identifier { get; set; } = "";
            public string Number { get; set; } = "";
            public string Series { get; set; } = "";
            public string Year { get; set; } = "";
            public string Office { get; set; } = "";
            public string Link { get; set; } = "";
            public DateTime? Received { get; set; }
            public DateTime? End { get; set; }
            public bool Ambiguous { get; set; }
            public List<string> Protocols { get; } = new();
            public Dictionary<string, DomicilioPdfFieldEvidence> Fields { get; } = new();
            public string Key => Year + "|" + Series + "|" + Number;
            public bool HasIdentity => Year.Length == 4 && Series.Length > 0 && Number.Length > 0 && Office.Length > 0;
            public bool RegistrationDenied => Kind == DomicilioPdfDocumentType.Contratto && Pages.Any(HasRegistrationDenial);
        }
        private sealed class OfficialReceiptEvidence
        {
            public OfficialReceipt? Primary { get; set; }
            public OfficialReceipt? Registration { get; set; }
            public bool Unambiguous { get; set; }
        }

        private static List<Match> ReceiptMatches(string text, string pattern)
            => Regex.Matches(text, pattern, ReceiptOptions).Cast<Match>().ToList();

        private static IEnumerable<(string Value, string Label)> ContractIdentifiers(PdfPageText page)
        {
            var repeated = ReceiptMatches(page.Text, ReceiptRegistrationPattern +
                @"\s+e\s+codice\s+identificativo\s*[:\-]?\s*(?<id>[A-Z][A-Z0-9]{2}\d{2}[A-Z0-9]\d{9}[A-Z]{2})\b")
                .Select(m => m.Groups["id"].Value.ToUpperInvariant()).Distinct().ToArray();
            foreach (var match in ReceiptMatches(page.Text, ReceiptIdentifierPattern))
            {
                var id = match.Groups[1].Value.ToUpperInvariant();
                // An OCR header may omit one glyph. The same page explicitly repeats the
                // complete identifier in the sentence that certifies this registration.
                if (page.FromOcr && id.Length == 16 && repeated.Length == 1 &&
                    Enumerable.Range(0, repeated[0].Length).Any(i => repeated[0].Remove(i, 1) == id))
                    continue;
                yield return (id, "Codice Identificativo del contratto");
            }
            foreach (var id in repeated) yield return (id, "Registrato il ... e codice identificativo");
        }

        private static bool IsRegisteredLeaseForm(string text) =>
            Regex.IsMatch(text, RliFormHeading, ReceiptOptions) &&
            Regex.IsMatch(text, RliFormOffice, ReceiptOptions) &&
            Regex.IsMatch(text, ReceiptRegistrationLabelPattern, ReceiptOptions) &&
            Regex.IsMatch(text, ReceiptNumberSeriesPattern, ReceiptOptions) &&
            Regex.IsMatch(text, @"\bnegozio\s+locazione\b", ReceiptOptions) &&
            !Regex.IsMatch(text, @"\b(?:comunicazione\s+di|negozio)\s+(?:proroga|cessione|subentro|risoluzione)\b", ReceiptOptions);

        private static string ReceiptSingle(OfficialReceipt receipt, IEnumerable<string> values)
        {
            var distinct = values.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count > 1) receipt.Ambiguous = true;
            return distinct.Count == 1 ? distinct[0] : "";
        }

        private static void ReceiptDateField(OfficialReceipt receipt, string field,
            params (string Pattern, int Group, string Label)[] patterns)
        {
            IEnumerable<DateTime?> Dates(string text, string pattern, int group) =>
                ReceiptMatches(text, pattern).Select(m => ParseDate(m.Groups[group].Value));
            var receiptText = receipt.Text;
            var candidates = patterns.SelectMany(p => Dates(receiptText, p.Pattern, p.Group)).ToList();
            if (candidates.Any(d => !d.HasValue)) receipt.Ambiguous = true;
            var dates = candidates.Where(d => d.HasValue).Select(d => d!.Value.Date).Distinct().ToList();
            if (dates.Count > 1) receipt.Ambiguous = true;
            if (dates.Count != 1) return;
            foreach (var (pattern, group, label) in patterns)
            {
                var page = receipt.Pages.FirstOrDefault(p => Dates(NormalizeSpaces(p.Text), pattern, group).Contains(dates[0]));
                if (page == null) continue;
                receipt.Fields[field] = new(field, IsoDate(dates[0]), page.Page, label);
                return;
            }
        }

        private static string NormalizeReceiptOffice(string office)
        {
            var value = NormalizeSpaces(office.ToUpperInvariant());
            value = Regex.Replace(value, @"^DP[A-Z0-9]+\s+UT\s+", "");
            return NormalizeAlphaNumeric(value);
        }

        private static OfficialReceipt ParseOfficialReceipt(OfficialReceipt receipt)
        {
            receipt.Identifier = ReceiptSingle(receipt, receipt.Pages.SelectMany(ContractIdentifiers).Select(id => id.Value));
            receipt.Office = ReceiptSingle(receipt, ReceiptMatches(receipt.RawText, @"Ufficio[ \t]+territoriale[ \t]+di[ \t]+([^\r\n]+)")
                .Concat(ReceiptMatches(receipt.RawText, RliFormOffice))
                .Select(m => NormalizeReceiptOffice(m.Groups[1].Value)));
            var registeredOffices = ReceiptMatches(receipt.RawText, @"registrato\s+presso\s+[l1]['’]ufficio\s+([^\r\n]+)")
                .Select(m => NormalizeReceiptOffice(m.Groups[1].Value)).Distinct().ToList();
            if (registeredOffices.Any(o => o != receipt.Office)) receipt.Ambiguous = true;
            var received = ReceiptSingle(receipt, ReceiptMatches(receipt.Text, ReceiptPresentationDatePattern)
                .Select(m => IsoDate(ParseDate(m.Groups[1].Value))));
            receipt.Received = ParseIsoDate(received);
            var eventKind = receipt.Kind switch
            {
                DomicilioPdfDocumentType.Contratto => @"richiesta\s+di\s+registrazione",
                DomicilioPdfDocumentType.Proroga => @"comunicazione\s+di\s+proroga",
                _ => @"comunicazione\s+di\s+(?:cessione|subentro)"
            };
            receipt.Protocols.AddRange(ReceiptMatches(receipt.Text,
                    @"acquisito\s+con\s+protocollo\s*(\d{17}(?:\s*[-/]?\s*\d{6})?)\s+la\s+" + eventKind)
                .Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())
                .GroupBy(p => Regex.Replace(p, @"\D", "")).Select(g => g.First()));
            if (receipt.Protocols.Select(p => Regex.Replace(p, @"\D", "")).Distinct().Count() > 1) receipt.Ambiguous = true;

            if (receipt.Kind == DomicilioPdfDocumentType.Contratto)
            {
                var references = ReceiptMatches(receipt.Text, ReceiptNumberSeriesPattern);
                receipt.Number = ReceiptSingle(receipt, references.Select(m => NormalizeRegistrationNumber(m.Groups["number"].Value)));
                receipt.Series = ReceiptSingle(receipt, references.Select(m => NormalizeSeries(m.Groups["series"].Value)));
                ReceiptDateField(receipt, "RegistrationDate", (ReceiptRegistrationPattern, 1, "Registrato il"),
                    (ReceiptRegistrationLabelPattern, 1, "Data registrazione"),
                    (ReceiptRegistrationOfficePattern, 1, "Registrato presso questo ufficio"));
                if (receipt.Fields.TryGetValue("RegistrationDate", out var registered))
                    receipt.Year = registered.Value[..4];
                ReceiptDateField(receipt, "StartDate", (ReceiptPeriodPattern, 1, "Durata/Periodo dal"),
                    (ReceiptLeaseStartLabelPattern, 1, "Data inizio locazione"));
                ReceiptDateField(receipt, "EndDate", (ReceiptPeriodPattern, 2, "Durata/Periodo al"),
                    (ReceiptLeaseEndLabelPattern, 1, "Data fine locazione"));
            }
            else
            {
                receipt.Link = ReceiptSingle(receipt, ReceiptMatches(receipt.Text, ReceiptLinkPattern)
                    .Select(m => m.Groups[1].Value + "|" + NormalizeSeries(m.Groups[2].Value) + "|" + NormalizeRegistrationNumber(m.Groups[3].Value)));
                var parts = receipt.Link.Split('|');
                if (parts.Length == 3) { receipt.Year = parts[0]; receipt.Series = parts[1]; receipt.Number = parts[2]; }
                if (receipt.Kind == DomicilioPdfDocumentType.Proroga)
                {
                    ReceiptDateField(receipt, "StartDate",
                        (@"(?:decorrenza|inizio)\s+(?:della\s+)?proroga\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})", 1, "Decorrenza della proroga"));
                    ReceiptDateField(receipt, "EndDate", (ReceiptExtensionEndPattern, 1, "Proroga fino alla data"),
                        (ReceiptExtensionEndLabelPattern, 1, "Scadenza della proroga"));
                }
                else ReceiptDateField(receipt, "StartDate",
                    (@"(?:data|decorrenza|inizio)\s+(?:del\s+|di\s+)?(?:subentro|cessione)\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})", 1, "Decorrenza del subentro/cessione"));
            }
            if (receipt.Fields.TryGetValue("EndDate", out var end)) receipt.End = ParseIsoDate(end.Value);
            if (receipt.Identifier.Length > 0)
            {
                var source = receipt.Pages.SelectMany(page => ContractIdentifiers(page)
                    .Where(id => id.Value == receipt.Identifier).Select(id => new DomicilioPdfFieldEvidence("Reference", id.Value, page.Page, id.Label))).FirstOrDefault();
                if (source != null) receipt.Fields["Reference"] = source;
            }
            if (receipt.Kind != DomicilioPdfDocumentType.Contratto && receipt.Protocols.Count == 1)
                receipt.Fields["Reference"] = new("Reference", receipt.Protocols[0], receipt.Pages[0].Page, "Protocollo dell'adempimento");
            return receipt;
        }

        private static bool IsRegistrationApplication(PdfPageText page)
        {
            if (Regex.IsMatch(page.Text, @"(?m)^[ \t]*modello\s+RLI\b", ReceiptOptions)) return true;
            // A certified receipt can describe the request it received, even across a line break.
            var text = Regex.Replace(page.Text, @"\bacquisit[oa]\s+con\s+protocollo\s*[\d\s/\-]+\s+la\s+richiesta\s+di\s+registrazione\b", "", ReceiptOptions);
            return Regex.IsMatch(text,
                @"(?m)^[ \t]*(?:(?:mod(?:ello|\.)?\s+RLI\b)|(?:richiesta|istanza|domanda)\s+(?:telematica\s+)?di\s+registrazione\b|" +
                @"(?:ricevuta|attestazione)\s+(?:di\s+)?(?:presentazione|ricezione|trasmissione|invio)\s+(?:dell[’']|(?:della|di\s+una)\s+)(?:richiesta|istanza|domanda)\b)", ReceiptOptions);
        }

        private static bool IsRegistrationReceipt(PdfPageText page)
        {
            if (Regex.IsMatch(page.Text, RegistrationReceiptTitle, ReceiptOptions) ||
                Regex.IsMatch(page.Text, @"(?m)^[ \t]*registrazione\s+(?:del\s+)?contratto\s+di\s+locazione[ \t]*\r?$", ReceiptOptions) &&
                Regex.IsMatch(page.Text, ReceiptRegistrationOfficePattern, ReceiptOptions)) return true;
            if (Regex.IsMatch(page.Text, RegistrationReceiptHeading, ReceiptOptions) &&
                Regex.IsMatch(page.Text, @"(?m)^[ \t]*il" + CertifiedContractAction, ReceiptOptions)) return true;

            // The heading may be painted out of order. Require the office, a unique contract identifier
            // and the tax administration's acknowledgement on the very page that certifies registration.
            return Regex.IsMatch(page.Text, @"(?m)^[ \t]*(?:il|(?-i:Zl))" + CertifiedContractAction, ReceiptOptions) &&
                !Regex.IsMatch(page.Text, @"(?m)^[ \t]*(?:scrittura\s+privata|dichiarazione\s+(?:privata|sostitutiva|del\s+(?:locatore|conduttore))|istruzioni\s+per\s+la\s+(?:compilazione|registrazione)|esempio\s+di\s+ricevuta)\b", ReceiptOptions) &&
                Regex.IsMatch(page.Text, @"(?m)^[ \t]*ufficio\s+territoriale\s+di\s+[\p{L}]", ReceiptOptions) &&
                Regex.IsMatch(page.Text, @"\bsistema\s+informativo\s+dell['’]\s*agenzia\s+delle\s+entrate\s+ha\s+acquisito\s+con\s+protocollo\s+\d{17}\b", ReceiptOptions) &&
                ContractIdentifiers(page).Select(i => i.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        }

        private static bool HasRegistrationDenial(PdfPageText page) => Regex.IsMatch(page.Text,
            @"(?m)^[ \t]*(?:fac[ -]?simile\b|bozza\b|(?:esito[ \t]*[:\-][ \t]*)?(?:(?:la\s+)?(?:richiesta\s+di\s+)?registrazione\s+" +
            @"(?:(?:è\s+stata\s+)?(?:scartata|respinta)|non\s+(?:avvenuta|effettuata|eseguita))|(?:il\s+)?(?:contratto|atto)\s+non\s+(?:è\s+stato\s+)?registrato)\b)", ReceiptOptions);

        private static List<OfficialReceipt> OfficialReceipts(IEnumerable<PdfPageText> pages)
        {
            var receipts = new List<OfficialReceipt>();
            OfficialReceipt? current = null;
            foreach (var page in pages)
            {
                var text = NormalizeSpaces(page.Text);
                if (IsRegistrationApplication(page))
                {
                    if (current?.Kind == DomicilioPdfDocumentType.Contratto && HasRegistrationDenial(page))
                        current.Pages.Add(page);
                    current = null;
                    continue;
                }
                DomicilioPdfDocumentType? kind = IsRegistrationReceipt(page)
                    ? DomicilioPdfDocumentType.Contratto
                    : Regex.IsMatch(text, @"Ricevuta\s+di\s+presentazione\s+adempimenti\s+successivi", ReceiptOptions)
                        ? Regex.IsMatch(text, @"comunicazione\s+di\s+proroga", ReceiptOptions) ? DomicilioPdfDocumentType.Proroga
                        : Regex.IsMatch(text, @"comunicazione\s+di\s+(?:cessione|subentro)", ReceiptOptions) ? DomicilioPdfDocumentType.Subentro : null
                        : null;
                if (kind.HasValue)
                {
                    current = new OfficialReceipt { Kind = kind.Value };
                    receipts.Add(current);
                }
                else if (Regex.IsMatch(text, @"COMUNICAZIONE\s+DI\s+AVVENUTO\s+RICEVIMENTO|REGISTRAZIONE\s+E\s+ADEMPIMENTI\s+SUCCESSIVI|RICEVUTA\s+DI\s+PRESENTAZIONE\s+ADEMPIMENTI\s+SUCCESSIVI", ReceiptOptions))
                    current = null;
                current?.Pages.Add(page);
            }
            return receipts;
        }

        // Only official receipt pages can supply fields when a file also contains an application.
        private static bool SelectReceiptPages(PdfExtractedData document, DomicilioPdfExpectation expectation)
        {
            var receipts = OfficialReceipts(document.PageTexts);
            var primary = receipts.Where(r => r.Kind == expectation.Type).ToList();
            if (primary.Count != 1 || primary[0].RegistrationDenied) return false;
            var confirmed = ParseOfficialReceipt(primary[0]);
            if (receipts.Count > 1)
            {
                if (expectation.Type != DomicilioPdfDocumentType.Contratto || !expectation.ReadAllPages ||
                    confirmed.Ambiguous || !confirmed.HasIdentity || !confirmed.End.HasValue) return false;
                foreach (var other in receipts.Where(r => r != confirmed))
                {
                    if (other.Kind != DomicilioPdfDocumentType.Proroga || !IsLinkedExtension(confirmed, ParseOfficialReceipt(other)))
                        return false;
                }
            }
            var pages = receipts.SelectMany(r => r.Pages).ToHashSet();
            var outside = document.PageTexts.Where(p => !pages.Contains(p)).ToList();
            if (receipts.Count > 1 && outside.Any(p => HasRegistrationDenial(p) || HasExtensionDenial(p))) return false;
            var confirmedIds = confirmed.Pages.SelectMany(ContractIdentifiers).Select(i => i.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var declaredIds = outside.SelectMany(ContractIdentifiers).Select(i => i.Value).ToList();
            if (declaredIds.Any(id => !confirmedIds.Contains(id))) return false;
            IEnumerable<string> Numbers(IEnumerable<PdfPageText> source) => source.SelectMany(p => ReceiptMatches(p.Text, ReceiptNumberSeriesPattern))
                .Select(m => NormalizeSeries(m.Groups["series"].Value) + "|" + NormalizeRegistrationNumber(m.Groups["number"].Value));
            var confirmedNumbers = Numbers(confirmed.Pages).ToList();
            if ((confirmedNumbers.Count > 0 || declaredIds.Count == 0) &&
                Numbers(outside).Except(confirmedNumbers, StringComparer.OrdinalIgnoreCase).Any()) return false;
            if (expectation.Type == DomicilioPdfDocumentType.Contratto)
            {
                var declaredDates = outside.SelectMany(p => ReceiptMatches(p.Text, ReceiptRegistrationLabelPattern))
                    .Select(m => DateKey(m.Groups[1].Value));
                if (declaredDates.Any(date => !confirmed.Fields.TryGetValue("RegistrationDate", out var actual) || actual.Value != date))
                    return false;
                var offices = outside.SelectMany(p => ReceiptMatches(p.Text, RliFormOffice));
                if (confirmed.Office.Length > 0 && offices.Any(m => !MatchesOffice(m.Groups[1].Value))) return false;
                bool MatchesOffice(string office)
                {
                    if (NormalizeReceiptOffice(office) == confirmed.Office) return true;
                    // A RLI summary can suffix the office name with its code, e.g. CASSINO (TJE).
                    office = office.Trim();
                    var suffix = Regex.Match(office, @"\s+\(([A-Z0-9]{3})\)$", ReceiptOptions);
                    return suffix.Success && NormalizeReceiptOffice(office[..suffix.Index]) == confirmed.Office &&
                        declaredIds.Contains(confirmed.Identifier) && confirmed.Identifier.StartsWith(suffix.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
                }
            }
            document.PageTexts.RemoveAll(p => !pages.Contains(p));
            document.RawText = string.Join("\n", document.PageTexts.Select(p => p.Text));
            document.Text = NormalizeSpaces(document.RawText);
            return true;
        }

        private static bool IsLinkedExtension(OfficialReceipt registration, OfficialReceipt extension) =>
            !extension.Ambiguous && extension.HasIdentity && extension.Link == registration.Key && extension.Office == registration.Office &&
            (extension.Identifier.Length == 0 || extension.Identifier == registration.Identifier) &&
            extension.End.HasValue && extension.Received.HasValue && extension.Protocols.Count == 1 &&
            !extension.Pages.Any(p => HasRegistrationDenial(p) || HasExtensionDenial(p));

        private static bool HasExtensionDenial(PdfPageText page) => Regex.IsMatch(page.Text,
            @"(?m)^[ \t]*(?:esito\s*[:\-]\s*)?(?:la\s+)?(?:(?:richiesta|comunicazione)\s+di\s+)?proroga\s+(?:(?:è\s+stata\s+)?(?:scartata|respinta)|non\s+(?:avvenuta|effettuata|eseguita))\b", ReceiptOptions);

        private static void ReadOfficialReceiptEvidence(PdfExtractedData document, DomicilioPdfExpectation expectation, DomicilioPdfAnalysis analysis)
        {
            var receipts = OfficialReceipts(document.PageTexts).Where(r => !r.RegistrationDenied).ToList();
            foreach (var receipt in receipts) ParseOfficialReceipt(receipt);
            var evidence = new OfficialReceiptEvidence();
            document.OfficialEvidence = evidence;
            var registrations = receipts.Where(r => r.Kind == DomicilioPdfDocumentType.Contratto).ToList();
            var distinctRegistrations = registrations.GroupBy(r => r.Identifier + "|" + r.Key + "|" + r.Office + "|" +
                string.Join("|", r.Fields.Where(f => f.Key != "Reference").OrderBy(f => f.Key).Select(f => f.Value.Value))).ToList();
            if (distinctRegistrations.Count == 1) evidence.Registration = distinctRegistrations[0].First();
            var candidates = receipts.Where(r => r.Kind == expectation.Type).ToList();
            if (expectation.Type == DomicilioPdfDocumentType.Contratto) evidence.Primary = evidence.Registration;
            else if (candidates.Count == 1) evidence.Primary = candidates[0];
            evidence.Unambiguous = evidence.Primary is { Ambiguous: false, HasIdentity: true } &&
                distinctRegistrations.Count <= 1 && receipts.All(r => !r.Ambiguous);
            if (!expectation.ReadAllPages) return;
            void Manual(string code, string message) => analysis.State.Add(DomicilioControlSeverity.VerificaManuale, code, message,
                "Verificare separatamente le ricevute e il loro collegamento; nessuna correzione automatica è proposta.");
            if (distinctRegistrations.Count > 1 || receipts.Any(r => r.Ambiguous))
            {
                evidence.Unambiguous = false;
                Manual("PDF_RECEIPT_AMBIGUOUS", "Il PDF contiene ricevute di registrazione o campi ufficiali distinti/non univoci.");
                return;
            }
            if (expectation.Type != DomicilioPdfDocumentType.Contratto || evidence.Registration == null) return;
            var baseline = evidence.Registration;
            var extensions = receipts.Where(r => r.Kind == DomicilioPdfDocumentType.Proroga).ToList();
            if (extensions.Count == 0) return;
            if (!baseline.HasIdentity || !baseline.End.HasValue || extensions.Any(r => !IsLinkedExtension(baseline, r)))
            {
                evidence.Unambiguous = false;
                Manual("PDF_BUNDLE_LINK", "Una proroga nel PDF non identifica univocamente lo stesso anno, serie, numero e ufficio del contratto base.");
                return;
            }
            var ordered = extensions.GroupBy(r => r.Received + "|" + r.Protocols[0] + "|" + r.End)
                .Select(g => g.First()).OrderBy(r => r.Received).ToList();
            var previousEnd = baseline.End.Value;
            var previousReceived = baseline.Fields.TryGetValue("RegistrationDate", out var registrationDate) ? ParseIsoDate(registrationDate.Value) : null;
            OfficialReceipt? last = null;
            foreach (var extension in ordered)
            {
                if (extension.Received < previousReceived || extension.End <= previousEnd ||
                    last != null && extension.Received == last.Received)
                {
                    evidence.Unambiguous = false;
                    Manual("PDF_BUNDLE_SEQUENCE", "Le proroghe non formano una sequenza documentata con ricevute cronologiche e scadenze crescenti.");
                    return;
                }
                previousEnd = extension.End!.Value; previousReceived = extension.Received; last = extension;
            }
            if (last != null)
            {
                document.EndDate = previousEnd.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                baseline.Fields["EndDate"] = last.Fields["EndDate"];
            }
        }

        private static bool OfficialReferenceMatches(OfficialReceipt? receipt, string expected)
        {
            if (receipt == null || receipt.Ambiguous || !receipt.HasIdentity) return false;
            var normalized = NormalizeAlphaNumeric(expected);
            if (receipt.Identifier.Length > 0 && (normalized == receipt.Identifier || normalized == "3T" + receipt.Identifier)) return true;
            if (Regex.IsMatch(expected.Trim(), @"^\d{17}(?:\s*[-/]?\s*\d{6})?$"))
            {
                var expectedDigits = Regex.Replace(expected, @"\D", "");
                return receipt.Protocols.Count == 1 && (expectedDigits == Regex.Replace(receipt.Protocols[0], @"\D", "") ||
                    expectedDigits.Length == 17 && Regex.Replace(receipt.Protocols[0], @"\D", "").StartsWith(expectedDigits, StringComparison.Ordinal));
            }
            var compact = Regex.Match(expected.Trim(), CompactRegistrationPattern, ReceiptOptions);
            if (compact.Success) return receipt.Series == "3T" && NormalizeRegistrationNumber(compact.Groups["number"].Value) == receipt.Number;
            return Regex.IsMatch(expected.Trim(), @"^\d+$") && NormalizeRegistrationNumber(expected) == receipt.Number;
        }

        private static bool IsCompleteReference(string value)
        {
            if (Regex.IsMatch(NormalizeAlphaNumeric(value), @"[A-Z][A-Z0-9]{2}\d{2}[A-Z0-9]\d{6}[A-Z0-9]*$") ||
                Regex.IsMatch(value.Trim(), @"^\d{17}(?:\s*[-/]?\s*\d{6})?$")) return true;
            var parts = ParseReferenceComponents(value);
            return parts.HasComparableNumber && parts.Year.Length > 0 && parts.Series.Length > 0 ||
                Regex.IsMatch(value.Trim(), @"^[A-Z]{2,3}\s*[/\-]\s*\d{2,4}\s*[/\-]\s*\d+$", ReceiptOptions);
        }

        private static void PopulateDocumentedCorrections(DomicilioPdfAnalysis analysis, PdfExtractedData document, DomicilioPdfExpectation expectation)
        {
            var evidence = document.OfficialEvidence;
            var receipt = evidence?.Primary;
            if (!expectation.ReadAllPages || expectation.Type is DomicilioPdfDocumentType.ContrattoEnte or DomicilioPdfDocumentType.LearningAgreement ||
                !analysis.PdfValid || !analysis.Readable || !analysis.TypeRecognized || analysis.FiscalCodePresent != true ||
                analysis.PagesRead != analysis.PageCount || document.FailedPages.Count > 0 || document.TextlessPages.Count > 0 ||
                evidence?.Unambiguous != true || receipt == null || string.IsNullOrWhiteSpace(expectation.ComuneDomicilio) ||
                MatchPropertyMunicipality((evidence.Registration ?? receipt).Pages, expectation).Matches != true ||
                MatchPropertyMunicipality(document.PageTexts, expectation).Matches == false) return;
            if (!receipt.Pages.Any(page => ContainsFiscalCode(page, expectation.CodFiscale)) ||
                evidence.Registration != null && receipt != evidence.Registration &&
                (receipt.Link != evidence.Registration.Key || receipt.Office != evidence.Registration.Office)) return;
            var allowed = new Dictionary<string, string>
            {
                ["PDF_010"] = "RegistrationDate", ["PDF_011"] = "StartDate", ["PDF_012"] = "EndDate",
                ["PDF_013"] = "StartDate", ["PDF_014"] = "StartDate", ["PDF_015"] = "EndDate", ["PDF_017"] = "Reference"
            };
            if (analysis.State.Findings.Count == 0 || analysis.State.Findings.Any(f => !allowed.ContainsKey(f.Code))) return;
            var fields = analysis.State.Findings.Select(f => allowed[f.Code]).Distinct().ToList();
            if (fields.Any(f => !receipt.Fields.ContainsKey(f)) || fields.Contains("Reference") &&
                (string.IsNullOrWhiteSpace(expectation.Reference) || IsCompleteReference(expectation.Reference))) return;
            foreach (var field in fields) analysis.DocumentedCorrections.Add(receipt.Fields[field]);
            analysis.CanProposeCorrections = analysis.DocumentedCorrections.Count > 0;
        }
    }
}
