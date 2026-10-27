using DomicilioAutomation;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Microsoft.VisualBasic.FileIO;
using ProcedureNet7;
using System.Data.SqlClient;

if (args.Length != 0) throw new ArgumentException("Questa suite esegue soltanto test locali con executor simulato, senza database.");
var tests = new (string Name, Action Run)[]
{
    ("simulazione riconosce le istanze idonee senza chiamare l'executor", TestSimulation),
    ("l'avvio predefinito è una simulazione con identità di audit dedicata", TestDefaults),
    ("una richiesta gratuita valida non richiede un PDF artificiale", TestNoRequiredPdf),
    ("presa in carico e validazione incompleta impediscono l'automatismo", TestClaimAndValidation),
    ("lo snapshot deve essere presente e composto da 64 cifre esadecimali", TestSnapshot),
    ("PDF non leggibili o difformi restano esclusi", TestDocumentUncertainty),
    ("contratto e proroga richiedono un ruolo di conduttore certo", TestStrictTenantRoles),
    ("una proposta di rifiuto non causa un rifiuto automatico", TestNoAutomaticReject),
    ("la chiamata reale contiene soltanto le istanze idonee con snapshot immutato", TestEligibleBatch),
    ("un risultato di concorrenza viene conservato senza tentare di nuovo", TestConcurrentSkip),
    ("un'eccezione dopo il primo risultato conserva gli accoglimenti già ricevuti", TestPartialFailure),
    ("risultati mancanti e ID estranei non diventano accoglimenti", TestMissingAndUnknownResults),
    ("lettura parziale e campi PDF non verificati impediscono l'accoglimento", TestCompleteDocumentGate),
    ("il contratto originario non richiede il CF quando il subentro lo sostituisce", TestOriginFiscalCodePolicy),
    ("richieste duplicate e protocolli di risposta invalidi non provocano retry", TestProtocolGuards),
    ("il backend mancante produce un errore visibile senza perdere le istanze", TestMissingExecutor),
    ("il CSV distingue simulazione ed effetti reali su riepilogo e operazioni", TestCsvReport),
    ("il worker riceve le credenziali originali e i valori TLS del provider legacy", TestConnectionSettings),
    ("connessioni SQL incomplete falliscono senza esporre credenziali", TestInvalidConnectionSettings)
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine("PASS: " + test.Name);
}
Console.WriteLine($"PASS: {tests.Length} gruppi locali dell'automatismo completati; nessun database o backend reale eseguito.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void OutcomeIs(IReadOnlyList<DomicilioAutomationResult> results, int id, string expected)
{
    var row = results.Single(r => r.NumIstanza == id);
    Assert(row.Outcome == expected, $"Istanza sintetica {id}: atteso {expected}, ricevuto {row.Outcome}: {row.Reason}");
    Assert(!string.IsNullOrWhiteSpace(row.Reason), "L'esito deve spiegare perché l'istanza è stata lavorata o ignorata.");
}

static IstanzaDomicilioRecord NewRequest(int id = 1, bool claimed = false)
    => new()
    {
        NumIstanza = id, AnnoAccademico = "20262027", NumDomanda = 125, CodFiscale = "RSSMRA00A01H501U",
        Studente = "Studente sintetico", DataPresentazione = new DateTime(2026, 10, 1), PresaCarico = claimed,
        Operatore = claimed ? "OPERATORE_SINTETICO" : "", ExpectedFingerprint = new string('A', 64)
    };

static void Validate(IstanzaDomicilioRecord request)
    => IstanzaDomicilioValidator.Validate(request,
        new Dictionary<string, IstanzaDomicilioComune> { ["H501"] = new("ROMA", "RM") }, new DateTime(2026, 10, 23));

static IstanzaDomicilioRecord FreeRequest(int id = 1, bool claimed = false)
{
    var request = NewRequest(id, claimed);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = id * 10, Ordine = 1, Entita = 1, Operazione = 1,
        PayloadJson = "{\"codProvinciaDomicilio\":\"RM\",\"codComuneDomicilio\":\"H501\",\"indirizzoDomicilio\":\"Via Sintetica\",\"numCivicoDomicilio\":\"15\",\"capDomicilio\":\"00100\",\"tipoTitoloDomicilio\":0,\"dataInizio\":\"2026-09-01\",\"dataFine\":\"2027-08-31\"}"
    });
    Validate(request);
    Assert(IstanzaDomicilioReport.Decide(request).Azione == "ACCETTABILE_DIRETTAMENTE", "La fixture del domicilio gratuito deve essere coerente con le regole reali.");
    return request;
}

