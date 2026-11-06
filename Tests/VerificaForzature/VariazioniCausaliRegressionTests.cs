using System.Data.SqlClient;
using System.Reflection;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class VariazioniCausaliRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        foreach (string benefit in new[] { "BS", "PA", "PK", "CI" })
        {
            Check(benefit, "21: revoca ISEE assente in INPS", facts => Apply(facts, 21, "00"),
                "VAR021", revocatoIsee: true, decadutoPagamento: false);
            Check(benefit, "30: decadenza per modalità di pagamento", facts => Apply(facts, 30, "00"),
                "VAR030", revocatoIsee: false, decadutoPagamento: true);
            Check(benefit, "causali distinte e ordine dei motivi", facts =>
            {
                Apply(facts, 30, "00");
                Apply(facts, 21, "00");
            }, "VAR021;VAR030", revocatoIsee: true, decadutoPagamento: true);
            Check(benefit, "causali generali ignorate su un singolo beneficio", facts =>
            {
                Apply(facts, 21, benefit);
                Apply(facts, 30, benefit);
            }, string.Empty, revocatoIsee: false, decadutoPagamento: false);
        }

        if (failures.Count > 0)
            throw new InvalidOperationException($"Causali variazioni: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: causali DB 21/30 dal loader agli esiti, con flag e diagnostica distinti.");

        void Check(string benefit, string scenario, Action<EsitoBorsaFacts> configure,
            string expectedReasons, bool revocatoIsee, bool decadutoPagamento)
        {
            checks++;
            try
            {
                using var connection = new SqlConnection();
                var context = new VerificaPipelineContext(connection)
                {
                    AnnoAccademico = "20262027",
                    ReferenceDate = new DateTime(2026, 9, 1),
                    FaseElaborativa = VerificaFaseElaborativa.GraduatorieProvvisorie
                };
                var key = new StudentKey("TEST", "1");
                var info = context.GetOrCreateStudent(key);
                info.StatusCompilazione = 90;
                info.InformazioniIscrizione.TipoCorso = 6;
                info.InformazioniIscrizione.AnnoCorso = 2;
                info.InformazioniIscrizione.AnnoImmatricolazione = 20252026;
                info.InformazioniIscrizione.ComuneSedeStudi = "H501";
                info.InformazioniSede.Residenza.codComune = "F205";
                info.InformazioniSede.StatusSedeSuggerito = "B";
                info.InformazioniEconomiche.Calcolate.ISEEDSU = 1000m;
                info.InformazioniEconomiche.Calcolate.ISPEDSU = 1000m;
                info.EsitoBorsaFacts.BeneficiRichiesti.Add(benefit);
                context.ComuniPensionatiAttivi.Add("H501");
                var facts = info.EsitoBorsaFacts;
                configure(facts);

                if (facts.RevocatoISEE != revocatoIsee
                    || facts.DecadutoMancataComunicazionePagamento != decadutoPagamento)
                    throw new InvalidOperationException("Il loader ha impostato una causale diversa da quella prevista.");

                var result = CalcoloEsitoBorsa.CalculateBenefitResult(new EsitoBorsaStudentContext(
                    context, key, info, 2026, 20262027,
                    new EsitoBorsaRuleConfig { SogliaIsee = 20000m, SogliaIsp = 50000m }, benefit));
                int expectedOutcome = expectedReasons.Length == 0 ? 1 : 0;
                if (result.EsitoCalcolato != expectedOutcome || result.EsitoCalcolatoSenzaForzature != expectedOutcome
                    || result.CodiciMotivo != expectedReasons)
                    throw new InvalidOperationException($"Esito={result.EsitoCalcolato}/{result.EsitoCalcolatoSenzaForzature}, motivi={result.CodiciMotivo}, attesi={expectedReasons}.");

                string summary = EsitoBorsaSupport.GetVariazioniEscludentiSummary(facts, benefit);
                if (summary.Contains("DECADENZA_MANCATA_COMUNICAZIONE_PAGAMENTO", StringComparison.Ordinal) != decadutoPagamento
                    || summary.Contains("REVOCA_TRASMISSIONE_ISEE_FUORI_TERMINE", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Diagnostica della causale 30 non corretta: {summary}.");
                if (benefit == "BS" && EsitoBorsaSupport.GetVariazioniEscludentiBsSummary(facts)
                    .Contains("DECADENZA_MANCATA_COMUNICAZIONE_PAGAMENTO", StringComparison.Ordinal) != decadutoPagamento)
                    throw new InvalidOperationException("Diagnostica BS della causale 30 non corretta.");
            }
            catch (Exception ex)
            {
                failures.Add($"{benefit} {scenario}: {ex.GetBaseException().Message}");
            }
        }
    }

    private static void Apply(EsitoBorsaFacts facts, int codice, string benefit)
        => typeof(VerificaRaccoltaDati)
            .GetMethod("ApplyVariazione", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { facts, codice, benefit });
}
