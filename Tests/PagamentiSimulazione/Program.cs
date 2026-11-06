using ProcedureNet7;
using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using System.Runtime.CompilerServices;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
var type = typeof(ProceduraPagamenti);
// Evita costruttori di form, connessioni e configurazioni dell'applicazione.
using var procedure = (ProceduraPagamenti)RuntimeHelpers.GetUninitializedObject(type);
void Set(string name, object? value) => type.GetField(name, flags)!.SetValue(procedure, value);
T Get<T>(string name) => (T)type.GetField(name, flags)!.GetValue(procedure)!;
object? Call(string name, params object?[] args) => type.GetMethod(name, flags)!.Invoke(procedure, args);
bool Flag(string name) => (bool)type.GetProperty(name, flags)!.GetValue(procedure)!;

foreach (string field in new[] { "studentiDaPagare", "studentiConErroriPA", "impegniList", "dictQueryWhere", "_movimentiSimulati", "_detrazioniSimulate" })
    Set(field, Activator.CreateInstance(type.GetField(field, flags)!.FieldType));
using var connection = new SqlConnection(); // Intenzionalmente chiusa: qualsiasi esecuzione SQL fallirebbe.
Set("CONNECTION", connection);

var procedureArgs = new ArgsPagamenti
{
    _selectedSaveFolder = Path.Combine(Path.GetTempPath(), "pagamenti-simulazione-fixture"),
    _annoAccademico = "20252026",
    _dataRiferimento = "02/11/2026",
    _tipoProcedura = "0"
};
Call("AssignArgumentValues", procedureArgs);
Set("dbTableName", "FixturePagamenti");
Set("dbTableExists", false);
Set("tipoBeneficio", "BS");
Set("codTipoPagamento", "BSP0");
string extraction = (string)Call("BuildCreateDBTableQuery")!;
Check(extraction.Contains("CREATE TABLE FixturePagamenti") && extraction.Contains("INSERT INTO FixturePagamenti"),
    "La procedura reale deve continuare a usare la tabella selezionata.");
Check(!Flag("IsSimulazione") && !Flag("SimulazioneSenzaImpegni"), "La procedura reale non deve essere simulata.");

