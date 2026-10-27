using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using System.Text.Json;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using ProcedureNet7;

if (args.Length > 0 && args[0] is "--integration" or "--automation-simulation")
{
    Integration(args);
    return;
}
if (args.Length > 0) throw new ArgumentException("Usare senza argomenti per i test locali oppure --integration configPath outputFolder [codiceFiscale].");

var tests = new (string Name, Action Run)[]
{
    ("istanza vuota e validazione incompleta richiedono operatore", TestIncomplete),
    ("documento leggibile e congruente ammette accettazione", TestAccept),
    ("assenza di codice fiscale non equivale ad altra identità", TestMissingFiscalCode),
    ("identità estranea certa e ruolo incompatibile propongono rifiuto", TestRejectIdentityAndRole),
    ("lettura o contesto incerti impediscono il rifiuto", TestUncertaintyPreventsReject),
    ("date, estremi e comune difformi restano da validare", TestCorrectableMismatch),
    ("tutte le operazioni concorrono alla decisione dell'istanza", TestAggregate),
    ("payload malformato e filiazione errata richiedono verifica", TestMalformed),
    ("i tipi enumerati JSON mantengono il formato numerico del backoffice", TestStrictEnumNumbers),
    ("un allegato proposto inatteso impedisce l'accettazione diretta", TestUnexpectedAttachment),
    ("nuovo domicilio e contratto mantengono il loro collegamento", TestParentAndChild),
    ("subentro parziale usa dati correnti senza modificarli", TestPartialOverlay),
    ("versione originaria superata impedisce accettazione diretta", TestStaleSource),
    ("proroghe distinguono continuità, interruzioni e sovrapposizioni", TestExtensionBoundaries),
    ("contratti risultanti rispettano la tolleranza di dieci giorni", TestContractOverlap),
    ("versione di ripiego e allegati correnti restano in sola lettura", TestVersionFallbackAndAttachments),
    ("Learning Agreement richiede la lettura di un operatore", TestLearningAgreement),
    ("contratto ente leggibile con tutti i campi congruenti supera il controllo", TestEnteComplete),
    ("testo generico e campi ente assenti restano da validare", TestEnteMissingLabels),
    ("difformità ente leggibili richiedono validazione senza rifiuto", TestEnteMismatch),
    ("tipi ente e importi italiani sono confrontati senza ambiguità", TestEnteTypesAndAmounts),
    ("il contratto ente legge anche conflitti nelle ultime pagine", TestEnteFullRead),
    ("documenti ente senza testo e Learning Agreement restano manuali", TestSpecialManualPolicy),
    ("la lettura integrale RLI richiede tutti i campi univoci", TestRentalFullRead),
    ("il subentro integrale confronta il protocollo esplicito e il progressivo", TestSubentryFullRead),
    ("domicilio modificato e contratto inserito condividono lo scenario risultante", TestDomicileAndContractTogether),
    ("riepilogo e operazioni CSV mantengono ordine e numero", TestRows),
    ("anomalie globali mantengono tutte le righe da validare", TestGlobalFindingRows),
    ("CSV conserva punto e virgola, virgolette e nuove righe", TestCsvEscaping)
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine("PASS: " + test.Name);
}
Console.WriteLine($"PASS: {tests.Length} gruppi di regressione locali completati.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void ActionIs(IstanzaDomicilioRecord request, string expected)
{
    var actual = IstanzaDomicilioReport.Decide(request);
    Assert(actual.Azione == expected, $"Atteso {expected}, ricevuto {actual.Azione}: {actual.Motivazione}");
    Assert(!string.IsNullOrWhiteSpace(actual.Motivazione), "La proposta deve avere una motivazione operativa.");
}

static IstanzaDomicilioRecord Request(int number = 12, string student = "Studente sintetico", bool validated = true)
    => new()
    {
        NumIstanza = number, AnnoAccademico = "20262027", NumDomanda = 125,
        CodFiscale = "RSSMRA00A01H501U", Studente = student,
        DataPresentazione = new DateTime(2026, 10, 1), ValidazioneCompletata = validated
    };

static IstanzaDomicilioOperation Operation(int id = 1, int order = 1)
    => new() { Id = id, Ordine = order, Entita = 2, Operazione = 1, PayloadJson = "{}" };

static DomicilioPdfExpectation Expectation(DomicilioPdfDocumentType type = DomicilioPdfDocumentType.Contratto)
    => type == DomicilioPdfDocumentType.Subentro
        ? new(type, "RSSMRA00A01H501U", "26070911540515489", null, new DateTime(2026, 7, 1), null, "TJR25T017168000GG")
        : new(type, "RSSMRA00A01H501U", "ABC123", new DateTime(2026, 3, 31), new DateTime(2026, 4, 1), new DateTime(2027, 3, 31), null, ComuneDomicilio: "Roma");

static string ContractText(string fiscalCode = "RSSMRA00A01H501U", string reference = "ABC123", string start = "01/04/2026", string town = "Roma")
    => "Ricevuta di avvenuta registrazione\nDati del contratto\n" +
       $"Codice Identificativo del contratto {reference}\nData di registrazione 31/03/2026\n" +
       $"Durata dal {start} al 31/03/2027\n{fiscalCode}\nComune immobile: {town}";

static string SubentryText(string expectedRole)
    => "RICEVUTA DI PRESENTAZIONE ADEMPIMENTI SUCCESSIVI (mod.RLI12)\n" +
       "Acquisito con protocollo 26070911540515489 - 000001 la comunicazione di\n" +
       "cessione di un contratto di locazione dalla data 01/07/2026\n" +
       "La richiesta si riferisce al contratto: anno 2025 serie 3T n. 17168\n" +
       "SOGGETTI COINVOLTI NELLA CESSIONE/SUBENTRO DEL CONTRATTO\n" +
       $"001 RSSMRA00A01H501U {expectedRole} 002 VRDLGI00A01H501U {(expectedRole == "D" ? "C" : "D")}\n" +
       "TIPO DI DOCUMENTO: Registrazione dei contratti di locazione e di affitto";

static IstanzaDomicilioDocument Document(DomicilioPdfExpectation? expectation = null, string? text = null, int id = 98)
{
    expectation ??= Expectation();
    return new IstanzaDomicilioDocument
    {
        Tipo = expectation.Type.ToString().ToUpperInvariant(), IdAllegato = id, Expectation = expectation,
        Attachment = new DomicilioAttachmentRecord
        {
            IdAllegato = id, CodFiscale = expectation.CodFiscale, NumDomanda = 125,
            CodTipoAllegato = expectation.Type == DomicilioPdfDocumentType.Subentro ? "45" : "05",
            CodStatus = "01", FileName = "documento-sintetico.pdf", ContentType = "application/pdf",
            Analysis = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text ?? ContractText()), expectation)
        }
    };
}

