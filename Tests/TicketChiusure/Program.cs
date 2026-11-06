using ProcedureNet7;
using System.Data;
using ClosedXML.Excel;

if (args.Length == 2 && args[0] == "--csv") { CsvProbe.Run(args[1]); return; }
if (args.Length == 3 && args[0] is "--database" or "--database-cases")
{ CsvProbe.Run(args[1], args[2], args[0] == "--database-cases"); return; }

var failures = new List<string>();
Check("L'importo dell'esito prevale sugli impegni", () =>
{
    var data = new PaymentsTicketDomainModule().Extract(new TicketOfficeRecord
    {
        BenefitAmount = 7000m, SpecificheAmount = 3500m, BsPaidAmount = 3500m
    });
    Assert(data.AssignedAmount == 7000m && data.ResidualAmount == 3500m,
        $"Assegnato {data.AssignedAmount}, residuo {data.ResidualAmount}");
});
Check("Il confronto sul lordo non usa detrazioni", () =>
{
    var data = new PaymentsTicketDomainModule().Extract(new TicketOfficeRecord
    {
        BenefitAmount = 7000m, BsPaidAmount = 6500m, DeductionAmount = 500m
    });
    Assert(data.ResidualAmount == 500m, $"Residuo {data.ResidualAmount}");
});
Check("Esclusione di chiusi, UID, messaggi operatore e categorie fuori competenza", () =>
{
    var source = Table();
    Add(source, "1", "Vorrei cambiare IBAN.");
    var assigned = Add(source, "2", "Vorrei cambiare IBAN."); assigned["UID"] = "0";
    var answered = Add(source, "3", "Vorrei cambiare IBAN."); answered["PRIMO_MSG_OPERATORE"] = ".";
    var closed = Add(source, "4", "Vorrei cambiare IBAN."); closed["STATO"] = "CHIUSO";
    var category = Add(source, "5", "Vorrei cambiare IBAN."); category["CATEGORIA"] = " AREA 8 ";
    var taken = Add(source, "6", "Vorrei cambiare IBAN."); taken["AZIONE"] = "PRESA_IN_CARICO";
    var selected = TicketClosureReview.SelectEligible(source);
    Assert(selected.Rows.Count == 1 && Text(selected.Rows[0], "ID_TICKET") == "1", "Devono restare solo i ticket elaborabili.");
    Assert(source.Rows.Count == 6 && Text(source.Rows[1], "UID") == "0", "Il sorgente deve rimanere intatto.");
});

