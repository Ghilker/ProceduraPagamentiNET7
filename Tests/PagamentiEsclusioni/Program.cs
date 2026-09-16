using ClosedXML.Excel;
using ProcedureNet7;
using System.Data;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

string Describe(string codice) => "Descrizione " + codice;
var absent = PagamentoEsclusioniRules.RiemissioneNonAmmessa("BSP", "2",
    new[] { new Pagamento("BSP0", 750, true) }, Describe);
Check(absent.Motivo.Contains("assente") && absent.Dettaglio.Contains("BSP1"),
    "La seconda riemissione deve identificare come precedente la prima riemissione, non l'emissione iniziale.");
var active = PagamentoEsclusioniRules.RiemissioneNonAmmessa("BSP", "1",
    new[] { new Pagamento("BSP0", 750, false) }, Describe);
Check(active.Motivo.Contains("non stornato") && active.Dettaglio.Contains("ritirato dall'azienda"), "Pagamento esistente non stornato deve avere una causa distinta dall'assenza e usare il termine scelto dall'ufficio.");
foreach (var (next, previous) in new[] { ("1", "0"), ("2", "1"), ("7", "6"), ("8", "7"), ("A", "9"), ("B", "A") })
{
    var cause = PagamentoEsclusioniRules.RiemissioneNonAmmessa("BSI", next, Array.Empty<Pagamento>(), Describe);
    Check(cause.Dettaglio.Contains("BSI" + previous), "Precedente errato per riemissione " + next);
}
var integration = PagamentoEsclusioniRules.IntegrazioneNonAmmessa("BS", "I9",
    new[] { new Pagamento("BSP0", 750, false) }, Describe);
Check(integration.Motivo.Contains("assente") && integration.Dettaglio.Contains("un saldo, anche riemesso"), "L'integrazione saldo richiede il saldo, non la prima rata.");
var withdrawn = PagamentoEsclusioniRules.IntegrazioneNonAmmessa("BS", "I0",
    new[] { new Pagamento("BSP0", 750, true) }, Describe);
Check(withdrawn.Motivo.Contains("stornato") && withdrawn.Dettaglio.Contains("750,00"), "L'integrazione deve distinguere pagamento stornato da pagamento assente e indicare l'importo interessato.");
Check(PagamentoEsclusioniRules.IntegrazioneNonAmmessa("BS", "II", null, Describe).Dettaglio.Contains("un'integrazione del saldo"), "La successiva integrazione richiede l'integrazione saldo.");

Check(PagamentoEsclusioniRules.TassaRegionaleNonAmmessa(new[] { new Pagamento("BSI0", 50, false) }, 1, false, false).Motivo.Contains("assente"),
    "Un'integrazione non deve essere descritta come prima rata o saldo propedeutico al rimborso.");
Check(PagamentoEsclusioniRules.TassaRegionaleNonAmmessa(new[] { new Pagamento("BSS0", 750, true) }, 2, false, false).Motivo.Contains("stornato"),
    "Il rimborso deve distinguere un saldo ritirato da un saldo assente.");
Check(PagamentoEsclusioniRules.TassaRegionaleNonAmmessa(new[] { new Pagamento("BSP0", 750, false) }, 1, true, false).Dettaglio.Contains("specifico per la tassa regionale"),
    "La sola prima rata richiede il superamento specifico della tassa regionale anche con superamento borsa registrato.");
Check(PagamentoEsclusioniRules.TassaRegionaleNonAmmessa(new[] { new Pagamento("BSS0", 750, false) }, 1, false, false).Dettaglio.Contains("Risulta un saldo"),
    "Un saldo presente con merito non registrato non deve essere descritto come pagamento mancante.");

Check(PagamentoEsclusioniRules.VincitoreSenzaAssegnazione(2, false), "Vincitore senza assegnazione deve essere segnalato.");
Check(!PagamentoEsclusioniRules.VincitoreSenzaAssegnazione(2, true), "Vincitore con assegnazione non deve essere segnalato come privo di assegnazione.");
Check(!PagamentoEsclusioniRules.VincitoreSenzaAssegnazione(1, false), "Non vincitore senza assegnazione non deve ricevere la causa riservata ai vincitori.");
Check(PagamentoEsclusioniRules.AssegnazioneMancante("20262027").Importo == null, "L'assenza di assegnazione non equivale a un importo zero.");
Check(PagamentoEsclusioniRules.AssegnazioneMancante("20262027").Motivo == "Nessuna assegnazione utilizzabile per il pagamento", "Mantenere la causa unica richiesta per le assegnazioni PA non utilizzabili.");
Check(PagamentoEsclusioniRules.AssegnazioneMancante("20262027").Dettaglio.Contains("2026/2027"), "La spiegazione PA deve indicare l'anno verificato.");

