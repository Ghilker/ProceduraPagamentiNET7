using ProcedureNet7;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;
using iText.Kernel.Pdf.Canvas.Parser;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

const string FiscalCode = "RSSMRA00A01H501U";
const string Identifier = "TJN26T012345000AA";
const string Protocol = "26090112345612345";
var expected = new DomicilioPdfExpectation(DomicilioPdfDocumentType.Contratto, FiscalCode,
    Protocol, new DateTime(2026, 9, 1), new DateTime(2026, 9, 1), new DateTime(2027, 8, 31), null,
    ComuneDomicilio: "ROMA", ReadAllPages: true);
var result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration()), expected);
Assert(result.State.Outcome == "CONFORME", "Protocollo della stessa ricevuta non riconosciuto: " + result.State.Descriptions);
Console.WriteLine("PASS protocollo di registrazione collegato alla ricevuta");
foreach (var alias in new[] { Protocol + "000001", Protocol + " - 000001", "3T/" + Identifier, "3T-" + Identifier + ".", "0123453T", "3T012345", "n. 012345-3T", "012 345" })
{
    result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration()), expected with { Reference = alias });
    Assert(result.State.Outcome == "CONFORME" && result.DocumentedCorrections.Count == 0 && !result.CanProposeCorrections,
        "Alias non riconosciuto senza cambiare dati: " + alias + ": " + result.State.Descriptions);
}
Console.WriteLine("PASS identificativi decorati, protocolli concatenati e numeri/serie compatti");

var wrongDates = expected with { Reference = Identifier, RegistrationDate = new DateTime(2026, 9, 2), StartDate = new DateTime(2026, 9, 2), EndDate = new DateTime(2028, 8, 31) };
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration()), wrongDates);
Assert(result.State.Outcome == "NON_CONFORME" && result.CanProposeCorrections && result.DocumentedCorrections.Count == 3,
    "Una correzione proposta non deve rendere conforme la discordanza: " + result.State.Descriptions);
Assert(result.DocumentedCorrections.All(e => e.Page == 1 && e.Label.Length > 0 && Regex.IsMatch(e.Value, @"^\d{4}-\d{2}-\d{2}$")) &&
    result.DocumentedCorrections.Select(e => e.Field).OrderBy(x => x).SequenceEqual(new[] { "EndDate", "RegistrationDate", "StartDate" }),
    "Evidenze con nomi campo, pagina, etichetta e date ISO non corrette.");
Console.WriteLine("PASS correzioni date documentate mantenendo NON_CONFORME");

result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration()), expected with { Reference = "T3" });
Assert(result.CanProposeCorrections && result.DocumentedCorrections.Single().Field == "Reference" && result.DocumentedCorrections[0].Value == Identifier,
    "Una serie incompleta non propone l'identificativo ufficiale.");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration()), wrongDates with { Reference = "TJS26T999999000AA" });
Assert(!result.CanProposeCorrections && result.DocumentedCorrections.Count == 0 && result.State.Codes.Contains("PDF_017"),
    "Un identificativo completo diverso non deve essere sostituito automaticamente.");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration()), wrongDates with { Reference = "3T/TJS26T999999000AA" });
Assert(!result.CanProposeCorrections && result.DocumentedCorrections.Count == 0, "Un ID completo diverso con prefisso non deve diventare un riferimento incompleto correggibile.");
Console.WriteLine("PASS riferimento incompleto distinguibile da un contratto completo diverso");