static IstanzaDomicilioRecord RequestWithDocument(IstanzaDomicilioDocument? document = null)
{
    var request = Request();
    var operation = Operation();
    operation.Documenti.Add(document ?? Document());
    request.Operazioni.Add(operation);
    return request;
}

static void TestIncomplete()
{
    ActionIs(Request(), "DA_VALIDARE");
    var notValidated = Request(validated: false);
    notValidated.Operazioni.Add(Operation());
    ActionIs(notValidated, "DA_VALIDARE");
}

static void TestAccept()
{
    var valid = Document();
    Assert(valid.Attachment!.Analysis!.State.Outcome == "CONFORME", "Il PDF sintetico di controllo deve essere conforme.");
    ActionIs(RequestWithDocument(valid), "ACCETTABILE_DIRETTAMENTE");
    ActionIs(RequestWithDocument(Document(Expectation(DomicilioPdfDocumentType.Subentro), SubentryText("D"))), "ACCETTABILE_DIRETTAMENTE");
}

static void TestMissingFiscalCode()
{
    var missing = Document(text: ContractText(fiscalCode: ""));
    Assert(string.IsNullOrWhiteSpace(missing.Attachment!.Analysis!.ExtractedFiscalCodes), "Il campione non deve contenere codici fiscali.");
    ActionIs(RequestWithDocument(missing), "DA_VALIDARE");
    // The other person may be the landlord. A fiscal code alone does not establish
    // an incompatible tenant identity; neither does its explicit landlord role.
    ActionIs(RequestWithDocument(Document(text: ContractText("VRDLGI00A01H501U"))), "DA_VALIDARE");
    ActionIs(RequestWithDocument(Document(text: ContractText("001 VRDLGI00A01H501U A"))), "DA_VALIDARE");
    var unknown = Document(text: "Scansione senza dati riconoscibili");
    ActionIs(RequestWithDocument(unknown), "DA_VALIDARE");
    var unavailable = Document();
    unavailable.Attachment!.Analysis = ControlloDomicilioPdfAnalyzer.Analyze(Array.Empty<byte>(), Expectation());
    ActionIs(RequestWithDocument(unavailable), "DA_VALIDARE");
}

static void TestRejectIdentityAndRole()
{
    var other = Document(text: ContractText("001 VRDLGI00A01H501U B"));
    Assert(other.Attachment!.Analysis!.Readable && other.Attachment.Analysis.TypeRecognized, "L'identità estranea deve provenire da PDF leggibile e riconosciuto.");
    ActionIs(RequestWithDocument(other), "RIFIUTARE");
    var outgoing = Document(Expectation(DomicilioPdfDocumentType.Subentro), SubentryText("C"));
    Assert(outgoing.Attachment!.Analysis!.State.Codes.Contains("PDF_020"), "Il campione deve individuare il cedente.");
    ActionIs(RequestWithDocument(outgoing), "RIFIUTARE");
}

static void TestUncertaintyPreventsReject()
{
    foreach (var uncertainty in new[] { "unreadable", "type", "warning", "metadata", "notvalidated", "missingdoc", "uncertainotherdoc" })
    {
        var request = RequestWithDocument(Document(text: ContractText("001 VRDLGI00A01H501U B")));
        var document = request.Operazioni[0].Documenti[0];
        var analysis = document.Attachment!.Analysis!;
        switch (uncertainty)
        {
            case "unreadable": analysis.Readable = false; break;
            case "type": analysis.TypeRecognized = false; break;
            case "warning": analysis.State.Add(DomicilioControlSeverity.VerificaManuale, "PDF_019", "Una pagina non estratta.", "Leggere il documento completo."); break;
            case "metadata": request.Operazioni[0].State.Add(DomicilioControlSeverity.NonConforme, "IST_META_TEST", "Allegato collegato a domanda diversa.", "Verificare il collegamento."); break;
            case "notvalidated": request.ValidazioneCompletata = false; break;
            case "missingdoc": request.Operazioni[0].Documenti.Add(new IstanzaDomicilioDocument { Tipo = "PROROGA", IdAllegato = 99 }); break;
            case "uncertainotherdoc": request.Operazioni[0].Documenti.Add(Document(text: "Non interpretabile", id: 99)); break;
        }
        ActionIs(request, "DA_VALIDARE");
    }
}

static void TestCorrectableMismatch()
{
    foreach (var text in new[]
    {
        ContractText(start: "02/04/2026"), ContractText(reference: "XYZ999"), ContractText(town: "Milano")
    })
    {
        var document = Document(text: text);
        Assert(document.Attachment!.Analysis!.State.Outcome != "CONFORME", "Il campione deve rilevare una difformità reale.");
        ActionIs(RequestWithDocument(document), "DA_VALIDARE");
    }
}

static void TestAggregate()
{
    var request = RequestWithDocument();
    var second = Operation(2, 2);
    second.Documenti.Add(Document(text: ContractText("001 VRDLGI00A01H501U B"), id: 99));
    request.Operazioni.Add(second);
    ActionIs(request, "RIFIUTARE");
    request.State.Add(DomicilioControlSeverity.VerificaManuale, "IST_TEST", "Richiesta concorrente.", "Verificare le richieste dello studente.");
    ActionIs(request, "DA_VALIDARE");
}

static IReadOnlyDictionary<string, IstanzaDomicilioComune> Towns()
    => new Dictionary<string, IstanzaDomicilioComune>(StringComparer.OrdinalIgnoreCase) { ["H501"] = new("ROMA", "RM") };