var diff = PagamentoEsclusioniRules.DifferenzaEntroSoglia(1000, 1003.4, "Beneficio");
Check(diff.Importo == -3.4 && diff.Dettaglio.Contains("-3,40"), "La differenza deve conservare segno e centesimi.");
Check(PagamentoEsclusioniRules.ImportoNonLiquidabile(-3.4, "Conguaglio").Motivo.Contains("negativo"), "Un residuo negativo anche entro 5 euro va distinto da un piccolo residuo positivo.");
Check(PagamentoEsclusioniRules.ImportoNonLiquidabile(0, "Conguaglio").Motivo.Contains("Nessun"), "Zero deve avere una motivazione specifica.");
Check(PagamentoEsclusioniRules.ImportoNonLiquidabile(3.4, "Conguaglio").Motivo.Contains("5 €"), "Il piccolo residuo positivo deve indicare la soglia.");
Check(PagamentoEsclusioniRules.DifferenzaEntroSoglia(1000, 1000, "Beneficio").Motivo.Contains("Nessuna differenza"), "L'assenza di residuo deve distinguersi dal residuo positivo sotto soglia.");
var calcolo = PagamentoEsclusioniRules.DettaglioCalcolo(1000, 750, 250, 0, 0);
Check(calcolo.Contains("1.000,00") && calcolo.Contains("750,00") && calcolo.Contains("250,00"), "Il dettaglio deve consentire di ricostruire il netto con gli importi rilevanti.");
Check(!calcolo.Contains("detrazioni") && !calcolo.Contains("reversali"), "Non mostrare trattenute nulle che non spiegano l'esclusione.");
var incompleto = PagamentoEsclusioniRules.ImportiNonCoincidenti(1000, null);
Check(incompleto.Motivo.Contains("incompleti") && incompleto.Dettaglio.Contains("non registrato"), "Un importo contabile mancante non deve essere descritto come zero.");
var diverso = PagamentoEsclusioniRules.ImportiNonCoincidenti(1000, 800);
Check(diverso.Dettaglio.Contains("1.000,00") && diverso.Dettaglio.Contains("800,00") && diverso.Dettaglio.Contains("200,00"), "Mostrare i due importi confrontati e la differenza.");