var cases = new (string Name, string Message, TicketOfficeRecord? Record, bool Close)[]
{
    ("Domicilio tramite istanza", "Devo inserire il contratto di locazione.", null, true),
    ("Sollecito istanza pendente", "Ho presentato istanza di domicilio e chiedo di lavorarla.", Record(), true),
    ("Cambio corso tramite istanza", "Vorrei cambiare corso di laurea.", null, true),
    ("Rinuncia tramite istanza", "Vorrei rinunciare alla borsa di studio.", null, true),
    ("IBAN nel profilo", "Vorrei modificare il mio IBAN.", null, true),
    ("Residenza tramite ticket", "Vorrei modificare la residenza.", Record(), false),
    ("Rettifica codice fiscale tramite ticket", "Chiedo rettifica del codice fiscale.", Record(), false),
    ("Rateizzazione non è semplice rinuncia", "Chiedo la rateizzazione della restituzione della borsa per rinuncia agli studi.", Record(), false),
    ("Saldo totalmente liquidato", "Non ho ricevuto il saldo della borsa.", Paid(), true),
    ("Saldo incompleto", "Quando arriva il saldo della borsa?", Record(), false),
    ("Esito prevale sugli impegni anche per chiusura", "Non ho ricevuto il saldo della borsa.", new() { BenefitAmount = 7000, SpecificheAmount = 3500, BsPaidAmount = 3500, BsMandates = "M1" }, false),
    ("Lordo senza detrazioni per chiusura", "Quando arriva il saldo della borsa?", new() { BenefitAmount = 7000, BsPaidAmount = 6500, DeductionAmount = 500, BsMandates = "M1" }, false),
    ("Mandato indispensabile", "Quando arriva il saldo della borsa?", new() { BenefitAmount = 7000, BsPaidAmount = 7000 }, false),
    ("Prima rata pagata, saldo pendente", "Non ho ricevuto la prima rata della borsa.", new() { BenefitAmount = 7000, BsPaidAmount = 3500, BsFirstInstallmentOriginalAmount = 3500, BsFirstInstallmentMandates = "M1", BsMandates = "M1" }, true),
    ("Prima rata e saldo richiedono liquidazione completa", "Chiedo il pagamento della prima rata e del saldo della borsa.", new() { BenefitAmount = 7000, BsPaidAmount = 3500, BsFirstInstallmentOriginalAmount = 3500, BsFirstInstallmentMandates = "M1", BsMandates = "M1" }, false),
    ("Riemissione valida e totale liquidato", "Il bonifico del saldo della borsa è stato respinto e chiedo riemissione.", new() { BenefitAmount = 7000, BsPaidAmount = 7000, BsBalanceReissueAmount = 3500, BsStornedAmount = 3500, BsMandates = "M1 | M2", BsBalanceMandates = "M2" }, true),
    ("Rimborso tassa pagato", "Chiedo il rimborso della tassa regionale.", new() { TaxRefundPaidAmount = 140, TaxRefundMandates = "T1" }, true),
    ("Rimborso non dimostrato dal pagamento borsa", "Chiedo il rimborso della tassa regionale.", Paid(), false),
    ("Rimborso senza mandato", "Chiedo il rimborso della tassa regionale.", new() { TaxRefundPaidAmount = 140 }, false),
    ("Permesso senza blocco e senza documenti", "Ho caricato il permesso di soggiorno, potete verificare?", Record(), true),
    ("Altri blocchi non impediscono il permesso", "Chiedo lo sblocco del permesso di soggiorno.", new() { Blocks = "BDR - documentazione redditi esteri mancante" }, true),
    ("Permesso con richiesta di sblocco ancora attiva", "Chiedo di rimuovere il blocco del permesso di soggiorno.", new() { Blocks = "BPP - permesso di soggiorno mancante" }, false),
    ("Comunicazione caricamento permesso", "Ho caricato il permesso di soggiorno.", new() { Blocks = "BPP - permesso di soggiorno mancante" }, true),
    ("Comunicazione non risolve anche saldo richiesto", "Ho caricato il permesso di soggiorno. Quando arriva il saldo della borsa?", Record(), false),
    ("Due richieste entrambe soddisfatte", "Ho caricato il permesso di soggiorno. Quando arriva il saldo della borsa?", Paid(), true),
    ("Blocco iscrizione assente con altro blocco", "Chiedo di rimuovere il blocco di verifica iscrizione.", new() { Blocks = "BDR - redditi esteri mancanti" }, true),
    ("Blocco merito assente con iscrizione ancora bloccata", "Chiedo lo sblocco dei crediti CFU.", new() { Blocks = "Verifica iscrizione mancante" }, true),
    ("Blocco merito ancora presente", "Chiedo lo sblocco dei crediti CFU.", new() { Blocks = "Incongruenza merito crediti" }, false),
    ("Due blocchi uno ancora presente", "Chiedo di rimuovere i blocchi di iscrizione e dei redditi esteri.", new() { Blocks = "BDR - redditi esteri mancanti" }, false),
    ("Invio domanda ora completato", "Non riesco a trasmettere la domanda.", new() { CompilationStatus = 90 }, true),
    ("Invio domanda ancora incompleto", "Non riesco a trasmettere la domanda.", new() { CompilationStatus = 80 }, false),
    ("Anomalia contratto non risolta da istanza esistente", "Il sistema dà errore nelle date del contratto di locazione.", new() { HasOpenDomicileRequest = true }, false),
    ("Invio istanza domicilio ora completato", "Non riesco a inviare l'istanza di domicilio.", new() { HasOpenDomicileRequest = true, DomicileRequestNumber = "42", DomicileRequestDate = new DateTime(2026, 11, 2, 9, 0, 0) }, true),
    ("Problema accesso non dimostrato risolto", "Non riesco ad accedere al portale con SPID.", new() { CompilationStatus = 90 }, false),
    ("Informazioni premio laurea", "Quando escono le graduatorie del premio di laurea?", null, true),
    ("Premio laurea con anomalia personale", "Il portale dà errore nella richiesta di premio di laurea.", Record(), false),
    ("Informazioni generali sui pagamenti", "Quando iniziano i pagamenti della borsa?", null, true),
    ("Richiesta vaga non chiusa come informazione", "Vorrei sapere quando verrà effettuato.", null, false),
    ("Oggetto non deve coprire una richiesta diversa", "Vorrei prenotare un appuntamento con il direttore.", null, false),
    ("Più richieste, una non riconosciuta", "Devo inserire il contratto di locazione e vorrei prenotare un appuntamento.", null, false),
    ("Certificato e contratto", "Devo inserire il contratto di locazione e richiedo un certificato.", null, false),
    ("Permesso e trasferimento importo sono richieste distinte", "Ho caricato il permesso di soggiorno. Potrei chiedere quando è previsto il trasferimento dell'importo della borsa di studio?", Record(), false),
    ("Permesso inglese e blocco non rimosso", "I uploaded the residence permit. However the block has not yet been removed. I ask you to check the documents and remove the block.", new() { Blocks = "BPP - permesso di soggiorno mancante" }, false),
    ("Fuorisede non è cambio sede", "Voglio capire perché mi hanno cambiato lo status da fuorisede a insede.", Record(), false),
    ("Mensa non è rinuncia agli studi", "Alla domanda monetizzazione mensa ho premuto NO rinunciando alla monetizzazione. È possibile modificare questa cosa?", Record(), false),
    ("Merito personale non è informazione generale", "Risulto escluso dalla graduatoria per i crediti. Vorrei sapere come procedere con l'aggiornamento dei CFU.", Record(), false),
    ("Ricevuta tassa non coperta dal contratto", "Di seguito la ricevuta del pagamento della tassa regionale. Sto completando la registrazione del contratto di locazione.", null, false),
    ("Comunicazione altro beneficio richiede verifica", "Desidero comunicare che ho vinto un altro bando INPS. Come posso aggiornare la mia posizione?", Record(), false),
    ("Domicilio non copre altre richieste su righe diverse", "Devo inserire il contratto di locazione.\nVorrei prenotare un appuntamento.", null, false),
    ("Domanda ulteriore senza verbo di richiesta", "Devo inserire il contratto di locazione. Il mio esame vale come credito per il merito?", null, false),
    ("Funzione istanza assente non è informazione generale", "Ho effettuato il cambio di corso. Nelle operazioni bando non è attualmente presente il pulsante per presentare l'istanza, come procedere?", Record(), false),
    ("Contratto universitario non è domicilio", "Ho effettuato il recesso dal contratto con l'università. Non si tratta di rinuncia agli studi. Come devo compilare la carriera pregressa?", Record(), false),
    ("Status fuori sede non ancora corretto", "Il domicilio è corretto ma risulto pendolare, chiedo di correggere lo status.", Record(), false),
    ("Status fuori sede già corretto", "Il domicilio è corretto ma risulto pendolare, chiedo di correggere lo status.", new() { CampusStatus = "B" }, true),
    ("Comunicazione e anomalia dichiarativa italiana", "Ho caricato il permesso di soggiorno. I miei esami non sono riconosciuti.", Record(), false),
    ("Comunicazione e anomalia dichiarativa inglese", "I uploaded the residence permit. My credits are not registered.", Record(), false),
};
foreach (var c in cases)
    Check(c.Name, () =>
    {
        var table = Table();
        var row = Add(table, "1", c.Message);
        if (c.Name.StartsWith("Oggetto")) row["OGGETTO"] = "Contratto di locazione";
        var data = TicketClosureReview.Build(table, Records(c.Record));
        Assert(data.Rows.Count == 1, "Atteso un ticket.");
        Assert((Text(data.Rows[0], "AZIONE_PROPOSTA") == "CHIUDERE") == c.Close,
            Text(data.Rows[0], "MOTIVO") + " / " + Text(data.Rows[0], "EVIDENZE"));
        Assert(Text(data.Rows[0], "EVIDENZE").Length > 0 || !c.Close, "Chiusura senza evidenze.");
    });