static IstanzaDomicilioRecord ContractRequest(int id = 1, string? fiscalCode = "001 RSSMRA00A01H501U B", string start = "01/09/2026")
{
    var request = NewRequest(id);
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = id * 10, Ordine = 1, Entita = 1, Operazione = 1,
        PayloadJson = "{\"codProvinciaDomicilio\":\"RM\",\"codComuneDomicilio\":\"H501\",\"indirizzoDomicilio\":\"Via Sintetica\",\"numCivicoDomicilio\":\"15\",\"capDomicilio\":\"00100\",\"tipoTitoloDomicilio\":1,\"inserimentoDatiContratto\":true}"
    });
    request.Operazioni.Add(new IstanzaDomicilioOperation
    {
        Id = id * 10 + 1, Ordine = 2, Entita = 2, Operazione = 1, IdOperazionePadre = id * 10, IdAllegatoContratto = 98,
        PayloadJson = "{\"tipoContrattoTitoloOneroso\":0,\"dataRegistrazioneContratto\":\"2026-08-31\",\"dataInizioContratto\":\"2026-09-01\",\"dataFineContratto\":\"2027-08-31\",\"numeroSerieContratto\":\"TJR25T017168000GG\"}"
    });
    Validate(request);
    var document = request.Operazioni[1].Documenti.Single();
    var text = fiscalCode == null ? "" : "Ricevuta di avvenuta registrazione\nDati del contratto\n" +
        "Codice Identificativo del contratto TJR25T017168000GG\nData di registrazione 31/08/2026\n" +
        $"Durata dal {start} al 31/08/2027\n{fiscalCode}\nComune immobile: Roma";
    document.Attachment = new DomicilioAttachmentRecord
    {
        IdAllegato = 98, CodTipoAllegato = "05", CodStatus = "01", CodFiscale = request.CodFiscale,
        NumDomanda = request.NumDomanda, FileName = "contratto-sintetico.pdf", ContentType = "application/pdf",
        Analysis = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text), document.Expectation! with { ReadAllPages = true })
    };
    return request;
}

static void TestSimulation()
{
    var executor = new RecordingExecutor(_ => throw new Exception("L'executor non può essere chiamato in simulazione."));
    var results = DomicilioAutomaticEngine.Run(new[] { FreeRequest(1), ContractRequest(2) }, true, executor);
    OutcomeIs(results, 1, "IDONEA");
    OutcomeIs(results, 2, "IDONEA");
    Assert(executor.Calls == 0, "La simulazione non deve mai invocare il backend, neppure per le istanze idonee.");
}

static void TestDefaults()
{
    Assert(new ArgsLavorazioneAutomaticaDomicilio().Simulazione, "La nuova procedura deve aprirsi con Simulazione attiva.");
    Assert(DomicilioAutomaticEngine.AuditUser == "AUTO_DOMICILIO", "L'identificativo di audit deve restare distinto dagli operatori umani.");
}

static void TestNoRequiredPdf()
{
    var request = FreeRequest();
    Assert(request.Operazioni.All(o => o.Documenti.Count == 0), "La fixture non deve contenere documenti non richiesti dal backend.");
    OutcomeIs(DomicilioAutomaticEngine.Run(new[] { request }, true), request.NumIstanza, "IDONEA");
}

static void TestClaimAndValidation()
{
    var claimed = FreeRequest(1, claimed: true);
    var incomplete = FreeRequest(2); incomplete.ValidazioneCompletata = false;
    var empty = NewRequest(3); empty.ValidazioneCompletata = true;
    var results = DomicilioAutomaticEngine.Run(new[] { claimed, incomplete, empty }, true);
    foreach (var request in new[] { claimed, incomplete, empty }) OutcomeIs(results, request.NumIstanza, "IGNORATA");
}

