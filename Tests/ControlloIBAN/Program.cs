using System.Data.SqlClient;
using ProcedureNet7;

const string minimumYear = "20262027";
const string validIban = "IT60X0542811101000000123456";
var tests = new (string Name, Action Run)[]
{
    ("Blocchi e selezione anni rispettano il limite prima/uguale/dopo", TestYears),
    ("L'instradamento dei blocchi protegge il limite anche con righe precedenti inattese", TestUnexpectedPreviousYear),
    ("Soli anni precedenti e IBAN validi non generano blocchi o notifiche", TestExcluded),
    ("Rimozioni e relativi avvisi rimangono limitati all'anno indicato", TestExistingBlocks)
};
foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine("PASS " + name);
}
Console.WriteLine($"PASS {tests.Length} test; SQL, blocchi, messaggi e interfaccia sono simulati in memoria.");

void TestYears()
{
    var fixture = new IbanFixture();
    fixture.Applications.AddRange(new[]
    {
        new IbanApplication("INVALID_CURRENT", "20252026", "ERRATO"),
        new IbanApplication("INVALID_CURRENT", minimumYear, "ERRATO"),
        new IbanApplication("INVALID_CURRENT", "20272028", "ERRATO"),
        new IbanApplication("OLD_ONLY", "20252026", "ERRATO"),
        new IbanApplication("VALID_CURRENT", minimumYear, validIban)
    });
    Run(fixture);
    Assert(fixture.Added.Select(x => x.Year).Order().SequenceEqual(new[] { minimumYear, "20272028" }),
        "AddBlock deve ricevere soltanto l'anno indicato e i successivi.");
    Assert(fixture.CheckedYears.SequenceEqual(new[] { minimumYear, "20272028" }), "HasBlock non deve interrogare anni precedenti.");
    Assert(fixture.Added.All(x => x.FiscalCode == "INVALID_CURRENT"), "Blocchi riservati ai CF con IBAN non valido nel periodo richiesto.");
    Assert(fixture.YearRows.All(x => string.CompareOrdinal(x.Year, minimumYear) >= 0), "Anche la query anni deve escludere gli AA precedenti.");
    Assert(fixture.Messages.SequenceEqual(new[] { "INVALID_CURRENT" }), "Un solo avviso al CF che riceve un nuovo blocco.");
    Assert(fixture.Committed && !fixture.RolledBack, "La simulazione deve raggiungere il commit di conferma.");
}

void TestExcluded()
{
    var fixture = new IbanFixture();
    fixture.Applications.Add(new("OLD_ONLY", "20252026", "ERRATO"));
    fixture.Applications.Add(new("VALID_CURRENT", minimumYear, validIban));
    Run(fixture);
    Assert(fixture.Added.Count == 0 && fixture.Messages.Count == 0 && fixture.Removed.Count == 0,
        "L'IBAN errato di un CF con sole domande precedenti non deve produrre effetti.");
    Assert(fixture.RolledBack && !fixture.Committed, "Senza IBAN da bloccare termina con rollback come prima.");
}

void TestUnexpectedPreviousYear()
{
    var fixture = new IbanFixture { IgnoreYearFilter = true };
    fixture.Applications.Add(new("INVALID_CURRENT", "20252026", "ERRATO"));
    fixture.Applications.Add(new("INVALID_CURRENT", minimumYear, "ERRATO"));
    Run(fixture);
    Assert(fixture.YearRows.Any(x => x.Year == "20252026"), "La fixture deve restituire una riga precedente inattesa.");
    Assert(fixture.Added.Select(x => x.Year).SequenceEqual(new[] { minimumYear }) &&
        fixture.CheckedYears.SequenceEqual(new[] { minimumYear }), "La protezione applicativa deve impedire blocchi negli AA precedenti.");
}

void TestExistingBlocks()
{
    var fixture = new IbanFixture();
    fixture.Applications.Add(new("INVALID_CURRENT", minimumYear, "ERRATO"));
    fixture.Applications.Add(new("CORRECTED", "20252026", validIban));
    fixture.Applications.Add(new("CORRECTED", minimumYear, validIban));
    fixture.Applications.Add(new("CORRECTED", "20272028", validIban));
    fixture.ExistingBlocks.AddRange(new[]
    {
        new IbanExistingBlock("CORRECTED", "20252026"),
        new IbanExistingBlock("CORRECTED", minimumYear),
        new IbanExistingBlock("CORRECTED", "20272028")
    });
    Run(fixture);
    Assert(fixture.BlockRows.Select(x => x.Year).SequenceEqual(new[] { minimumYear }),
        "Il join dei blocchi preesistenti deve includere anche l'AA, evitando record di anni diversi.");
    Assert(fixture.Removed.SequenceEqual(new[] { ("CORRECTED", minimumYear) }), "RemoveBlock deve conservare lo scope dell'AA selezionato.");
}

void Run(IbanFixture fixture)
{
    Logger.Errors.Clear();
    var procedure = new ProceduraControlloIBAN(new MasterForm(), new SqlConnection(fixture));
    procedure.RunProcedure(new ArgsProceduraControlloIBAN { _annoAccademico = minimumYear });
    Assert(Logger.Errors.Count == 0, string.Join(Environment.NewLine, Logger.Errors));
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