var genericDate = Registration().Replace("Il contratto e' stato registrato il 01/09/2026 al n. 012345-serie 3T", "In data 01/09/2026");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(genericDate), expected with { Reference = Identifier, RegistrationDate = new DateTime(2026, 9, 2) });
Assert(!result.CanProposeCorrections && result.DocumentedCorrections.Count == 0, "Il fallback generico 'in data' non e' prova per correggere la registrazione.");
foreach (var text in new[] { Registration().Replace(FiscalCode + " B", FiscalCode + " A"), Registration().Replace("Comune immobile: ROMA", "Comune immobile: MILANO") })
{
    result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text), wrongDates);
    Assert(!result.CanProposeCorrections && result.DocumentedCorrections.Count == 0, "Ruolo o comune incoerenti non bloccano le proposte.");
}
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration(), ""), wrongDates);
Assert(result.State.Codes.Contains("PDF_TEXT_INCOMPLETE") && !result.CanProposeCorrections && result.DocumentedCorrections.Count == 0,
    "Una pagina senza testo in lettura integrale deve bloccare le proposte, senza OCR.");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration()), wrongDates with { ReadAllPages = false });
Assert(!result.CanProposeCorrections && result.DocumentedCorrections.Count == 0, "Le proposte richiedono lettura integrale esplicita.");
Assert(!new DomicilioPdfExpectation(DomicilioPdfDocumentType.Contratto, FiscalCode, null, null, null, null, null).ReadAllPages,
    "Il default ReadAllPages deve restare false.");
Console.WriteLine("PASS guardie CF/ruolo/comune, lettura completa e provenienza non generica");

var baseReceipt = Registration().Replace("31/08/2027", "31/08/2025").Replace("01/09/2026", "01/09/2024").Replace("TJN26", "TJN24");
var chainExpected = expected with { Reference = "TJN24T012345000AA", RegistrationDate = new DateTime(2024, 9, 1), StartDate = new DateTime(2024, 9, 1) };
var extension1 = Extension("01/09/2025", "31/08/2026", "25090112345612345", "2024", "12345");
var extension2 = Extension("01/09/2026", "31/08/2027", Protocol, "2024", "12345");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(baseReceipt, extension1, extension2), chainExpected);
Assert(result.State.Outcome == "CONFORME" && result.ExtractedEndDate == chainExpected.EndDate && result.DocumentedCorrections.Count == 0,
    "La scadenza della catena documentata non e' riconosciuta: " + result.State.Descriptions);
Console.WriteLine("PASS scadenza da proroghe collegate per anno serie numero ufficio e ordine ricevute");
foreach (var bad in new[] {
    extension2.Replace("anno 2024", "anno 2023"),
    extension2.Replace("n. 12345", "n. 99999"),
    extension2.Replace("serie 3T", "serie 2T"),
    extension2.Replace("ROMA 1 - TRASTEVERE", "ROMA 5 - TUSCOLANO"),
    extension2.Replace("01/09/2026", "01/09/2025")
})
{
    result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(baseReceipt, extension1, bad), chainExpected);
    Assert(result.State.Findings.Any(f => f.Code.StartsWith("PDF_BUNDLE_")) && !result.CanProposeCorrections,
        "Una proroga non collegata o non ordinabile e' stata usata come scadenza.");
}
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(baseReceipt, extension2, extension1), chainExpected);
Assert(result.ExtractedEndDate == chainExpected.EndDate && !result.State.Codes.Contains("PDF_BUNDLE_"),
    "L'ordine fisico delle pagine non deve sostituire l'ordine temporale delle ricevute.");
result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(baseReceipt, extension1, extension2.Replace("31/08/2027", "31/07/2026")), chainExpected);
Assert(result.State.Codes.Contains("PDF_BUNDLE_SEQUENCE") && !result.CanProposeCorrections, "Una ricevuta successiva con scadenza inferiore non deve diventare un MAX silenzioso.");
Console.WriteLine("PASS ricevute estranee, ufficio diverso e sequenza incoerente restano manuali");

result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(Registration(), Registration().Replace(Identifier, "TJS26T999999000AA")), wrongDates);
Assert(result.State.Codes.Contains("PDF_RECEIPT_AMBIGUOUS") && !result.CanProposeCorrections, "Piu' contratti ufficiali nel PDF non bloccano le proposte.");
Console.WriteLine("PASS documenti ufficiali ambigui non producono correzioni");