static void TestSnapshot()
{
    foreach (var fingerprint in new string?[] { null, "", new string('A', 63), new string('A', 65), new string('G', 64), " " + new string('A', 64), new string('A', 64) + "\n" })
    {
        var request = FreeRequest(); request.ExpectedFingerprint = fingerprint;
        OutcomeIs(DomicilioAutomaticEngine.Run(new[] { request }, true), 1, "IGNORATA");
    }
    var valid = FreeRequest(); valid.ExpectedFingerprint = new string('a', 64);
    OutcomeIs(DomicilioAutomaticEngine.Run(new[] { valid }, true), 1, "IDONEA");
}

static void TestDocumentUncertainty()
{
    var scanned = ContractRequest(1, fiscalCode: null);
    var mismatch = ContractRequest(2, start: "02/09/2026");
    var requestWarning = FreeRequest(3);
    requestWarning.State.Add(DomicilioControlSeverity.VerificaManuale, "IST_TEST", "Conflitto sui dati d'origine.", "Verificare nel backoffice.");
    var results = DomicilioAutomaticEngine.Run(new[] { scanned, mismatch, requestWarning }, true);
    foreach (var id in new[] { 1, 2, 3 }) OutcomeIs(results, id, "IGNORATA");
}

static void TestNoAutomaticReject()
{
    var rejectable = FreeRequest();
    var expectation = new DomicilioPdfExpectation(DomicilioPdfDocumentType.Subentro, rejectable.CodFiscale,
        "26070911540515489", null, new DateTime(2026, 7, 1), null, "TJR25T017168000GG", ReadAllPages: true);
    var text = "RICEVUTA DI PRESENTAZIONE ADEMPIMENTI SUCCESSIVI (mod.RLI12)\n" +
        "Acquisito con protocollo 26070911540515489 - 000001 la comunicazione di\n" +
        "cessione di un contratto di locazione dalla data 01/07/2026\n" +
        "La richiesta si riferisce al contratto: anno 2025 serie 3T n. 17168\n" +
        "SOGGETTI COINVOLTI NELLA CESSIONE/SUBENTRO DEL CONTRATTO\n001 RSSMRA00A01H501U C 002 VRDLGI00A01H501U D\n" +
        "TIPO DI DOCUMENTO: Registrazione dei contratti di locazione e di affitto";
    rejectable.Operazioni[0].Documenti.Add(new IstanzaDomicilioDocument
    {
        Tipo = "SUBENTRO", IdAllegato = 99, Expectation = expectation,
        Attachment = new DomicilioAttachmentRecord { IdAllegato = 99, Analysis = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text), expectation) }
    });
    Assert(IstanzaDomicilioReport.Decide(rejectable).Azione == "RIFIUTARE", "La fixture deve rappresentare un'incompatibilità d'identità riconosciuta dal controllo.");
    var executor = new RecordingExecutor(_ => throw new Exception("Una proposta di rifiuto non va inviata al backend automatico."));
    OutcomeIs(DomicilioAutomaticEngine.Run(new[] { rejectable }, false, executor), 1, "IGNORATA");
    Assert(executor.Calls == 0, "L'automatismo non deve eseguire rifiuti o correzioni.");
}