var causePA = PagamentoEsclusioniRules.AssegnazioneMancante("20262027");
var zero = PagamentoEsclusioniRules.ImportoNonLiquidabile(0, calcolo);
var anna = new StudenteEsclusoPagamento("TESTANNA00000001", "000123", "Rossi", "Anna", "02", "001", "Assegnazioni", causePA);
var luca = new StudenteEsclusoPagamento("TESTLUCA00000002", "000124", "Bianchi", "Luca", "05", "002", "Calcolo", zero);
var extra = new MotivoEsclusionePagamento("IBAN mancante", "Nessun IBAN registrato.");
var contesto = new ContestoEsclusioniPagamento("2026/2027", "Borsa di studio", "Saldo", "BSS0", "SA", "07/09/2026", new DateTime(2026, 9, 7, 14, 30, 0), true);
using var workbook = PagamentoEsclusioniWorkbook.Create(new[] { anna, anna, luca, anna with { Causa = extra, Fase = "IBAN" } }, contesto);
Check(workbook.Worksheets.Count == 3, "Il prospetto deve contenere elenco, riepilogo e dettagli tecnici.");
var elenco = workbook.Worksheet("Studenti esclusi");
Check(elenco.LastColumnUsed()!.ColumnNumber() == 6, "Il prospetto deve avere sei colonne, senza verifica suggerita.");
Check(elenco.Cell(7, 5).GetString() == "Motivo" && elenco.Cell(7, 6).GetString() == "Dettaglio", "Usare le due colonne richieste per le spiegazioni.");
Check(elenco.Cell(8, 1).GetString() == "Bianchi", "Ordinamento per cognome.");
Check(elenco.Cell(9, 4).GetString() == "000123", "Il numero domanda deve conservare gli zeri iniziali.");
Check(elenco.Cell(9, 5).GetString() == causePA.Motivo, "Il motivo principale non deve essere sovrascritto da una causa successiva.");
Check(elenco.Cell(9, 6).GetString().Contains("2. IBAN mancante"), "Le altre cause accertate devono comparire nel dettaglio dello stesso studente.");
Check(elenco.LastRowUsed()!.RowNumber() == 9, "Una sola riga per studente, senza duplicati.");
Check(elenco.SheetView.SplitRow == 7 && elenco.Tables.Single().ShowAutoFilter, "Intestazioni bloccate e filtri devono essere presenti.");
Check(elenco.Row(9).Height >= 64 && elenco.Cell(9, 6).Style.Alignment.WrapText, "Le motivazioni multiple devono essere leggibili.");
var summary = workbook.Worksheet("Riepilogo");
Check(summary.LastColumnUsed()!.ColumnNumber() == 2, "Anche il riepilogo deve essere privo di indicazioni operative.");
Check(!workbook.Worksheets.SelectMany(s => s.CellsUsed()).Any(c => c.GetString().Contains("Verifica suggerita")), "Non esportare la colonna rimossa in altri fogli.");
Check(summary.Cell(5, 2).GetDouble() == 2, "Il totale riepilogo deve contare gli studenti, non le cause.");
Check(summary.Range(8, 2, 9, 2).Cells().Sum(c => c.GetDouble()) == 2, "I conteggi dei motivi principali devono riconciliare con il totale.");
var technical = workbook.Worksheet("Dettagli tecnici");
Check(technical.Cell(8, 7).DataType == XLDataType.Number && technical.Cell(8, 7).GetDouble() == 0, "Lo zero calcolato deve essere numerico.");
Check(technical.Cell(9, 7).IsEmpty(), "L'importo non calcolato deve restare vuoto.");

using var stream = new MemoryStream();
workbook.SaveAs(stream);
stream.Position = 0;
using var reopened = new XLWorkbook(stream);
Check(reopened.Worksheet("Studenti esclusi").Cell(9, 4).GetString() == "000123", "Gli identificativi devono conservarsi dopo salvataggio e riapertura.");
Check(reopened.Worksheet("Studenti esclusi").Cell(9, 4).DataType == XLDataType.Text && reopened.Worksheet("Studenti esclusi").Cell(9, 4).Style.NumberFormat.Format == "@",
    "Il numero domanda deve rimanere esplicitamente testo, anche nei lettori Excel alternativi.");
Check(reopened.Worksheet("Studenti esclusi").Cell(1, 1).GetString() == "Studenti esclusi dall'elaborazione", "Il titolo deve essere salvato nel file.");
Check(reopened.Worksheet("Dettagli tecnici").Cell(8, 7).DataType == XLDataType.Number, "Gli importi devono rimanere numerici nel file Excel.");
using var partial = PagamentoEsclusioniWorkbook.Create(new[] { anna }, contesto with { Completata = false });
Check(partial.Worksheet(1).Cell(4, 1).GetString().Contains("INTERROTTA"), "Un'elaborazione interrotta deve essere riconoscibile.");
using var empty = PagamentoEsclusioniWorkbook.Create(Array.Empty<StudenteEsclusoPagamento>(), contesto);
Check(empty.Worksheet("Riepilogo").Cell(5, 2).GetDouble() == 0, "Un ciclo senza esclusi deve produrre un riepilogo a zero.");
using var literal = PagamentoEsclusioniWorkbook.Create(new[] { anna with { Nome = "=1+1", NumeroDomanda = "001" } }, contesto);
Check(!literal.Worksheet(1).Cell(8, 2).HasFormula, "I dati anagrafici non devono diventare formule Excel.");

var fuoriImpegno = PagamentoFlussoRules.ImpegnoNonElaborato("003", "0000", new[] { "001", "002" })!;
Check(fuoriImpegno.Motivo == "Impegno non compreso tra quelli da elaborare"
      && fuoriImpegno.Dettaglio.Contains("003") && fuoriImpegno.Dettaglio.Contains("001, 002"),
    "L'impegno non elaborato deve essere spiegato mostrando quello dello studente e quelli previsti.");
