using System.Text.Json;
using DomicilioAutomation;
using ProcedureNet7;

var tests = new (string Name, Action Run)[]
{
    ("contratto: estremi e tre date, nomi JSON e campi estranei conservati", ContractCorrections),
    ("cloni separati di operazioni, gerarchie e allegati, senza riuso delle analisi", DetachedClone),
    ("subentro parziale: solo estremi e data del subentro", SubentryCorrections),
    ("proroga: estremi, decorrenza e scadenza", ExtensionCorrections),
    ("anomalie manuali o non correggibili di qualsiasi operazione bloccano il piano", AggregateGuards),
    ("solo allegati proposti espliciti della stessa domanda producono correzioni", SourceGuards),
    ("scansioni, lettura parziale, formati non verificabili e dati incerti restano esclusi", ReadingGuards),
    ("evidenze ambigue o senza pagina ed etichetta non sono applicabili", EvidenceGuards),
    ("chiavi assenti, valori null e duplicati non vengono riparati", PayloadGuards),
    ("un campo congruente non viene normalizzato senza difformità documentata", NoUnrequestedChanges),
    ("più operazioni mantengono ordine e atomicità della proposta", MultipleOperations),
    ("la rivalidazione espone una nuova sovrapposizione senza modificare l'originale", ResultingGraphGuards)
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine("PASS: " + test.Name);
}
Console.WriteLine($"PASS: {tests.Length} gruppi di regressione del planner.");

static DateTime Today() => new(2026, 11, 3);
static Dictionary<string, IstanzaDomicilioComune> Comuni() => new() { ["H501"] = new("ROMA", "RM") };
static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static IstanzaDomicilioRecord Request() => new()
{
    NumIstanza = 123, NumDomanda = 125, AnnoAccademico = "20262027", CodFiscale = "RSSMRA00A01H501U",
    Studente = "Studente sintetico", DataPresentazione = new DateTime(2026, 10, 1),
    ExpectedFingerprint = new string('a', 64)
};
static string ContractPayload() => """
    { "tipoContrattoTitoloOneroso":0,"numeroSerieContratto":"ABC123","dataRegistrazioneContratto":"2026-03-31T00:00:00",
      "DataInizioContratto":"2026-04-01","dataFineContratto":"2027-03-31", "unknown":{"source":[1.00,true,"keep"]} }
    """;