foreach (var alias in new[] { Protocol, "3T/" + Identifier, "0123453T" })
    foreach (var text in new[] {
        Registration().Replace(FiscalCode, "VRDLGI00A01H501R"),
        Registration().Replace(FiscalCode + " B", FiscalCode + " A"),
        Registration().Replace("Comune immobile: ROMA", "Comune immobile: MILANO"),
        Registration() + "\n002 " + FiscalCode + " D"
    })
    {
        result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text), expected with { Reference = alias });
        Assert(result.State.Outcome != "CONFORME" && !result.CanProposeCorrections &&
            result.State.Findings.Any(f => f.Code is "PDF_008" or "PDF_024" || f.Code.StartsWith("PDF_TENANT_ROLE_")),
            "L'equivalenza degli estremi elimina un conflitto su identita', ruolo o comune.");
    }
foreach (var changedBase in new[] {
    baseReceipt.Replace(FiscalCode + " B", FiscalCode + " A"),
    baseReceipt.Replace("Comune immobile: ROMA", "Comune immobile: MILANO")
})
{
    result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(changedBase, extension1, extension2), chainExpected);
    Assert(result.ExtractedEndDate == chainExpected.EndDate && result.State.Outcome != "CONFORME" && !result.CanProposeCorrections,
        "La catena di proroghe elimina un conflitto di ruolo o comune.");
}
Console.WriteLine("PASS alias e proroghe conservano tutti i blocchi CF, ruolo e comune");

if (args.Length == 2 && args[0] == "--real") VerifyDownloaded(args[1]);
else if (args.Length != 0) throw new ArgumentException("Usare soltanto --real cartellaCampioni per i PDF locali gia' scaricati.");
Console.WriteLine("PASS suite evidenze completata senza database o OCR");

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static string Registration() => "Agenzia delle Entrate\nRicevuta di avvenuta registrazione contratti di locazione mod.RLI12\n" +
    "Ufficio territoriale di ROMA 1 - TRASTEVERE\nCodice Identificativo del contratto " + Identifier + "\n" +
    "In data 01/09/2026 il sistema ha acquisito con protocollo " + Protocol + " - 000001 la richiesta di registrazione telematica del contratto di locazione\n" +
    "Il contratto e' stato registrato il 01/09/2026 al n. 012345-serie 3T\n" +
    "Durata dal 01/09/2026 al 31/08/2027\n001 " + FiscalCode + " B\nComune immobile: ROMA";
static string Extension(string received, string end, string protocol, string year, string number) =>
    "Agenzia delle Entrate\nRicevuta di presentazione adempimenti successivi\nUfficio territoriale di DPRM1 UT ROMA 1 - TRASTEVERE\n" +
    "In data " + received + " il sistema ha acquisito con protocollo " + protocol + " - 000001 la comunicazione di proroga di un contratto di locazione fino alla data " + end + "\n" +
    "-registrato presso l'ufficio DPRM1 UT ROMA 1 - TRASTEVERE\n-anno " + year + " serie 3T n. " + number + "\n001 " + FiscalCode + " B";

