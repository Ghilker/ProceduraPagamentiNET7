using System.Data.SqlClient;
using ProcedureNet7;

// Preparazione: SqlLocalDB create CodexBlocksUtilTests -s
// Esecuzione: dotnet run --project Tests/BlocksUtil/BlocksUtil.Tests.csproj
// Solo dati fittizi in un database locale eliminato al termine.
const string aa = "20262027";
const string cf1 = "TESTCF0000000001";
const string cf2 = "TESTCF0000000002";
const string cf3 = "TESTCF0000000003";
const string missingCf = "TESTCF0000000099";
string database = "BlocksUtilTests_" + Guid.NewGuid().ToString("N");
var builder = new SqlConnectionStringBuilder
{
    DataSource = @"(localdb)\CodexBlocksUtilTests",
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
CREATE TABLE dbo.Tipologie_incongruenze (
    Anno_accademico CHAR(8) NOT NULL,
    Cod_incongruenza VARCHAR(2) NOT NULL,
    Descrizione VARCHAR(4000) NOT NULL,
    PRIMARY KEY (Anno_accademico, Cod_incongruenza)
);
CREATE TABLE dbo.Incongruenze (
    Anno_accademico CHAR(8) NOT NULL,
    Num_domanda NUMERIC(7,0) NOT NULL,
    Cod_incongruenza VARCHAR(2) NOT NULL,
    Data_validita DATETIME NOT NULL,
    Data_fine_validita DATETIME NULL,
    Cod_forzatura VARCHAR(4) NULL,
    Utente VARCHAR(20) NULL,
    Id_Incongruenze INT IDENTITY NOT NULL PRIMARY KEY,
    Id_Domanda INT NULL REFERENCES dbo.Domanda(Id_domanda),
    EliminataDa VARCHAR(20) NULL,
    FOREIGN KEY (Anno_accademico, Cod_incongruenza)
        REFERENCES dbo.Tipologie_incongruenze(Anno_accademico, Cod_incongruenza)
);
CREATE INDEX IDX_AA_NUM ON dbo.Incongruenze (Anno_accademico, Num_domanda, Data_fine_validita);
CREATE INDEX IDX_COD_AA_NUM ON dbo.Incongruenze (Cod_incongruenza) INCLUDE (Anno_accademico, Num_domanda);
INSERT INTO dbo.Tipologie_incongruenze VALUES
    ('20262027', '01', 'Test 1'), ('20262027', '02', 'Test 2'),
    ('20252026', '01', 'Test precedente'), ('20252026', 'ZZ', 'Solo anno precedente');
INSERT INTO dbo.Domanda VALUES
    (1001, '20262027', 1, 'TESTCF0000000001', 'lz'),
    (1002, '20262027', 2, 'TESTCF0000000002', 'l2'),
    (1003, '20262027', 3, 'TESTCF0000000003', 'xx'),
    (1004, '20262027', 4, 'TESTCF0000000004', 'lz'),
    (2001, '20252026', 1, 'TESTCF0000000001', 'lz');
");

    int passed = 0;
    Run("Inserimento, normalizzazione CF e idempotenza", tx =>
    {
        var added = BlocksUtil.AddIncongruenza(conn, tx,
            new() { cf1.ToLowerInvariant(), " " + cf1 + " ", cf2, cf3, missingCf }, " 01 ", aa, "Inserimento");
        EqualSet(added.ActuallyAdded, cf1, cf2);
        EqualSet(added.AlreadyHasIncongruenza);
        EqualSet(added.PreviouslyRemovedButSkipped);
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze") == 2, "Solo le domande lz/l2 richieste");
        Assert(Count(conn, tx, @"SELECT COUNT(*) FROM dbo.Incongruenze
WHERE Anno_accademico = '20262027' AND Cod_incongruenza = '01'
  AND Data_validita IS NOT NULL AND Data_fine_validita IS NULL
  AND Cod_forzatura IS NULL AND EliminataDa IS NULL AND Utente = 'Inserimento'
  AND ((Num_domanda = 1 AND Id_Domanda = 1001) OR (Num_domanda = 2 AND Id_Domanda = 1002))") == 2,
            "Campi e collegamento alla domanda");
        var repeated = BlocksUtil.AddIncongruenza(conn, tx, new() { cf1, cf2 }, "01", aa, "Ripetizione");
        EqualSet(repeated.ActuallyAdded);
        EqualSet(repeated.AlreadyHasIncongruenza, cf1, cf2);
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze") == 2, "Nessun duplicato attivo");
    });

    Run("Chiusura, storico, duplicati attivi e isolamento di anno/codice/bando", tx =>
    {
        Execute(conn, tx, @"
INSERT INTO dbo.Incongruenze
    (Anno_accademico, Num_domanda, Cod_incongruenza, Data_validita, Data_fine_validita, Cod_forzatura, Utente, Id_Domanda, EliminataDa)
VALUES
    ('20262027', 1, '01', '20260901', NULL, 'F001', 'Originale', 1001, NULL),
    ('20262027', 1, '01', '20260902', NULL, NULL, 'Duplicato', 1001, NULL),
    ('20262027', 1, '01', '20250801', '20250802', NULL, 'Storico', 1001, 'GiaChiuso'),
    ('20262027', 1, '02', '20260901', NULL, NULL, 'AltroCodice', 1001, NULL),
    ('20252026', 1, '01', '20250901', NULL, NULL, 'AltroAnno', 2001, NULL),
    ('20262027', 3, '01', '20260901', NULL, NULL, 'AltroBando', 1003, NULL),
    ('20262027', 4, '01', '20260901', NULL, NULL, 'AltroCF', 1004, NULL);
");
        var removed = BlocksUtil.RemoveIncongruenza(conn, tx, new() { cf1, cf1, cf2, cf3, missingCf }, "01", aa, "Chiusura");
        EqualSet(removed.ActuallyRemoved, cf1);
        EqualSet(removed.NothingToRemove, cf2, cf3, missingCf);
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze") == 7, "Nessuna cancellazione fisica");
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze WHERE EliminataDa = 'Chiusura' AND Data_fine_validita IS NOT NULL") == 2,
            "Tutti i duplicati attivi sono chiusi");
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze WHERE Utente = 'Originale' AND Cod_forzatura = 'F001' AND Data_validita = '20260901'") == 1,
            "Dati originali conservati");
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze WHERE Utente = 'Storico' AND Data_fine_validita = '20250802' AND EliminataDa = 'GiaChiuso'") == 1,
            "Chiusure precedenti conservate");
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze WHERE Data_fine_validita IS NULL AND EliminataDa IS NULL") == 4,
            "Altri anni, codici, bandi e CF invariati");
        var repeated = BlocksUtil.RemoveIncongruenza(conn, tx, new() { cf1 }, "01", aa, "Ripetizione");
        EqualSet(repeated.ActuallyRemoved);
        EqualSet(repeated.NothingToRemove, cf1);
    });

    Run("Reinserimento solo su richiesta e conservazione dello storico", tx =>
    {
        BlocksUtil.AddIncongruenza(conn, tx, new() { cf1 }, "01", aa, "Originale");
        BlocksUtil.RemoveIncongruenza(conn, tx, new() { cf1 }, "01", aa, "Chiusura");
        var skipped = BlocksUtil.AddIncongruenza(conn, tx, new() { cf1 }, "01", aa, "Saltato");
        EqualSet(skipped.ActuallyAdded);
        EqualSet(skipped.PreviouslyRemovedButSkipped, cf1);
        var readded = BlocksUtil.AddIncongruenza(conn, tx, new() { cf1 }, "01", aa, "Reinserimento", inserisciGiaRimossi: true);
        EqualSet(readded.ActuallyAdded, cf1);
        EqualSet(readded.PreviouslyRemovedButSkipped);
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze WHERE Data_fine_validita IS NOT NULL AND Utente = 'Originale' AND EliminataDa = 'Chiusura'") == 1,
            "Riga chiusa conservata");
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze WHERE Data_fine_validita IS NULL AND Utente = 'Reinserimento'") == 1,
            "Nuova riga attiva");
        var repeated = BlocksUtil.AddIncongruenza(conn, tx, new() { cf1 }, "01", aa, "Ripetizione", inserisciGiaRimossi: true);
        EqualSet(repeated.ActuallyAdded);
        EqualSet(repeated.AlreadyHasIncongruenza, cf1);
    });

    Run("Catalogo relativo all'anno e pulizia dopo errore SQL", tx =>
    {
        foreach (string code in new[] { "99", "ZZ" })
        {
            bool rejected = false;
            try { BlocksUtil.AddIncongruenza(conn, tx, new() { cf1 }, code, aa, "Test"); }
            catch (SqlException ex) when (ex.Number == 50001) { rejected = true; }
            Assert(rejected, "Codice assente dal catalogo annuale rifiutato");
            Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze") == 0, "Nessun inserimento dopo errore");
            Assert(Count(conn, tx, "SELECT CASE WHEN OBJECT_ID('tempdb..#CodFiscaleTempTable') IS NULL THEN 1 ELSE 0 END") == 1,
                "Tabella temporanea rimossa dopo errore");
        }
        EqualSet(BlocksUtil.AddIncongruenza(conn, tx, new() { cf1 }, "01", aa, "Test").ActuallyAdded, cf1);
    });

    Run("Parametri non troncati silenziosamente", tx =>
    {
        foreach (bool add in new[] { true, false })
        {
            foreach (var invalid in new[] { ("001", aa, "Test"), ("01", "2026", "Test"), ("01", "202X2027", "Test"), ("01", aa, new string('x', 21)) })
            {
                bool rejected = false;
                try
                {
                    if (add) BlocksUtil.AddIncongruenza(conn, tx, new() { cf1 }, invalid.Item1, invalid.Item2, invalid.Item3);
                    else BlocksUtil.RemoveIncongruenza(conn, tx, new() { cf1 }, invalid.Item1, invalid.Item2, invalid.Item3);
                }
                catch (ArgumentException) { rejected = true; }
                Assert(rejected, "Parametro non valido rifiutato");
            }
        }
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze") == 0, "Nessuna modifica con parametri non validi");
    });

    Run("Liste vuote e CF senza domanda", tx =>
    {
        EqualSet(BlocksUtil.AddIncongruenza(conn, tx, new(), "01", aa, "Test").ActuallyAdded);
        EqualSet(BlocksUtil.RemoveIncongruenza(conn, tx, new(), "01", aa, "Test").ActuallyRemoved);
        EqualSet(BlocksUtil.AddIncongruenza(conn, tx, new() { missingCf }, "01", aa, "Test").ActuallyAdded);
        EqualSet(BlocksUtil.RemoveIncongruenza(conn, tx, new() { missingCf }, "01", aa, "Test").NothingToRemove, missingCf);
        Assert(Count(conn, tx, "SELECT COUNT(*) FROM dbo.Incongruenze") == 0, "Nessuna modifica senza domande");
    });

    Run("Commit affidato al chiamante e rollback", tx =>
    {
        BlocksUtil.AddIncongruenza(conn, tx, new() { cf1 }, "01", aa, "Test");
        Assert(Count(conn, tx, "SELECT @@TRANCOUNT") == 1, "La transazione resta aperta");
    });
    Assert(Count(conn, null, "SELECT COUNT(*) FROM dbo.Incongruenze") == 0, "Rollback effettivo delle modifiche");
    Console.WriteLine($"PASS: {passed} test di integrazione.");

    void Run(string name, Action<SqlTransaction> test)
    {
        using var tx = conn.BeginTransaction();
        try { test(tx); }
        finally { tx.Rollback(); }
        passed++;
        Console.WriteLine("PASS: " + name);
    }
}
finally
{
    SqlConnection.ClearAllPools();
    Execute(master, null, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
}

static void Execute(SqlConnection conn, SqlTransaction? tx, string sql)
{
    using var cmd = new SqlCommand(sql, conn, tx);
    cmd.ExecuteNonQuery();
}

static int Count(SqlConnection conn, SqlTransaction? tx, string sql)
{
    using var cmd = new SqlCommand(sql, conn, tx);
    return Convert.ToInt32(cmd.ExecuteScalar());
}

static void EqualSet(IEnumerable<string> actual, params string[] expected)
{
    string[] values = actual.ToArray();
    Assert(values.Length == expected.Length && new HashSet<string>(values).SetEquals(expected),
        $"Attesi [{string.Join(",", expected)}], ottenuti [{string.Join(",", values)}]");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