static void Validate(IstanzaDomicilioRecord request)
    => IstanzaDomicilioValidator.Validate(request, Towns(), new DateTime(2026, 10, 16));

static IEnumerable<DomicilioControlFinding> Findings(IstanzaDomicilioRecord request)
    => request.State.Findings.Concat(request.Operazioni.SelectMany(o => o.State.Findings));

static void TestMalformed()
{
    foreach (var payload in new[] { "{non-json", "null", "[]" })
    {
        var request = Request(validated: false);
        request.Operazioni.Add(new IstanzaDomicilioOperation { Id = 1, Ordine = 1, Entita = 1, Operazione = 1, PayloadJson = payload });
        Validate(request);
        Assert(Findings(request).Any(), "Il payload non applicabile deve produrre un'anomalia.");
        ActionIs(request, "DA_VALIDARE");
    }
    var orphan = Request(validated: false);
    orphan.Operazioni.Add(new IstanzaDomicilioOperation { Id = 2, Ordine = 2, Entita = 2, Operazione = 1, IdOperazionePadre = 999, PayloadJson = ContractPayload() });
    Validate(orphan);
    Assert(Findings(orphan).Any(), "Il collegamento orfano deve impedire l'applicazione.");
    ActionIs(orphan, "DA_VALIDARE");
}

static void TestStrictEnumNumbers()
{
    foreach (var quoted in new[] { false, true })
    {
        var title = quoted ? "\"0\"" : "0";
        var request = Request(validated: false);
        request.Operazioni.Add(new IstanzaDomicilioOperation
        {
            Id = 1, Ordine = 1, Entita = 1, Operazione = 1,
            PayloadJson = "{\"codProvinciaDomicilio\":\"RM\",\"codComuneDomicilio\":\"H501\",\"indirizzoDomicilio\":\"Via Sintetica\",\"numCivicoDomicilio\":\"15\",\"capDomicilio\":\"00100\",\"tipoTitoloDomicilio\":" + title + ",\"dataInizio\":\"2026-04-01\",\"dataFine\":\"2027-03-31\"}"
        });
        Validate(request);
        Assert(Findings(request).Any(f => f.Code == "IST_008") == quoted, "Il titolo domicilio espresso come stringa deve essere incompatibile con il payload del backoffice.");
        ActionIs(request, quoted ? "DA_VALIDARE" : "ACCETTABILE_DIRETTAMENTE");
    }

    foreach (var property in new[] { "tipoContrattoTitoloOneroso", "tipoEnte" })
    foreach (var quoted in new[] { false, true })
    {
        var request = Request(validated: false);
        var current = CurrentDomicile();
        current.Contratti.Clear();
        request.DomiciliCorrenti.Add(current);
        var payload = property == "tipoContrattoTitoloOneroso"
            ? ContractPayload()
            : "{\"tipoContrattoTitoloOneroso\":1,\"tipoEnte\":0,\"denominazioneEnte\":\"Ente sintetico\",\"importoMensileRata\":100,\"dataInizioContratto\":\"2026-04-01\",\"dataFineContratto\":\"2027-03-31\"}";
        if (quoted) payload = payload.Replace("\"" + property + "\":0", "\"" + property + "\":\"0\"");
        request.Operazioni.Add(new IstanzaDomicilioOperation
        {
            Id = 1, Ordine = 1, Entita = 2, Operazione = 1, IdDomicilio = current.Id,
            DataValiditaOrigine = current.DataValidita, DataModificaOrigine = current.DataModifica,
            IdAllegatoContratto = 98, PayloadJson = payload
        });
        Validate(request);
        Assert(Findings(request).Any(f => f.Code == "IST_008") == quoted, "Il campo " + property + " deve richiedere il valore JSON numerico previsto dal backoffice.");
        if (quoted) ActionIs(request, "DA_VALIDARE");
        else Assert(!Findings(request).Any(), "Il payload numerico di controllo deve essere valido per " + property + ".");
    }
}

static void TestUnexpectedAttachment()
{
    var request = Request(validated: false);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = 1, Ordine = 1, Entita = 1, Operazione = 1, IdAllegatoContratto = 98,
        PayloadJson = "{\"codProvinciaDomicilio\":\"RM\",\"codComuneDomicilio\":\"H501\",\"indirizzoDomicilio\":\"Via Sintetica\",\"numCivicoDomicilio\":\"15\",\"capDomicilio\":\"00100\",\"tipoTitoloDomicilio\":0,\"dataInizio\":\"2026-04-01\",\"dataFine\":\"2027-03-31\"}"
    });
    Validate(request);
    Assert(Findings(request).Any(f => f.Code == "IST_DOC_07" && f.Severity == DomicilioControlSeverity.VerificaManuale), "Il domicilio gratuito con un allegato contrattuale inatteso deve richiedere la verifica del documento non controllato.");
    ActionIs(request, "DA_VALIDARE");
}

static string DomicilePayload()
    => "{\"codProvinciaDomicilio\":\"RM\",\"codComuneDomicilio\":\"H501\",\"indirizzoDomicilio\":\"Via Sintetica\",\"numCivicoDomicilio\":\"15\",\"capDomicilio\":\"00100\",\"tipoTitoloDomicilio\":1,\"inserimentoDatiContratto\":true}";

static string ContractPayload()
    => "{\"tipoContrattoTitoloOneroso\":0,\"dataRegistrazioneContratto\":\"2026-03-31\",\"dataInizioContratto\":\"2026-04-01\",\"dataFineContratto\":\"2027-03-31\",\"numeroSerieContratto\":\"ABC123\"}";

