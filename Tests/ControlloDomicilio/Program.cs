using ProcedureNet7;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;

var expectation = new DomicilioPdfExpectation(DomicilioPdfDocumentType.Contratto,
    "RSSMRA00A01H501U", "ABC123", new DateTime(2026, 3, 31),
    new DateTime(2026, 4, 1), new DateTime(2027, 3, 31), null, ComuneDomicilio: "Roma");

// Facsimile testuale della schermata, con dati personali sintetici.
var screenshot = "Modello RLI TELEMATICO\nUfficio registrazione DPMI DI ROMA 2 - AURELIO\n" +
    "Serie 3T Numero 004285\nData registrazione 31/3/2026\n" +
    "Data inizio locazione 1/4/2026 Data fine locazione 31/3/2027\n" +
    "Codice identificativo contratto ABC123\nRSSMRA00A01H501U";
var result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(screenshot), expectation);
Assert(result.State.Findings.Any(x => x.Code == "PDF_024" &&
    x.Severity == DomicilioControlSeverity.NonConforme), "Documento senza comune: manca NON_CONFORME PDF_024");
Console.WriteLine("PASS: documento senza comune rifiutato.");

var complete = "Ricevuta di avvenuta registrazione\nDati del contratto\n" +
    "Codice Identificativo del contratto ABC123\n" +
    "Data di registrazione 31/03/2026\nDurata dal 01/04/2026 al 31/03/2027\nRSSMRA00A01H501U";

foreach (var invalidLocation in new[] {
    "Ufficio registrazione ROMA", "Residenza conduttore: Comune ROMA",
    "Comune immobile: Milano", "Comune immobile: Romagna" })
{
    result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(complete + "\n" + invalidLocation), expectation);
    Assert(result.State.Outcome == "NON_CONFORME" && result.State.Codes.Contains("PDF_024"),
        "Accettata località non valida: " + invalidLocation);
}
Console.WriteLine("PASS: ufficio, residenza e comuni diversi non accettati.");

result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(screenshot, complete, "Comune immobile: ROMA"), expectation);
Assert(result.State.Outcome == "CONFORME" && result.PagesRead == 3,
    "Il PDF con allegato valido in terza pagina deve essere conforme: " + result.State.Descriptions);
Console.WriteLine("PASS: schermata iniziale più allegato valido accettati.");

result = ControlloDomicilioPdfAnalyzer.Analyze(
    Pdf(complete, "Altra pagina", "Immobile sito nel comune di Roma", "Appendice"), expectation);
Assert(result.State.Outcome == "CONFORME" && result.PagesRead == 3,
    "La lettura deve aspettare il comune prima di terminare.");
Console.WriteLine("PASS: arresto anticipato solo dopo il comune.");

var pages = Enumerable.Repeat("Appendice", 10).ToArray();
pages[0] = complete;
pages[9] = "Dati degli immobili Comune: Roma";
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(pages), expectation);
Assert(result.State.Outcome == "CONFORME" && result.PagesRead == 10,
    "Allegato valido in decima pagina non accettato.");
Console.WriteLine("PASS: allegato valido in decima pagina accettato.");

result = ControlloDomicilioPdfAnalyzer.Analyze(
    Pdf(new[] { complete }.Concat(Enumerable.Repeat("Appendice", 9)).Append("Comune immobile: Roma").ToArray()),
    expectation);
Assert(result.State.Outcome == "CONFORME" && result.PagesRead == 11,
    "Allegato valido oltre la decima pagina non accettato.");
Console.WriteLine("PASS: allegato valido oltre la decima pagina accettato.");
var rliProperty = "DATI DEGLI IMMOBILI (importi in euro)\n" +
    "-N.progr. 001 Categoria cat. A3 Rendita cat. 1000,00\n" +
    "Tipologia immobile IMMOBILE Tipo catasto URBANO\nUbicato nel comune di ROMA Prov. RM";
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(complete, rliProperty), expectation);
Assert(result.State.Outcome == "CONFORME", "Comune nel formato RLI non riconosciuto.");
result = ControlloDomicilioPdfAnalyzer.Analyze(
    Pdf(complete, "DATI DEGLI IMMOBILI\nUfficio ubicato nel comune di ROMA"), expectation);
Assert(result.State.Codes.Contains("PDF_024"), "Il comune dell'ufficio non deve validare l'immobile.");

var subentryText = "RICEVUTA DI PRESENTAZIONE ADEMPIMENTI SUCCESSIVI (mod.RLI12)\n" +
    "Acquisito con protocollo 26070911540515489 - 000001 la comunicazione di\n" +
    "cessione di un contratto di locazione dalla data 01/07/2026\n" +
    "La richiesta si riferisce al contratto: anno 2025 serie 3T n. 17168\n" +
    "SOGGETTI COINVOLTI NELLA CESSIONE/SUBENTRO DEL CONTRATTO\n" +
    "001 RSSMRA00A01H501U C 002 VRDLGI00A01H501U D\n" +
    "TIPO DI DOCUMENTO: Registrazione dei contratti di locazione e di affitto";