static IstanzaDomicilioRecord Contract(string? payload = null, bool extraGraph = false)
{
    var request = Request();
    request.Operazioni.Add(new()
    {
        Id = 10, Ordine = 1, Entita = 1, Operazione = 1,
        PayloadJson = """
          {"codProvinciaDomicilio":"RM","codComuneDomicilio":"H501","indirizzoDomicilio":"Via Nuova",
           "numCivicoDomicilio":"7","capDomicilio":"00100","tipoTitoloDomicilio":1,"inserimentoDatiContratto":true}
          """
    });
    request.Operazioni.Add(new()
    {
        Id = 11, Ordine = 2, Entita = 2, Operazione = 1, IdOperazionePadre = 10,
        IdAllegatoContratto = 98, PayloadJson = payload ?? ContractPayload()
    });
    if (extraGraph)
    {
        var old = Current();
        old.Contratti[0].DataInizio = new(2020, 4, 1);
        old.Contratti[0].DataFine = new(2021, 3, 31);
        old.Contratti[0].DataCessazione = new(2021, 3, 31);
        old.Contratti[0].Proroghe.Add(new() { Id = 50, IdContratto = 30, NumeroSerie = "EXT123", DataDecorrenza = new(2021, 4, 1), DataScadenza = new(2022, 3, 31) });
        old.Contratti[0].AllegatiContratto.Add(Attachment(71));
        old.Contratti[0].AllegatiSubentro.Add(Attachment(72));
        old.Contratti[0].AllegatiLearningAgreement.Add(Attachment(73));
        old.Contratti[0].Proroghe[0].Allegati.Add(Attachment(74));
        request.DomiciliCorrenti.Add(old);
    }
    Validate(request);
    return request;
}
static DomicilioControlRecord Current()
{
    var current = new DomicilioControlRecord
    {
        Id = 20, NumDomanda = 125, AnnoAccademico = "20262027", CodFiscale = "RSSMRA00A01H501U", Studente = "Studente sintetico",
        DataValidita = new(2026, 4, 1), DataModifica = new(2026, 4, 2), InserimentoDatiContratto = true,
        CodProvincia = "RM", CodComune = "H501", Comune = "ROMA", Indirizzo = "Via Corrente", NumeroCivico = "1", Cap = "00100", TipoDomicilio = "1"
    };
    current.Contratti.Add(new()
    {
        Id = 30, IdDomicilio = 20, TipoContratto = 0, NumeroSerie = "ABC123", DataRegistrazione = new(2026, 3, 31),
        DataInizio = new(2026, 4, 1), DataFine = new(2027, 3, 31), DataValidita = new(2026, 4, 1), DataModifica = new(2026, 4, 2)
    });
    return current;
}
static DomicilioAttachmentRecord Attachment(decimal id) => new()
{
    IdAllegato = id, CodFiscale = "RSSMRA00A01H501U", NumDomanda = 125, CodTipoAllegato = "44", CodStatus = "01",
    StatusDescription = "Documento sintetico", DataValidita = new(2026, 10, 1), FileName = "test.pdf", ContentType = "application/pdf"
};
static void Validate(IstanzaDomicilioRecord request)
{
    IstanzaDomicilioValidator.Validate(request, Comuni(), Today());
    Assert(request.ValidazioneCompletata && request.State.Findings.Count == 0,
        "Fixture strutturalmente valida: " + request.State.Descriptions);
    foreach (var document in request.Operazioni.SelectMany(o => o.Documenti))
    {
        document.Attachment = Attachment(document.IdAllegato ?? 98);
        document.Attachment.Analysis = new()
        {
            FileAvailable = true, PdfValid = true, Readable = true, TypeRecognized = true,
            FiscalCodePresent = true, DataCoherent = true, PageCount = 3, PagesRead = 3, CanProposeCorrections = true
        };
    }
}
static DomicilioPdfAnalysis Analysis(IstanzaDomicilioRecord request) => request.Operazioni.SelectMany(o => o.Documenti).Last().Attachment!.Analysis!;
static void Mismatch(IstanzaDomicilioRecord request, params (string Code, string Field, string Value)[] corrections)
{
    var analysis = Analysis(request);
    analysis.DataCoherent = false;
    foreach (var correction in corrections)
    {
        analysis.State.Add(DomicilioControlSeverity.NonConforme, correction.Code, correction.Field + " non congruente", "Verificare.");
        analysis.DocumentedCorrections.Add(new(correction.Field, correction.Value, 2, "Etichetta " + correction.Field));
    }
}
static DomicilioAutomaticCorrectionPlan Plan(IstanzaDomicilioRecord request) =>
    DomicilioAutomaticCorrectionPlanner.Plan(request, Comuni(), Today()) ?? throw new Exception("Piano atteso ma assente.");
static void NoPlan(IstanzaDomicilioRecord request) => Assert(DomicilioAutomaticCorrectionPlanner.Plan(request, Comuni(), Today()) == null, "Il caso incerto non deve produrre un piano.");
static string Value(string payload, string key)
{
    using var parsed = JsonDocument.Parse(payload);
    return parsed.RootElement.GetProperty(key).GetString()!;
}