foreach (var mode in new[] { TipologiaProcedura.Simulazione, TipologiaProcedura.SimulazioneSenzaImpegni, TipologiaProcedura.SimulazionePATollerante })
{
    procedureArgs._tipoProcedura = mode.ToCode();
    Call("AssignArgumentValues", procedureArgs);
    Set("tipoBeneficio", "BS");
    Set("codTipoPagamento", "BSP0");
    Set("dbTableName", "TabellaPermanenteDaProteggere");
    Set("dbTableExists", true);
    Check(Flag("IsSimulazione"), "Tutte le simulazioni devono bloccare le scritture permanenti.");
    Check(Flag("SimulazioneSenzaImpegni") == (mode != TipologiaProcedura.Simulazione),
        "Le due simulazioni meno stringenti devono ammettere l'assenza dell'impegno.");
    Check(Flag("SimulazionePATollerante") == (mode == TipologiaProcedura.SimulazionePATollerante),
        "La tolleranza delle date PA deve attivarsi soltanto nella nuova modalità.");
    string query = (string)Call("BuildCreateDBTableQuery")!;
    Check(query == extraction.Replace("FixturePagamenti", "#PagamentiSimulazione"),
        "La simulazione deve estrarre gli stessi studenti, cambiando soltanto la destinazione temporanea.");
    Check(!query.Contains("TabellaPermanenteDaProteggere") && !query.Contains("TRUNCATE"),
        "La tabella permanente non deve essere troncata o popolata in simulazione.");

    Call("HandleTableNameSelectionDialog");
    Check(Get<string>("dbTableName") == "#PagamentiSimulazione", "La simulazione deve evitare la selezione di una tabella permanente.");
    Check(RiepilogoArguments.Instance.nomeTabella.Contains("simulazione"), "Il riepilogo deve identificare la tabella temporanea.");
    string outputFolder = Get<string>("selectedSaveFolder");
    Check(outputFolder == Path.Combine(procedureArgs._selectedSaveFolder,
        mode == TipologiaProcedura.Simulazione ? "SIMULAZIONE" :
        mode == TipologiaProcedura.SimulazionePATollerante ? "SIMULAZIONE_PA_TOLLERANTE" : "SIMULAZIONE_SENZA_IMPEGNI"),
        "I risultati simulati devono avere una cartella dedicata.");

    // Questi metodi devono rientrare prima di qualsiasi istruzione SQL o modifica agli studenti.
    Call("ResetCFEstrazione");
    Set("selectedVecchioMandato", "BSP0_VECCHIO");
    Call("ClearMovimentiContabili");
    var students = new List<StudentePagamenti> { null! };
    Set("insertInDatabase", true); // Verifica anche la difesa indipendente dal prompt.
    Call("InsertIntoMovimentazioni", students, "1367", null);
    Check(students.Count == 1 && students[0] == null,
        "La simulazione deve saltare gli inserimenti prima di modificare la lista o consultare il DB.");
    Check(connection.State == System.Data.ConnectionState.Closed, "Le protezioni dalle scritture non devono richiedere SQL.");

    var virtualMovements = Get<List<(string CodFiscale, string Mandato)>>("_movimentiSimulati");
    virtualMovements.Add(("CF1", "BSP0_VECCHIO_1367"));
    virtualMovements.Add(("CF2", "BSS0_NUOVO_326"));
    Call("ClearMovimentiContabili");
    Check(virtualMovements.SequenceEqual(new[] { ("CF2", "BSS0_NUOVO_326") }),
        "L'aggiornamento del mandato deve rimuovere solo i movimenti simulati di quel mandato.");
    Call("ResetStatoPerCiclo");
    Call("AssignArgumentValues", procedureArgs);
    Check(Get<string>("selectedSaveFolder") == outputFolder && Flag("IsSimulazione"),
        "Ogni ciclo automatico deve conservare la modalità e ripartire dalla cartella originale.");
    Check(virtualMovements.Count == 1, "I pagamenti simulati devono restare visibili nei cicli successivi.");
    virtualMovements.Clear();
}

procedureArgs._tipoProcedura = TipologiaProcedura.SimulazioneSenzaImpegni.ToCode();
Call("AssignArgumentValues", procedureArgs);
Set("codTipoPagamento", "BSP0");
Set("categoriaPagam", "PR");
var student = new StudentePagamenti("1001", "RSSMRA00A01H501A", "ROSSI", "MARIO", "01/01/2000", "M",
    "01", "0001", false, false, 2000, 1, 1, 1, 0, true, true, "A", false);
student.InformazioniPagamento.ImportoDaPagareLordo = 1050.25;
student.InformazioniPagamento.ImportoDaPagare = 1000.25;
student.InformazioniConto.IBAN = "IT60X0542811101000000123456";
student.InformazioniSede.Residenza.indirizzo = "VIA DI PROVA 1";
student.InformazioniSede.Residenza.codComune = "H501";
student.InformazioniSede.Residenza.nomeComune = "ROMA";
student.InformazioniSede.Residenza.provincia = "RM";
student.InformazioniSede.Residenza.CAP = "00100";
student.InformazioniPersonali.LuogoNascita.codComune = "H501";
student.InformazioniPersonali.LuogoNascita.nomeComune = "ROMA";
student.InformazioniPersonali.LuogoNascita.provincia = "RM";
using var flow = (DataTable)Call("GenerareFlussoDataTable", new List<StudentePagamenti> { student }, "00")!;
Check(flow.Rows.Count == 1 && (string)flow.Rows[0]["Cod_fiscale"] == student.InformazioniPersonali.CodFiscale,
    "Lo studente senza impegno deve essere presente nel flusso reale prodotto dalla simulazione.");