var impegnoDiverso = PagamentoFlussoRules.ImpegnoNonElaborato("002", "001", new[] { "001" })!;
Check(impegnoDiverso.Motivo == "Impegno diverso da quello selezionato"
      && impegnoDiverso.Dettaglio.Contains("studente: 002") && impegnoDiverso.Dettaglio.Contains("pagamento: 001"),
    "La selezione di un singolo impegno deve distinguersi da un impegno assente dall'elenco complessivo.");
Check(PagamentoFlussoRules.ImpegnoNonElaborato("001", "0000", new[] { "001", "002" }) == null
      && PagamentoFlussoRules.ImpegnoNonElaborato("001", "001", new[] { "001" }) == null,
    "Uno studente con impegno compreso nella generazione non deve ricevere una causa di mancato flusso.");
Check(PagamentoFlussoRules.ImpegnoNonElaborato(null, "0000", new[] { "001" })!.Motivo.Contains("non disponibile")
      && PagamentoFlussoRules.ImpegnoNonElaborato("  ", "0000", new[] { "001" })!.Motivo.Contains("non disponibile"),
    "Un impegno mancante deve avere una spiegazione specifica.");
Check(PagamentoFlussoRules.ImpegnoNonElaborato("001", "0000", Array.Empty<string>())!.Dettaglio.Contains("elenco degli impegni da elaborare è vuoto"),
    "Un elenco vuoto degli impegni deve spiegare perché non viene generato alcun flusso.");
var fuoriAnno = PagamentoFlussoRules.AnnoCorsoNonElaborato(2, true, false)!;
Check(fuoriAnno.Dettaglio.Contains("studente: 2") && fuoriAnno.Dettaglio.Contains("limitata alle matricole"),
    "Il filtro matricole deve riportare l'anno dello studente escluso dal flusso.");
Check(PagamentoFlussoRules.AnnoCorsoNonElaborato(1, false, true)!.Dettaglio.Contains("limitata agli anni successivi"),
    "Il filtro anni successivi deve spiegare la mancata emissione delle matricole.");
Check(PagamentoFlussoRules.AnnoCorsoNonElaborato(1, true, false) == null
      && PagamentoFlussoRules.AnnoCorsoNonElaborato(2, false, true) == null
      && PagamentoFlussoRules.AnnoCorsoNonElaborato(1, true, true) == null
      && PagamentoFlussoRules.AnnoCorsoNonElaborato(2, true, true) == null,
    "Il filtro anno non deve attribuire motivazioni agli studenti ammessi alla generazione.");

// Whole-payment audit: two commitments, early and late exclusions, and a student without a flow.
using var datiAudit = new DataTable();
foreach (string col in new[] { "Impegno", "CategoriaPagam", "CategoriaCU", "NumDomanda", "CodFiscale", "Cognome", "Nome",
             "IBAN", "NumeroImpegno", "ImportoLordo", "ImportoNetto", "ImportoBeneficio", "NumAssegnazioni", "Assegnazioni_Sintesi" })
    datiAudit.Columns.Add(col);