var subentryExpectation = new DomicilioPdfExpectation(DomicilioPdfDocumentType.Subentro,
    "VRDLGI00A01H501U", "26070911540515489", null, new DateTime(2026, 7, 1), null, "TJR25T017168000GG");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(subentryText), subentryExpectation);
Assert(result.State.Outcome == "CONFORME" && result.TypeRecognized &&
    result.FiscalCodePresent == true && result.ExtractedStartDate == new DateTime(2026, 7, 1),
    "Subentro RLI non riconosciuto.");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(subentryText),
    subentryExpectation with { CodFiscale = "RSSMRA00A01H501U" });
Assert(result.State.Outcome == "NON_CONFORME" && result.State.Codes.Contains("PDF_020"),
    "Il cedente non deve essere accettato come cessionario.");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(subentryText),
    subentryExpectation with { ContractReference = "TJR25T099999000GG" });
Assert(result.State.Outcome == "NON_CONFORME" && result.State.Codes.Contains("PDF_023"),
    "Subentro associato al contratto sbagliato accettato.");
Assert(!ControlloDomicilioPdfAnalyzer.Analyze(Pdf(subentryText), expectation).TypeRecognized,
    "Subentro scambiato per contratto.");
Assert(!ControlloDomicilioPdfAnalyzer.Analyze(Pdf(complete, rliProperty), subentryExpectation).TypeRecognized,
    "Contratto scambiato per subentro.");
Console.WriteLine("PASS: formato RLI, distinzione documenti, ruolo e collegamento del subentro.");
if (args.Length == 2)
{
    var subentryBytes = File.ReadAllBytes(args[1]);
    using var subentryPdf = new PdfDocument(new PdfReader(new MemoryStream(subentryBytes)));
    var subjectText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(subentryPdf.GetPage(2));
    var incoming = System.Text.RegularExpressions.Regex.Match(subjectText, @"\b([A-Z0-9]{16})\s+D\b").Groups[1].Value;
    Assert(incoming.Length == 16, "Conduttore cessionario non trovato nel campione.");
    var contractExpected = new DomicilioPdfExpectation(DomicilioPdfDocumentType.Contratto,
        incoming, "TJR25T017168000GG", new DateTime(2025, 11, 22),
        new DateTime(2025, 9, 1), new DateTime(2028, 8, 31), null,
        RequireFiscalCode: false, ComuneDomicilio: "ROMA");
    var subentryExpected = new DomicilioPdfExpectation(DomicilioPdfDocumentType.Subentro,
        incoming, "26070911540515489", null, new DateTime(2026, 7, 1), null, "TJR25T017168000GG");
    var contractActual = ControlloDomicilioPdfAnalyzer.Analyze(File.ReadAllBytes(args[0]), contractExpected);
    var subentryActual = ControlloDomicilioPdfAnalyzer.Analyze(subentryBytes, subentryExpected);
    Console.WriteLine($"Campione contratto: {contractActual.State.Outcome}; tipo={contractActual.TypeRecognized}; pagine={contractActual.PagesRead}; anomalie={contractActual.State.Codes}");
    Console.WriteLine($"Campione subentro: {subentryActual.State.Outcome}; tipo={subentryActual.TypeRecognized}; pagine={subentryActual.PagesRead}; anomalie={subentryActual.State.Codes}");
    Assert(contractActual.TypeRecognized && contractActual.State.Outcome == "CONFORME", "Contratto campione non conforme: " + contractActual.State.Descriptions);
    Assert(subentryActual.TypeRecognized && subentryActual.State.Outcome == "CONFORME", "Subentro campione non conforme: " + subentryActual.State.Descriptions);
    Assert(contractActual.ExtractedRegistrationDate == contractExpected.RegistrationDate &&
        contractActual.ExtractedStartDate == contractExpected.StartDate &&
        contractActual.ExtractedEndDate == contractExpected.EndDate, "Date del contratto errate.");
    Assert(subentryActual.ExtractedStartDate == subentryExpected.StartDate &&
        subentryActual.FiscalCodePresent == true, "Decorrenza o ruolo del subentro errati.");
    Console.WriteLine("PASS: entrambi i PDF originali riconosciuti e coerenti.");
}
static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static byte[] Pdf(params string[] pages)
{
    using var stream = new MemoryStream();
    using (var pdf = new PdfDocument(new PdfWriter(stream)))
    {
        var font = PdfFontFactory.CreateFont();
        foreach (var text in pages)
        {
            var canvas = new PdfCanvas(pdf.AddNewPage());
            canvas.BeginText().SetFontAndSize(font, 10).MoveText(30, 800);
            foreach (var line in text.Split('\n'))
                canvas.ShowText(line).MoveText(0, -15);
            canvas.EndText();
        }
    }
    return stream.ToArray();
}
