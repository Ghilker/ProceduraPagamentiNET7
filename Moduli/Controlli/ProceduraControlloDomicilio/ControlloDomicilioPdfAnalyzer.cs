using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace ProcedureNet7
{
    internal static class ControlloDomicilioPdfAnalyzer
    {
        private const int MaxPagesToRead = 10;
        private const int MaxPdfBytes = 20 * 1024 * 1024;

        static ControlloDomicilioPdfAnalyzer()
        {
            // L'analizzatore usa più espressioni regolari del limite predefinito
            // della cache di Regex. Un limite più ampio evita di ricrearle migliaia
            // di volte durante l'elaborazione massiva, senza persistere alcun dato.
            Regex.CacheSize = Math.Max(Regex.CacheSize, 128);
        }

        public static DomicilioPdfAnalysis Analyze(byte[]? file, DomicilioPdfExpectation expectation)
        {
            var analysis = new DomicilioPdfAnalysis
            {
                FileAvailable = file is { Length: > 0 }
            };

            if (file == null || file.Length == 0)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.NonConforme,
                    "PDF_001",
                    "Il contenuto binario dell'allegato non è disponibile in ADISU_ALLEGATI.",
                    "Recuperare o richiedere nuovamente il documento allo studente.");
                return analysis;
            }

            if (file.Length > MaxPdfBytes)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    "PDF_002",
                    $"Il documento supera il limite operativo di {MaxPdfBytes / 1024 / 1024} MB e non è stato analizzato automaticamente.",
                    "Aprire e verificare manualmente il documento.");
                return analysis;
            }

            if (!HasPdfSignature(file))
            {
                analysis.State.Add(
                    DomicilioControlSeverity.NonConforme,
                    "PDF_003",
                    "Il file archiviato non contiene una firma PDF valida.",
                    "Richiedere un nuovo documento PDF allo studente.");
                return analysis;
            }

            PdfExtractedData extracted;
            try
            {
                extracted = ReadPdf(file, expectation);
                analysis.PdfValid = true;
                analysis.PageCount = extracted.PageCount;
                analysis.PagesRead = extracted.PagesRead;
            }
            catch (Exception ex)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.NonConforme,
                    "PDF_004",
                    $"Il PDF è danneggiato, protetto o non leggibile da iText ({CompactException(ex)}).",
                    "Aprire il documento; se non è utilizzabile, richiederne una nuova copia.");
                return analysis;
            }

            if (extracted.Truncated)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    "PDF_005",
                    $"Il PDF contiene {extracted.PageCount} pagine; il controllo automatico ha letto le prime {MaxPagesToRead}.",
                    "Verificare manualmente le pagine non analizzate.");
            }

            if (extracted.FailedPages.Count > 0)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    "PDF_019",
                    $"iText non ha potuto estrarre il testo dalle pagine {string.Join(", ", extracted.FailedPages)}.",
                    "Aprire e verificare manualmente le pagine indicate; le altre pagine sono state comunque analizzate.");
            }

            if (string.IsNullOrWhiteSpace(extracted.Text))
            {
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    "PDF_006",
                    "Il PDF non contiene testo estraibile; potrebbe essere una scansione.",
                    "Aprire il PDF e confrontarlo manualmente con i dati dichiarati.");
                return analysis;
            }

            analysis.Readable = true;
            analysis.TypeRecognized = LooksLikeDocument(extracted.Text, expectation.Type);
            if (!analysis.TypeRecognized)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    "PDF_007",
                    $"Il documento non è stato riconosciuto con certezza come {DocumentLabel(expectation.Type)} dell'Agenzia delle Entrate.",
                    "Verificare manualmente che la tipologia del documento sia corretta.");
            }

            ExtractFields(extracted, expectation.Type);
            analysis.ExtractedReference = BestReference(extracted, expectation.Type);
            analysis.ExtractedRegistrationDate = ParseDate(extracted.RegistrationDate);
            analysis.ExtractedStartDate = ParseDate(expectation.Type == DomicilioPdfDocumentType.Subentro
                ? extracted.EventDate
                : extracted.StartDate);
            analysis.ExtractedEndDate = ParseDate(extracted.EndDate);
            analysis.ExtractedFiscalCodes = FormatFiscalCodes(extracted);

            ValidateFiscalCode(analysis, extracted, expectation);
            ValidateReference(analysis, extracted, expectation.Reference);

            if (expectation.Type == DomicilioPdfDocumentType.Contratto)
            {
                ValidateDate(analysis, "data di registrazione", expectation.RegistrationDate, analysis.ExtractedRegistrationDate, "PDF_010");
                ValidateDate(analysis, "data di decorrenza", expectation.StartDate, analysis.ExtractedStartDate, "PDF_011");
                ValidateDate(analysis, "data di scadenza", expectation.EndDate, analysis.ExtractedEndDate, "PDF_012");
            }
            else if (expectation.Type == DomicilioPdfDocumentType.Subentro)
            {
                ValidateDate(analysis, "data del subentro", expectation.StartDate, analysis.ExtractedStartDate, "PDF_013");
                ValidateContractLink(analysis, extracted, expectation.ContractReference);
            }
            else
            {
                ValidateDate(analysis, "data di decorrenza della proroga", expectation.StartDate, analysis.ExtractedStartDate, "PDF_014");
                ValidateDate(analysis, "data di scadenza della proroga", expectation.EndDate, analysis.ExtractedEndDate, "PDF_015");
                ValidateContractLink(analysis, extracted, expectation.ContractReference);
            }

            analysis.DataCoherent = analysis.State.Findings.Any(x =>
                    x.Severity == DomicilioControlSeverity.NonConforme &&
                    x.Code.StartsWith("PDF_", StringComparison.Ordinal))
                ? false
                : analysis.State.Findings.Any(x =>
                    x.Severity == DomicilioControlSeverity.VerificaManuale &&
                    x.Code.StartsWith("PDF_", StringComparison.Ordinal))
                    ? null
                    : true;
            return analysis;
        }

        private static PdfExtractedData ReadPdf(byte[] file, DomicilioPdfExpectation expectation)
        {
            using var memory = new MemoryStream(file, writable: false);
            using var reader = new PdfReader(memory);
            using var pdf = new PdfDocument(reader);

            var extracted = new PdfExtractedData { PageCount = pdf.GetNumberOfPages() };
            if (extracted.PageCount <= 0)
                throw new InvalidDataException("nessuna pagina presente");

            var pages = Math.Min(extracted.PageCount, MaxPagesToRead);
            var text = new StringBuilder();
            for (var page = 1; page <= pages; page++)
            {
                extracted.PagesRead = page;
                try
                {
                    var pdfPage = pdf.GetPage(page);
                    if (pdfPage == null)
                    {
                        extracted.FailedPages.Add(page);
                        continue;
                    }

                    var strategy = new SimpleTextExtractionStrategy();
                    text.AppendLine(PdfTextExtractor.GetTextFromPage(pdfPage, strategy) ?? string.Empty);

                    // Le ricevute RLI normalmente contengono tutti i dati utili nelle
                    // prime due pagine. Le pagine successive vengono saltate soltanto
                    // quando tipologia, soggetto, estremi, date e contratto richiamato
                    // risultano già tutti presenti e coerenti con i dati attesi.
                    if (page >= 2 && page < pages &&
                        CanStopAfterCurrentPage(text.ToString(), expectation, out var completedExtraction))
                    {
                        completedExtraction.PageCount = extracted.PageCount;
                        completedExtraction.PagesRead = extracted.PagesRead;
                        completedExtraction.FailedPages.AddRange(extracted.FailedPages);
                        completedExtraction.StoppedWhenComplete = true;
                        extracted = completedExtraction;
                        break;
                    }
                }
                catch (Exception)
                {
                    // Alcuni PDF formalmente apribili contengono una singola pagina con una
                    // struttura interna incompleta. La pagina viene demandata all'ufficio,
                    // senza perdere il testo estraibile dalle altre pagine.
                    extracted.FailedPages.Add(page);
                }
            }

            extracted.Truncated = !extracted.StoppedWhenComplete && extracted.PageCount > pages;
            extracted.Text = NormalizeSpaces(text.ToString());
            return extracted;
        }

        private static bool CanStopAfterCurrentPage(
            string text,
            DomicilioPdfExpectation expectation,
            out PdfExtractedData snapshot)
        {
            var normalizedText = NormalizeSpaces(text);
            snapshot = new PdfExtractedData { Text = normalizedText };
            if (!LooksLikeDocument(normalizedText, expectation.Type))
                return false;

            ExtractFields(snapshot, expectation.Type);

            var expectedCf = NormalizeAlphaNumeric(expectation.CodFiscale);
            if (expectation.RequireFiscalCode)
            {
                if (string.IsNullOrWhiteSpace(expectedCf))
                    return false;

                if (expectation.Type == DomicilioPdfDocumentType.Subentro)
                {
                    var role = snapshot.FiscalCodeRoles.FirstOrDefault(x =>
                        string.Equals(NormalizeAlphaNumeric(x.FiscalCode), expectedCf, StringComparison.OrdinalIgnoreCase));
                    if (role == null || !string.Equals(role.Role, "D", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                else if (!snapshot.FiscalCodes.Any(x =>
                             string.Equals(NormalizeAlphaNumeric(x), expectedCf, StringComparison.OrdinalIgnoreCase)) &&
                         !NormalizeAlphaNumeric(normalizedText).Contains(expectedCf, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            var extractedReference = BestReference(snapshot, expectation.Type);
            if (string.IsNullOrWhiteSpace(expectation.Reference))
            {
                if (string.IsNullOrWhiteSpace(extractedReference))
                    return false;
            }
            else if (!ContainsNormalized(normalizedText, expectation.Reference) &&
                     !AreEqualOrContained(expectation.Reference, extractedReference))
            {
                return false;
            }

            if (expectation.Type == DomicilioPdfDocumentType.Contratto)
            {
                if (!DateMatches(expectation.RegistrationDate, snapshot.RegistrationDate) ||
                    !DateMatches(expectation.StartDate, snapshot.StartDate) ||
                    !DateMatches(expectation.EndDate, snapshot.EndDate))
                    return false;
            }
            else if (expectation.Type == DomicilioPdfDocumentType.Subentro)
            {
                if (!DateMatches(expectation.StartDate, snapshot.EventDate) ||
                    !ContractReferenceMatches(snapshot, expectation.ContractReference, out _))
                    return false;
            }
            else if (!DateMatches(expectation.StartDate, snapshot.StartDate) ||
                     !DateMatches(expectation.EndDate, snapshot.EndDate) ||
                     !ContractReferenceMatches(snapshot, expectation.ContractReference, out _))
            {
                return false;
            }

            return true;
        }

        private static bool DateMatches(DateTime? expected, string? extracted)
            => !expected.HasValue || ParseDate(extracted)?.Date == expected.Value.Date;

        private static void ValidateFiscalCode(
            DomicilioPdfAnalysis analysis,
            PdfExtractedData extracted,
            DomicilioPdfExpectation expectation)
        {
            if (!expectation.RequireFiscalCode)
            {
                // Con il subentro il contratto originario appartiene al precedente
                // conduttore: il CF dello studente va cercato nel subentro, non qui.
                analysis.FiscalCodePresent = null;
                return;
            }

            var expectedFiscalCode = expectation.CodFiscale;
            var expected = NormalizeAlphaNumeric(expectedFiscalCode);
            if (string.IsNullOrWhiteSpace(expected))
            {
                analysis.FiscalCodePresent = null;
                analysis.State.Add(
                    DomicilioControlSeverity.NonConforme,
                    "PDF_008",
                    "Il codice fiscale associato al domicilio è mancante.",
                    "Correggere il dato anagrafico prima di lavorare il documento.");
                return;
            }

            if (expectation.Type == DomicilioPdfDocumentType.Subentro)
            {
                ValidateSubentryFiscalCode(analysis, extracted, expectedFiscalCode, expected);
                return;
            }

            var found = extracted.FiscalCodes.Any(x =>
                string.Equals(NormalizeAlphaNumeric(x), expected, StringComparison.OrdinalIgnoreCase));
            if (!found)
                found = NormalizeAlphaNumeric(extracted.Text).Contains(expected, StringComparison.OrdinalIgnoreCase);

            analysis.FiscalCodePresent = found;
            if (!found)
            {
                var detected = extracted.FiscalCodes.Count == 0
                    ? "nessun codice fiscale rilevato"
                    : string.Join(", ", extracted.FiscalCodes);
                analysis.State.Add(
                    DomicilioControlSeverity.NonConforme,
                    "PDF_009",
                    $"Il codice fiscale {expectedFiscalCode.Trim()} non è presente nel PDF ({detected}).",
                    "Verificare l'intestatario e richiedere il documento corretto se necessario.");
            }
        }

        private static void ValidateSubentryFiscalCode(
            DomicilioPdfAnalysis analysis,
            PdfExtractedData extracted,
            string expectedFiscalCode,
            string expected)
        {
            var role = extracted.FiscalCodeRoles
                .FirstOrDefault(x => string.Equals(NormalizeAlphaNumeric(x.FiscalCode), expected, StringComparison.OrdinalIgnoreCase));

            if (role != null && string.Equals(role.Role, "D", StringComparison.OrdinalIgnoreCase))
            {
                analysis.FiscalCodePresent = true;
                return;
            }

            if (role != null)
            {
                analysis.FiscalCodePresent = false;
                analysis.State.Add(
                    DomicilioControlSeverity.NonConforme,
                    "PDF_020",
                    $"Nel subentro il codice fiscale {expectedFiscalCode.Trim()} ha il ruolo {RoleDescription(role.Role)}, non quello di conduttore cessionario (D).",
                    "Verificare i soggetti del subentro e richiedere il documento corretto se necessario.");
                return;
            }

            var presentWithoutRole = extracted.FiscalCodes.Any(x =>
                string.Equals(NormalizeAlphaNumeric(x), expected, StringComparison.OrdinalIgnoreCase));
            if (presentWithoutRole || NormalizeAlphaNumeric(extracted.Text).Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                analysis.FiscalCodePresent = null;
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    "PDF_021",
                    $"Il codice fiscale {expectedFiscalCode.Trim()} è presente nel subentro, ma iText non ne ha riconosciuto con certezza il ruolo di conduttore cessionario (D).",
                    "Verificare manualmente il ruolo dello studente nella tabella dei soggetti coinvolti.");
                return;
            }

            analysis.FiscalCodePresent = false;
            analysis.State.Add(
                DomicilioControlSeverity.NonConforme,
                "PDF_022",
                $"Il codice fiscale {expectedFiscalCode.Trim()} non è presente tra i soggetti del subentro.",
                "Richiedere il documento di subentro riferito allo studente.");
        }

        private static void ValidateReference(
            DomicilioPdfAnalysis analysis,
            PdfExtractedData extracted,
            string? expectedReference)
        {
            if (string.IsNullOrWhiteSpace(expectedReference))
                return;

            if (ContainsNormalized(extracted.Text, expectedReference) ||
                AreEqualOrContained(expectedReference, analysis.ExtractedReference))
                return;

            if (string.IsNullOrWhiteSpace(analysis.ExtractedReference))
            {
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    "PDF_016",
                    $"Gli estremi '{expectedReference}' non sono stati estratti automaticamente dal PDF.",
                    "Confrontare manualmente gli estremi del documento con quelli registrati.");
                return;
            }

            analysis.State.Add(
                DomicilioControlSeverity.NonConforme,
                "PDF_017",
                $"Gli estremi registrati '{expectedReference}' sono diversi da quelli letti nel PDF '{analysis.ExtractedReference}'.",
                "Correggere gli estremi o richiedere il documento riferito al contratto corretto.");
        }

        private static void ValidateDate(
            DomicilioPdfAnalysis analysis,
            string label,
            DateTime? expected,
            DateTime? extracted,
            string mismatchCode)
        {
            if (!expected.HasValue)
                return;

            if (!extracted.HasValue)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    mismatchCode + "N",
                    $"La {label} non è stata estratta automaticamente dal PDF; nel database risulta {expected:dd/MM/yyyy}.",
                    $"Verificare manualmente la {label}.");
                return;
            }

            if (expected.Value.Date != extracted.Value.Date)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.NonConforme,
                    mismatchCode,
                    $"La {label} registrata ({expected:dd/MM/yyyy}) è diversa da quella letta nel PDF ({extracted:dd/MM/yyyy}).",
                    "Correggere il dato o richiedere il documento corretto.");
            }
        }

        private static void ValidateContractLink(
            DomicilioPdfAnalysis analysis,
            PdfExtractedData extracted,
            string? contractReference)
        {
            if (string.IsNullOrWhiteSpace(contractReference))
                return;

            if (ContractReferenceMatches(extracted, contractReference, out var comparable))
                return;

            if (!comparable)
            {
                analysis.State.Add(
                    DomicilioControlSeverity.VerificaManuale,
                    "PDF_018",
                    $"Non è stato possibile confrontare automaticamente il subentro/proroga con il contratto '{contractReference}'. Nel documento è stato letto '{BestContractReference(extracted)}'.",
                    "Confrontare manualmente anno, serie e numero del contratto richiamato dal documento.");
                return;
            }

            analysis.State.Add(
                DomicilioControlSeverity.NonConforme,
                "PDF_023",
                $"Il documento richiama il contratto '{BestContractReference(extracted)}', non coerente con il contratto registrato '{contractReference}'.",
                "Verificare l'associazione del subentro/proroga al contratto e correggerla oppure richiedere il documento corretto.");
        }

        private static bool ContractReferenceMatches(
            PdfExtractedData extracted,
            string? contractReference,
            out bool comparable)
        {
            if (string.IsNullOrWhiteSpace(contractReference))
            {
                comparable = true;
                return true;
            }

            if (ContainsNormalized(extracted.Text, contractReference))
            {
                comparable = true;
                return true;
            }

            var expected = ParseReferenceComponents(contractReference);
            var actual = new ContractReferenceComponents(
                NormalizeSeries(extracted.ReferencedContractSeries),
                NormalizeRegistrationNumber(extracted.ReferencedContractNumber),
                NormalizeYear(extracted.ReferencedContractYear));
            comparable = expected.HasComparableNumber && actual.HasComparableNumber;
            if (!comparable)
                return false;

            var numberMatches = string.Equals(expected.Number, actual.Number, StringComparison.OrdinalIgnoreCase);
            var yearMatches = string.IsNullOrWhiteSpace(expected.Year) || string.IsNullOrWhiteSpace(actual.Year) ||
                              string.Equals(expected.Year, actual.Year, StringComparison.OrdinalIgnoreCase);
            var seriesMatches = string.IsNullOrWhiteSpace(expected.Series) || string.IsNullOrWhiteSpace(actual.Series) ||
                                string.Equals(expected.Series, actual.Series, StringComparison.OrdinalIgnoreCase);
            return numberMatches && yearMatches && seriesMatches;
        }

        private static void ExtractFields(PdfExtractedData document, DomicilioPdfDocumentType type)
        {
            if (document.FieldsExtracted)
                return;

            var text = document.Text;
            document.EventProtocol = ExtractEventProtocol(text);
            document.ContractIdentifier = MatchValue(text,
                @"Codice\s+Identificativo\s+del\s+contratto\s*[:\-]?\s*([A-Z0-9]{10,60})");

            var referencedContract = Regex.Match(text,
                @"\banno\s*(\d{4})\s*[,;/\-]?\s*serie\s*([A-Z0-9]+)\s*[,;/\-]?\s*n(?:umero)?\.?\s*0*([0-9]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (referencedContract.Success)
            {
                document.ReferencedContractYear = referencedContract.Groups[1].Value.Trim();
                document.ReferencedContractSeries = referencedContract.Groups[2].Value.Trim();
                document.ReferencedContractNumber = referencedContract.Groups[3].Value.Trim();
            }

            var officeSeries = Regex.Match(text,
                @"ufficio\s+([A-Z0-9]+)\s*,?\s*serie\s*([A-Z0-9]+)\s*,?\s*numero\s*([0-9]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (officeSeries.Success)
            {
                document.Series = officeSeries.Groups[2].Value.Trim();
                document.Number = officeSeries.Groups[3].Value.Trim();
            }

            var registration = Regex.Match(text,
                @"registrat[oa]\s+il\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{4})\s+al\s+n\.?\s*([0-9]+)\s*[-–—\/]?\s*serie\s*([A-Z0-9]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (registration.Success)
            {
                document.RegistrationDate = registration.Groups[1].Value.Trim();
                document.Number = registration.Groups[2].Value.Trim();
                document.Series = registration.Groups[3].Value.Trim();
                document.RegistrationYear = ExtractYear(document.RegistrationDate);
            }

            if (string.IsNullOrWhiteSpace(document.RegistrationDate))
            {
                document.RegistrationDate = MatchValue(text,
                    @"(?:data\s+di\s+registrazione|registrazione\s+in\s+data|in\s+data)\s*[:\-]?\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{4})");
                document.RegistrationYear = ExtractYear(document.RegistrationDate);
            }

            if (string.IsNullOrWhiteSpace(document.Series) || string.IsNullOrWhiteSpace(document.Number))
            {
                var numberSeries = Regex.Match(text,
                    @"(?:al\s+)?n\.?\s*([0-9]+)\s*[-–—\/]?\s*serie\s*([A-Z0-9]+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (numberSeries.Success)
                {
                    document.Number = numberSeries.Groups[1].Value.Trim();
                    document.Series = numberSeries.Groups[2].Value.Trim();
                }
            }

            var duration = Regex.Match(text,
                @"(?:Durata|Periodo)\s*[:\-]?\s*dal\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{4})\s*al\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (duration.Success)
            {
                document.StartDate = duration.Groups[1].Value.Trim();
                document.EndDate = duration.Groups[2].Value.Trim();
            }

            if (type == DomicilioPdfDocumentType.Proroga)
            {
                document.StartDate = FirstNotEmpty(
                    MatchValue(text, @"(?:decorrenza|inizio)\s+(?:della\s+)?proroga\s*[:\-]?\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{4})"),
                    document.StartDate);
                document.EndDate = FirstNotEmpty(
                    MatchValue(text, @"(?:scadenza|fine)\s+(?:della\s+)?proroga\s*[:\-]?\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{4})"),
                    document.EndDate);
            }
            else if (type == DomicilioPdfDocumentType.Subentro)
            {
                document.EventDate = FirstNotEmpty(
                    MatchValue(text,
                        @"(?:cessione|subentro)[\s\S]{0,140}?(?:dalla\s+data|con\s+decorrenza(?:\s+dal)?|dal)\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{4})"),
                    MatchValue(text,
                        @"(?:data|decorrenza|inizio)\s+(?:del\s+|di\s+)?(?:subentro|cessione)\s*[:\-]?\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{4})"));
                if (string.IsNullOrWhiteSpace(document.EventDate))
                    document.EventDate = document.StartDate;
            }

            if (string.IsNullOrWhiteSpace(document.ReferencedContractNumber) &&
                type == DomicilioPdfDocumentType.Contratto)
            {
                document.ReferencedContractSeries = document.Series;
                document.ReferencedContractNumber = document.Number;
                document.ReferencedContractYear = document.RegistrationYear;
            }

            document.FiscalCodes = Regex.Matches(text.ToUpperInvariant(), @"\b[A-Z]{6}[0-9]{2}[A-Z][0-9]{2}[A-Z][0-9]{3}[A-Z]\b")
                .Cast<Match>()
                .Select(x => x.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            document.FiscalCodeRoles = Regex.Matches(
                    text.ToUpperInvariant(),
                    @"\b\d{3}\s+([A-Z]{6}[0-9]{2}[A-Z][0-9]{2}[A-Z][0-9]{3}[A-Z])\s+([ABCD])\b",
                    RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(x => new FiscalCodeRole(x.Groups[1].Value, x.Groups[2].Value))
                .GroupBy(x => $"{x.FiscalCode}|{x.Role}", StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
            document.FieldsExtracted = true;
        }

        private static bool LooksLikeDocument(string text, DomicilioPdfDocumentType type)
        {
            if (type == DomicilioPdfDocumentType.Proroga)
            {
                return Regex.IsMatch(text, @"\bproroga\b", RegexOptions.IgnoreCase) &&
                       Regex.IsMatch(text, @"Agenzia\s+delle\s+Entrate|\bRLI\b|adempiment[io]\s+successiv[io]", RegexOptions.IgnoreCase);
            }

            if (type == DomicilioPdfDocumentType.Subentro)
            {
                return Regex.IsMatch(text, @"\bsubentr[oa]\b|\bcession[ei]\b|\bcessionario\b", RegexOptions.IgnoreCase) &&
                       Regex.IsMatch(text, @"Agenzia\s+delle\s+Entrate|\bRLI\b|adempiment[io]\s+successiv[io]", RegexOptions.IgnoreCase);
            }

            var score = 0;
            if (Regex.IsMatch(text, @"Registrazione\s+(?:dei\s+)?contratt[oi]\s+di\s+locazione", RegexOptions.IgnoreCase)) score++;
            if (Regex.IsMatch(text, @"Ricevuta\s+di\s+avvenuta\s+registrazione", RegexOptions.IgnoreCase)) score++;
            if (Regex.IsMatch(text, @"mod\.?\s*RLI\s*12|RLI12", RegexOptions.IgnoreCase)) score++;
            if (Regex.IsMatch(text, @"Dati\s+(?:generali\s+)?del\s+contratto", RegexOptions.IgnoreCase)) score++;
            if (Regex.IsMatch(text, @"Codice\s+Identificativo\s+del\s+contratto", RegexOptions.IgnoreCase)) score++;
            if (Regex.IsMatch(text, @"Locatori|Conduttori|locatore\s*/\s*\(B\)\s*conduttore", RegexOptions.IgnoreCase)) score++;
            return score >= 2;
        }

        private static bool HasPdfSignature(byte[] file)
        {
            var length = Math.Min(file.Length, 1024);
            for (var index = 0; index <= length - 5; index++)
            {
                if (file[index] == (byte)'%' && file[index + 1] == (byte)'P' &&
                    file[index + 2] == (byte)'D' && file[index + 3] == (byte)'F' &&
                    file[index + 4] == (byte)'-')
                    return true;
            }
            return false;
        }

        private static string BestReference(PdfExtractedData document, DomicilioPdfDocumentType type)
        {
            if (type is DomicilioPdfDocumentType.Subentro or DomicilioPdfDocumentType.Proroga &&
                !string.IsNullOrWhiteSpace(document.EventProtocol))
                return document.EventProtocol.Trim();
            if (!string.IsNullOrWhiteSpace(document.ContractIdentifier))
                return document.ContractIdentifier.Trim();
            return string.Join(" ", new[] { document.Series, document.Number, document.RegistrationYear }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string BestContractReference(PdfExtractedData document)
        {
            var values = new[]
            {
                document.ReferencedContractYear,
                string.IsNullOrWhiteSpace(document.ReferencedContractSeries) ? null : $"serie {document.ReferencedContractSeries}",
                string.IsNullOrWhiteSpace(document.ReferencedContractNumber) ? null : $"n. {NormalizeRegistrationNumber(document.ReferencedContractNumber)}"
            };
            var result = string.Join(" ", values.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(result) ? "riferimento non estratto" : result;
        }

        private static ContractReferenceComponents ParseReferenceComponents(string value)
        {
            var labeled = Regex.Match(
                value,
                @"(?:anno\s*)?(?<year>\d{4})?\s*[,;/\-]?\s*serie\s*(?<series>[A-Z0-9]+)\s*[,;/\-]?\s*n(?:umero)?\.?\s*0*(?<number>\d+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (labeled.Success)
            {
                return new ContractReferenceComponents(
                    NormalizeSeries(labeled.Groups["series"].Value),
                    NormalizeRegistrationNumber(labeled.Groups["number"].Value),
                    NormalizeYear(labeled.Groups["year"].Value));
            }

            var numberFirst = Regex.Match(
                value,
                @"(?:n(?:umero)?\.?\s*)?0*(?<number>\d+)\s*[-/;,]?\s*serie\s*(?<series>[A-Z0-9]+)(?:\s*[-/;,]?\s*(?:anno\s*)?(?<year>\d{4}))?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (numberFirst.Success)
            {
                return new ContractReferenceComponents(
                    NormalizeSeries(numberFirst.Groups["series"].Value),
                    NormalizeRegistrationNumber(numberFirst.Groups["number"].Value),
                    NormalizeYear(numberFirst.Groups["year"].Value));
            }

            var normalized = NormalizeAlphaNumeric(value);
            var identifier = Regex.Match(
                normalized,
                @"^[A-Z]{3}(?<year>\d{2})[A-Z0-9](?<number>\d{6})",
                RegexOptions.CultureInvariant);
            if (identifier.Success)
            {
                return new ContractReferenceComponents(
                    string.Empty,
                    NormalizeRegistrationNumber(identifier.Groups["number"].Value),
                    NormalizeYear(identifier.Groups["year"].Value));
            }

            var tokens = Regex.Matches(value.ToUpperInvariant(), @"[A-Z0-9]+")
                .Cast<Match>()
                .Select(x => x.Value)
                .ToList();
            var year = tokens.FirstOrDefault(x => Regex.IsMatch(x, @"^(?:19|20)\d{2}$")) ?? string.Empty;
            var ignoredWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SERIE", "NUMERO", "NUM", "ANNO", "CONTRATTO", "CODICE", "PROTOCOLLO"
            };
            var series = tokens.FirstOrDefault(x =>
                !ignoredWords.Contains(x) && Regex.IsMatch(x, @"^(?=.*[A-Z])[A-Z0-9]{1,6}$")) ?? string.Empty;
            var number = tokens
                .Where(x => Regex.IsMatch(x, @"^\d{3,10}$") && !string.Equals(x, year, StringComparison.Ordinal))
                .OrderByDescending(x => x.Length)
                .FirstOrDefault() ?? string.Empty;
            return new ContractReferenceComponents(
                NormalizeSeries(series),
                NormalizeRegistrationNumber(number),
                NormalizeYear(year));
        }

        private static string ExtractEventProtocol(string text)
        {
            var matches = Regex.Matches(
                    text,
                    @"(?:protocollo(?:\s+di\s+ricezione)?\s*[:\-]?|acquisito\s+con\s+protocollo)\s*([0-9]{10,25}(?:\s*[-/]\s*[0-9]{4,8})?)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(x => Regex.Replace(x.Groups[1].Value, @"\s+", " ").Trim())
                .OrderByDescending(x => x.Contains('-') || x.Contains('/') ? 1 : 0)
                .ThenByDescending(x => x.Length)
                .ToList();
            return matches.FirstOrDefault() ?? string.Empty;
        }

        private static string FormatFiscalCodes(PdfExtractedData document)
        {
            if (document.FiscalCodeRoles.Count == 0)
                return string.Join(", ", document.FiscalCodes);

            var withRoles = document.FiscalCodeRoles
                .Select(x => $"{x.FiscalCode} ({RoleDescription(x.Role)})")
                .ToList();
            var other = document.FiscalCodes
                .Where(cf => document.FiscalCodeRoles.All(x =>
                    !string.Equals(x.FiscalCode, cf, StringComparison.OrdinalIgnoreCase)))
                .Select(cf => $"{cf} (ruolo non rilevato)");
            return string.Join(", ", withRoles.Concat(other));
        }

        private static string RoleDescription(string role) => role.ToUpperInvariant() switch
        {
            "A" => "locatore cedente/locatore",
            "B" => "locatore cessionario/conduttore",
            "C" => "conduttore cedente",
            "D" => "conduttore cessionario",
            _ => $"ruolo {role}"
        };

        private static string NormalizeSeries(string? value)
            => Regex.Replace(value?.ToUpperInvariant() ?? string.Empty, @"[^A-Z0-9]", string.Empty);

        private static string NormalizeRegistrationNumber(string? value)
        {
            var digits = Regex.Replace(value ?? string.Empty, @"\D", string.Empty).TrimStart('0');
            return digits.Length == 0 && Regex.IsMatch(value ?? string.Empty, @"\d") ? "0" : digits;
        }

        private static string NormalizeYear(string? value)
        {
            var digits = Regex.Replace(value ?? string.Empty, @"\D", string.Empty);
            if (digits.Length == 2 && int.TryParse(digits, out var year))
                return (year >= 50 ? 1900 + year : 2000 + year).ToString(CultureInfo.InvariantCulture);
            return digits.Length == 4 ? digits : string.Empty;
        }

        private static bool ContainsNormalized(string? text, string? value)
        {
            var haystack = NormalizeReference(text);
            var needle = NormalizeReference(value);
            return !string.IsNullOrWhiteSpace(haystack) && !string.IsNullOrWhiteSpace(needle) && haystack.Contains(needle);
        }

        private static bool AreEqualOrContained(string? left, string? right)
        {
            var first = NormalizeReference(left);
            var second = NormalizeReference(right);
            return !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
                   (first == second || first.Contains(second) || second.Contains(first));
        }

        private static string NormalizeReference(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var normalized = value.ToUpperInvariant();
            normalized = Regex.Replace(normalized, @"\b(SERIE|NUMERO|NUM|N|NR|PROTOCOLLO|PROT|CODICE|IDENTIFICATIVO|CONTRATTO)\b", string.Empty);
            return Regex.Replace(normalized, @"[^A-Z0-9]", string.Empty);
        }

        private static string NormalizeAlphaNumeric(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);

        private static string NormalizeSpaces(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Regex.Replace(value, @"[ \t\r\f\v]+", " ").Trim();

        private static string MatchValue(string text, string pattern)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var normalized = Regex.Replace(value.Trim(), @"(\d{1,2})[\-.](\d{1,2})[\-.](\d{4})", "$1/$2/$3");
            var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy" };
            return DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.Date
                : null;
        }

        private static string ExtractYear(string? value)
        {
            var match = Regex.Match(value ?? string.Empty, @"\b(\d{4})\b");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string FirstNotEmpty(string first, string second) =>
            string.IsNullOrWhiteSpace(first) ? second : first;

        private static string DocumentLabel(DomicilioPdfDocumentType type) => type switch
        {
            DomicilioPdfDocumentType.Subentro => "documento di subentro",
            DomicilioPdfDocumentType.Proroga => "documento di proroga",
            _ => "contratto di locazione"
        };

        private static string CompactException(Exception exception)
        {
            var message = exception.GetBaseException().Message;
            message = Regex.Replace(message ?? string.Empty, @"\s+", " ").Trim();
            return message.Length <= 180 ? message : message[..180];
        }

        private sealed record FiscalCodeRole(string FiscalCode, string Role);

        private sealed record ContractReferenceComponents(string Series, string Number, string Year)
        {
            public bool HasComparableNumber => !string.IsNullOrWhiteSpace(Number);
        }

        private sealed class PdfExtractedData
        {
            public int PageCount { get; set; }
            public int PagesRead { get; set; }
            public bool Truncated { get; set; }
            public bool StoppedWhenComplete { get; set; }
            public bool FieldsExtracted { get; set; }
            public List<int> FailedPages { get; } = new();
            public string Text { get; set; } = string.Empty;
            public string ContractIdentifier { get; set; } = string.Empty;
            public string Series { get; set; } = string.Empty;
            public string Number { get; set; } = string.Empty;
            public string RegistrationYear { get; set; } = string.Empty;
            public string ReferencedContractSeries { get; set; } = string.Empty;
            public string ReferencedContractNumber { get; set; } = string.Empty;
            public string ReferencedContractYear { get; set; } = string.Empty;
            public string EventProtocol { get; set; } = string.Empty;
            public string RegistrationDate { get; set; } = string.Empty;
            public string StartDate { get; set; } = string.Empty;
            public string EndDate { get; set; } = string.Empty;
            public string EventDate { get; set; } = string.Empty;
            public List<string> FiscalCodes { get; set; } = new();
            public List<FiscalCodeRole> FiscalCodeRoles { get; set; } = new();
        }
    }
}