static void ContractCorrections()
{
    var request = Contract();
    var before = request.Operazioni[1].PayloadJson;
    Mismatch(request, ("PDF_017", "Reference", "DEF456"), ("PDF_010", "RegistrationDate", "2026-03-30"),
        ("PDF_011", "StartDate", "2026-04-02"), ("PDF_012", "EndDate", "2027-04-01"));
    var plan = Plan(request);
    var correction = plan.Corrections.Single();
    Assert(correction.OperationId == 11 && correction.BeforePayload == before && correction.Fields.Count == 4, "Prima del patch resta il JSON esatto.");
    Assert(correction.Fields.Single(f => f.Field == "DataInizioContratto").After == "2026-04-02", "Il nome effettivo del campo mantiene il maiuscolo.");
    Assert(correction.Fields.All(f => f.Page == 2 && f.AttachmentId == 98 && f.Label.StartsWith("Etichetta ")), "Le fonti PDF accompagnano ogni campo.");
    Assert(Value(correction.AfterPayload, "numeroSerieContratto") == "DEF456" && Value(correction.AfterPayload, "dataFineContratto") == "2027-04-01", "Tutti i campi richiesti cambiano.");
    using var after = JsonDocument.Parse(correction.AfterPayload);
    Assert(after.RootElement.GetProperty("unknown").GetProperty("source")[0].GetRawText() == "1.00", "Il numero estraneo preserva il formato originale.");
    Assert(plan.Candidate.ValidazioneCompletata && plan.Candidate.State.Findings.Count == 0, "Il grafo corretto viene rivalidato.");
    Assert(request.Operazioni[1].PayloadJson == before && Analysis(request).State.Severity == DomicilioControlSeverity.NonConforme,
        "I dati e le evidenze originali non vengono cambiati.");
    Assert(DomicilioAutomationCorrectionPatch.Apply(before, correction, 2, 1) == correction.AfterPayload, "Il backend condivide lo stesso guard.");
}
static void DetachedClone()
{
    var request = Contract(extraGraph: true);
    Mismatch(request, ("PDF_017", "Reference", "DEF456"));
    var plan = Plan(request);
    var candidate = plan.Candidate;
    Assert(!ReferenceEquals(candidate, request) && !ReferenceEquals(candidate.Operazioni[0], request.Operazioni[0]), "Operazioni separate.");
    Assert(candidate.ExpectedFingerprint == request.ExpectedFingerprint && candidate.CorrectionCandidate == null && candidate.ProposedCorrections.Count == 0, "Metadati iniziali conservati senza ricorsione dei piani.");
    var old = request.DomiciliCorrenti[0]; var clone = candidate.DomiciliCorrenti[0];
    Assert(!ReferenceEquals(old, clone) && !ReferenceEquals(old.Contratti[0], clone.Contratti[0]) && !ReferenceEquals(old.Contratti[0].Proroghe[0], clone.Contratti[0].Proroghe[0]), "Gerarchia interamente separata.");
    var originals = old.Contratti[0].AllegatiContratto.Concat(old.Contratti[0].AllegatiSubentro).Concat(old.Contratti[0].AllegatiLearningAgreement).Concat(old.Contratti[0].Proroghe[0].Allegati).ToArray();
    var copies = clone.Contratti[0].AllegatiContratto.Concat(clone.Contratti[0].AllegatiSubentro).Concat(clone.Contratti[0].AllegatiLearningAgreement).Concat(clone.Contratti[0].Proroghe[0].Allegati).ToArray();
    Assert(originals.Zip(copies).All(p => !ReferenceEquals(p.First, p.Second) && p.First.IdAllegato == p.Second.IdAllegato && p.Second.Analysis == null), "Tutti gli allegati sono cloni da rianalizzare.");
    clone.Contratti[0].NumeroSerie = "mutato";
    copies[0].FileName = "mutato.pdf";
    Assert(old.Contratti[0].NumeroSerie == "ABC123" && originals[0].FileName == "test.pdf", "La copia non modifica i dati correnti.");
    Assert(candidate.Operazioni.SelectMany(o => o.Documenti).All(d => d.Attachment?.Analysis == null), "Le analisi proposte non ereditano le vecchie difformità.");
}
static IstanzaDomicilioRecord ChildOperation(bool subentry)
{
    var request = Request(); var domicile = Current(); var contract = domicile.Contratti[0];
    request.DomiciliCorrenti.Add(domicile);
    request.Operazioni.Add(new()
    {
        Id = 1, Ordine = 1, Entita = subentry ? 2 : 3, Operazione = subentry ? 2 : 1, IdDomicilio = 20, IdContratto = 30,
        DataValiditaOrigine = contract.DataValidita, DataModificaOrigine = contract.DataModifica,
        IdAllegatoSubentro = subentry ? 99 : null, IdAllegatoProroga = subentry ? null : 100,
        PayloadJson = subentry ? "{\"addSubentro\":true,\"numeroSerieSubentro\":\"SUB123\",\"dataInizioSubentro\":\"2026-07-01\",\"unknown\":17}" :
            "{\"numeroSerieProroga\":\"EXT123\",\"dataDecorrenza\":\"2027-03-31\",\"dataScadenza\":\"2028-03-31\"}"
    });
    Validate(request);
    return request;
}
static void SubentryCorrections()
{
    var request = ChildOperation(true);
    Mismatch(request, ("PDF_017", "Reference", "SUB456"), ("PDF_013", "StartDate", "2026-08-01"));
    var plan = Plan(request); var op = plan.Candidate.Operazioni[0];
    Assert(plan.Corrections.Single().Fields.Select(f => f.Field).SequenceEqual(new[] { "numeroSerieSubentro", "dataInizioSubentro" }), "Soltanto il payload parziale del subentro cambia.");
    Assert(op.Documenti.Single().Expectation!.ContractReference == "ABC123" && op.Documenti.Single().Expectation!.StartDate == new DateTime(2026, 8, 1), "Il documento usa il nuovo subentro e mantiene il contratto originario.");
    Assert(request.DomiciliCorrenti[0].Contratti[0].NumeroSerieSubentro == "", "La registrazione corrente resta invariata.");
}
static void ExtensionCorrections()
{
    var request = ChildOperation(false);
    Mismatch(request, ("PDF_017", "Reference", "EXT456"), ("PDF_014", "StartDate", "2027-04-01"), ("PDF_015", "EndDate", "2028-04-01"));
    var plan = Plan(request);
    Assert(plan.Corrections.Single().Fields.Select(f => f.Field).SequenceEqual(new[] { "numeroSerieProroga", "dataDecorrenza", "dataScadenza" }), "Nomi effettivi dei tre campi proroga.");
    Assert(plan.Candidate.State.Findings.Count == 0 && plan.Candidate.Operazioni[0].Documenti.Single().Expectation!.EndDate == new DateTime(2028, 4, 1), "La proroga risultante è rivalidata.");
}
static void AggregateGuards()
{
    foreach (var place in new[] { "request", "otherOperation", "pdf" })
    {
        var request = Contract(); Mismatch(request, ("PDF_017", "Reference", "DEF456"));
        var state = place == "request" ? request.State : place == "otherOperation" ? request.Operazioni[0].State : Analysis(request).State;
        state.Add(DomicilioControlSeverity.VerificaManuale, "PDF_TEXT_INCOMPLETE", "Scansione", "Verificare.");
        NoPlan(request);
    }
    foreach (var code in new[] { "PDF_023", "PDF_ROLE_CONTRACT", "IST_PER_10" })
    {
        var request = Contract(); Mismatch(request, ("PDF_017", "Reference", "DEF456"));
        request.State.Add(DomicilioControlSeverity.NonConforme, code, "Altro problema", "Verificare."); NoPlan(request);
    }
    var incomplete = Contract(); Mismatch(incomplete, ("PDF_017", "Reference", "DEF456")); incomplete.ValidazioneCompletata = false; NoPlan(incomplete);
}
static void SourceGuards()
{
    var request = Contract(); Mismatch(request, ("PDF_017", "Reference", "DEF456"));
    var doc = request.Operazioni[1].Documenti[0]; doc.Attachment = Attachment(999); doc.Attachment.Analysis = new() { FileAvailable = true, PdfValid = true, Readable = true, TypeRecognized = true, DataCoherent = false, FiscalCodePresent = true, PageCount = 3, PagesRead = 3, CanProposeCorrections = true };
    Mismatch(request, ("PDF_017", "Reference", "DEF456")); NoPlan(request);
    request = Contract(); Mismatch(request, ("PDF_017", "Reference", "DEF456"));
    doc = request.Operazioni[1].Documenti[0]; var analysis = doc.Attachment!.Analysis;
    doc.Attachment = new() { IdAllegato = 98, CodFiscale = request.CodFiscale, NumDomanda = 999, Analysis = analysis }; NoPlan(request);
    request = ChildOperation(false); Mismatch(request, ("PDF_017", "Reference", "EXT456"));
    var original = request.Operazioni[0];
    var replacement = new IstanzaDomicilioOperation { Id = original.Id, Ordine = original.Ordine, Entita = 3, Operazione = 2, PayloadJson = original.PayloadJson };
    replacement.Documenti.AddRange(original.Documenti); request.Operazioni.Clear(); request.Operazioni.Add(replacement); NoPlan(request);
}
static void ReadingGuards()
{
    foreach (var mutate in new Action<DomicilioPdfAnalysis>[]
    {
        a => a.PagesRead = 2, a => a.PageCount = 0, a => a.Readable = false, a => a.PdfValid = false,
        a => a.FileAvailable = false, a => a.TypeRecognized = false, a => a.CanProposeCorrections = false,
        a => a.FiscalCodePresent = null, a => a.DataCoherent = null
    })
    {
        var request = Contract(); Mismatch(request, ("PDF_017", "Reference", "DEF456")); mutate(Analysis(request)); NoPlan(request);
    }
    foreach (var type in new[] { DomicilioPdfDocumentType.ContrattoEnte, DomicilioPdfDocumentType.LearningAgreement })
    {
        var request = Contract(); Mismatch(request, ("PDF_017", "Reference", "DEF456"));
        var original = request.Operazioni[1].Documenti[0]; request.Operazioni[1].Documenti[0] = new()
        { Tipo = original.Tipo, IdAllegato = original.IdAllegato, Expectation = original.Expectation! with { Type = type }, Attachment = original.Attachment };
        NoPlan(request);
    }
}
static void EvidenceGuards()
{
    foreach (var evidence in new[]
    {
        new DomicilioPdfFieldEvidence("Reference", "DEF456", 0, "Estremi"), new("Reference", "DEF456", 4, "Estremi"),
        new("Reference", "DEF456", 2, ""), new("reference", "DEF456", 2, "Estremi"),
        new("Reference", "", 2, "Estremi"), new("Role", "B", 2, "Ruolo")
    })
    {
        var request = Contract(); Mismatch(request, ("PDF_017", "Reference", "DEF456"));
        Analysis(request).DocumentedCorrections.Clear(); Analysis(request).DocumentedCorrections.Add(evidence); NoPlan(request);
    }
    var ambiguous = Contract(); Mismatch(ambiguous, ("PDF_017", "Reference", "DEF456"));
    Analysis(ambiguous).DocumentedCorrections.Add(new("Reference", "GHI789", 3, "Estremi")); NoPlan(ambiguous);
    var badDate = Contract(); Mismatch(badDate, ("PDF_011", "StartDate", "02/04/2026")); NoPlan(badDate);
}
static void PayloadGuards()
{
    foreach (var payload in new[]
    {
        ContractPayload().Replace("\"numeroSerieContratto\":\"ABC123\",", ""),
        ContractPayload().Replace("\"numeroSerieContratto\":\"ABC123\"", "\"numeroSerieContratto\":null"),
        ContractPayload().Replace("\"numeroSerieContratto\":\"ABC123\"", "\"numeroSerieContratto\":123"),
        ContractPayload().Replace("\"numeroSerieContratto\":\"ABC123\"", "\"numeroSerieContratto\":\"ABC123\",\"NumeroSerieContratto\":\"ABC123\""),
        ContractPayload().Replace("\"unknown\":{\"source\":[1.00,true,\"keep\"]}", "\"unknown\":{\"x\":1,\"X\":1}"),
        ContractPayload().Replace("\"tipoContrattoTitoloOneroso\":0", "\"tipoContrattoTitoloOneroso\":\"0\""),
        ContractPayload().Replace("\"tipoContrattoTitoloOneroso\":0", "\"tipoContrattoTitoloOneroso\":1"),
        ContractPayload().Replace("\"tipoContrattoTitoloOneroso\":0", "\"tipoContrattoTitoloOneroso\":0,\"chiusura\":true"),
        ContractPayload().Replace("\"tipoContrattoTitoloOneroso\":0", "\"tipoContrattoTitoloOneroso\":0,\"addCessazione\":true"),
        "[]", "{"
    })
    {
        // Deliberately mark a corrupt pre-existing result as analysed: the producer must still reject its unsafe JSON patch.
        var request = Contract(); var original = request.Operazioni[1];
        var changed = new IstanzaDomicilioOperation { Id = 11, Ordine = 2, Entita = 2, Operazione = 1, IdOperazionePadre = 10, IdAllegatoContratto = 98, PayloadJson = payload };
        changed.Documenti.AddRange(original.Documenti); request.Operazioni[1] = changed;
        Mismatch(request, ("PDF_017", "Reference", "DEF456")); NoPlan(request);
    }
}
static void NoUnrequestedChanges()
{
    var request = Contract(); NoPlan(request);
    Mismatch(request, ("PDF_017", "Reference", "DEF456"));
    Analysis(request).DocumentedCorrections.Add(new("StartDate", "2026-04-03", 3, "Decorrenza"));
    var correction = Plan(request).Corrections.Single();
    Assert(correction.Fields.Count == 1 && Value(correction.AfterPayload, "DataInizioContratto") == "2026-04-01", "Le sole evidenze senza una difformità non autorizzano altri cambiamenti.");
    request = Contract(); Mismatch(request, ("PDF_011", "StartDate", "2026-04-01")); NoPlan(request);
}
static void ResultingGraphGuards()
{
    var request = Contract();
    var existing = Current(); existing.Contratti[0].DataInizio = new(2027, 4, 1); existing.Contratti[0].DataFine = new(2028, 3, 31);
    existing.Contratti[0].NumeroSerie = "PRE123"; request.DomiciliCorrenti.Add(existing);
    Mismatch(request, ("PDF_012", "EndDate", "2027-05-01"));
    var plan = Plan(request);
    Assert(plan.Candidate.State.Findings.Any(f => f.Code == "IST_PER_10"), "La correzione documentata rivela la sovrapposizione aggregata e resta una proposta bloccata.");
    Assert(!request.State.Findings.Any(f => f.Code == "IST_PER_10") && Value(request.Operazioni[1].PayloadJson, "dataFineContratto") == "2027-03-31", "L'originale non riceve gli errori o le date della copia.");
}

