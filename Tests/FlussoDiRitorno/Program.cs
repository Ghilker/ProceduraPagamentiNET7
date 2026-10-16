using System.Data.SqlClient;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ProcedureNet7;

// dotnet run --project Tests/FlussoDiRitorno/FlussoDiRitorno.Tests.csproj
// Solo dati fittizi in un database LocalDB creato e rimosso dal test.
const string aa1 = "20262027", aa2 = "20252026";
const string cf1 = "TESTCF0000000001", cf2 = "TESTCF0000000002";
const string cf4 = "TESTCF0000000004", missingCf = "TESTCF0000000099";
string database = "FlussoRitornoTests_" + Guid.NewGuid().ToString("N");
var builder = new SqlConnectionStringBuilder
{
    DataSource = @"(localdb)\MSSQLLocalDB",
    InitialCatalog = "master",
    IntegratedSecurity = true
};
using var master = new SqlConnection(builder.ConnectionString);
master.Open();
Execute(master, null, $"CREATE DATABASE [{database}] COLLATE Latin1_General_CI_AS;");
try
{
    builder.InitialCatalog = database;
    using var conn = new SqlConnection(builder.ConnectionString);
    conn.Open();
    Execute(conn, null, @"
CREATE TABLE dbo.Domanda (
    Id_domanda INT NOT NULL PRIMARY KEY,
    Anno_accademico CHAR(8) NOT NULL,
    Num_domanda NUMERIC(10,0) NOT NULL,
    Cod_fiscale CHAR(16) NOT NULL,
    Tipo_bando NCHAR(4) NOT NULL
);
CREATE TABLE dbo.Pagamenti (
    Anno_accademico CHAR(8) NOT NULL,
    Num_domanda NUMERIC(10,0) NOT NULL,
    Cod_mandato VARCHAR(20) NOT NULL,
    Ese_finanziario INT NOT NULL
);
CREATE TABLE dbo.Motivazioni_blocco_pagamenti (
    Anno_accademico CHAR(8) NOT NULL,
    Num_domanda NUMERIC(10,0) NOT NULL,
    Cod_tipologia_blocco VARCHAR(10) NOT NULL,
    Blocco_pagamento_attivo BIT NOT NULL,
    Data_validita DATETIME NOT NULL,
    Utente NVARCHAR(50) NULL,
    Data_fine_validita DATETIME NULL,
    Utente_sblocco NVARCHAR(50) NULL
);
CREATE TABLE dbo.DatiGenerali_dom (
    Anno_accademico CHAR(8) NOT NULL,
    Num_domanda NUMERIC(10,0) NOT NULL,
    Id_domanda INT NOT NULL,
    Blocco_pagamento BIT NOT NULL,
    Data_validita DATETIME NOT NULL,
    Utente NVARCHAR(50) NULL
);
INSERT INTO dbo.Domanda VALUES
    (1, '20262027', 1, 'TESTCF0000000001', 'lz'),
    (2, '20262027', 2, 'TESTCF0000000002', 'lz'),
    (3, '20252026', 3, 'TESTCF0000000002', 'l2'),
    (4, '20262027', 4, 'TESTCF0000000003', 'lz'),
    (5, '20252026', 5, 'TESTCF0000000001', 'lz'),
    (6, '20262027', 6, 'TESTCF0000000004', 'xx');
DECLARE @year INT = YEAR(GETDATE());
INSERT INTO dbo.Pagamenti VALUES
    ('20262027', 1, '100', @year), ('20252026', 3, '200', @year),
    ('20262027', 4, '300', @year), ('20252026', 5, '900', @year),
    ('20252026', 5, '100', @year - 1), ('20262027', 6, '400', @year);
CREATE TABLE dbo.Annullamenti (Cod_fiscale CHAR(16));
");
    Execute(conn, null, @"
CREATE VIEW dbo.vDATIGENERALI_dom AS
SELECT Anno_accademico, Num_domanda, Id_domanda,
       CAST(0 AS BIT) AS Blocco_pagamento, GETDATE() AS Data_validita,
       CAST('Test' AS NVARCHAR(50)) AS Utente
FROM dbo.Domanda;");
    Execute(conn, null, @"
CREATE PROCEDURE dbo.SP_annulla_pagamento
    @NumMandato VARCHAR(20), @esercizio_finanziario INT, @cod_fiscale CHAR(16)
AS
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM dbo.Motivazioni_blocco_pagamenti b
        INNER JOIN dbo.Domanda d ON d.Anno_accademico = b.Anno_accademico AND d.Num_domanda = b.Num_domanda
        WHERE d.Cod_fiscale = @cod_fiscale AND b.Cod_tipologia_blocco = 'BSS'
          AND b.Blocco_pagamento_attivo = 1 AND b.Data_fine_validita IS NULL
    ) THROW 50000, 'BSS mancante prima dell''annullamento', 1;
    INSERT INTO dbo.Annullamenti VALUES (@cod_fiscale);
    DELETE p FROM dbo.Pagamenti p
    INNER JOIN dbo.Domanda d ON d.Anno_accademico = p.Anno_accademico AND d.Num_domanda = p.Num_domanda
    WHERE d.Cod_fiscale = @cod_fiscale AND p.Cod_mandato = @NumMandato
      AND p.Ese_finanziario = @esercizio_finanziario;
END;");

    int passed = 0;
    Run("Lista vuota", tx =>
    {
        Apply(NewProcedure(), tx);
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti") == 0, "Nessun blocco senza scartati");
    });
    Run("Scartati, anno, mandato, esercizio e idempotenza", tx =>
    {
        var procedure = NewProcedure((cf1, "100"), (cf1, "100"), (cf2, "200"));
        Apply(procedure, tx);
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti") == 2, "Solo due BSS, anche con righe duplicate");
        Assert(Active(tx, aa1, 1) == 1 && Active(tx, aa2, 3) == 1, "Ogni scartato bloccato nell'anno del pagamento");
        Assert(Active(tx, aa1, 2) == 0 && Active(tx, aa2, 5) == 0 && Active(tx, aa1, 4) == 0,
            "Nessun blocco per altri anni, mandati, esercizi o studenti non scartati");
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.DatiGenerali_dom WHERE Blocco_pagamento = 1 AND Utente = 'IBANFlussoRitorno'") == 2,
            "Snapshot e utente del flusso di ritorno");
        Apply(procedure, tx);
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti") == 2, "Nessun duplicato dei BSS già attivi");
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.DatiGenerali_dom") == 2, "Nessuno snapshot duplicato");
        Assert(Logger.Messages.Any(m => m.Contains("gia_presenti=1")), "Log dei BSS già presenti");
    });
    Run("Reinserimento dopo rimozione", tx =>
    {
        Execute(conn, tx, @"INSERT INTO dbo.Motivazioni_blocco_pagamenti VALUES
('20262027', 1, 'BSS', 0, '20260101', 'Storico', '20260102', 'Sblocco');");
        Apply(NewProcedure((cf1, "100")), tx);
        Assert(Active(tx, aa1, 1) == 1, "BSS reinserito");
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti WHERE Data_fine_validita IS NOT NULL AND Utente = 'Storico'") == 1,
            "Storico conservato");
    });
    Run("Blocco prima dell'annullamento, stessa transazione", tx =>
    {
        var procedure = NewProcedure((cf1, "100"), (cf2, "200"));
        Apply(procedure, tx);
        Invoke(procedure, "Scartati", conn, tx);
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.Annullamenti") == 2, "Tutti gli scartati annullati dopo il BSS");
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.Pagamenti") == 4, "Pagamenti scartati annullati");
    });
    Assert(Count(null, "SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti") == 0, "Rollback dei blocchi");
    Assert(Count(null, "SELECT COUNT(*) FROM dbo.DatiGenerali_dom") == 0, "Rollback degli snapshot");
    Assert(Count(null, "SELECT COUNT(*) FROM dbo.Pagamenti") == 6, "Rollback degli annullamenti");
    Run("Pagamento non associabile: nessuno scartato saltato", tx =>
    {
        ExpectFailure(() => Apply(NewProcedure((cf1, "100"), (missingCf, "999")), tx), missingCf);
        Assert(Count(tx, "SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti") == 0, "Verifica degli anni prima di inserire i blocchi");
        ExpectFailure(() => Apply(NewProcedure((cf1, "999")), tx), cf1);
        ExpectFailure(() => Apply(NewProcedure((cf4, "400")), tx), cf4);
    });
    Run("BSS non inseribile: errore esplicito", tx =>
    {
        Execute(conn, tx, @"INSERT INTO dbo.Motivazioni_blocco_pagamenti VALUES
('20262027', 1, 'BSS', 0, GETDATE(), 'Inattivo', NULL, NULL);");
        ExpectFailure(() => Apply(NewProcedure((cf1, "100")), tx), "Blocco BSS non applicato");
    });
    Run("BSS e riepilogo anche con Ignora flussi a 1", tx =>
    {
        var procedure = NewProcedure((cf1, "100"));
        Field("ignoraFlussi1").SetValue(procedure, true);
        Apply(procedure, tx);
        Assert(Active(tx, aa1, 1) == 1, "Scartato bloccato anche con Ignora flussi a 1");
        string summary = (string)Invoke(procedure, "BuildRiepilogoConferma")!;
        Assert(summary.Contains("blocco BSS per IBAN errato") && summary.Contains("non verranno annullati"),
            "Riepilogo distingue blocchi e annullamenti");
    });
    Console.WriteLine($"PASS: {passed} test SQL del flusso di ritorno.");

    ProceduraFlussoDiRitorno NewProcedure(params (string Cf, string Mandato)[] rejected)
    {
        var procedure = new ProceduraFlussoDiRitorno(null!, conn);
        var list = (List<StudenteRitorno>)Field("studentiScartati").GetValue(procedure)!;
        list.AddRange(rejected.Select(s => new StudenteRitorno(s.Cf, s.Mandato, DateTime.Today, "", true)));
        return procedure;
    }
    void Apply(ProceduraFlussoDiRitorno procedure, SqlTransaction tx)
        => Invoke(procedure, "AddBlocksStudentiScartati", conn, tx);
    int Count(SqlTransaction? tx, string query)
    {
        using var cmd = new SqlCommand(query, conn, tx);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    int Active(SqlTransaction tx, string aa, int domanda)
        => Count(tx, $"SELECT COUNT(*) FROM dbo.Motivazioni_blocco_pagamenti WHERE Anno_accademico = '{aa}' AND Num_domanda = {domanda} AND Cod_tipologia_blocco = 'BSS' AND Blocco_pagamento_attivo = 1 AND Data_fine_validita IS NULL");
    void Run(string name, Action<SqlTransaction> test)
    {
        Logger.Messages.Clear();
        using var tx = conn.BeginTransaction();
        try
        {
            test(tx);
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        finally { tx.Rollback(); }
    }
}
finally
{
    SqlConnection.ClearAllPools();
    Execute(master, null, $"DROP DATABASE [{database}];");
}

static FieldInfo Field(string name)
    => typeof(ProceduraFlussoDiRitorno).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
static object? Invoke(ProceduraFlussoDiRitorno procedure, string name, params object[] args)
{
    var method = typeof(ProceduraFlussoDiRitorno).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Metodo della procedura mancante: " + name);
    try { return method.Invoke(procedure, args); }
    catch (TargetInvocationException ex) when (ex.InnerException != null)
    {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
    }
}
static void Execute(SqlConnection conn, SqlTransaction? tx, string sql)
{
    using var cmd = new SqlCommand(sql, conn, tx);
    cmd.ExecuteNonQuery();
}
static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void ExpectFailure(Action action, string detail)
{
    try { action(); }
    catch (InvalidOperationException ex) when (ex.Message.Contains(detail)) { return; }
    throw new InvalidOperationException("Errore atteso non rilevato: " + detail);
}