static void TestParentAndChild()
{
    var request = Request(validated: false);
    request.Operazioni.Add(new IstanzaDomicilioOperation { Id = 10, Ordine = 1, Entita = 1, Operazione = 1, PayloadJson = DomicilePayload() });
    request.Operazioni.Add(new IstanzaDomicilioOperation { Id = 11, Ordine = 2, Entita = 2, Operazione = 1, IdOperazionePadre = 10, IdAllegatoContratto = 98, PayloadJson = ContractPayload() });
    Validate(request);
    Assert(request.ValidazioneCompletata, "La validazione della richiesta deve risultare completata.");
    Assert(!Findings(request).Any(), "La coppia nuovo domicilio/contratto è applicabile: " + string.Join(" | ", Findings(request).Select(f => f.Description)));
    var document = request.Operazioni[1].Documenti.Single(d => d.Tipo == "CONTRATTO");
    Assert(document.Expectation?.ComuneDomicilio == "ROMA" && document.Expectation.Reference == "ABC123", "Il contratto nuovo deve usare il comune del domicilio padre e gli estremi proposti.");
    document.Attachment = Document(document.Expectation).Attachment;
    ActionIs(request, "ACCETTABILE_DIRETTAMENTE");
}

static DomicilioControlRecord CurrentDomicile()
{
    var current = new DomicilioControlRecord
    {
        Id = 20, AnnoAccademico = "20262027", NumDomanda = 125, CodFiscale = "RSSMRA00A01H501U",
        DataValidita = new DateTime(2026, 4, 1), DataModifica = new DateTime(2026, 4, 2),
        CodComune = "H501", CodProvincia = "RM", Comune = "ROMA", Indirizzo = "Via Sintetica",
        NumeroCivico = "15", Cap = "00100", TipoDomicilio = "1", InserimentoDatiContratto = true
    };
    current.Contratti.Add(new ContrattoControlRecord
    {
        Id = 30, IdDomicilio = 20, TipoContratto = 0, NumeroSerie = "ABC123",
        DataRegistrazione = new DateTime(2026, 3, 31), DataInizio = new DateTime(2026, 4, 1), DataFine = new DateTime(2027, 3, 31),
        DataValidita = new DateTime(2026, 4, 1), DataModifica = new DateTime(2026, 4, 2)
    });
    return current;
}

static void TestPartialOverlay()
{
    var request = Request(validated: false);
    var current = CurrentDomicile();
    request.DomiciliCorrenti.Add(current);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = 1, Ordine = 1, Entita = 2, Operazione = 2, IdDomicilio = 20, IdContratto = 30,
        DataValiditaOrigine = current.Contratti[0].DataValidita, DataModificaOrigine = current.Contratti[0].DataModifica,
        IdAllegatoSubentro = 99, PayloadJson = "{\"addSubentro\":true,\"numeroSerieSubentro\":\"SUB123\",\"dataInizioSubentro\":\"2026-07-01\"}"
    });
    Validate(request);
    Assert(!Findings(request).Any(), "Il subentro parziale con origine valida deve essere applicabile: " + string.Join(" | ", Findings(request).Select(f => f.Description)));
    var subentry = request.Operazioni[0].Documenti.Single(d => d.Tipo == "SUBENTRO");
    Assert(subentry.Expectation?.ContractReference == "ABC123" && subentry.Expectation.Reference == "SUB123" && subentry.Expectation.StartDate == new DateTime(2026, 7, 1), "Il PDF di subentro deve confrontarsi con dati risultanti e contratto originario.");
    Assert(current.Contratti[0].NumeroSerieSubentro == "" && !current.Contratti[0].DataInizioSubentro.HasValue && current.Contratti[0].DataInizio == new DateTime(2026, 4, 1), "La validazione in sola lettura non deve modificare i dati correnti.");
}

static void TestStaleSource()
{
    var request = Request(validated: false);
    var current = CurrentDomicile();
    request.DomiciliCorrenti.Add(current);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = 1, Ordine = 1, Entita = 2, Operazione = 3, IdDomicilio = 20, IdContratto = 30,
        DataValiditaOrigine = current.Contratti[0].DataValidita, DataModificaOrigine = new DateTime(2026, 4, 1),
        PayloadJson = "{\"addCessazione\":true,\"chiusura\":true,\"dataCessazione\":\"2026-10-01\"}"
    });
    Validate(request);
    Assert(Findings(request).Any(), "La revisione originaria non coincide con quella corrente e deve essere segnalata.");
    ActionIs(request, "DA_VALIDARE");
    Assert(!current.Contratti[0].DataCessazione.HasValue, "La cessazione proposta non deve modificare il dato corrente.");
}

static void TestRows()
{
    var first = Request(2);
    first.Operazioni.Add(Operation(22, 2));
    first.Operazioni.Add(Operation(21, 1));
    var second = Request(1);
    second.Operazioni.Add(Operation(11, 1));
    var rows = IstanzaDomicilioReport.BuildRows(new[] { first, second }).ToList();
    Assert(rows.Count == 5, "Occorrono una riga riepilogo per istanza e una riga per operazione.");
    Assert(rows.All(r => r.Length == IstanzaDomicilioReport.Headers.Length), "Le righe devono avere lo stesso numero di colonne dell'intestazione.");
    var h = IstanzaDomicilioReport.Headers;
    var num = Header(h, "NUM_ISTANZA");
    var operation = Header(h, "ID_OPERAZIONE");
    Assert(rows.Select(r => r[num]).SequenceEqual(new[] { "1", "1", "2", "2", "2" }), "Le istanze devono essere ordinate e mantenute contigue.");
    Assert(rows.Select(r => r[operation]).SequenceEqual(new[] { "", "11", "", "21", "22" }), "Il riepilogo deve precedere le operazioni ordinate per Ordine.");
}

static void TestGlobalFindingRows()
{
    foreach (var duplicateOrder in new[] { false, true })
    {
        var request = Request(validated: false);
        request.Operazioni.Add(Operation(1, 1));
        request.Operazioni.Add(Operation(2, duplicateOrder ? 1 : 2));
        if (duplicateOrder)
            Validate(request);
        else
        {
            request.ValidazioneCompletata = true;
            request.State.Add(DomicilioControlSeverity.NonConforme, "IST_TEST_GLOBAL",
                "La richiesta completa contiene un conflitto applicativo.", "Verificare tutte le operazioni prima dell'accoglimento.");
        }
        Assert(request.State.Findings.Any(), "Il caso di regressione deve contenere un'anomalia globale.");
        Assert(request.Operazioni.All(o => o.State.Findings.Count == 0), "Le singole operazioni del caso devono restare prive di anomalie locali.");
        var headers = IstanzaDomicilioReport.Headers;
        var action = Header(headers, "AZIONE_ISTANZA");
        var rows = IstanzaDomicilioReport.BuildRows(new[] { request }).ToList();
        Assert(rows.Count == 3 && rows.All(r => r[action] == "DA_VALIDARE"), "Il blocco globale deve impedire l'accettazione in ogni riga, anche se l'operazione isolata non ha errori.");
        Assert(!headers.Contains("AZIONE_OPERAZIONE"), "Il CSV deve fornire una sola proposta per l'istanza completa.");
    }
}