static void MultipleOperations()
{
    var request = Contract();
    request.Operazioni.Add(new()
    {
        Id = 12, Ordine = 3, Entita = 2, Operazione = 1, IdOperazionePadre = 10, IdAllegatoContratto = 101,
        PayloadJson = ContractPayload().Replace("ABC123", "NEXT123").Replace("2026-04-01", "2027-04-01").Replace("2027-03-31", "2028-03-31")
    });
    // The second validation starts with separate derived state/document collections.
    var raw = Request(); raw.Operazioni.AddRange(request.Operazioni.Select(o => new IstanzaDomicilioOperation
    {
        Id = o.Id, Ordine = o.Ordine, Entita = o.Entita, Operazione = o.Operazione, IdOperazionePadre = o.IdOperazionePadre,
        IdAllegatoContratto = o.IdAllegatoContratto, PayloadJson = o.PayloadJson
    }));
    Validate(raw);
    foreach (var operation in raw.Operazioni.Where(o => o.Entita == 2))
    {
        var analysis = operation.Documenti.Single().Attachment!.Analysis!;
        analysis.DataCoherent = false;
        analysis.State.Add(DomicilioControlSeverity.NonConforme, "PDF_017", "Estremi difformi", "Verificare.");
        analysis.DocumentedCorrections.Add(new("Reference", "CORR" + operation.Id, 2, "Estremi registrazione"));
    }
    raw.Operazioni.Reverse();
    var plan = Plan(raw);
    Assert(plan.Corrections.Select(c => c.OperationId).SequenceEqual(new[] { 11, 12 }), "Il piano segue l'ordine applicativo anche se la lista è invertita.");
    Assert(plan.Candidate.State.Findings.Count == 0 && plan.Corrections.SelectMany(c => c.Fields).Select(f => f.AttachmentId).SequenceEqual(new decimal[] { 98, 101 }), "Ogni operazione conserva la propria fonte PDF.");
    var second = raw.Operazioni.Single(o => o.Id == 12).Documenti.Single().Attachment!.Analysis!;
    second.CanProposeCorrections = false;
    NoPlan(raw);
    Assert(Value(raw.Operazioni.Single(o => o.Id == 11).PayloadJson, "numeroSerieContratto") == "ABC123", "Una seconda operazione non correggibile non applica parzialmente la prima.");
}