static void VerifyDownloaded(string folder)
{
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "pdf-manifest.json")));
    using var references = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "ref-pdf-review.json")));
    using var dates = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "date-pdf-review.json")));
    using var samples = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "samples.json")));
    var outcomes = new List<object>();
    foreach (var item in references.RootElement.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("result").GetString() == "EQUIVALENZA_DOCUMENTATA"))
    {
        var id = item.GetProperty("id").GetInt32();
        var source = samples.RootElement.GetProperty("pure").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id);
        var operation = source.GetProperty("operations").EnumerateArray().First(o => o.GetProperty("TIPI_DOCUMENTI").GetString()!.Contains("CONTRATTO"));
        var path = item.GetProperty("provenance")[0].GetProperty("path").GetString()!;
        var result = ControlloDomicilioPdfAnalyzer.Analyze(File.ReadAllBytes(path), Expected(operation.GetProperty("DATI_PROPOSTI").GetString()!, Raw(path)));
        outcomes.Add(new { id, kind = "alias", passed = !result.State.Codes.Contains("PDF_017"), codes = result.State.Codes });
    }
    foreach (var item in dates.RootElement.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("category").GetString() == "SCADENZA_CONFERMATA_DA_PROROGA_NEL_PDF"))
    {
        var id = item.GetProperty("id").GetInt32();
        var path = item.GetProperty("matching_extension_pages")[0].GetProperty("path").GetString()!;
        var expected = Expected(item.GetProperty("proposed_data").GetString()!, Raw(path));
        var result = ControlloDomicilioPdfAnalyzer.Analyze(File.ReadAllBytes(path), expected);
        outcomes.Add(new { id, kind = "chain", passed = result.ExtractedEndDate == expected.EndDate && !result.State.Codes.Contains("PDF_BUNDLE_"), codes = result.State.Codes });
    }
    var scan = references.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == 23793);
    var scanPath = scan.GetProperty("provenance")[0].GetProperty("path").GetString()!;
    var scanRaw = Raw(scanPath);
    var scanOperation = samples.RootElement.GetProperty("pure").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == 23793)
        .GetProperty("operations").EnumerateArray().First(o => o.GetProperty("TIPI_DOCUMENTI").GetString()!.Contains("CONTRATTO"));
    var scanExpectation = Expected(scanOperation.GetProperty("DATI_PROPOSTI").GetString()!, scanRaw);
    var scanResult = ControlloDomicilioPdfAnalyzer.Analyze(File.ReadAllBytes(scanPath), scanExpectation);
    outcomes.Add(new { id = 23793, kind = "corrupt_embedded_text", passed = scanResult.State.Codes.Contains("PDF_017") &&
        !scanResult.CanProposeCorrections && scanResult.DocumentedCorrections.Count == 0, codes = scanResult.State.Codes });
    var json = JsonSerializer.Serialize(outcomes, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(folder, "reader-regression.json"), json);
    using var outcomeDoc = JsonDocument.Parse(json);
    var failures = outcomeDoc.RootElement.EnumerateArray().Where(r => !r.GetProperty("passed").GetBoolean()).ToArray();
    Console.WriteLine($"PDF locali: {outcomes.Count - failures.Length}/{outcomes.Count} controlli di riferimenti/catene superati; non e' una verifica di accoglibilita'.");
    foreach (var failure in failures) Console.WriteLine("FAIL local id=" + failure.GetProperty("id") + " codes=" + failure.GetProperty("codes"));
    Assert(failures.Length == 0, "Regressioni sui PDF scaricati: vedere reader-regression.json.");

    static string Raw(string path)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        return string.Join("\n", Enumerable.Range(1, pdf.GetNumberOfPages()).Select(p => PdfTextExtractor.GetTextFromPage(pdf.GetPage(p))));
    }
    static DomicilioPdfExpectation Expected(string proposed, string raw)
    {
        string Value(string key) => Regex.Match(proposed, @"(?:^|;\s*)" + Regex.Escape(key) + @"=([^;]*)").Groups[1].Value.Trim();
        DateTime? Date(string key) => DateTime.TryParseExact(Value(key), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        var municipality = Regex.Match(raw, @"Ubicato\s+nel\s+comune\s+di\s+([^\r\n]+?)(?:\s+Prov\.?|\r|\n|$)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        return new(DomicilioPdfDocumentType.Contratto, "", Value("Serie"), Date("Registrazione"), Date("Inizio"), Date("Fine"), null,
            RequireFiscalCode: false, ComuneDomicilio: municipality, ReadAllPages: true);
    }
}
static byte[] Pdf(params string[] pages)
{
    using var stream = new MemoryStream();
    using (var pdf = new PdfDocument(new PdfWriter(stream)))
        foreach (var text in pages)
        {
            var canvas = new PdfCanvas(pdf.AddNewPage());
            canvas.BeginText().SetFontAndSize(PdfFontFactory.CreateFont(), 8).MoveText(20, 800);
            foreach (var line in text.Split('\n')) canvas.ShowText(line).MoveText(0, -12);
            canvas.EndText();
        }
    return stream.ToArray();
}
