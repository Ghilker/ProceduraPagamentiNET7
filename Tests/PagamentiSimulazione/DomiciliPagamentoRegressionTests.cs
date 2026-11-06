using ProcedureNet7;
using ProcedureNet7.PagamentiProcessor;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

internal static class DomiciliPagamentoRegressionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run(ProceduraPagamenti procedure)
    {
        int checks = 0;
        foreach (string category in new[] { "PR", "SA" })
        {
            checks += CheckPayment(Student(Contract()), category, new DateTime(2027, 9, 30), "B", false, true);
            checks += CheckPayment(Student(), category, new DateTime(2027, 9, 30), "D", true, false);
            var invalid = Contract();
            invalid.NumeroSerieContratto = "";
            checks += CheckPayment(Student(invalid), category, new DateTime(2027, 9, 30), "D", true, false);

            checks += CheckPayment(Student(Contract(new DateTime(2027, 1, 31))), category,
                new DateTime(2027, 3, 2), "B", category == "SA", true);
            checks += CheckPayment(Student(Contract(new DateTime(2027, 1, 31))), category,
                new DateTime(2027, 3, 3), "D", true, false);

            foreach (string benefit in new[] { "PA", "PK" })
            {
                var winner = Student();
                winner.EsitiConcorsoByBenefit[benefit] = new EsitoConcorsoBenefitRaw { CodTipoEsito = 2 };
                checks += CheckPayment(winner, category, new DateTime(2027, 9, 30), "B", false, false);
                var eligible = Student();
                eligible.EsitiConcorsoByBenefit[benefit] = new EsitoConcorsoBenefitRaw { CodTipoEsito = 1 };
                eligible.InformazioniSede.HasAlloggio12 = true;
                checks += CheckPayment(eligible, category, new DateTime(2027, 9, 30), "B", false, false);
            }
        }

        var formula = Student();
        formula.InformazioniPersonali.Sesso = "F";
        formula.InformazioniIscrizione.CorsoStem = true;
        formula.InformazioniIscrizione.AnnoCorso = -1;
        formula.InformazioniPagamento.ValoreISEE = 10000;
        formula.InformazioniPagamento.ConcessaMonetizzazioneMensa = true;
        checks += CheckPayment(formula, "SA", new DateTime(2027, 9, 30), "D", true, false, 975);

        // Il flag mantiene il percorso legacy anche se sono presenti dati del nuovo modello.
        var legacy = Student();
        legacy.InformazioniSede.UsaNuovaGestioneDomicili = false;
        legacy.SetDomicilio("H501", true, "LEGACY-101", new DateTime(2026, 9, 1), new DateTime(2026, 10, 1),
            new DateTime(2027, 9, 30), 12, false, 0, "", true, false, false, "", 0);
        checks += CheckPayment(legacy, "SA", new DateTime(2027, 9, 30), "B", false, true);

        var type = typeof(ProceduraPagamenti);
        var year = type.GetField("selectedAA", PrivateInstance)!;
        var selectedDate = type.GetField("selectedDataRiferimento", PrivateInstance)!;
        var reference = type.GetProperty("DataRiferimentoStatusSede", PrivateInstance)!;
        object? oldYear = year.GetValue(procedure), oldDate = selectedDate.GetValue(procedure);
        try
        {
            selectedDate.SetValue(procedure, "02/03/2027");
            year.SetValue(procedure, "20262027");
            Assert((DateTime)reference.GetValue(procedure)! == new DateTime(2027, 3, 2),
                "Dal 2627 lo status deve usare la data selezionata nell'estrazione.");
            checks++;
            foreach (string aa in new[] { "20242025", "20252026" })
            {
                year.SetValue(procedure, aa);
                Assert((DateTime)reference.GetValue(procedure)! == DateTime.Today,
                    "Gli anni legacy devono conservare la data corrente.");
                Assert((bool)type.GetMethod("PopulateDomiciliNuovaGestione", PrivateInstance)!.Invoke(procedure, null)! == false,
                    "Gli anni legacy non devono eseguire il nuovo caricamento SQL (la connessione è chiusa).");
                checks += 2;
            }
        }
        finally
        {
            year.SetValue(procedure, oldYear);
            selectedDate.SetValue(procedure, oldDate);
        }
        return checks;
    }

    internal static int RunSql()
    {
        // Database dedicato su LocalDB, nome e file univoci; nessuna configurazione dell'applicazione.
        const string localDb = @"Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=master;Integrated Security=true;Connect Timeout=15;Pooling=false";
        string database = "CodexPagamentiDomicili_" + Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Path.GetTempPath(), database);
        using var master = new SqlConnection(localDb);
        master.Open();
        Directory.CreateDirectory(folder);
        bool created = false;
        try
        {
            string dataPath = Path.Combine(folder, "fixture.mdf").Replace("'", "''");
            string logPath = Path.Combine(folder, "fixture_log.ldf").Replace("'", "''");
            using (var create = new SqlCommand($"CREATE DATABASE [{database}] ON PRIMARY (NAME=N'{database}', FILENAME=N'{dataPath}') LOG ON (NAME=N'{database}_log', FILENAME=N'{logPath}')", master))
                create.ExecuteNonQuery();
            created = true;
            var settings = new SqlConnectionStringBuilder(localDb) { InitialCatalog = database };
            using var connection = new SqlConnection(settings.ConnectionString);
            connection.Open();
            return RunSqlFixture(connection);
        }
        finally
        {
            // RunSqlFixture ha già disposto transazione e connessione prima del DROP.
            if (created)
            {
                using var drop = new SqlCommand($"DROP DATABASE [{database}]", master);
                drop.ExecuteNonQuery();
            }
            Directory.Delete(folder);
        }
    }

    private static int RunSqlFixture(SqlConnection connection)
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var command = new SqlCommand(FixtureSql, connection, transaction);
        command.ExecuteNonQuery();

        var valid = Student();
        valid.InformazioniPersonali.NumDomanda = "1";
        var absent = Student(Contract());
        absent.InformazioniPersonali.CodFiscale = "BNCLGU00A01H501B";
        absent.InformazioniPersonali.NumDomanda = "2";
        absent.InformazioniSede.UsaNuovaGestioneDomicili = false;
        var invalid = Student();
        invalid.InformazioniPersonali.CodFiscale = "VRDLGI00A01H501C";
        invalid.InformazioniPersonali.NumDomanda = "3";
        var students = new Dictionary<StudentKey, StudenteInfo>
        {
            [StudentKey.From(valid)] = valid,
            [StudentKey.From(absent)] = absent,
            [StudentKey.From(invalid)] = invalid
        };
        var loader = typeof(StudenteInfo).Assembly.GetType("ProcedureNet7.VerificaRaccoltaDati")!;
        string sql = (string)loader.GetField("DomiciliNuovaGestionePopulationSql", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        foreach (string table in new[] { "Domicili", "Contratti", "Proroghe" })
            sql = Regex.Replace(sql, @"\b" + table + @"\b", "#" + table);
        command.CommandText = sql.Replace("{TEMP_TABLE}", "#Studenti");
        command.Parameters.Add("@AA", SqlDbType.Char, 8).Value = "20262027";
        loader.GetMethod("LoadDomiciliNuovaGestione", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(SqlCommand), typeof(IReadOnlyDictionary<StudentKey, StudenteInfo>) }, null)!
            .Invoke(null, new object[] { command, students });

        Assert(students.Values.All(s => s.InformazioniSede.UsaNuovaGestioneDomicili), "Il loader deve attivare il nuovo modello anche senza righe.");
        Assert(absent.InformazioniSede.DomiciliNuovaGestione.Count == 0, "Il loader deve azzerare un domicilio precedente se ora è assente.");
        var domicile = valid.InformazioniSede.DomiciliNuovaGestione.Single();
        var contract = domicile.Contratti.Single();
        Assert(domicile.Id == 1 && contract.Id == 101 && contract.Proroghe.Select(p => p.Id).SequenceEqual(new long[] { 201, 202 }),
            "Duplicati della selezione, righe cancellate/storicizzate, altre domande/AA, residenza e luoghi non DOM devono essere ignorati.");
        Assert(invalid.InformazioniSede.DomiciliNuovaGestione.Single().Contratti.Count == 0,
            "Un domicilio presente senza contratto deve restare distinto da un domicilio assente.");
        command.CommandText = "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID";
        Assert(Convert.ToInt32(command.ExecuteScalar()) == 2, "La query condivisa deve conservare l'isolamento READ COMMITTED della transazione.");
        int checks = 5;
        foreach (string category in new[] { "PR", "SA" })
        {
            checks += CheckPayment(valid, category, new DateTime(2027, 9, 30), "B", false, true);
            checks += CheckPayment(absent, category, new DateTime(2027, 9, 30), "D", true, false);
            checks += CheckPayment(invalid, category, new DateTime(2027, 9, 30), "D", true, false);
        }
        Assert(valid.InformazioniSede.OutcomeDomicili?.MesiCoperti == 12, "Le due proroghe devono coprire l'intero anno senza duplicare mesi.");
        checks += CheckPaymentLoader(connection, transaction, students);
        transaction.Rollback();
        return checks + 1;
    }

    private static int CheckPaymentLoader(SqlConnection connection, SqlTransaction transaction,
        Dictionary<StudentKey, StudenteInfo> students)
    {
        using var command = new SqlCommand(AccessoriFixtureSql, connection, transaction);
        command.ExecuteNonQuery();
        using var procedure = (ProceduraPagamenti)RuntimeHelpers.GetUninitializedObject(typeof(ProceduraPagamenti));
        var type = typeof(ProceduraPagamenti);
        void Set(string field, object value) => type.GetField(field, PrivateInstance)!.SetValue(procedure, value);
        var selected = students.Values.Cast<StudentePagamenti>().ToDictionary(s => s.InformazioniPersonali.CodFiscale);
        foreach (var student in selected.Values)
            student.InformazioniBeneficio.EsitoPA = 1;
        selected["VRDLGI00A01H501C"].InformazioniBeneficio.EsitoPA = 2;
        selected["VRDLGI00A01H501C"].InformazioniBeneficio.VincitorePA = true;
        Set("selectedAA", "20262027");
        Set("selectedDataRiferimento", "30/09/2027");
        Set("CONNECTION", connection);
        Set("sqlTransaction", transaction);
        Set("studentiDaPagare", selected);
        void Populate() => Assert((bool)type.GetMethod("PopulateDomiciliNuovaGestione", PrivateInstance)!.Invoke(procedure, null)!,
            "Dal 2627 il vero helper pagamenti deve caricare il nuovo modello.");
        Populate();
        Assert(selected.Values.All(s => s.InformazioniIscrizione.CodSedeStudi == "01"
            && s.InformazioniIscrizione.CodSedeDistaccata == "00001"
            && s.InformazioniIscrizione.ComuneSedeStudi == "H501"
            && s.InformazioniIscrizione.ProvinciaSedeStudi == "RM"),
            "La sede distaccata deve prevalere sul comune del corso e caricare la provincia corretta.");
        Assert(selected.Values.All(s => !s.InformazioniSede.InSedeList && s.InformazioniSede.PendolareList
            && s.InformazioniSede.FuoriSedeList && !s.InformazioniSede.AlwaysA),
            "Il vero helper deve caricare la classificazione comuni per sede e sede distaccata.");
        var valid = selected["RSSMRA00A01H501A"];
        var eligible = selected["BNCLGU00A01H501B"];
        var winner = selected["VRDLGI00A01H501C"];
        Assert(eligible.InformazioniSede.HasAlloggio12 && winner.InformazioniSede.HasAlloggio12
            && eligible.EsitiConcorsoByBenefit["PK"].CodTipoEsito == 1
            && winner.EsitiConcorsoByBenefit["PK"].CodTipoEsito == 2,
            "PK richiesto idoneo/vincitore deve alimentare flag alloggio ed esiti del beneficio.");
        Assert(eligible.EsitiConcorsoByBenefit["PA"].CodTipoEsito == null
            && eligible.EsitiConcorsoByBenefit["PK"].CodTipoEsito == 1
            && selected.Values.All(s => s.InformazioniBeneficio.EsitoPA == (ReferenceEquals(s, winner) ? 2 : 1)
                && s.InformazioniBeneficio.VincitorePA == ReferenceEquals(s, winner)),
            "Esiti PA/PK successivi alla data selezionata devono essere ignorati e i campi economici EsitoPA/VincitorePA originali devono restare invariati.");
        int checks = 5;
        foreach (string category in new[] { "PR", "SA" })
        {
            checks += CheckPayment(valid, category, new DateTime(2027, 9, 30), "B", false, true);
            checks += CheckPayment(eligible, category, new DateTime(2027, 9, 30), "B", false, false);
            checks += CheckPayment(winner, category, new DateTime(2027, 9, 30), "B", false, false);
        }
        command.CommandText = "DELETE FROM ESITI_CONCORSI; DELETE FROM vBenefici_richiesti;";
        command.ExecuteNonQuery();
        eligible.InformazioniSede.UsaNuovaGestioneDomicili = false;
        eligible.SetDomicilio("H501", true, "LEGACY-101", new DateTime(2026, 9, 1), new DateTime(2026, 10, 1),
            new DateTime(2027, 9, 30), 12, false, 0, "", true, false, false, "", 0);
        Populate();
        Assert(!eligible.InformazioniSede.HasAlloggio12 && !winner.InformazioniSede.HasAlloggio12
            && eligible.InformazioniSede.UsaNuovaGestioneDomicili,
            "Il ricaricamento deve azzerare alloggio non più valido e attivare il nuovo modello anche senza domicilio.");
        checks++;
        foreach (string category in new[] { "PR", "SA" })
            checks += CheckPayment(eligible, category, new DateTime(2027, 9, 30), "D", true, false);
        command.CommandText = "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID";
        Assert(Convert.ToInt32(command.ExecuteScalar()) == 2, "Anche il vero helper deve preservare READ COMMITTED.");
        return checks + 1;
    }

    private static int CheckPayment(StudentePagamenti student, string category, DateTime reference,
        string expectedStatus, bool expectedPendolare, bool expectedDomicile, double pendolareAmount = 1000)
    {
        student.InformazioniPagamento.PagatoPendolare = false;
        double amount = 3000, maximum = 3000;
        var log = new ConcurrentBag<(string CodFiscale, string Motivazione)>();
        new AcademicProcessor2627().AdjustPendolarePayment(student, ref amount, ref maximum, log,
            30000, 1000, category, "20262027", referenceDate: reference);
        string label = $"{student.InformazioniPersonali.CodFiscale}/{category}/{reference:yyyy-MM-dd}";
        Assert(student.InformazioniSede.StatusSedeSuggerito == expectedStatus, $"{label}: status atteso {expectedStatus}, ottenuto {student.InformazioniSede.StatusSedeSuggerito}. {student.InformazioniSede.MotivoStatusSede}");
        Assert(student.InformazioniSede.DomicilioCheck == expectedDomicile, $"{label}: validità domicilio errata.");
        Assert(student.InformazioniPagamento.PagatoPendolare == expectedPendolare && log.Count == (expectedPendolare ? 1 : 0), $"{label}: conversione a pendolare errata.");
        Assert(amount == (expectedPendolare ? pendolareAmount : 3000) && maximum == amount, $"{label}: importi attesi non rispettati ({amount}/{maximum}).");
        return 4;
    }

    private static StudentePagamenti Student(params ContrattoDomicilioAnalisiInput[] contracts)
    {
        var student = new StudentePagamenti();
        student.InformazioniPersonali.CodFiscale = "RSSMRA00A01H501A";
        student.InformazioniPersonali.NumDomanda = "1";
        student.SetResidenza("", "F205", "MI", "", "MILANO");
        student.InformazioniIscrizione.ComuneSedeStudi = "H501";
        student.InformazioniIscrizione.ProvinciaSedeStudi = "RM";
        student.InformazioniIscrizione.AnnoCorso = 1;
        student.InformazioniSede.StatusSede = "B";
        student.InformazioniSede.UsaNuovaGestioneDomicili = true;
        student.InformazioniPagamento.ValoreISEE = 16000;
        if (contracts.Length > 0)
            student.InformazioniSede.DomiciliNuovaGestione.Add(new DomicilioAnalisiInput
            {
                Id = 1, CodComune = "H501", TipoDomicilio = TipoDomicilioAnalisi.Oneroso,
                InserimentoDatiContratto = true, Contratti = contracts.ToList()
            });
        return student;
    }

    private static ContrattoDomicilioAnalisiInput Contract(DateTime? end = null) => new()
    {
        Id = 101, TipoContratto = TipoContrattoDomicilioAnalisi.Registrato,
        NumeroSerieContratto = "BASE-101", DataRegistrazioneContratto = new DateTime(2026, 9, 1),
        DataInizio = new DateTime(2026, 10, 1), DataFine = end ?? new DateTime(2027, 9, 30)
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string AccessoriFixtureSql = @"
SELECT * INTO dbo.Domicili FROM #Domicili;
SELECT * INTO dbo.Contratti FROM #Contratti;
SELECT * INTO dbo.Proroghe FROM #Proroghe;
SELECT DISTINCT CodFiscale AS Cod_fiscale INTO #CFEstrazione FROM #Studenti;
SELECT DISTINCT NumDomanda AS Num_domanda, CodFiscale AS Cod_fiscale, CAST('20262027' AS char(8)) AS Anno_accademico
 INTO dbo.Domanda FROM #Studenti;
CREATE TABLE dbo.ISCRIZIONI (COD_FISCALE char(16),ANNO_ACCADEMICO char(8),TIPO_BANDO varchar(2),DATA_VALIDITA datetime,
 COD_SEDE_STUDI varchar(2),COD_CORSO_LAUREA varchar(4),COD_TIPO_ORDINAMENTO varchar(2),COD_FACOLTA varchar(2),
 ANNO_ACCAD_INIZIO varchar(8),COD_TIPOLOGIA_STUDI varchar(2));
INSERT dbo.ISCRIZIONI SELECT Cod_fiscale,Anno_accademico,'LZ','20260901','01','0001','01','01','20262027','01' FROM dbo.Domanda;
CREATE TABLE dbo.MERITO (ANNO_ACCADEMICO char(8),NUM_DOMANDA int,ANNO_IMMATRICOLAZ varchar(8),DATA_VALIDITA datetime);
INSERT dbo.MERITO SELECT Anno_accademico,Num_domanda,'2026','20260901' FROM dbo.Domanda;
CREATE TABLE dbo.CORSI_LAUREA (COD_SEDE_STUDI varchar(2),COD_CORSO_LAUREA varchar(4),COD_TIPO_ORDINAMENTO varchar(2),
 COD_FACOLTA varchar(2),ANNO_ACCAD_INIZIO varchar(8),COD_TIPOLOGIA_STUDI varchar(2),ANNO_ACCAD_FINE varchar(8),
 COD_SEDE_DISTACCATA varchar(5),COMUNE_SEDE_STUDI char(4),CORSO_IN_PRESENZA bit);
INSERT dbo.CORSI_LAUREA VALUES ('01','0001','01','01','20262027','01',NULL,'00001','F205',1);
CREATE TABLE dbo.SEDE_STUDI (COD_SEDE_STUDI varchar(2),TELEMATICA bit);
INSERT dbo.SEDE_STUDI VALUES ('01',0);
CREATE TABLE dbo.SEDI_DISTACCATE (COD_SEDE_DISTACCATA varchar(5),COD_COMUNE char(4));
INSERT dbo.SEDI_DISTACCATE VALUES ('00001','H501');
CREATE TABLE dbo.Comuni (COD_COMUNE char(4),COD_PROVINCIA char(2));
INSERT dbo.Comuni VALUES ('H501','RM'),('F205','MI');
CREATE TABLE dbo.ESITI_CONCORSI (Anno_accademico char(8),Num_domanda int,Cod_beneficio char(2),Cod_tipo_esito int,Data_validita datetime);
INSERT dbo.ESITI_CONCORSI VALUES ('20262027',2,'PK',1,'20260901'),('20262027',3,'PK',2,'20260901'),
 ('20262027',2,'PA',2,'20271001'),('20262027',2,'PK',2,'20271001');
CREATE TABLE dbo.vBenefici_richiesti (Anno_accademico char(8),Num_domanda int,Cod_beneficio char(2));
INSERT dbo.vBenefici_richiesti VALUES ('20262027',2,'PK'),('20262027',3,'PK');
CREATE TABLE dbo.COMUNI_INSEDE (riga_valida bit,cod_comune char(4),cod_sede_studi varchar(2),cod_sede_distaccata varchar(5));
CREATE TABLE dbo.COMUNI_PENDOLARI (cod_comune char(4),cod_sede_studi varchar(2),cod_sede_distaccata varchar(5));
INSERT dbo.COMUNI_PENDOLARI VALUES ('F205','01','00001');
CREATE TABLE dbo.COMUNI_FUORISEDE (cod_comune char(4),cod_sede_studi varchar(2),cod_sede_distaccata varchar(5));
INSERT dbo.COMUNI_FUORISEDE VALUES ('F205','01','00001');";

    private const string FixtureSql = @"
CREATE TABLE #Studenti (NumDomanda int, CodFiscale varchar(16));
INSERT #Studenti VALUES (1,'RSSMRA00A01H501A'),(1,'RSSMRA00A01H501A'),(2,'BNCLGU00A01H501B'),(3,'VRDLGI00A01H501C');
CREATE TABLE #Domicili (
 ID int, NUM_DOMANDA numeric(10), ANNO_ACCADEMICO char(8), COD_FISCALE char(16),
 TIPO_LUOGO varchar(3) DEFAULT 'DOM', IS_DELETED bit DEFAULT 0, DATA_FINE_VALIDITA datetime NULL,
 IS_EQUAL_RESIDENZA bit DEFAULT 0, COD_COMUNE char(4) DEFAULT 'H501', INDIRIZZO varchar(75) DEFAULT 'VIA TEST',
 NUMERO_CIVICO varchar(40) DEFAULT '1', CAP char(5) DEFAULT '00100', TIPO_DOMICILIO char(1) DEFAULT '1',
 DATA_INIZIO date NULL, DATA_FINE date NULL, INSERIMENTO_DATI_CONTRATTO bit DEFAULT 1);
