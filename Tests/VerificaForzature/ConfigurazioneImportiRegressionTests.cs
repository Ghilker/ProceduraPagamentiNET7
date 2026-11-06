using System.Data;
using System.Data.SqlClient;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class ConfigurazioneImportiRegressionTests
{
    private const string AnnoAccademico = "20262027";
    private static readonly string[] Fields =
    {
        "Franchigia", "tasso_rendimento_pat_mobiliare", "franchigia_pat_mobiliare",
        "Importo_borsa_A", "Importo_borsa_B", "Importo_borsa_C", "Soglia_Isee"
    };

    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();

        Check("record economico mancante", () => ExpectInvalid(CreateTable(addRow: false), "nessun record"));
        Check("record economici duplicati", () =>
        {
            var table = CreateTable();
            table.ImportRow(table.Rows[0]);
            ExpectInvalid(table, "più record");
        });
        foreach (string field in Fields)
        {
            Check($"{field} NULL", () => ExpectInvalid(CreateTable(field, DBNull.Value), field));
            Check($"{field} negativo", () => ExpectInvalid(CreateTable(field, -1m), field));
        }
        foreach (string field in Fields.Skip(3))
            Check($"{field} zero", () => ExpectInvalid(CreateTable(field, 0m), field));
        Check("valore non numerico", () => ExpectInvalid(CreateTable("Soglia_Isee", "non numerico"), "Soglia_Isee"));
        Check("franchigie e rendimento zero sono consentiti", () =>
        {
            var table = CreateTable();
            foreach (string field in Fields.Take(3))
                table.Rows[0][field] = 0m;
            using var reader = table.CreateDataReader();
            var values = VerificaRaccoltaDati.ReadCalcParams(reader, AnnoAccademico);
            Equal(values.Franchigia, 0m, "franchigia immobiliare");
            Equal(values.RendPatr, 0m, "rendimento patrimonio");
            Equal(values.FranchigiaPatMob, 0m, "franchigia mobiliare");
            Equal(values.ImportoBorsaA, 2000m, "base A");
            Equal(values.ImportoBorsaB, 3000m, "base B");
            Equal(values.ImportoBorsaC, 2500m, "base C");
            Equal(values.SogliaIsee, 20000m, "soglia ISEE");
        });
        Check("valori economici configurati", () =>
        {
            using var reader = CreateTable().CreateDataReader();
            var values = VerificaRaccoltaDati.ReadCalcParams(reader, AnnoAccademico);
            Equal(values.Franchigia, 5000m, "franchigia immobiliare");
            Equal(values.RendPatr, 0.03m, "rendimento patrimonio");
            Equal(values.FranchigiaPatMob, 10000m, "franchigia mobiliare");
        });
        Check("ISEE appena sotto metà soglia", () => CheckAmount(9999.99m, 2300m));
        Check("ISEE esattamente a metà soglia", () => CheckAmount(10000m, 2300m));
        Check("ISEE appena sopra metà soglia", () => CheckAmount(10000.01m, 2000m));
        Check("parametri assenti nel modulo importi", () => ExpectInvalidModule(c => c.CalcParams = null!, "parametri assenti"));
        Check("soglia zero nel modulo importi", () => ExpectInvalidModule(c => c.CalcParams.SogliaIsee = 0m, "Soglia_Isee"));
        Check("base zero dello status usato", () => ExpectInvalidModule(c => c.CalcParams.ImportoBorsaA = 0m, "status A"));

        if (failures.Count > 0)
            throw new InvalidOperationException($"Configurazione/importi: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: configurazione economica obbligatoria e maggiorazione ISEE alla metà soglia.");

        void Check(string label, Action action)
        {
            checks++;
            try { action(); }
            catch (Exception ex) { failures.Add($"{label}: {ex.GetBaseException().Message}"); }
        }
    }

    private static DataTable CreateTable(string? field = null, object? value = null, bool addRow = true)
    {
        var table = new DataTable();
        foreach (string name in Fields)
            table.Columns.Add(name, typeof(object));
        if (addRow)
        {
            table.Rows.Add(5000m, 0.03m, 10000m, 2000m, 3000m, 2500m, 20000m);
            if (field != null)
                table.Rows[0][field] = value ?? DBNull.Value;
        }
        return table;
    }

    private static void ExpectInvalid(DataTable table, string detail)
    {
        using var reader = table.CreateDataReader();
        try
        {
            VerificaRaccoltaDati.ReadCalcParams(reader, AnnoAccademico);
        }
        catch (InvalidOperationException ex)
        {
            if (!ex.Message.Contains(AnnoAccademico, StringComparison.Ordinal)
                || !ex.Message.Contains("da sistemare", StringComparison.OrdinalIgnoreCase)
                || !ex.Message.Contains(detail, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Diagnostica non sufficiente: {ex.Message}");
            return;
        }
        throw new InvalidOperationException("Configurazione non valida accettata senza segnalazione.");
    }

    private static void CheckAmount(decimal isee, decimal expected)
    {
        using var connection = new SqlConnection();
        var context = new VerificaPipelineContext(connection) { AnnoAccademico = AnnoAccademico };
        using (var reader = CreateTable().CreateDataReader())
            context.CalcParams = VerificaRaccoltaDati.ReadCalcParams(reader, AnnoAccademico);
        var info = context.GetOrCreateStudent(new StudentKey("TEST", "1"));
        info.InformazioniSede.StatusSedeSuggerito = "A";
        info.InformazioniIscrizione.AnnoCorso = 1;
        info.InformazioniEconomiche.Calcolate.ISEEDSU = isee;
        new CalcoloImportoBorsa().Calculate(context);
        if (info.InformazioniImportoBorsa.ImportoFinale != expected)
            throw new InvalidOperationException($"ISEE {isee}: atteso {expected}, ottenuto {info.InformazioniImportoBorsa.ImportoFinale}.");
    }

    private static void ExpectInvalidModule(Action<VerificaPipelineContext> configure, string detail)
    {
        using var connection = new SqlConnection();
        var context = new VerificaPipelineContext(connection) { AnnoAccademico = AnnoAccademico };
        using (var reader = CreateTable().CreateDataReader())
            context.CalcParams = VerificaRaccoltaDati.ReadCalcParams(reader, AnnoAccademico);
        var info = context.GetOrCreateStudent(new StudentKey("TEST", "1"));
        info.InformazioniSede.StatusSedeSuggerito = "A";
        info.InformazioniEconomiche.Calcolate.ISEEDSU = 12000m;
        configure(context);
        try
        {
            new CalcoloImportoBorsa().Calculate(context);
        }
        catch (InvalidOperationException ex)
        {
            if (!ex.Message.Contains(AnnoAccademico, StringComparison.Ordinal)
                || !ex.Message.Contains("da sistemare", StringComparison.OrdinalIgnoreCase)
                || !ex.Message.Contains(detail, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Diagnostica non sufficiente: {ex.Message}");
            return;
        }
        throw new InvalidOperationException("Configurazione non valida accettata senza segnalazione.");
    }

    private static void Equal(decimal actual, decimal expected, string label)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{label}: atteso {expected}, ottenuto {actual}.");
    }
}