datiAudit.Rows.Add("001", "SA", "311", "000125", "TESTPAOLO0000003", "Verdi", "Paolo", "IT00TEST", "001", 250, 250, 1000, 0, "");
datiAudit.Rows.Add("002", "SA", "311", "000126", "TESTSARA00000004", "Neri", "Sara", "IT01TEST", "002", 125, 125, 1000, 0, "");
datiAudit.Rows.Add("", "SA", "", "000123", anna.CodFiscale, anna.Cognome, anna.Nome, "", "", 0, 0, 1000, 0, "");
datiAudit.Rows.Add("002", "SA", "311", "000124", luca.CodFiscale, luca.Cognome, luca.Nome, "IT02TEST", "002", 100, 0, 1000, 1, "PA: 01/09/2026 - 30/06/2027");
datiAudit.Rows.Add("003", "SA", "311", "000127", "TESTMARCO0000005", "Gialli", "Marco", "IT03TEST", "003", 100, 100, 1000, 0, "");
var flussiAudit = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
{
    ["testpaolo0000003"] = new() { "flusso_SenzaPA_001" },
    ["TESTSARA00000004"] = new() { "flusso_SenzaPA_002", "flusso_SenzaPA_002" }
};
var importiAudit = new Dictionary<string, ImportiAuditPagamento>(StringComparer.OrdinalIgnoreCase)
{
    ["TESTPAOLO0000003"] = new(250, 250), ["TESTSARA00000004"] = new(125, 125),
    [luca.CodFiscale] = new(100, -3.4), ["TESTMARCO0000005"] = new(100, 100)
};
var esclusioniAudit = new[] { anna, anna, anna with { Causa = extra }, luca with
{
    Causa = PagamentoEsclusioniRules.ImportoNonLiquidabile(-3.4, "Lordo 100,00 €; trattenuta alloggio 103,40 €.")
} };
var motiviNonFlusso = new Dictionary<string, MotivoEsclusionePagamento>(StringComparer.OrdinalIgnoreCase)
{
    ["testmarco0000005"] = fuoriImpegno,
    // Existing outcomes must take precedence over a flow diagnostic.
    [anna.CodFiscale] = fuoriImpegno,
    ["TESTPAOLO0000003"] = fuoriAnno
};
using var auditWorkbook = PagamentoAuditWorkbook.Create(datiAudit, esclusioniAudit, flussiAudit, importiAudit, contesto, motiviNonFlusso);
var audit = auditWorkbook.Worksheet("Audit completo");
int AuditCol(string nome) => audit.Row(7).CellsUsed().Single(c => c.GetString() == nome).Address.ColumnNumber;
int AuditRow(string cf) => audit.Column(AuditCol("CodFiscale")).CellsUsed().Single(c => c.GetString() == cf).Address.RowNumber;
var auditSummary = auditWorkbook.Worksheet("Riepilogo");
Check(auditWorkbook.Worksheets.Count == 2, "L'audit deve contenere il dettaglio completo e il riepilogo.");
Check(audit.LastRowUsed()!.RowNumber() == 12 && auditSummary.Cell(5, 2).GetDouble() == 5,
    "L'audit deve conservare tutta la popolazione iniziale, inclusi esclusi e studenti senza flusso.");
Check(datiAudit.Columns.Cast<DataColumn>().All(c => audit.Row(7).CellsUsed().Any(cell => cell.GetString() == c.ColumnName)),
    "Nessun campo dell'audit per impegno deve andare perso nell'audit generale.");
Check(audit.Cell(AuditRow(anna.CodFiscale), AuditCol("Esito elaborazione")).GetString() == "Escluso dall'elaborazione"
      && audit.Cell(AuditRow(anna.CodFiscale), AuditCol("Impegno")).IsEmpty(),
    "Il vincitore PA escluso prima dell'attribuzione dell'impegno deve essere presente anche senza impegno.");
Check(audit.Cell(AuditRow(anna.CodFiscale), AuditCol("Motivo")).GetString() == causePA.Motivo
      && audit.Cell(AuditRow(anna.CodFiscale), AuditCol("Dettaglio")).GetString().Contains("2. IBAN mancante"),
    "L'audit deve condividere motivazione principale e altre cause accertate con il file degli esclusi.");
Check(audit.Cell(AuditRow("TESTMARCO0000005"), AuditCol("Esito elaborazione")).GetString() == "Non inserito nel flusso"
      && audit.Cell(AuditRow("TESTMARCO0000005"), AuditCol("Motivo")).GetString() == fuoriImpegno.Motivo
      && audit.Cell(AuditRow("TESTMARCO0000005"), AuditCol("Dettaglio")).GetString() == fuoriImpegno.Dettaglio
      && audit.Cell(AuditRow("TESTMARCO0000005"), AuditCol("Fase di esclusione")).GetString() == "Generazione flussi",
    "Lo studente non in flusso deve avere motivo, dettaglio e fase effettivi anche con CF di diversa capitalizzazione.");
Check(audit.Cell(AuditRow("TESTPAOLO0000003"), AuditCol("Motivo")).IsEmpty()
      && audit.Cell(AuditRow("TESTPAOLO0000003"), AuditCol("Fase di esclusione")).IsEmpty(),
    "Un flusso effettivamente generato non deve essere descritto come un mancato inserimento.");
Check(audit.Cell(AuditRow(anna.CodFiscale), AuditCol("ImportoNetto")).IsEmpty()
      && audit.Cell(AuditRow(anna.CodFiscale), AuditCol("ImportoLordo")).IsEmpty(),
    "Gli importi non calcolati non devono apparire come zero.");
Check(audit.Cell(AuditRow(luca.CodFiscale), AuditCol("ImportoNetto")).GetDouble() == -3.4,
    "L'audit deve mostrare anche il netto calcolato prima di un'esclusione, conservando segno e centesimi.");