Check("Anno assente non usa l'ultima domanda e non chiude duplicati", () =>
{
    var table = Table();
    foreach (string id in new[] { "1", "2" }) Add(table, id, "Vorrei cambiare IBAN.")["ANNO_ACCADEMICO_RICHIESTA"] = "";
    var result = TicketClosureReview.Build(table, Records(Paid()));
    Assert(result.AsEnumerable().All(r => Text(r, "AZIONE_PROPOSTA") == "VERIFICARE"), "Anno assente deve impedire tutte le chiusure.");
});
Check("L'anno dichiarato conserva il suo significato", () =>
{
    var table = Table();
    Add(table, "1", "Chiedo il saldo della borsa.");
    var previous = Paid(); previous.AcademicYear = 20242025;
    Assert(Text(TicketClosureReview.Build(table, Records(previous)).Rows[0], "AZIONE_PROPOSTA") == "VERIFICARE", "Non utilizzare pagamenti di un altro anno.");
});
Check("Anno dichiarato discordante dal messaggio richiede verifica", () =>
{
    var table = Table();
    Add(table, "1", "Chiedo il saldo della borsa 2026/2027.");
    Assert(Text(TicketClosureReview.Build(table, Records(Paid())).Rows[0], "AZIONE_PROPOSTA") == "VERIFICARE", "Il pagamento del 2025/2026 non risolve una richiesta sul 2026/2027.");
});
Check("Anno nel messaggio e messaggi con più anni", () =>
{
    var table = Table();
    Add(table, "1", "Chiedo il saldo della borsa 2025/26.")["ANNO_ACCADEMICO_RICHIESTA"] = "";
    var result = TicketClosureReview.Build(table, Records(Paid()));
    Assert(Text(result.Rows[0], "AZIONE_PROPOSTA") == "CHIUDERE", "L'anno nel messaggio deve essere riconosciuto.");
    table.Rows[0]["PRIMO_MSG_STUDENTE"] = "Chiedo il saldo della borsa 2024/25 e 2025/26.";
    Assert(Text(TicketClosureReview.Build(table, Records(Paid())).Rows[0], "AZIONE_PROPOSTA") == "VERIFICARE", "Più anni richiedono verifica.");
});
Check("Duplicati: più vecchio, stessa richiesta e nessuna contaminazione fra anni", () =>
{
    var table = Table();
    Add(table, "20", "Quando arriva il saldo della borsa?", "02/11/2026 09:00:00");
    Add(table, "10", "Vorrei il pagamento del saldo della borsa.", "01/11/2026 09:00:00");
    Add(table, "30", "Quando arriva il saldo della borsa?", "03/11/2026 09:00:00");
    Add(table, "40", "Quando arriva il saldo della borsa?", "04/11/2026 09:00:00")["ANNO_ACCADEMICO_RICHIESTA"] = "2026/2027";
    var result = TicketClosureReview.Build(table, Records(Record()));
    Assert(Text(result.Rows[0], "ID_TICKET") == "10" && Text(result.Rows[0], "AZIONE_PROPOSTA") == "VERIFICARE", "Il più vecchio deve restare da lavorare.");
    foreach (var r in result.AsEnumerable().Where(r => Text(r, "ID_TICKET") is "20" or "30"))
        Assert(Text(r, "AZIONE_PROPOSTA") == "CHIUDERE" && Text(r, "TICKET_PRINCIPALE") == "10", "I più nuovi devono puntare direttamente al più vecchio.");
    Assert(Text(result.Rows[3], "AZIONE_PROPOSTA") == "VERIFICARE", "Nessun duplicato fra anni diversi.");
});
Check("Testo identico e presa in carico non riappaiono nel risultato", () =>
{
    var table = Table();
    Add(table, "1", "Richiedo una rettifica anagrafica.", "01/11/2026 09:00:00");
    Add(table, "2", "Richiedo una rettifica anagrafica.", "02/11/2026 09:00:00");
    Add(table, "3", "Richiedo una rettifica anagrafica.", "03/11/2026 09:00:00")["UID"] = "operatore";
    var result = TicketClosureReview.Build(table, Records(Record()));
    Assert(result.Rows.Count == 2 && Text(result.Rows[1], "TICKET_PRINCIPALE") == "1", "Duplicati esatti solo fra elaborabili.");
});
Check("Comunicazione permesso non è duplicato della richiesta di sblocco", () =>
{
    var table = Table();
    Add(table, "1", "Ho caricato il permesso di soggiorno.", "01/11/2026 09:00:00");
    Add(table, "2", "Chiedo lo sblocco del permesso di soggiorno.", "02/11/2026 09:00:00");
    var result = TicketClosureReview.Build(table, Records(new() { Blocks = "BPP - permesso mancante" }));
    Assert(Text(result.Rows[1], "AZIONE_PROPOSTA") == "VERIFICARE" && Text(result.Rows[1], "TICKET_PRINCIPALE") == "", "Richieste differenti sullo stesso argomento non sono duplicati.");
});
Check("Particolari diversi del merito non sono duplicati semantici", () =>
{
    var table = Table();
    Add(table, "1", "Chiedo lo sblocco dei crediti dell'esame di matematica.", "01/11/2026 09:00:00");
    Add(table, "2", "Chiedo lo sblocco dei crediti dell'esame di chimica.", "02/11/2026 09:00:00");
    var result = TicketClosureReview.Build(table, Records(new() { Blocks = "Incongruenza merito" }));
    Assert(result.AsEnumerable().All(r => Text(r, "TICKET_PRINCIPALE") == "" && Text(r, "AZIONE_PROPOSTA") == "VERIFICARE"), "Non confondere esami diversi.");
});
Check("Anni abbreviati multipli richiedono verifica", () =>
{
    var table = Table();
    Add(table, "1", "Ho fatto un passaggio di corso nel 24/25 e chiedo la borsa per il 25/26 e 26/27.");
    Assert(Text(TicketClosureReview.Build(table, Records(Paid())).Rows[0], "AZIONE_PROPOSTA") == "VERIFICARE", "Non selezionare arbitrariamente un anno.");
});
Check("Excel con due liste, evidenze, date/importi numerici e senza risposte", () =>
{
    var table = Table();
    Add(table, "1", "Vorrei cambiare IBAN.");
    Add(table, "2", "Chiedo la rettifica del codice fiscale.");
    Add(table, "3", "Chiedo il saldo della borsa.")["UID"] = "operatore";
    var result = TicketClosureReview.Build(table, Records(Paid()));
    string folder = Path.Combine(Path.GetTempPath(), "ProcedureNet7-TicketChiusure", Guid.NewGuid().ToString("N"));
    string path = TicketClosureWorkbook.Export(result, folder);
    using var book = new XLWorkbook(path);
    Assert(book.Worksheets.Select(s => s.Name).SequenceEqual(new[] { "Ticket chiudibili", "Da verificare" }), "Fogli errati.");
    Assert(book.Worksheet(1).Cell(2, 1).GetString() == "1" && book.Worksheet(2).Cell(2, 1).GetString() == "2", "Liste non separate.");
    Assert(book.Worksheets.All(s => s.Row(1).CellsUsed().All(c => !c.GetString().Contains("RISPOSTA") && !c.GetString().Contains("RISCONTRO"))), "Non devono esserci testi di risposta.");
    Assert(book.Worksheet(1).Cell(2, result.Columns["DATA_CREAZIONE"]!.Ordinal + 1).DataType == XLDataType.DateTime, "Data non numerica.");
    Assert(book.Worksheet(1).Cell(2, result.Columns["IMPORTO_BORSA_LORDO"]!.Ordinal + 1).DataType == XLDataType.Number, "Importo non numerico.");
});
Check("Excel valido anche quando una delle due liste è vuota", () =>
{
    foreach (bool close in new[] { true, false })
    {
        var table = Table();
        Add(table, "1", close ? "Vorrei modificare il mio IBAN." : "Richiedo la rettifica anagrafica.");
        var result = TicketClosureReview.Build(table, Records(Record()));
        string folder = Path.Combine(Path.GetTempPath(), "ProcedureNet7-TicketChiusure", Guid.NewGuid().ToString("N"));
        using var book = new XLWorkbook(TicketClosureWorkbook.Export(result, folder));
        Assert(book.Worksheets.Count == 2 && book.Worksheets.All(s => s.Cell(1, 1).GetString() == "ID_TICKET"), "Mantenere entrambi i fogli e le intestazioni.");
    }
});
TicketRecognitionRegressionTests.Run(Check);
if (failures.Count > 0)
    throw new Exception(string.Join(Environment.NewLine, failures));