static void TestStrictTenantRoles()
{
    foreach (var sample in new[]
    {
        ("RSSMRA00A01H501U", "PDF_TENANT_ROLE_MISSING"),
        ("001 RSSMRA00A01H501U A", "PDF_TENANT_ROLE_INCOMPATIBLE"),
        ("001 RSSMRA00A01H501U C", "PDF_TENANT_ROLE_INCOMPATIBLE"),
        ("001 RSSMRA00A01H501U D", "PDF_TENANT_ROLE_INCOMPATIBLE"),
        ("001 RSSMRA00A01H501U B 002 RSSMRA00A01H501U C", "PDF_TENANT_ROLE_AMBIGUOUS")
    })
    {
        var request = ContractRequest(fiscalCode: sample.Item1);
        Assert(request.Operazioni[1].Documenti[0].Attachment!.Analysis!.State.Codes.Contains(sample.Item2), "Il ruolo del contratto deve essere verificato esplicitamente: " + sample.Item2);
        OutcomeIs(DomicilioAutomaticEngine.Run(new[] { request }, true), 1, "IGNORATA");
    }
    var expectation = new DomicilioPdfExpectation(DomicilioPdfDocumentType.Proroga, "RSSMRA00A01H501U", "26070911540515489", null,
        new DateTime(2027, 9, 1), new DateTime(2028, 8, 31), "TJR25T017168000GG", ReadAllPages: true);
    foreach (var sample in new[]
    {
        ("001 RSSMRA00A01H501U B", ""),
        ("RSSMRA00A01H501U", "PDF_TENANT_ROLE_MISSING"),
        ("001 RSSMRA00A01H501U A", "PDF_TENANT_ROLE_INCOMPATIBLE"),
        ("001 RSSMRA00A01H501U C", "PDF_TENANT_ROLE_INCOMPATIBLE"),
        ("001 RSSMRA00A01H501U D", "PDF_TENANT_ROLE_UNSUPPORTED"),
        ("001 RSSMRA00A01H501U B 002 RSSMRA00A01H501U C", "PDF_TENANT_ROLE_AMBIGUOUS")
    })
    {
        var text = "RICEVUTA DI PRESENTAZIONE ADEMPIMENTI SUCCESSIVI (mod.RLI12)\n" +
            "Acquisito con protocollo 26070911540515489 - 000001 la comunicazione di proroga\n" +
            "La richiesta si riferisce al contratto: anno 2025 serie 3T n. 17168\n" +
            "Decorrenza proroga: 01/09/2027\nScadenza proroga: 31/08/2028\n" + sample.Item1;
        var analysis = ControlloDomicilioPdfAnalyzer.Analyze(Pdf(text), expectation);
        Assert(sample.Item2 == "" ? analysis.State.Outcome == "CONFORME" : analysis.State.Codes.Contains(sample.Item2),
            "Il ruolo del conduttore della proroga deve essere certo: " + analysis.State.Descriptions);
        var request = FreeRequest();
        request.Operazioni[0].Documenti.Add(new IstanzaDomicilioDocument
        {
            Tipo = "PROROGA", IdAllegato = 99, Expectation = expectation,
            Attachment = new DomicilioAttachmentRecord { IdAllegato = 99, Analysis = analysis }
        });
        OutcomeIs(DomicilioAutomaticEngine.Run(new[] { request }, true), 1, sample.Item2 == "" ? "IDONEA" : "IGNORATA");
    }
}

static void TestEligibleBatch()
{
    var first = FreeRequest(1);
    var claimed = FreeRequest(2, claimed: true);
    var third = ContractRequest(3); third.ExpectedFingerprint = new string('b', 64);
    var executor = new RecordingExecutor(requests => requests.Select(r => new DomicilioAutomationResult(r.NumIstanza, "ACCOLTA", "Accoglimento sintetico completo.")));
    var results = DomicilioAutomaticEngine.Run(new[] { first, claimed, third }, false, executor);
    Assert(executor.Calls == 1 && executor.Received.Select(r => r.NumIstanza).ToHashSet().SetEquals(new[] { 1, 3 }), "La chiamata reale deve contenere una volta tutte e sole le istanze idonee.");
    Assert(executor.Received.Single(r => r.NumIstanza == 1).Fingerprint == first.ExpectedFingerprint && executor.Received.Single(r => r.NumIstanza == 3).Fingerprint == third.ExpectedFingerprint,
        "Lo snapshot verificato deve essere passato al backend senza sostituzioni o ricostruzioni tardive.");
    OutcomeIs(results, 1, "ACCOLTA"); OutcomeIs(results, 2, "IGNORATA"); OutcomeIs(results, 3, "ACCOLTA");
    Assert(third.Operazioni.Count == 2 && third.Operazioni.All(o => o.PayloadJson.Length > 0), "L'orchestrazione non deve correggere o rimuovere le operazioni originali.");
}

static void TestConcurrentSkip()
{
    var executor = new RecordingExecutor(requests => requests.Select(r => new DomicilioAutomationResult(r.NumIstanza, "IGNORATA", "Snapshot cambiato: nessuna modifica applicata.")));
    var results = DomicilioAutomaticEngine.Run(new[] { FreeRequest() }, false, executor);
    OutcomeIs(results, 1, "IGNORATA");
    Assert(executor.Calls == 1 && results[0].Reason.Contains("Snapshot cambiato"), "L'esito di concorrenza deve restare visibile e non attivare retry automatici.");
}

