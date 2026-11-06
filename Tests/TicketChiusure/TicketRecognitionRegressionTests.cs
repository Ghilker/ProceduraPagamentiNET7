using System.Data;
using ClosedXML.Excel;
using ProcedureNet7;

internal static class TicketRecognitionRegressionTests
{
    public static void Run(Action<string, Action> check)
    {
        var cases = new (string Name, string Subject, string Message, bool Close, TicketOfficeRecord? Record)[]
        {
            ("540539: prima rata nel racconto, saldo richiesto", "2 rata borsa di studio", "Ho ricevuto la prima rata nel mese di aprile, quando riceverò il resto?", false,
                new() { BenefitAmount = 2587.80m, BsPaidAmount = 1293.90m, BsFirstInstallmentOriginalAmount = 1293.90m, BsFirstInstallmentMandates = "M1", BsMandates = "M1" }),
            ("531209: tassa regionale non è borsa", "Rimborso tasse", "Risultato vincitore della borsa di studio, non ho ricevuto l'accredito del pagamento delle tre annualità della tassa regionale versata. Si chiede di verificare quando verrà erogata.", false, Paid()),
            ("438035: revisione personale non è informazione", "Revisione importo", "Chiedo la revisione dell'importo in quanto come specificato nella domanda ho diritto alla maggiorazione del 10% come da bando.", false, Paid()),
            ("426734: trattenuta contestata anche con borsa pagata", "Possibile errore nel calcolo seconda rata", "Ho ricevuto la seconda rata con una trattenuta di 338 euro. Vorrei sapere se potrebbe trattarsi di un errore nel calcolo della rata.", false, Paid()),
            ("487503: residenza prima del verbo", "Cambio residenza e domicilio", "Vi comunico che la mia residenza e il mio domicilio sono cambiati.", false, Paid()),
            ("486039: non me lo permette", "Domicilio", "Quando provo a modificare il domicilio, non me lo permette.", false, Paid()),
            ("507018: impossibilità e non compariva", "Contratto di locazione", "Comunico impossibilità di caricare il contratto di locazione in quanto non compariva la finestra apposita.", false, Paid()),
            ("536918: sezione consultabile ma modifica assente", "Contratto di locazione", "Come suggerito nel bando stavo per inserire il contratto di locazione, ma riesco solamente a consultare e non mi dà l'opzione di modificare e aggiungere allegati.", false, Paid()),
            ("488249: documento richiesto per il permesso", "Documento residenza", "Potete inviarmi il documento di residenza che mi serve per l'appuntamento del permesso di soggiorno?", false, Paid()),
            ("515025: domanda trasmessa non prova presenza in graduatoria", "Assenza in graduatoria", "Nonostante abbia inviato la domanda, non risulto presente in alcuna categoria della graduatoria. Allego ricevuta di trasmissione.", false, Paid()),
            ("516510: invio non prova aggiornamento ISEE", "Richiesta modifica ISEE", "Ho trasmesso la domanda ma non riesco ad inserire il nuovo ISEE senza difformità. Chiedo di aggiornare il mio ISEE.", false, Paid()),
            ("544105: trasmissione non prova posizione premio", "Premio di laurea", "Ho richiesto il premio di laurea con trasmissione della domanda ma non riesco a visualizzare la mia posizione.", false, Paid()),
            ("546779: assenza blocco non prova rettifica CFU", "Riesame domanda", "Nel portale risultano 120 CFU mentre sul libretto risultano 84 CFU. Chiedo di correggere il dato e riesaminare la domanda affinché sia rimossa l'esclusione.", false, Paid()),
            ("541577: apostrofo corrotto nel contratto", "Contratto affitto", "Allego il contratto d?affitto, non saprei dove altro mandarlo", true, null),
            ("544362: apostrofo corrotto nell'istanza", "Contratto di locazione", "Salve potete accettare l?istanza relativa al cambio indirizzo del domicilio per la borsa di studio", true, null),
            ("Conferma invio istanza nel contesto", "Domicilio", "Ho trasmesso l'istanza con il contratto di locazione. Vorrei conferma della ricezione corretta.", true, null),
            ("Canale di caricamento nel contesto", "Contratto", "Carico contratto di locazione e registrazione. È necessario caricarli da qualche altra parte?", true, null),
            ("Esclusione non è semplice calendario", "Graduatoria", "Risulto ancora idoneo nonostante lo scorrimento totale della graduatoria, come mai?", false, Paid()),
            ("Indipendente non è merito", "Blocco studente indipendente", "Vorrei spiegazioni sull'incongruenza in merito al blocco studente indipendente.", false, new() { Blocks = "BSI - Condizione studente indipendente irregolare" }),
            ("Doppio blocco include titolo magistrale", "Blocchi", "Ho due blocchi: verifica iscrizione e matricola specialistica non in possesso del titolo.", false, new() { Blocks = "Matricola specialistica non in possesso del titolo" }),
            ("Iscrizione inglese con blocco ancora attivo", "Enrollment", "Please remove my enrollment and regional tax blocks.", false, new() { Blocks = "BVI - Verifica iscrizione" }),
            ("Redditi inglese con blocco ancora attivo", "Foreign income documentation", "Please remove the missing definitive foreign income documentation block.", false, new() { Blocks = "BDR - redditi esteri mancanti" }),
            ("Borsa e rimborso nella stessa frase", "Pagamenti", "Chiedo il saldo della borsa e il rimborso della tassa regionale.", false, Paid()),
            ("Appuntamento oltre al domicilio", "Domicilio", "Devo inserire il domicilio e fissare un appuntamento con l'ufficio.", false, null),
            ("Informazioni generali crediti", "Requisiti borsa", "Quanti crediti servono per mantenere la borsa di studio?", true, null),
            ("Informazioni generali rimborso", "Rimborso tassa", "Come funziona il rimborso della tassa regionale, è automatico o devo fare richiesta?", true, null),
            ("Saldo emesso non ricevuto resta chiudibile", "Saldo", "Non ho ricevuto il saldo della borsa sul mio conto, chiedo quando verrà pagato.", true, Paid()),
            ("473705: allegati Erasmus non sono informazioni", "Learning agreement", "Con il presente ticket, come specificato nel bando, allego l'attestazione del learning agreement e le ricevute dei biglietti aerei della mobilità Erasmus.", false, Paid()),
            ("534509: attestazione tirocinio non è informazione", "Esami e crediti di tirocinio", "Con questo ticket attesto i crediti delle ore di tirocinio e con il presente allegato confermo di aver adempiuto a tutti i requisiti specificati dal bando.", false, Paid()),
            ("Più annualità non coperte dal solo rimborso AA", "Rimborso tasse", "Chiedo l'accredito delle tre annualità della tassa regionale.", false, new() { TaxRefundPaidAmount = 140, TaxRefundMandates = "T1" }),
            ("Blocco permesso PSR ancora presente", "Permesso", "Chiedo la rimozione del blocco permesso di soggiorno.", false, new() { ActiveBlockCodes = "PSR" }),
            ("Blocco iscrizione VII ancora presente", "Iscrizione", "Please remove the registration block.", false, new() { ActiveBlockCodes = "VII" }),
            ("Blocco DOV DV3 ancora presente", "CIMEA", "Chiedo lo sblocco della dichiarazione di valore DOV.", false, new() { ActiveBlockCodes = "DV3" }),
            ("Blocco tirocinio BET ancora presente", "Tirocinio", "Chiedo lo sblocco dell'esame di tirocinio.", false, new() { ActiveBlockCodes = "BET" }),
            ("Merito assente con solo permesso attivo", "Merito", "Chiedo lo sblocco dei CFU.", true, new() { ActiveBlockCodes = "BPP" }),
            ("Istanza anteriore non prova invio dopo il ticket", "Domicilio", "Non riesco a inviare l'istanza di domicilio.", false, new() { HasOpenDomicileRequest = true, DomicileRequestNumber = "1", DomicileRequestDate = new DateTime(2026, 10, 1) }),
            ("Mandato ancora ignoto impedisce conclusione", "Saldo", "Quando arriva il saldo della borsa?", false, new() { BenefitAmount = 7000, BsPaidAmount = 7000, BsMandates = "M1", BsUnclassifiedPaymentTypes = "BSX0" }),
            ("Sblocco generico con codice non classificato", "Blocchi", "Chiedo lo sblocco della domanda.", false, new() { ActiveBlockCodes = "ZZZ" }),
            ("453625: non appare la possibilità", "Contratto di locazione", "Non appare la possibilità di allegare il contratto di locazione.", false, Paid()),
            ("529676: funzione non trovata", "Domicilio", "Non ho trovato la possibilità di modificare la sezione domicilio.", false, Paid()),
            ("443705: domicilio non corretto", "Domicilio", "Controllando i dati vedo che il mio domicilio non è corretto senza che facessi alcuna modifica.", false, Paid()),
            ("445306: comunicare posizione all'università", "Borsa di studio", "Ho ottenuto la borsa e non presento blocchi, quando comunicherete la mia posizione all'università per non pagare la seconda rata?", false, Paid()),
            ("529227: quietanze non soddisfatte dalla borsa pagata", "Quietanze pagamento", "Mi servirebbero questi documenti: le quietanze di pagamento della borsa di studio in PDF.", false, Paid()),
            ("528585: errore dichiarazione esami", "Incongruenze", "Ho superato 9 esami con 60 crediti ma ho sbagliato la mia domanda e ne ho accidentalmente dichiarati 61, chiedo lo sblocco.", false, Paid()),
            ("Rimborso generico e premio non coprono saldo", "Pagamenti", "Quando riceverò il saldo della borsa di studio e il premio di laurea ed eventuale rimborso tasse?", false, Paid()),
            ("ISEE IOD ancora difforme", "ISEE", "Chiedo la rimozione del blocco ISEE.", false, new() { ActiveBlockCodes = "IOD" }),
            ("ISEE INU ancora non universitario", "ISEE", "Chiedo di rimuovere il blocco ISEE universitario.", false, new() { ActiveBlockCodes = "INU" }),
            ("Codice futuro non prova assenza iscrizione", "Iscrizione", "Chiedo lo sblocco della verifica iscrizione.", false, new() { ActiveBlockCodes = "ZZZ" }),
            ("Seconda parte non soddisfatta dalla prima", "Borsa di studio", "Ho ricevuto la prima rata, quando riceverò la seconda parte?", false, new() { BenefitAmount = 7000, BsPaidAmount = 3500, BsFirstInstallmentOriginalAmount = 3500, BsFirstInstallmentMandates = "M1", BsMandates = "M1" }),
            ("Integrazione prima rata non è prima rata originale", "Integrazione", "Quando arriva l'integrazione della prima rata della borsa?", false, new() { BenefitAmount = 7000, BsPaidAmount = 3500, BsFirstInstallmentOriginalAmount = 3500, BsFirstInstallmentMandates = "M1", BsMandates = "M1" }),
            ("Integrazione prima rata emessa", "Integrazione", "Quando arriva l'integrazione della prima rata della borsa?", true, new() { BenefitAmount = 7000, BsPaidAmount = 4000, BsFirstInstallmentOriginalAmount = 3500, BsIntegrationFirstInstallmentOriginalAmount = 500, BsIntegrationMandates = "M2", BsMandates = "M1|M2" }),
            ("Permesso non copre rettifica CF nella stessa frase", "Permesso", "Ho caricato il permesso di soggiorno ma il mio codice fiscale è sbagliato.", false, Paid()),
            ("English appointment besides permit", "Residence permit", "I uploaded my residence permit and I need an appointment.", false, Paid()),
            ("English certificate besides permit", "Residence permit", "I uploaded my residence permit and I need a certificate.", false, Paid()),
            ("English amount dispute", "Scholarship", "I received the scholarship payment but the amount is incorrect.", false, Paid()),
            ("English withdrawal", "Withdrawal", "I want to withdraw from my scholarship.", true, null),
            ("English credit requirements", "Requirements", "How many credits are required to keep the scholarship?", true, null),
            ("Verifica permesso su frase successiva e blocco assente", "Permesso", "Ho caricato il permesso di soggiorno. Potete verificarlo?", true, Paid()),
            ("Verifica permesso su frase successiva e blocco presente", "Permesso", "Ho caricato il permesso di soggiorno. Potete verificarlo?", false, new() { ActiveBlockCodes = "BPP" }),
            ("426833: tasse di iscrizione non sono pagamento borsa", "Status in corso fuori corso", "Ho fatto domanda per la borsa di studio. Desidererei sapere se sarò fuori corso. Per l'accademia non è previsto il pagamento delle tasse di iscrizione.", false, Paid()),
            ("428427: rata universitaria senza richiesta di borsa", "Certificato laurea e immatricolazione", "Invio il certificato di laurea e l'avvenuta immatricolazione con l'avvenuto pagamento della rata.", false, Paid()),
        };
        foreach (var c in cases)
            check(c.Name, () =>
            {
                DataTable tickets = Table();
                Add(tickets, "1", c.Subject, c.Message, "01/11/2026 10:00:00");
                var records = Records(c.Record);
                DataRow result = TicketClosureReview.Build(tickets, records).Rows[0];
                Assert((Text(result, "AZIONE_PROPOSTA") == "CHIUDERE") == c.Close,
                    $"Atteso {(c.Close ? "CHIUDERE" : "VERIFICARE")}; {Text(result, "REGOLA")}: {Text(result, "MOTIVO")}");
            });
        check("544134: redditi e iscrizione non sono duplicati", () =>
        {
            var tickets = Table();
            Add(tickets, "544123", "Verifica iscrizione", "Chiedo lo sblocco verify registration. Ho completato l'immatricolazione.", "01/11/2026 10:00:00");
            Add(tickets, "544134", "Redditi esteri", "Chiedo lo sblocco missing definitive foreign income documentation. Allego i redditi esteri.", "02/11/2026 10:00:00");
            var result = TicketClosureReview.Build(tickets, Records(new() { Blocks = "BDR - redditi esteri mancanti | BPP - permesso mancante" }));
            Assert(Text(result.Rows[0], "AZIONE_PROPOSTA") == "CHIUDERE", "Iscrizione assente: gli altri blocchi non impediscono la chiusura.");
            Assert(Text(result.Rows[1], "AZIONE_PROPOSTA") == "VERIFICARE" && Text(result.Rows[1], "TICKET_PRINCIPALE") == "", "Non chiudere redditi come duplicato dell'iscrizione.");
        });
        check("531216: premio laurea e rimborso non sono duplicati", () =>
        {
            var tickets = Table();
            Add(tickets, "1", "Rimborso tasse", "Vincitore borsa, chiedo l'accredito delle annualità della tassa regionale.", "01/11/2026 10:00:00");
            Add(tickets, "2", "Premio laurea", "Vincitore borsa, chiedo quando verrà erogato il mio premio di laurea.", "02/11/2026 10:00:00");
            var result = TicketClosureReview.Build(tickets, Records(Paid()));
            Assert(result.AsEnumerable().All(r => Text(r, "TICKET_PRINCIPALE") == ""), "Benefici distinti.");
        });
        check("Dati blocchi non disponibili non equivalgono ad assenza", () =>
        {
            var tickets = Table(); Add(tickets, "1", "Iscrizione", "Chiedo lo sblocco dell'iscrizione.", "01/11/2026 10:00:00");
            var record = new TicketOfficeRecord { AcademicYear = 20252026, FiscalCode = "CF_TEST", ApplicationNumber = "100", HasBlockCodeData = false };
            var result = TicketClosureReview.Build(tickets, new Dictionary<string, List<TicketOfficeRecord>> { ["CF_TEST"] = new() { record } });
            Assert(Text(result.Rows[0], "AZIONE_PROPOSTA") == "VERIFICARE", "Non chiudere su dato non letto.");
        });
        check("Anno nell'oggetto discordante non usa i pagamenti dell'anno dichiarato", () =>
        {
            var tickets = Table(); Add(tickets, "1", "Saldo borsa 2024/2025", "Quando arriva il saldo della borsa?", "01/11/2026 10:00:00");
            var result = TicketClosureReview.Build(tickets, Records(Paid()));
            Assert(Text(result.Rows[0], "AZIONE_PROPOSTA") == "VERIFICARE", "Non usare un anno diverso dall'oggetto.");
        });
        check("Excel distingue dati mancanti da importi zero", () =>
        {
            var tickets = Table(); Add(tickets, "1", "IBAN", "Vorrei cambiare IBAN.", "01/11/2026 10:00:00");
            var result = TicketClosureReview.Build(tickets, Records(null));
            string folder = Path.Combine(Path.GetTempPath(), "TicketClosureMissingData", Guid.NewGuid().ToString("N"));
            using var book = new XLWorkbook(TicketClosureWorkbook.Export(result, folder));
            foreach (string column in new[] { "IMPORTO_BORSA_LORDO", "PAGATO_BORSA_LORDO", "RESIDUO_BORSA_LORDO" })
                Assert(book.Worksheet(1).Cell(2, result.Columns[column]!.Ordinal + 1).IsEmpty(), "Il dato mancante deve essere vuoto: " + column);
        });
        check("Allegato mail mantiene il report precedente e il rimborso separato", () =>
        {
            var tickets = Table();
            foreach (string name in new[] { "ESITO_BS_20252026", "BLOCCHI_20252026", "SEDE_DESCR_20252026", "HA_RIMBORSO_20252026", "ARGOMENTO_PRIMARIO" }) tickets.Columns.Add(name);
            Add(tickets, "1", "Modifica IBAN", "Vorrei cambiare IBAN.", "01/11/2026 10:00:00");
            var records = Records(new() { BsOutcome = "2", Blocks = "BVI - Verifica iscrizione", StudyLocation = "Sede di prova", BsPaymentTypes = "BSP0 | BSS0", TaxRefundPaidAmount = 140, TaxRefundMandates = "T1" });
            var info = TicketOfficeDataIndex.Create(records, new[] { 20252026 }).YearInfoByFiscalCode["CF_TEST"][20252026];
            Assert(info.HasRimborso, "Il rimborso BST separato deve comparire nel report precedente.");
            var row = tickets.Rows[0];
            row["ESITO_BS_20252026"] = info.EsitoBSJoined;
            row["BLOCCHI_20252026"] = info.BlocchiJoined;
            row["SEDE_DESCR_20252026"] = info.SediDescrizioniJoined;
            row["HA_RIMBORSO_20252026"] = "SI";
            row["ARGOMENTO_PRIMARIO"] = "IBAN";
            string folder = Path.Combine(Path.GetTempPath(), "TicketMailAttachment", Guid.NewGuid().ToString("N"));
            string mailFile = TicketWorkbookExporter.ExportMailAttachment(tickets, folder);
            string closureFile = TicketClosureWorkbook.Export(TicketClosureReview.Build(tickets, records), folder);
            Assert(mailFile != closureFile && Path.GetFileName(mailFile).StartsWith("Export_"), "La mail richiede il vecchio file separato.");
            using var mailBook = new XLWorkbook(mailFile);
            Assert(mailBook.Worksheets.Count == 1 && mailBook.Worksheet(1).Name == "Sheet1", "La mail deve conservare il foglio precedente.");
            var headers = mailBook.Worksheet(1).Row(1).CellsUsed().Select(c => c.GetString()).ToArray();
            Assert(headers.Length == 41 && !headers.Contains("AZIONE_PROPOSTA") && !headers.Contains("EVIDENZE"), "Non allegare il formato delle chiusure.");
            foreach (var expected in new[] { ("ESITO_BS_20252026", "Vincitore"), ("SEDE_DESCR_20252026", "Sede di prova"), ("HA_RIMBORSO_20252026", "SI"), ("ARGOMENTO_PRIMARIO", "IBAN") })
                Assert(mailBook.Worksheet(1).Cell(2, Array.IndexOf(headers, expected.Item1) + 1).GetString() == expected.Item2, "Dato del report precedente mancante: " + expected.Item1);
            using var closureBook = new XLWorkbook(closureFile);
            Assert(closureBook.Worksheets.Select(s => s.Name).SequenceEqual(new[] { "Ticket chiudibili", "Da verificare" }), "Il nuovo report locale deve restare disponibile.");
        });
    }