INSERT #Domicili(ID,NUM_DOMANDA,ANNO_ACCADEMICO,COD_FISCALE) VALUES
 (1,1,'20262027','RSSMRA00A01H501A'),(3,3,'20262027','VRDLGI00A01H501C'),
 (10,1,'20262027','RSSMRA00A01H501A'),(11,1,'20262027','RSSMRA00A01H501A'),
 (12,1,'20262027','RSSMRA00A01H501A'),(13,1,'20252026','RSSMRA00A01H501A'),
 (14,99,'20262027','RSSMRA00A01H501A'),(15,1,'20262027','RSSMRA00A01H501A');
UPDATE #Domicili SET IS_DELETED=1 WHERE ID=10;
UPDATE #Domicili SET DATA_FINE_VALIDITA='20261001' WHERE ID=11;
UPDATE #Domicili SET TIPO_LUOGO='RES' WHERE ID=12;
UPDATE #Domicili SET IS_EQUAL_RESIDENZA=1 WHERE ID=15;
CREATE TABLE #Contratti (
 ID int, ID_DOMICILIO int, IS_DELETED bit DEFAULT 0, DATA_FINE_VALIDITA datetime NULL,
 TIPO_CONTRATTO_TITOLO_ONEROSO tinyint DEFAULT 0, N_SERIE_CONTRATTO varchar(100) DEFAULT 'BASE-101',
 DATA_REG_CONTRATTO datetime DEFAULT '20260901', DENOM_ENTE varchar(60) NULL, IMPORTO_RATA varchar(20) NULL,
 TIPO_ENTE varchar(2) NULL, N_SERIE_SUBENTRO varchar(100) NULL, DATA_INIZIO_SUBENTRO datetime NULL,
 DATA_CESSAZIONE datetime NULL, DATA_INIZIO datetime DEFAULT '20261001', DATA_FINE datetime DEFAULT '20270131');
INSERT #Contratti(ID,ID_DOMICILIO) VALUES (101,1),(102,1),(103,1);
UPDATE #Contratti SET IS_DELETED=1 WHERE ID=102;
UPDATE #Contratti SET DATA_FINE_VALIDITA='20261001' WHERE ID=103;
CREATE TABLE #Proroghe (
 ID int, ID_CONTRATTO int, IS_DELETED bit DEFAULT 0, DATA_FINE_VALIDITA datetime NULL,
 N_SERIE_PROROGA varchar(50), DATA_DECORRENZA datetime, DATA_SCADENZA datetime);
INSERT #Proroghe(ID,ID_CONTRATTO,N_SERIE_PROROGA,DATA_DECORRENZA,DATA_SCADENZA) VALUES
 (201,101,'EXT-201','20270201','20270430'),(202,101,'EXT-202','20270501','20270930'),
 (203,101,'DELETED','20271001','20271231'),(204,101,'HISTORICAL','20271001','20271231');
UPDATE #Proroghe SET IS_DELETED=1 WHERE ID=203;
UPDATE #Proroghe SET DATA_FINE_VALIDITA='20261001' WHERE ID=204;";
}