static IstanzaDomicilioRecord ExtensionRequest(DateTime start)
{
    var request = Request(validated: false);
    var current = CurrentDomicile();
    request.DomiciliCorrenti.Add(current);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = 1, Ordine = 1, Entita = 3, Operazione = 1, IdDomicilio = 20, IdContratto = 30,
        DataValiditaOrigine = current.Contratti[0].DataValidita, DataModificaOrigine = current.Contratti[0].DataModifica,
        IdAllegatoProroga = 99,
        PayloadJson = JsonSerializer.Serialize(new { numeroSerieProroga = "PRO123", dataDecorrenza = start, dataScadenza = new DateTime(2028, 3, 31) })
    });
    return request;
}

static void TestExtensionBoundaries()
{
    var continuous = ExtensionRequest(new DateTime(2027, 4, 1));
    Validate(continuous);
    Assert(!Findings(continuous).Any(), "La proroga dal giorno successivo alla scadenza deve essere continua.");
    var tenDays = ExtensionRequest(new DateTime(2027, 3, 21));
    Validate(tenDays);
    Assert(!Findings(tenDays).Any(), "Lo scarto di dieci giorni rientra nella tolleranza contrattuale.");
    var elevenDays = ExtensionRequest(new DateTime(2027, 3, 20));
    Validate(elevenDays);
    Assert(Findings(elevenDays).Any(f => f.Code == "IST_PER_07"), "La proroga che anticipa di undici giorni la scadenza deve essere segnalata.");
    ActionIs(elevenDays, "DA_VALIDARE");
    var gap = ExtensionRequest(new DateTime(2027, 4, 3));
    Validate(gap);
    Assert(Findings(gap).Any(f => f.Code == "IST_PER_09" && f.Severity == DomicilioControlSeverity.VerificaManuale && f.Description.Contains("2 giorni")), "I due giorni scoperti devono essere segnalati come verifica manuale.");
    ActionIs(gap, "DA_VALIDARE");
}

static void TestContractOverlap()
{
    foreach (var days in new[] { 10, 11 })
    {
        var request = Request(validated: false);
        var current = CurrentDomicile();
        request.DomiciliCorrenti.Add(current);
        var start = current.Contratti[0].DataFine!.Value.AddDays(-days);
        request.Operazioni.Add(new IstanzaDomicilioOperation
        {
            Id = 1, Ordine = 1, Entita = 2, Operazione = 1, IdDomicilio = current.Id,
            DataValiditaOrigine = current.DataValidita, DataModificaOrigine = current.DataModifica,
            IdAllegatoContratto = 99,
            PayloadJson = JsonSerializer.Serialize(new
            {
                tipoContrattoTitoloOneroso = 0, dataRegistrazioneContratto = new DateTime(2026, 10, 1),
                dataInizioContratto = start, dataFineContratto = new DateTime(2028, 3, 31), numeroSerieContratto = "NEW123"
            })
        });
        Validate(request);
        Assert(Findings(request).Any(f => f.Code == "IST_PER_10") == (days > 10), "La sovrapposizione deve cambiare esito oltre la tolleranza di dieci giorni.");
    }
}

static void TestVersionFallbackAndAttachments()
{
    var request = Request(validated: false);
    var current = CurrentDomicile();
    var contract = current.Contratti[0];
    contract.DataModifica = null;
    var original = Document().Attachment!;
    original.Analysis = null;
    contract.AllegatiContratto.Add(original);
    request.DomiciliCorrenti.Add(current);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = 1, Ordine = 1, Entita = 2, Operazione = 2, IdDomicilio = 20, IdContratto = 30,
        DataValiditaOrigine = contract.DataValidita, DataModificaOrigine = current.DataModifica,
        PayloadJson = ContractPayload()
    });
    Validate(request);
    Assert(!Findings(request).Any(), "La versione di ripiego dal domicilio deve coincidere con quella inviata: " + string.Join(" | ", Findings(request).Select(f => f.Description)));
    var proposed = request.Operazioni[0].Documenti.Single();
    Assert(proposed.Attachment != null && !ReferenceEquals(proposed.Attachment, original), "Il documento corrente deve essere copiato prima dell'analisi.");
    proposed.Attachment!.Analysis = Document().Attachment!.Analysis;
    Assert(original.Analysis == null, "L'analisi richiesta non deve essere assegnata all'allegato corrente originale.");
    ActionIs(request, "ACCETTABILE_DIRETTAMENTE");
}

static void TestLearningAgreement()
{
    var request = Request(validated: false);
    var current = CurrentDomicile();
    current.Contratti.Clear();
    request.DomiciliCorrenti.Add(current);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = 1, Ordine = 1, Entita = 2, Operazione = 1, IdDomicilio = 20,
        DataValiditaOrigine = current.DataValidita, DataModificaOrigine = current.DataModifica,
        IdAllegatoContratto = 98, IdAllegatoLearningAgreement = 99,
        PayloadJson = "{\"tipoContrattoTitoloOneroso\":1,\"tipoEnte\":3,\"denominazioneEnte\":\"Ente sintetico\",\"importoMensileRata\":100,\"dataInizioContratto\":\"2026-04-01\",\"dataFineContratto\":\"2027-03-31\"}"
    });
    Validate(request);
    Assert(!Findings(request).Any(), "Il rapporto Erasmus completo deve avere dati applicabili.");
    Assert(request.Operazioni[0].Documenti.Any(d => d.Tipo == "LEARNING_AGREEMENT" && d.IdAllegato == 99 && d.Expectation?.Type == DomicilioPdfDocumentType.LearningAgreement), "Il Learning Agreement deve avere una verifica dedicata, con esito sostanziale demandato all'operatore.");
    Assert(request.Operazioni[0].Documenti.Any(d => d.Tipo == "CONTRATTO" && d.Expectation?.Type == DomicilioPdfDocumentType.ContrattoEnte &&
        d.Expectation.TipoEnte == "SE" && d.Expectation.DenominazioneEnte == "Ente sintetico" && d.Expectation.ImportoMensileRata == 100m),
        "Il contratto ente deve confrontarsi con tipo, denominazione e rata dichiarati nell'istanza.");
    ActionIs(request, "DA_VALIDARE");
}

