using System.Globalization;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal static partial class ControlloDomicilioPdfAnalyzer
    {
        private const string ReceiptIdentifierPattern = @"Codice\s+Identificativo\s+del\s+contratto\s*[:\-]?\s*([A-Z0-9]{10,60})";
        private const string ReceiptRegistrationPattern = @"registrat[oa]\s+il\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})\s+al\s+n\.?\s*(\d+)\s*[-–—/]?\s*serie\s*([A-Z0-9]+)";
        private const string ReceiptPeriodPattern = @"(?:Durata|Periodo)\s*[:\-]?\s*dal\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})\s*al\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})";
        private const string ReceiptLinkPattern = @"\banno\s*(\d{4})\s*[,;/\-]?\s*serie\s*([A-Z0-9]+)\s*[,;/\-]?\s*n(?:umero)?\.?\s*0*([0-9]+)";
        private const string ReceiptExtensionEndPattern = @"proroga\s+di\s+un\s+contratto\s*di\s+locazione\s+fino\s+(?:alla\s+data|al)\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})";
        private const RegexOptions ReceiptOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private sealed record PdfPageText(int Page, string Text);
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
        }
        private sealed class OfficialReceiptEvidence
        {
            public OfficialReceipt? Primary { get; set; }
            public OfficialReceipt? Registration { get; set; }
            public bool Unambiguous { get; set; }
        }

        private static List<Match> ReceiptMatches(string text, string pattern)
            => Regex.Matches(text, pattern, ReceiptOptions).Cast<Match>().ToList();

        private static string ReceiptSingle(OfficialReceipt receipt, IEnumerable<string> values)
        {
            var distinct = values.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count > 1) receipt.Ambiguous = true;
            return distinct.Count == 1 ? distinct[0] : "";
        }

        private static void ReceiptDateField(OfficialReceipt receipt, string field, string pattern, int group, string label)
        {
            var candidates = ReceiptMatches(receipt.Text, pattern).Select(m => ParseDate(m.Groups[group].Value)).ToList();
            if (candidates.Any(d => !d.HasValue)) receipt.Ambiguous = true;
            var dates = candidates.Where(d => d.HasValue).Select(d => d!.Value.Date).Distinct().ToList();
            if (dates.Count > 1) receipt.Ambiguous = true;
            if (dates.Count != 1) return;
            var page = receipt.Pages.FirstOrDefault(p => ReceiptMatches(NormalizeSpaces(p.Text), pattern)
                .Any(m => ParseDate(m.Groups[group].Value)?.Date == dates[0]));
            if (page != null) receipt.Fields[field] = new(field, IsoDate(dates[0]), page.Page, label);
        }

        private static string NormalizeReceiptOffice(string office)
        {
            var value = NormalizeSpaces(office.ToUpperInvariant());
            value = Regex.Replace(value, @"^DP[A-Z0-9]+\s+UT\s+", "");
            return NormalizeAlphaNumeric(value);
        }

        private static OfficialReceipt ParseOfficialReceipt(OfficialReceipt receipt)
        {
            receipt.Identifier = ReceiptSingle(receipt, ReceiptMatches(receipt.Text, ReceiptIdentifierPattern)
                .Select(m => m.Groups[1].Value.ToUpperInvariant()));
            receipt.Office = ReceiptSingle(receipt, ReceiptMatches(receipt.RawText, @"Ufficio[ \t]+territoriale[ \t]+di[ \t]+([^\r\n]+)")
                .Select(m => NormalizeReceiptOffice(m.Groups[1].Value)));
            var registeredOffices = ReceiptMatches(receipt.RawText, @"registrato\s+presso\s+[l1]['’]ufficio\s+([^\r\n]+)")
                .Select(m => NormalizeReceiptOffice(m.Groups[1].Value)).Distinct().ToList();
            if (registeredOffices.Any(o => o != receipt.Office)) receipt.Ambiguous = true;
            var received = ReceiptSingle(receipt, ReceiptMatches(receipt.Text,
                    @"In\s+data\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})[\s\S]{0,220}?acquisito\s+con\s+protocollo")
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
                var registration = ReceiptSingle(receipt, ReceiptMatches(receipt.Text, ReceiptRegistrationPattern)
                    .Select(m => IsoDate(ParseDate(m.Groups[1].Value)) + "|" + NormalizeRegistrationNumber(m.Groups[2].Value) + "|" + NormalizeSeries(m.Groups[3].Value)));
                var parts = registration.Split('|');
                if (parts.Length == 3)
                {
                    receipt.Year = ParseIsoDate(parts[0])?.Year.ToString(CultureInfo.InvariantCulture) ?? "";
                    receipt.Number = parts[1]; receipt.Series = parts[2];
                }
                ReceiptDateField(receipt, "RegistrationDate", ReceiptRegistrationPattern, 1, "Registrato il");
                ReceiptDateField(receipt, "StartDate", ReceiptPeriodPattern, 1, "Durata/Periodo dal");
                ReceiptDateField(receipt, "EndDate", ReceiptPeriodPattern, 2, "Durata/Periodo al");
            }
            else
            {
                receipt.Link = ReceiptSingle(receipt, ReceiptMatches(receipt.Text, ReceiptLinkPattern)
                    .Select(m => m.Groups[1].Value + "|" + NormalizeSeries(m.Groups[2].Value) + "|" + NormalizeRegistrationNumber(m.Groups[3].Value)));
                var parts = receipt.Link.Split('|');
                if (parts.Length == 3) { receipt.Year = parts[0]; receipt.Series = parts[1]; receipt.Number = parts[2]; }
                if (receipt.Kind == DomicilioPdfDocumentType.Proroga)
                {
                    ReceiptDateField(receipt, "StartDate", @"(?:decorrenza|inizio)\s+(?:della\s+)?proroga\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})", 1, "Decorrenza della proroga");
                    ReceiptDateField(receipt, "EndDate", ReceiptExtensionEndPattern, 1, "Proroga fino alla data");
                    if (!receipt.Fields.ContainsKey("EndDate")) ReceiptDateField(receipt, "EndDate",
                        @"(?:scadenza|fine)\s+(?:della\s+)?proroga\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})", 1, "Scadenza della proroga");
                }
                else ReceiptDateField(receipt, "StartDate",
                    @"(?:data|decorrenza|inizio)\s+(?:del\s+|di\s+)?(?:subentro|cessione)\s*[:\-]?\s*(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4})", 1, "Decorrenza del subentro/cessione");
            }
            if (receipt.Fields.TryGetValue("EndDate", out var end)) receipt.End = ParseIsoDate(end.Value);
            if (receipt.Identifier.Length > 0)
            {
                var page = receipt.Pages.FirstOrDefault(p => Regex.IsMatch(p.Text, ReceiptIdentifierPattern, ReceiptOptions));
                if (page != null) receipt.Fields["Reference"] = new("Reference", receipt.Identifier, page.Page, "Codice Identificativo del contratto");
            }
            if (receipt.Kind != DomicilioPdfDocumentType.Contratto && receipt.Protocols.Count == 1)
                receipt.Fields["Reference"] = new("Reference", receipt.Protocols[0], receipt.Pages[0].Page, "Protocollo dell'adempimento");
            return receipt;
        }

        private static void ReadOfficialReceiptEvidence(PdfExtractedData document, DomicilioPdfExpectation expectation, DomicilioPdfAnalysis analysis)
        {
            var receipts = new List<OfficialReceipt>();
            OfficialReceipt? current = null;
            foreach (var page in document.PageTexts)
            {
                var text = NormalizeSpaces(page.Text);
                DomicilioPdfDocumentType? kind = Regex.IsMatch(text, @"Ricevuta\s+di\s+avvenuta\s+registrazione", ReceiptOptions)
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
                else if (Regex.IsMatch(text, @"COMUNICAZIONE\s+DI\s+AVVENUTO\s+RICEVIMENTO|REGISTRAZIONE\s+E\s+ADEMPIMENTI\s+SUCCESSIVI", ReceiptOptions))
                    current = null;
                current?.Pages.Add(page);
            }
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
            if (!baseline.HasIdentity || !baseline.End.HasValue || extensions.Any(r => !r.HasIdentity || r.Link != baseline.Key ||
                r.Office != baseline.Office || !r.End.HasValue || !r.Received.HasValue || r.Protocols.Count != 1))
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
            var compact = Regex.Match(normalized, @"^(?:3T(?<first>\d+)|N?(?<last>\d+)3T)$");
            if (compact.Success) return receipt.Series == "3T" && NormalizeRegistrationNumber(compact.Groups["first"].Value + compact.Groups["last"].Value) == receipt.Number;
            return Regex.IsMatch(expected, @"^\d[\d\s]*$") && NormalizeRegistrationNumber(expected) == receipt.Number;
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
                !HasExpectedMunicipality((evidence.Registration ?? receipt).Text, expectation.ComuneDomicilio)) return;
            var expectedFiscalCode = NormalizeAlphaNumeric(expectation.CodFiscale);
            var roles = ReceiptMatches(receipt.Text.ToUpperInvariant(), @"\b\d{3}\s+(" + Regex.Escape(expectedFiscalCode) + @")\s+([ABCD])\b")
                .Select(m => m.Groups[2].Value).Distinct().ToList();
            if (roles.Count != 1 || roles[0] != (expectation.Type == DomicilioPdfDocumentType.Subentro ? "D" : "B") ||
                evidence.Registration != null && receipt != evidence.Registration && receipt.Link != evidence.Registration.Key) return;
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