void Check(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures.Add(name + ": " + e.Message); Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
static void Assert(bool value, string message)
{
    if (!value) throw new Exception(message);
}

static TicketOfficeRecord Record() => new() { AcademicYear = 20252026, FiscalCode = "CF_TEST", ApplicationNumber = "100", CompilationStatus = 90, BenefitAmount = 7000m };
static TicketOfficeRecord Paid() => new() { AcademicYear = 20252026, FiscalCode = "CF_TEST", ApplicationNumber = "100", CompilationStatus = 90, BenefitAmount = 7000m, BsPaidAmount = 7000m, BsMandates = "M1 | M2", BsBalanceMandates = "M2", BsBalanceOriginalAmount = 3500m };
static Dictionary<string, List<TicketOfficeRecord>> Records(TicketOfficeRecord? record)
{
    if (record == null) return new(StringComparer.OrdinalIgnoreCase);
    if (record.AcademicYear == 0) record.AcademicYear = 20252026;
    record.FiscalCode = "CF_TEST";
    OfficeRecordFixtures.SetKnownBlocks(record);
    return new(StringComparer.OrdinalIgnoreCase) { ["CF_TEST"] = new() { record } };
}
static DataTable Table()
{
    var table = new DataTable();
    foreach (string c in new[] { "ID_TICKET", "CODFISC", "STATO", "UID", "AZIONE", "PRIMO_MSG_OPERATORE", "CATEGORIA", "SOTTOCATEGORIA", "OGGETTO", "PRIMO_MSG_STUDENTE", "ANNO_ACCADEMICO_RICHIESTA", "DATA_CREAZIONE" }) table.Columns.Add(c);
    return table;
}
static DataRow Add(DataTable table, string id, string message, string created = "02/11/2026 08:00:00")
{
    var row = table.NewRow();
    row["ID_TICKET"] = id; row["CODFISC"] = "CF_TEST"; row["STATO"] = "NUOVO"; row["CATEGORIA"] = "Borse di Studio";
    row["PRIMO_MSG_STUDENTE"] = message; row["ANNO_ACCADEMICO_RICHIESTA"] = "2025/2026 Anno Accademico della richiesta";
    row["DATA_CREAZIONE"] = created; table.Rows.Add(row); return row;
}
static string Text(DataRow row, string name) => Convert.ToString(row[name]) ?? "";