Check(audit.Cell(AuditRow("TESTPAOLO0000003"), AuditCol("Impegno")).GetString() == "001"
      && audit.Cell(AuditRow("TESTSARA00000004"), AuditCol("Impegno")).GetString() == "002",
    "L'audit deve riportare l'impegno di ciascuno studente, senza imporre un impegno unico.");
Check(auditSummary.Range(8, 2, 11, 2).Cells().Sum(c => c.GetDouble()) == 5
      && auditSummary.Cell(8, 2).GetDouble() == 2 && auditSummary.Cell(9, 2).GetDouble() == 2
      && auditSummary.Cell(10, 2).GetDouble() == 1,
    "I conteggi per esito devono riconciliare con tutti gli studenti, senza duplicare le cause.");
Check(auditSummary.Cell(13, 2).GetDouble() == 375,
    "Il totale netto dei flussi deve includere solo gli studenti effettivamente inseriti, una sola volta.");
Check(audit.Cell(AuditRow("TESTSARA00000004"), AuditCol("File flusso")).GetString() == "flusso_SenzaPA_002",
    "I nomi dei flussi devono essere presenti e privi di duplicati.");
Check(audit.SheetView.SplitRow == 7 && audit.SheetView.SplitColumn == 4 && audit.Tables.Single().ShowAutoFilter,
    "L'audit deve avere filtri, intestazioni e identificativi bloccati durante lo scorrimento.");
Check(audit.Cell(AuditRow(anna.CodFiscale), AuditCol("NumDomanda")).GetString() == "000123",
    "Gli identificativi dell'audit devono mantenere gli zeri iniziali.");
using var auditStream = new MemoryStream();
auditWorkbook.SaveAs(auditStream);
auditStream.Position = 0;
using var auditReopened = new XLWorkbook(auditStream);
Check(auditReopened.Worksheet("Audit completo").Cell(AuditRow("TESTMARCO0000005"), AuditCol("Dettaglio")).GetString() == fuoriImpegno.Dettaglio,
    "La spiegazione del mancato flusso deve conservarsi nel file Excel salvato e riaperto.");
Check(auditReopened.Worksheet("Audit completo").Cell(AuditRow(anna.CodFiscale), AuditCol("Impegno")).IsEmpty()
      && auditReopened.Worksheet("Audit completo").Cell(AuditRow("TESTPAOLO0000003"), AuditCol("Motivo")).IsEmpty(),
    "Impegni mancanti e motivazioni non pertinenti devono rimanere celle vuote anche nel file salvato.");
Check(auditReopened.Worksheet("Audit completo").Cell(AuditRow(luca.CodFiscale), AuditCol("ImportoNetto")).DataType == XLDataType.Number
      && auditReopened.Worksheet("Audit completo").Cell(AuditRow(anna.CodFiscale), AuditCol("NumDomanda")).GetString() == "000123",
    "Il salvataggio deve conservare importi numerici e identificativi testuali.");
using var auditPartial = PagamentoAuditWorkbook.Create(datiAudit, esclusioniAudit, flussiAudit, importiAudit, contesto with { Completata = false });
Check(auditPartial.Worksheet(1).Cell(4, 1).GetString().Contains("INTERROTTA")
      && auditPartial.Worksheet("Riepilogo").Cell(11, 2).GetDouble() == 1
      && auditPartial.Worksheet("Riepilogo").Cell(5, 2).GetDouble() == 5,
    "L'audit interrotto deve mantenere tutti gli studenti e distinguere chi non ha un esito definitivo.");
Check(auditPartial.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Motivo")).GetString() == "Generazione del flusso non completata"
      && auditPartial.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Dettaglio")).GetString().Contains("non è disponibile un esito definitivo")
      && auditPartial.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Fase di esclusione")).IsEmpty(),
    "Se la procedura si interrompe senza una causa registrata, non attribuire un'esclusione definitiva o una fase non raggiunta.");
using var auditPartialConMotivi = PagamentoAuditWorkbook.Create(datiAudit, esclusioniAudit, flussiAudit, importiAudit,
    contesto with { Completata = false }, motiviNonFlusso);
Check(auditPartialConMotivi.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Motivo")).GetString() == fuoriImpegno.Motivo
      && auditPartialConMotivi.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Dettaglio")).GetString().Contains("complessiva si è interrotta"),
    "Un'interruzione successiva non deve cancellare una causa di mancato flusso già rilevata.");
