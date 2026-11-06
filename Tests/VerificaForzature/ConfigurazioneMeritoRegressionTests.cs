using System.Data.SqlClient;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class ConfigurazioneMeritoRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        foreach (decimal value in new[] { 0m, -1m })
        {
            Check($"Soglia ISEE {value}", () =>
                ExpectConfigurationError(() => ApplyIncome(value, 50000m), "Soglia_Isee"));
            Check($"Soglia ISPE {value}", () =>
                ExpectConfigurationError(() => ApplyIncome(20000m, value), "Soglia_ISPE"));
        }
        Check("soglie valide applicate", () =>
        {
            var result = ApplyIncome(20000m, 50000m);
            if (!result.ErrorCodes.Contains("RED012") || !result.ErrorCodes.Contains("RED013"))
                throw new InvalidOperationException("ISEE e ISPE oltre soglia devono escludere.");
        });

        Check("catalogo CFU vuoto", () => CheckMerit(null, 1m, expectError: true));
        Check("catalogo CFU vuoto anche con merito assente", () => CheckMerit(null, 0m, expectError: true));
        Check("soglia di altro corso non sostituisce quella mancante", () => CheckMerit("ALTRO", 1m, expectError: true));
        Check("soglia configurata esclude merito insufficiente", () => CheckMerit("TEST", 1m, expectError: false));
        Check("soglia configurata ammette alla soglia", () => CheckMerit("TEST", 25m, expectError: false));

        Check("tipologia soglia CFU NULL", () => ExpectConfigurationError(
            () => new CreditiRichiestiCatalog().Add(null, 2, "0", 25m, false, 1), "Tipologia_corso"));
        Check("anno soglia CFU NULL", () => ExpectConfigurationError(
            () => new CreditiRichiestiCatalog().Add(3, null, "0", 25m, false, 1), "Anno_corso"));
        Check("crediti soglia CFU NULL", () => ExpectConfigurationError(
            () => new CreditiRichiestiCatalog().Add(3, 2, "0", null, false, 1), "Crediti_richiesti"));
        Check("disabile soglia CFU NULL", () => ExpectConfigurationError(
            () => new CreditiRichiestiCatalog().Add(3, 2, "0", 25m, null, 1), "Disabile"));
        Check("soglia CFU negativa", () => ExpectConfigurationError(
            () => new CreditiRichiestiCatalog().Add(3, 2, "0", -1m, false, 1), "Crediti_richiesti"));
        Check("soglia CFU zero esplicita resta configurazione valida", () =>
        {
            var catalog = new CreditiRichiestiCatalog();
            catalog.Add(3, 2, null, 0m, false, 1);
            if (catalog.Resolve(3, 2, 2, false, "TEST", "")?.CreditiRichiesti != 0m)
                throw new InvalidOperationException("Una riga esplicita con zero non deve essere confusa con una riga assente.");
        });

        if (failures.Count > 0)
            throw new InvalidOperationException($"Configurazione/merito: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: configurazioni economiche e di merito obbligatorie.");

        void Check(string scenario, Action action)
        {
            checks++;
            try { action(); }
            catch (Exception ex) { failures.Add($"{scenario}: {ex.GetBaseException().Message}"); }
        }
    }

    private static EsitoBorsaEvaluation ApplyIncome(decimal iseeThreshold, decimal ispThreshold)
    {
        using var connection = new SqlConnection();
        var pipeline = new VerificaPipelineContext(connection) { AnnoAccademico = "20262027" };
        var key = new StudentKey("TEST", "1");
        var info = pipeline.GetOrCreateStudent(key);
        info.InformazioniEconomiche.Calcolate.ISEEDSU = 100000m;
        info.InformazioniEconomiche.Calcolate.ISPEDSU = 100000m;
        var context = new EsitoBorsaStudentContext(pipeline, key, info, 2026, 20262027,
            new EsitoBorsaRuleConfig { SogliaIsee = iseeThreshold, SogliaIsp = ispThreshold }, "BS");
        var result = new EsitoBorsaEvaluation();
        new EsitoBorsaIncomeRules().Apply(context, result);
        return result;
    }

    private static void CheckMerit(string? configuredCourse, decimal credits, bool expectError)
    {
        using var connection = new SqlConnection();
        var pipeline = new VerificaPipelineContext(connection) { AnnoAccademico = "20262027" };
        var key = new StudentKey("TEST", "1");
        var info = pipeline.GetOrCreateStudent(key);
        var iscr = info.InformazioniIscrizione;
        iscr.TipoCorso = 3;
        iscr.AnnoCorso = 2;
        iscr.AnnoImmatricolazione = 20252026;
        iscr.CodCorsoLaurea = "TEST";
        iscr.NumeroCrediti = credits;
        if (configuredCourse != null)
            pipeline.CreditiRichiestiCatalog.Add(3, 2, configuredCourse, 25m, false, 1);
        var context = new EsitoBorsaStudentContext(pipeline, key, info, 2026, 20262027,
            new EsitoBorsaRuleConfig { SogliaIsee = 20000m, SogliaIsp = 50000m }, "BS");
        var result = new EsitoBorsaEvaluation();
        if (expectError)
        {
            ExpectConfigurationError(() => new EsitoBorsaMeritRules().Apply(context, result), "20262027", "TEST", "Crediti_richiesti");
            return;
        }
        new EsitoBorsaMeritRules().Apply(context, result);
        if (result.ErrorCodes.Contains("MER012") != (credits < 25m))
            throw new InvalidOperationException("Esito di merito incoerente con la soglia configurata.");
    }

    private static void ExpectConfigurationError(Action action, params string[] details)
    {
        try { action(); }
        catch (InvalidOperationException ex)
        {
            foreach (string detail in details)
                if (!ex.Message.Contains(detail, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Errore senza dettaglio {detail}: {ex.Message}");
            return;
        }
        throw new InvalidOperationException("Atteso errore di configurazione, ma il calcolo è proseguito.");
    }
}