static void TestPartialFailure()
{
    static IEnumerable<DomicilioAutomationResult> Responses(IReadOnlyList<DomicilioAutomationRequest> requests)
    {
        yield return new DomicilioAutomationResult(requests[0].NumIstanza, "ACCOLTA", "Accoglimento già confermato dal backend sintetico.");
        throw new IOException("Interruzione sintetica dopo il primo risultato.");
    }
    var executor = new RecordingExecutor(Responses);
    var results = DomicilioAutomaticEngine.Run(new[] { FreeRequest(1), FreeRequest(2) }, false, executor);
    OutcomeIs(results, 1, "ACCOLTA"); OutcomeIs(results, 2, "ERRORE");
    Assert(executor.Calls == 1, "Un guasto non deve provocare una seconda applicazione dell'istanza già accolta.");
}

static void TestMissingAndUnknownResults()
{
    var executor = new RecordingExecutor(_ => new[]
    {
        new DomicilioAutomationResult(1, "ACCOLTA", "ID richiesto completo."),
        new DomicilioAutomationResult(999, "ACCOLTA", "ID non richiesto.")
    });
    var results = DomicilioAutomaticEngine.Run(new[] { FreeRequest(1), FreeRequest(2) }, false, executor);
    Assert(results.Count == 2 && results.All(r => r.NumIstanza != 999), "Un ID estraneo non può creare una riga di accoglimento.");
    OutcomeIs(results, 1, "ACCOLTA"); OutcomeIs(results, 2, "ERRORE");
}

static void TestCompleteDocumentGate()
{
    foreach (var flag in new[] { "pages", "zero-pages", "fiscal-code", "data" })
    {
        var request = ContractRequest();
        var analysis = request.Operazioni[1].Documenti[0].Attachment!.Analysis!;
        Assert(analysis.State.Findings.Count == 0 && analysis.FiscalCodePresent == true, "La fixture iniziale deve essere leggibile e congruente.");
        switch (flag)
        {
            case "pages": analysis.PageCount = 2; analysis.PagesRead = 1; break;
            case "zero-pages": analysis.PageCount = 0; analysis.PagesRead = 0; break;
            case "fiscal-code": analysis.FiscalCodePresent = null; break;
            case "data": analysis.DataCoherent = false; break;
        }
        OutcomeIs(DomicilioAutomaticEngine.Run(new[] { request }, true), 1, "IGNORATA");
    }
}

static void TestProtocolGuards()
{
    var duplicateExecutor = new RecordingExecutor(_ => throw new Exception("Una richiesta duplicata non deve arrivare al backend."));
    var duplicates = DomicilioAutomaticEngine.Run(new[] { FreeRequest(1), FreeRequest(1) }, false, duplicateExecutor);
    Assert(duplicates.Count == 1 && duplicateExecutor.Calls == 0, "Le istanze duplicate devono essere ignorate senza esecuzioni ripetute.");
    OutcomeIs(duplicates, 1, "IGNORATA");
    var invalidOutcome = new RecordingExecutor(_ => new[] { new DomicilioAutomationResult(1, "RIFIUTATA", "Esito non ammesso dal protocollo automatico.") });
    OutcomeIs(DomicilioAutomaticEngine.Run(new[] { FreeRequest(1) }, false, invalidOutcome), 1, "ERRORE");
    var repeatedResponse = new RecordingExecutor(_ => new[]
    {
        new DomicilioAutomationResult(1, "ACCOLTA", "Accoglimento già confermato."),
        new DomicilioAutomationResult(1, "ACCOLTA", "Risposta ripetuta."),
        new DomicilioAutomationResult(2, "ACCOLTA", "Non attendibile dopo la violazione del protocollo.")
    });
    var results = DomicilioAutomaticEngine.Run(new[] { FreeRequest(1), FreeRequest(2) }, false, repeatedResponse);
    OutcomeIs(results, 1, "ACCOLTA"); OutcomeIs(results, 2, "ERRORE");
    Assert(repeatedResponse.Calls == 1, "Una risposta ripetuta non deve riavviare un lotto potenzialmente già applicato.");
}