using var auditFiltroAnno = PagamentoAuditWorkbook.Create(datiAudit, esclusioniAudit, flussiAudit, importiAudit, contesto,
    new Dictionary<string, MotivoEsclusionePagamento> { ["TESTMARCO0000005"] = fuoriAnno });
Check(auditFiltroAnno.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Motivo")).GetString() == fuoriAnno.Motivo
      && auditFiltroAnno.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Dettaglio")).GetString() == fuoriAnno.Dettaglio,
    "Anche il motivo del filtro matricole/anni successivi deve essere leggibile nell'audit.");
using var senzaFlussi = PagamentoAuditWorkbook.Create(datiAudit, esclusioniAudit,
    new Dictionary<string, List<string>>(), importiAudit, contesto);
Check(senzaFlussi.Worksheet("Riepilogo").Cell(8, 2).GetDouble() == 0
      && senzaFlussi.Worksheet("Riepilogo").Cell(13, 2).GetDouble() == 0,
    "Un pagamento senza flussi non deve ereditare studenti o totali da un pagamento precedente.");
Check(senzaFlussi.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Motivo")).GetString() == "Mancato inserimento da verificare"
      && senzaFlussi.Worksheet(1).Cell(AuditRow("TESTMARCO0000005"), AuditCol("Dettaglio")).GetString().Contains("motivo non è determinabile"),
    "Se manca una causa registrata, indicare esplicitamente l'anomalia senza inventare un motivo o riusare quello del ciclo precedente.");
using var auditEmpty = PagamentoAuditWorkbook.Create(datiAudit.Clone(), Array.Empty<StudenteEsclusoPagamento>(),
    new Dictionary<string, List<string>>(), new Dictionary<string, ImportiAuditPagamento>(), contesto);
Check(auditEmpty.Worksheet("Riepilogo").Cell(5, 2).GetDouble() == 0 && auditEmpty.Worksheet(1).Tables.Count() == 1,
    "Un pagamento senza studenti deve produrre un audit vuoto con intestazioni e riepilogo a zero.");
using var datiTuttiEsclusi = datiAudit.Clone();
foreach (DataRow r in datiAudit.Rows)
    if (new[] { anna.CodFiscale, luca.CodFiscale }.Contains((string)r["CodFiscale"])) datiTuttiEsclusi.ImportRow(r);
using var auditTuttiEsclusi = PagamentoAuditWorkbook.Create(datiTuttiEsclusi, esclusioniAudit,
    new Dictionary<string, List<string>>(), importiAudit, contesto);
Check(auditTuttiEsclusi.Worksheet("Audit completo").LastRowUsed()!.RowNumber() == 9
      && auditTuttiEsclusi.Worksheet("Riepilogo").Cell(9, 2).GetDouble() == 2
      && auditTuttiEsclusi.Worksheet("Riepilogo").Cell(13, 2).GetDouble() == 0,
    "L'audit deve essere completo anche quando tutti gli studenti sono esclusi e non esistono flussi per impegno.");
using var importoAssente = PagamentoAuditWorkbook.Create(datiAudit, esclusioniAudit, flussiAudit,
    new Dictionary<string, ImportiAuditPagamento>(), contesto);
Check(importoAssente.Worksheet("Riepilogo").Cell(13, 2).GetString() == "Non disponibile",
    "Un importo non acquisito non deve produrre un totale netto falsamente pari a zero.");
importiAudit[luca.CodFiscale] = new(100, 0);
using var auditZero = PagamentoAuditWorkbook.Create(datiAudit, esclusioniAudit, flussiAudit, importiAudit, contesto);
Check(auditZero.Worksheet(1).Cell(AuditRow(luca.CodFiscale), AuditCol("ImportoNetto")).DataType == XLDataType.Number
      && auditZero.Worksheet(1).Cell(AuditRow(luca.CodFiscale), AuditCol("ImportoNetto")).GetDouble() == 0,
    "Un netto realmente calcolato a zero deve rimanere distinto da un calcolo assente.");

if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    workbook.SaveAs(Path.Combine(args[0], "Esempio esclusioni - dati fittizi.xlsx"));
    auditWorkbook.SaveAs(Path.Combine(args[0], "Esempio audit - dati fittizi.xlsx"));
}
Console.WriteLine($"Superate {checks} verifiche su motivazioni, casi PA, esclusioni e audit completo.");