Check((double)flow.Rows[0]["totale_lordo"] == 1050.25 && (double)flow.Rows[0]["importo_netto"] == 1000.25,
    "La simulazione senza impegni deve conservare gli importi calcolati nel flusso.");
Check(student.InformazioniPagamento.NumeroImpegno == string.Empty,
    "La simulazione non deve inventare un impegno di spesa per produrre il flusso.");
student.AddDetrazione("03", 50, "Detrazione già presente", true);
student.AddDetrazione("01", 10, "Acconto già presente", true);
student.AddDetrazione("04", 20, "Detrazione calcolata", false);
Call("RegisterSimulatedMovements", new List<StudentePagamenti> { student });
var deductions = Get<Dictionary<(string CodFiscale, string Causale), string>>("_detrazioniSimulate");
Check(deductions.Count == 2 && deductions.ContainsKey((student.InformazioniPersonali.CodFiscale, "03")),
    "Le detrazioni già presenti e usate nel pagamento simulato devono essere consumate per i cicli successivi.");
Set("selectedVecchioMandato", student.InformazioniPagamento.MandatoProvvisorio);
Call("ClearMovimentiContabili");
Check(deductions.Count == 1 && deductions.ContainsKey((student.InformazioniPersonali.CodFiscale, "01")),
    "La sostituzione del mandato deve ripristinare solo le detrazioni ripristinabili nella procedura reale.");
string fixtureFolder = Path.Combine(Path.GetTempPath(), "ProcedureNet7-PagamentiSimulazione-" + Guid.NewGuid().ToString("N"));
string fixtureFile = Path.Combine(fixtureFolder, "flusso_senza_impegno.txt");
try
{
    Utilities.WriteDataTableToTextFile(flow, fixtureFolder, "flusso_senza_impegno");
    var lines = File.ReadAllLines(fixtureFile);
    Check(lines.Length == 1 && lines[0].Split(';')[1] == student.InformazioniPersonali.CodFiscale,
        "Il flusso dello studente senza impegno deve poter essere salvato senza SQL.");
}
finally
{
    if (File.Exists(fixtureFile)) File.Delete(fixtureFile);
    if (Directory.Exists(fixtureFolder)) Directory.Delete(fixtureFolder);
}

foreach (var mode in new[] { TipologiaProcedura.Completa, TipologiaProcedura.SenzaCreazioneTabella, TipologiaProcedura.SoloCreazioneTabella })
{
    procedureArgs._tipoProcedura = mode.ToCode();
    Call("AssignArgumentValues", procedureArgs);
    Check(!Flag("IsSimulazione") && !Flag("SimulazioneSenzaImpegni"), "Le modalità esistenti devono mantenere il comportamento reale.");
    Check(Get<string>("selectedSaveFolder") == procedureArgs._selectedSaveFolder, "La cartella delle procedure reali deve restare invariata.");
}

var settings = type.Assembly.GetType("ProcedureNet7.PagamentiSettings")!;
var options = (Dictionary<string, string>)settings.GetField("pagamentiTipoProcedura")!.GetValue(null)!;
Check(options["3"] == "Simulazione" && options["4"] == "Simulazione anche senza impegni" && options["5"] == "Simulazione PA tollerante",
    "Il selettore della procedura deve esporre tutte e tre le simulazioni.");
checks += AssegnazioniPaRegressionTests.Run(procedure);
checks += DomiciliPagamentoRegressionTests.Run(procedure);
bool domiciliSql = args.Contains("--domicili-sql", StringComparer.OrdinalIgnoreCase);
if (domiciliSql) checks += DomiciliPagamentoRegressionTests.RunSql();
Console.WriteLine($"OK: {checks} verifiche simulazione, {(domiciliSql ? "incluse fixture domicili su LocalDB" : "senza connessione al database")}.");