static DomicilioPdfExpectation EnteExpectation()
    => new(DomicilioPdfDocumentType.ContrattoEnte, "RSSMRA00A01H501U", null, null,
        new DateTime(2026, 9, 1), new DateTime(2027, 8, 31), null,
        ComuneDomicilio: "Roma", TipoEnte: "FA", DenominazioneEnte: "Fondazione Test", ImportoMensileRata: 500m);

static string EnteText()
    => "Contratto di alloggio\nCodice fiscale studente: RSSMRA00A01H501U\n" +
       "Data inizio contratto: 01/09/2026\nData fine contratto: 31/08/2027\n" +
       "Comune immobile: Roma\nDenominazione ente: Fondazione Test\nTipo ente: FA\nRata mensile: 500,00 euro";

static void TestEnteComplete()
{
    var document = Document(EnteExpectation(), EnteText());
    var analysis = document.Attachment!.Analysis!;
    Assert(analysis.State.Outcome == "CONFORME" && analysis.Readable && analysis.TypeRecognized && analysis.DataCoherent == true,
        "Il contratto ente con campi espliciti congruenti deve essere conforme: " + analysis.State.Descriptions);
    Assert(analysis.ExtractedStartDate == new DateTime(2026, 9, 1) && analysis.ExtractedEndDate == new DateTime(2027, 8, 31),
        "Le date estratte devono coincidere con il periodo di alloggio proposto.");
    ActionIs(RequestWithDocument(document), "ACCETTABILE_DIRETTAMENTE");
}

static void TestEnteMissingLabels()
{
    var generic = Document(EnteExpectation(), "Contratto di alloggio\nRSSMRA00A01H501U dal 01/09/2026 al 31/08/2027 a Roma.\nFondazione Test FA 500,00 euro.");
    Assert(generic.Attachment!.Analysis!.State.Findings.Any(f => f.Code == "PDF_ENTE_RATA"),
        "Una cifra nel testo senza etichetta non identifica una rata mensile.");
    ActionIs(RequestWithDocument(generic), "DA_VALIDARE");
    foreach (var label in new[] { "Codice fiscale studente", "Data inizio contratto", "Data fine contratto", "Comune immobile", "Denominazione ente", "Tipo ente", "Rata mensile" })
    {
        var text = string.Join("\n", EnteText().Split('\n').Where(line => !line.StartsWith(label + ":", StringComparison.Ordinal)));
        var document = Document(EnteExpectation(), text);
        Assert(document.Attachment!.Analysis!.State.Severity == DomicilioControlSeverity.VerificaManuale,
            "L'assenza del campo obbligatorio " + label + " deve produrre verifica manuale.");
        ActionIs(RequestWithDocument(document), "DA_VALIDARE");
    }
}

static void TestEnteMismatch()
{
    foreach (var replacement in new[]
    {
        ("01/09/2026", "02/09/2026", "INIZIO"), ("31/08/2027", "30/08/2027", "FINE"),
        ("Comune immobile: Roma", "Comune immobile: Milano", "COMUNE"),
        ("Fondazione Test", "Fondazione Diversa", "ENTE"), ("Tipo ente: FA", "Tipo ente: IR", "TIPO_ENTE"),
        ("500,00 euro", "600,00 euro", "RATA")
    })
    {
        var document = Document(EnteExpectation(), EnteText().Replace(replacement.Item1, replacement.Item2));
        Assert(document.Attachment!.Analysis!.State.Codes.Contains("PDF_ENTE_" + replacement.Item3 + "_DIFF"),
            "Il campo diverso letto univocamente deve essere segnalato: " + replacement.Item3);
        ActionIs(RequestWithDocument(document), "DA_VALIDARE");
    }
}

static void TestEnteTypesAndAmounts()
{
    foreach (var alias in new[] { ("EP", "Ente pubblico o privato"), ("IR", "Istituto religioso"), ("FA", "Fondazione o associazione"), ("SE", "Erasmus/Socrates") })
    {
        var document = Document(EnteExpectation() with { TipoEnte = alias.Item1 }, EnteText().Replace("Tipo ente: FA", "Tipo ente: " + alias.Item2));
        Assert(document.Attachment!.Analysis!.State.Outcome == "CONFORME", "La tipologia ente deve riconoscere l'etichetta " + alias.Item2 + ": " + document.Attachment.Analysis.State.Descriptions);
    }
    var ambiguous = Document(EnteExpectation() with { ImportoMensileRata = 1000m }, EnteText().Replace("500,00 euro", "1.000 euro"));
    Assert(ambiguous.Attachment!.Analysis!.State.Codes.Contains("PDF_ENTE_RATA"), "La rata 1.000 senza decimali è ambigua e richiede verifica manuale.");
    ActionIs(RequestWithDocument(ambiguous), "DA_VALIDARE");
    var explicitAmount = Document(EnteExpectation() with { ImportoMensileRata = 1000m }, EnteText().Replace("500,00 euro", "1.000,00 euro"));
    ActionIs(RequestWithDocument(explicitAmount), "ACCETTABILE_DIRETTAMENTE");
}