    private static TicketOfficeRecord Paid() => new() { BenefitAmount = 7000, BsPaidAmount = 7000, BsMandates = "M1|M2", BsBalanceMandates = "M2", CompilationStatus = 90 };
    private static Dictionary<string, List<TicketOfficeRecord>> Records(TicketOfficeRecord? record)
    {
        if (record == null) return new(StringComparer.OrdinalIgnoreCase);
        record.FiscalCode = "CF_TEST"; record.AcademicYear = 20252026; record.ApplicationNumber = "100";
        OfficeRecordFixtures.SetKnownBlocks(record);
        return new(StringComparer.OrdinalIgnoreCase) { ["CF_TEST"] = new() { record } };
    }
    private static DataTable Table()
    {
        var t = new DataTable();
        foreach (string column in new[] { "ID_TICKET", "CODFISC", "STATO", "UID", "PRIMO_MSG_OPERATORE", "CATEGORIA", "OGGETTO", "PRIMO_MSG_STUDENTE", "ANNO_ACCADEMICO_RICHIESTA", "DATA_CREAZIONE" }) t.Columns.Add(column);
        return t;
    }
    private static void Add(DataTable t, string id, string subject, string message, string created)
    {
        var r = t.NewRow(); r["ID_TICKET"] = id; r["CODFISC"] = "CF_TEST"; r["STATO"] = "NUOVO"; r["CATEGORIA"] = "Borse di studio";
        r["OGGETTO"] = subject; r["PRIMO_MSG_STUDENTE"] = message; r["ANNO_ACCADEMICO_RICHIESTA"] = "2025/2026"; r["DATA_CREAZIONE"] = created; t.Rows.Add(r);
    }
    private static string Text(DataRow r, string column) => Convert.ToString(r[column]) ?? "";
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