static void TestOriginFiscalCodePolicy()
{
    var request = ContractRequest();
    var original = request.Operazioni[1].Documenti.Single();
    original.Attachment!.Analysis!.FiscalCodePresent = null;
    request.Operazioni[1].Documenti[0] = new IstanzaDomicilioDocument
    {
        Tipo = original.Tipo, IdAllegato = original.IdAllegato, Attachment = original.Attachment,
        Expectation = original.Expectation! with { RequireFiscalCode = false }
    };
    OutcomeIs(DomicilioAutomaticEngine.Run(new[] { request }, true), 1, "IDONEA");
}

static void TestMissingExecutor()
{
    var results = DomicilioAutomaticEngine.Run(new[] { FreeRequest(1), FreeRequest(2) }, false);
    OutcomeIs(results, 1, "ERRORE"); OutcomeIs(results, 2, "ERRORE");
}

static void TestCsvReport()
{
    var requests = new[] { FreeRequest(1), ContractRequest(2) };
    var folder = Path.Combine(Path.GetTempPath(), "DomicilioAutomaticReport-" + Guid.NewGuid().ToString("N"));
    const string reason = "Motivo; con \"virgolette\"\ne seconda riga";
    try
    {
        foreach (var simulation in new[] { true, false })
        {
            var firstOutcome = simulation ? "IDONEA" : "ACCOLTA";
            var secondOutcome = simulation ? "IGNORATA" : "ERRORE";
            var results = new[] { new DomicilioAutomationResult(1, firstOutcome, reason), new DomicilioAutomationResult(2, secondOutcome, "Motivo sintetico.") };
            var path = DomicilioAutomaticReport.Export(requests, results, simulation, folder, "20262027");
            using var parser = new TextFieldParser(path, System.Text.Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            parser.SetDelimiters(";");
            var rows = new List<string[]>();
            while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
            Assert(rows.Count == 6, "Il CSV deve mantenere una riga per istanza e per operazione, anche con una nuova riga nella motivazione.");
            var headers = rows[0];
            int Col(string name)
            {
                var index = Array.IndexOf(headers, name);
                Assert(index >= 0, "Colonna di audit mancante: " + name);
                return index;
            }
            Assert(rows.Skip(1).All(row => row.Length == headers.Length && row[Col("MODALITA")] == (simulation ? "SIMULAZIONE" : "LAVORAZIONE") &&
                row[Col("UTENTE_LAVORAZIONE")] == "AUTO_DOMICILIO"), "Ogni riga deve rendere esplicita la modalità e l'identificativo di audit.");
            Assert(rows.Skip(1).Where(row => row[Col("NUM_ISTANZA")] == "1").All(row => row[Col("AZIONE_ISTANZA")] == firstOutcome && row[Col("MOTIVO_AZIONE")] == reason),
                "Riepilogo e dettaglio devono conservare esito e motivazione esatti, inclusa la punteggiatura CSV.");
            Assert(rows.Skip(1).Where(row => row[Col("NUM_ISTANZA")] == "2").All(row => row[Col("AZIONE_ISTANZA")] == secondOutcome),
                "Le operazioni non devono apparire accolte quando l'esito complessivo è ignorato o non confermato.");
        }
    }
    finally
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}

static void TestConnectionSettings()
{
    var original = new SqlConnectionStringBuilder
    {
        DataSource = "SERVER_SINTETICO", InitialCatalog = "DATABASE_SINTETICO", UserID = "UTENTE_SINTETICO",
        Password = "PASSWORD_SINTETICA;con=separatori", ApplicationName = "Applicazione sintetica"
    };
    var normalized = new SqlConnectionStringBuilder(DomicilioAutomaticConnectionSettings.FromOriginal(original.ConnectionString));
    Assert(normalized.Password == original.Password && normalized.UserID == original.UserID &&
        normalized.DataSource == original.DataSource && normalized.InitialCatalog == original.InitialCatalog &&
        normalized.ApplicationName == original.ApplicationName, "L'esportazione deve conservare i valori originali, inclusa la password con separatori.");
    Assert(normalized.ShouldSerialize("Encrypt") && !normalized.Encrypt &&
        normalized.ShouldSerialize("TrustServerCertificate") && !normalized.TrustServerCertificate,
        "Il worker deve ricevere Encrypt=False e TrustServerCertificate=False espliciti quando omessi nella connessione legacy.");
    Assert(!normalized.PersistSecurityInfo, "L'esportazione non deve abilitare PersistSecurityInfo.");

    original.Encrypt = true;
    original.TrustServerCertificate = false;
    normalized = new SqlConnectionStringBuilder(DomicilioAutomaticConnectionSettings.FromOriginal(original.ConnectionString));
    Assert(normalized.Encrypt && !normalized.TrustServerCertificate, "Encrypt=True e TrustServerCertificate=False espliciti devono rimanere invariati.");
    original.Encrypt = false;
    original.TrustServerCertificate = true;
    normalized = new SqlConnectionStringBuilder(DomicilioAutomaticConnectionSettings.FromOriginal(original.ConnectionString));
    Assert(!normalized.Encrypt && normalized.TrustServerCertificate, "Anche TrustServerCertificate=True esplicito deve rimanere invariato.");
    var integrated = new SqlConnectionStringBuilder(DomicilioAutomaticConnectionSettings.FromOriginal(
        "Server=SERVER_SINTETICO;Database=DATABASE_SINTETICO;Integrated Security=True"));
    Assert(integrated.IntegratedSecurity && integrated.Password.Length == 0, "L'autenticazione integrata deve essere ammessa senza password SQL.");
}

static void TestInvalidConnectionSettings()
{
    const string secret = "PASSWORD_SINTETICA_DA_NON_STAMPARE";
    string?[] invalid =
    {
        null, " ", "Server=SERVER_SINTETICO;Database=DATABASE_SINTETICO;User ID=UTENTE_SINTETICO",
        "Server=SERVER_SINTETICO;Database=DATABASE_SINTETICO;Password=" + secret,
        "Server=SERVER_SINTETICO;User ID=UTENTE_SINTETICO;Password=" + secret,
        "ChiaveNonValida=" + secret
    };
    var previousOutput = Console.Out;
    using var output = new StringWriter();
    string? expectedMessage = null;
    try
    {
        Console.SetOut(output);
        foreach (var value in invalid)
        {
            try
            {
                DomicilioAutomaticConnectionSettings.FromOriginal(value);
                throw new Exception("Una connessione incompleta non deve essere inviata al worker.");
            }
            catch (InvalidOperationException error)
            {
                expectedMessage ??= error.Message;
                Assert(error.Message == expectedMessage && !error.ToString().Contains(secret) && error.InnerException == null,
                    "Gli errori di connessione devono essere generici e senza dettagli o eccezioni interne contenenti l'input.");
            }
        }
        Assert(output.ToString().Length == 0, "La normalizzazione non deve scrivere connessioni o credenziali sulla console.");
    }
    finally { Console.SetOut(previousOutput); }
}

static byte[] Pdf(string text)
{
    using var stream = new MemoryStream();
    using (var pdf = new PdfDocument(new PdfWriter(stream)))
    {
        var canvas = new PdfCanvas(pdf.AddNewPage());
        canvas.BeginText().SetFontAndSize(PdfFontFactory.CreateFont(), 10).MoveText(30, 800);
        foreach (var line in text.Split('\n')) canvas.ShowText(line).MoveText(0, -15);
        canvas.EndText();
    }
    return stream.ToArray();
}

internal sealed class RecordingExecutor : IDomicilioAutomaticExecutor
{
    private readonly Func<IReadOnlyList<DomicilioAutomationRequest>, IEnumerable<DomicilioAutomationResult>> _execute;
    public RecordingExecutor(Func<IReadOnlyList<DomicilioAutomationRequest>, IEnumerable<DomicilioAutomationResult>> execute) => _execute = execute;
    public int Calls { get; private set; }
    public IReadOnlyList<DomicilioAutomationRequest> Received { get; private set; } = Array.Empty<DomicilioAutomationRequest>();
    public IEnumerable<DomicilioAutomationResult> Execute(IReadOnlyList<DomicilioAutomationRequest> requests)
    {
        Calls++;
        Received = requests.ToArray();
        return _execute(requests);
    }
}