static void TestEnteFullRead()
{
    var document = Document(EnteExpectation(), EnteText());
    document.Attachment!.Analysis = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(EnteText(), "Appendice al contratto", "Rata mensile: 600,00 euro"), EnteExpectation());
    Assert(document.Attachment.Analysis.PagesRead == 3 && document.Attachment.Analysis.State.Codes.Contains("PDF_ENTE_RATA"),
        "L'analisi deve leggere e segnalare la rata diversa presente nella terza pagina.");
    ActionIs(RequestWithDocument(document), "DA_VALIDARE");
    document.Attachment.Analysis = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(EnteText(), ""), EnteExpectation());
    Assert(document.Attachment.Analysis.PagesRead == 2 && document.Attachment.Analysis.State.Codes.Contains("PDF_TEXT_INCOMPLETE"),
        "Una pagina non estraibile dopo i dati completi impedisce la validazione automatica integrale.");
    ActionIs(RequestWithDocument(document), "DA_VALIDARE");
}

static void TestSpecialManualPolicy()
{
    var scan = Document(EnteExpectation(), "");
    Assert(scan.Attachment!.Analysis!.PdfValid && !scan.Attachment.Analysis.Readable && scan.Attachment.Analysis.State.Codes.Contains("PDF_006"),
        "Il PDF senza testo deve restare manuale senza tentativi OCR.");
    ActionIs(RequestWithDocument(scan), "DA_VALIDARE");
    var learning = new DomicilioPdfExpectation(DomicilioPdfDocumentType.LearningAgreement, "RSSMRA00A01H501U", null, null, null, null, null);
    var document = Document(learning, "Learning Agreement\nRSSMRA00A01H501U\nAcademic year 2026/2027\nUniversity Test");
    Assert(document.Attachment!.Analysis!.Readable && document.Attachment.Analysis.TypeRecognized && document.Attachment.Analysis.State.Codes.Contains("PDF_LA_POLICY"),
        "Il titolo e il CF leggibili del Learning Agreement non bastano per verificare i dati di mobilità.");
    ActionIs(RequestWithDocument(document), "DA_VALIDARE");
}

static void TestRentalFullRead()
{
    const string reference = "TJR25T017168000GG";
    var expectation = Expectation() with { Reference = reference, ReadAllPages = true };
    var complete = ContractText(reference: reference).Replace("RSSMRA00A01H501U", "001 RSSMRA00A01H501U B");
    var good = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(complete, "Appendice", "Ultima pagina del contratto"), expectation);
    Assert(good.PagesRead == 3 && good.State.Outcome == "CONFORME", "La lettura integrale del contratto RLI completo deve leggere tutte le pagine: " + good.State.Descriptions);
    foreach (var conflict in new[] { "Durata dal 02/04/2026 al 31/03/2027", "Comune immobile: Milano", "Codice Identificativo del contratto TJR25T099999000GG" })
    {
        var result = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(complete, "Appendice", conflict), expectation);
        Assert(result.PagesRead == 3 && result.State.Codes.Contains("PDF_AMBIGUOUS_FIELDS"), "La lettura integrale non deve scegliere automaticamente tra campi incompatibili: " + conflict);
    }
    var blank = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(complete, ""), expectation);
    Assert(blank.State.Codes.Contains("PDF_TEXT_INCOMPLETE"), "Una pagina senza testo del contratto RLI impedisce il controllo integrale automatico.");
    var unlabelled = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(complete.Replace("Codice Identificativo del contratto " + reference, "Nota libera: " + reference)), expectation);
    Assert(unlabelled.State.Outcome != "CONFORME", "La sola presenza degli estremi in una nota non deve sostituire il campo contrattuale nella lettura integrale.");
}

static void TestSubentryFullRead()
{
    var expectation = Expectation(DomicilioPdfDocumentType.Subentro) with { ReadAllPages = true };
    var text = SubentryText("D");
    var positive = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text), expectation);
    Assert(positive.State.Outcome == "CONFORME", "Il protocollo base esplicito deve corrispondere anche con il progressivo presente nel PDF: " + positive.State.Descriptions);
    var exactProgressive = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text), expectation with { Reference = "26070911540515489-000001" });
    Assert(exactProgressive.State.Outcome == "CONFORME", "Il protocollo completo del subentro deve riconoscere il medesimo progressivo.");
    var otherProgressive = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text), expectation with { Reference = "26070911540515489-000002" });
    Assert(otherProgressive.State.Outcome != "CONFORME", "Un progressivo dichiarato diverso da quello del PDF non deve superare il controllo.");
    var noExplicitProtocol = text.Replace("Acquisito con protocollo 26070911540515489 - 000001 la comunicazione di", "Nota libera: 26070911540515489 - 000001. Comunicazione di");
    var missing = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(noExplicitProtocol), expectation);
    Assert(missing.State.Codes.Contains("PDF_EVENT_REFERENCE"), "Gli estremi in una nota libera non sostituiscono il protocollo esplicito dell'adempimento.");
}

static void TestDomicileAndContractTogether()
{
    var request = Request(validated: false);
    var current = CurrentDomicile();
    current.Contratti[0].AllegatiContratto.Add(Document().Attachment!);
    request.DomiciliCorrenti.Add(current);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = 1, Ordine = 1, Entita = 1, Operazione = 2, IdDomicilio = current.Id,
        DataValiditaOrigine = current.DataValidita, DataModificaOrigine = current.DataModifica,
        PayloadJson = DomicilePayload()
    });
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = 2, Ordine = 2, Entita = 2, Operazione = 1, IdDomicilio = current.Id,
        DataValiditaOrigine = current.DataValidita, DataModificaOrigine = current.DataModifica,
        IdAllegatoContratto = 99,
        PayloadJson = "{\"tipoContrattoTitoloOneroso\":0,\"dataRegistrazioneContratto\":\"2026-10-01\",\"dataInizioContratto\":\"2027-04-01\",\"dataFineContratto\":\"2028-03-31\",\"numeroSerieContratto\":\"NEW123\"}"
    });
    Validate(request);
    Assert(!Findings(request).Any(), "La modifica del domicilio e il nuovo contratto coerenti non devono richiedere documenti duplicati: " + string.Join(" | ", Findings(request).Select(f => f.Description)));
    Assert(request.Operazioni[0].Documenti.Count == 1 && request.Operazioni[0].Documenti[0].IdAllegato == 98, "L'operazione domicilio deve controllare il documento del contratto corrente.");
    Assert(request.Operazioni[1].Documenti.Count == 1 && request.Operazioni[1].Documenti[0].IdAllegato == 99 && request.Operazioni[1].Documenti[0].Expectation?.ComuneDomicilio == "ROMA", "Il nuovo documento deve appartenere alla sua operazione e usare il comune risultante.");
}

