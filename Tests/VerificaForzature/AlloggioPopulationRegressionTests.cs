using System.Data.SqlClient;
using System.Reflection;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class AlloggioPopulationRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        foreach (string benefit in new[] { "PA", "PK" })
        {
            foreach (int? outcome in new int?[] { null, 0, 1, 2, 3 })
            {
                foreach (bool requested in new[] { false, true })
                    Check($"{benefit} esito {outcome}, richiesto {requested}", requested ? benefit : null,
                        benefit, outcome, requested && outcome is 1 or 2);
            }
            Check($"richiesta {benefit} ed esito dell'altro alloggio", benefit, benefit == "PA" ? "PK" : "PA", 1, false);
        }
        Check("vincitore BS non alimenta alloggio", "BS", "BS", 2, false);

        if (failures.Count > 0)
            throw new InvalidOperationException($"Caricamento alloggio: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: caricamento idonei e vincitori PA/PK sul beneficio richiesto.");

        void Check(string label, string? requested, string benefit, int? outcome, bool expected)
        {
            checks++;
            try
            {
                using var connection = new SqlConnection();
                var pipeline = new VerificaPipelineContext(connection) { AnnoAccademico = "20262027" };
                var key = new StudentKey("TEST", "1");
                var info = pipeline.GetOrCreateStudent(key);
                // Il caricamento deve anche azzerare un precedente valore non più valido.
                info.InformazioniSede.HasAlloggio12 = true;
                if (requested != null)
                    info.EsitoBorsaFacts.BeneficiRichiesti.Add(requested);
                if (outcome.HasValue)
                    pipeline.GetOrCreateEsitiConcorsoByBenefit(key)[benefit] = new EsitoConcorsoBenefitRaw
                    {
                        CodBeneficio = benefit,
                        CodTipoEsito = outcome
                    };
                var loader = new VerificaRaccoltaDati(connection);
                typeof(VerificaRaccoltaDati).GetMethod("LoadEsitoPaPerAlloggio", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(loader, new object[] { pipeline });
                if (info.InformazioniSede.HasAlloggio12 != expected)
                    throw new InvalidOperationException($"HasAlloggio12={info.InformazioniSede.HasAlloggio12}, atteso {expected}.");
                if (connection.State != System.Data.ConnectionState.Closed)
                    throw new InvalidOperationException("Il test non deve aprire connessioni DB.");
            }
            catch (Exception ex) { failures.Add($"{label}: {ex.GetBaseException().Message}"); }
        }
    }
}