static int Header(string[] headers, string name)
{
    var index = Array.IndexOf(headers, name);
    Assert(index >= 0, "Colonna operativa mancante: " + name);
    return index;
}

static void TestCsvEscaping()
{
    var request = Request(student: "Cognome; \"Nome\"\nSeconda riga");
    request.Operazioni.Add(Operation());
    var folder = Path.Combine(Path.GetTempPath(), "ControlloIstanzeDomicilio-" + Guid.NewGuid().ToString("N"));
    try
    {
        var path = IstanzaDomicilioReport.Export(new[] { request }, folder, "20262027");
        Assert(File.Exists(path), "L'esportazione deve creare il file richiesto.");
        var rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
        Assert(rows.Count == 3, "Le nuove righe dentro un valore non devono creare righe CSV spurie.");
        var student = Header(rows[0], "STUDENTE");
        Assert(rows.Skip(1).All(r => r.Length == rows[0].Length && r[student] == request.Studente), "L'esportazione deve conservare esattamente semicolon, virgolette e newline nei valori.");
    }
    finally
    {
        // The target is a newly created, unique directory directly below the system temp directory.
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}

static List<string[]> ParseCsv(string text)
{
    var rows = new List<string[]>();
    var fields = new List<string>();
    var field = new StringBuilder();
    var quoted = false;
    for (var i = 0; i < text.Length; i++)
    {
        var c = text[i];
        if (c == '"')
        {
            if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
            else quoted = !quoted;
        }
        else if (!quoted && c == ';') { fields.Add(field.ToString()); field.Clear(); }
        else if (!quoted && (c == '\r' || c == '\n'))
        {
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            fields.Add(field.ToString()); rows.Add(fields.ToArray()); fields.Clear(); field.Clear();
        }
        else field.Append(c);
    }
    Assert(!quoted, "Campo CSV con virgolette non bilanciate.");
    if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); rows.Add(fields.ToArray()); }
    return rows;
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
            foreach (var line in text.Split('\n')) canvas.ShowText(line).MoveText(0, -15);
            canvas.EndText();
        }
    }
    return stream.ToArray();
}

static void Integration(string[] arguments)
{
    if (arguments.Length < 3 || arguments.Length > 4) throw new ArgumentException("Usare --integration configPath outputFolder [codiceFiscale].");
    using var config = JsonDocument.Parse(File.ReadAllText(arguments[1]), new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    });
    var connections = FindConnectionMain(config.RootElement).Distinct(StringComparer.Ordinal).ToArray();
    if (connections.Length != 1) throw new InvalidOperationException("Il file esterno deve contenere una sola connessione ConnectionMain.");
    var builder = new SqlConnectionStringBuilder(connections[0]);
    if (!string.Equals(builder.DataSource, "database-disco.ctayekcu0lnn.eu-south-1.rds.amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(builder.InitialCatalog, "ADISU", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(builder.UserID, "Verifica", StringComparison.Ordinal) || builder.IntegratedSecurity || builder.AttachDBFilename.Length > 0)
        throw new InvalidOperationException("Connessione esterna rifiutata: server, database o utente non corrispondono all'ambiente di test autorizzato.");
    if (arguments.Length == 4 && !System.Text.RegularExpressions.Regex.IsMatch(arguments[3], "^[A-Za-z0-9]{16}$"))
        throw new ArgumentException("Il filtro fiscale deve contenere sedici caratteri alfanumerici.");
    using var connection = new SqlConnection(builder.ConnectionString);
    using var procedure = new ControlloDomicilio(null, connection);
    var analysisArgs = new ArgsControlloDomicilio
    {
        _selectedAA = "20262027", _folderPath = Path.GetFullPath(arguments[2]),
        _codiciFiscali = arguments.Length == 4 ? new[] { arguments[3].ToUpperInvariant() } : null
    };
    if (arguments[0] == "--automation-simulation")
    {
        var requests = procedure.Analyze(analysisArgs, forAutomation: true);
        var results = DomicilioAutomaticEngine.Run(requests, simulation: true);
        Assert(results.Count == requests.Count, "Ogni istanza deve avere un risultato.");
        Assert(results.All(x => x.Outcome is "IDONEA" or "IGNORATA"), "La simulazione non può accogliere istanze.");
        Assert(requests.Where(x => x.PresaCarico).All(x => results.Single(r => r.NumIstanza == x.NumIstanza).Outcome == "IGNORATA"),
            "Le istanze prese in carico devono essere ignorate.");
        // A second SELECT-only capture must match every state used for analysis.
        var after = DomicilioAutomation.DomicilioAutomationSnapshot.CaptureFree(connection, analysisArgs._selectedAA);
        var changed = requests.Where(x => x.ExpectedFingerprint != null &&
            (!after.TryGetValue(x.NumIstanza, out var fingerprint) || fingerprint != x.ExpectedFingerprint)).ToList();
        var path = DomicilioAutomaticReport.Export(requests, results, true, analysisArgs._folderPath, analysisArgs._selectedAA);
        Console.WriteLine($"SIMULAZIONE CSV: {path}");
        Console.WriteLine($"SIMULAZIONE: {requests.Count} istanze; " + string.Join("; ", results.GroupBy(x => x.Outcome).Select(x => $"{x.Key}: {x.Count()}")));
        Console.WriteLine($"STATI CONFRONTATI: {requests.Count(x => x.ExpectedFingerprint != null)}; cambiati durante il controllo: {changed.Count}.");
    }
    else procedure.RunProcedure(analysisArgs);
}

static IEnumerable<string> FindConnectionMain(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("ConnectionMain") && property.Value.ValueKind == JsonValueKind.String)
                yield return property.Value.GetString()!;
            else foreach (var connection in FindConnectionMain(property.Value)) yield return connection;
        }
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray())
            foreach (var connection in FindConnectionMain(item)) yield return connection;
    }
}
