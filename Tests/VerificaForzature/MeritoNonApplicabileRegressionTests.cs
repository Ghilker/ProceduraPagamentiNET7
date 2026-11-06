using System.Data.SqlClient;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class MeritoNonApplicabileRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        foreach (string benefit in new[] { "BS", "PA", "PK", "CI" })
        {
            string motivoLimite = benefit == "BS" ? "BS001" : benefit == "CI" ? "CI003" : "PA003";
            Check(benefit, "anno immatricolazione mancante", info =>
                info.InformazioniIscrizione.AnnoImmatricolazione = null, $"ISC001;{motivoLimite}");
            Check(benefit, "anno calcolato non classificabile", info =>
                info.InformazioniIscrizione.AnnoCorso = 3, motivoLimite);
            Check(benefit, "fuori corso oltre il limite", info =>
            {
                info.InformazioniIscrizione.AnnoCorso = -5;
                info.InformazioniIscrizione.AnnoImmatricolazione = 20192020;
            }, motivoLimite);
            Check(benefit, "anno dichiarato zero", info =>
                info.InformazioniIscrizione.AnnoCorso = 0, benefit == "BS" ? "ISC008;BS001" : "ISC008");
            Check(benefit, "interruzione carriera senza durata", info =>
            {
                info.EsitoBorsaFacts.CarrieraInterrotta = true;
                info.EsitoBorsaFacts.NumAnniInterruzione = null;
            }, "ISC004");
            Check(benefit, "più dati accademici invalidi", info =>
            {
                info.InformazioniIscrizione.AnnoCorso = 0;
                info.InformazioniIscrizione.AnnoImmatricolazione = null;
            }, $"ISC001;ISC008;{motivoLimite}");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException($"Merito non applicabile: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: dati accademici invalidi e anni non ammessi escludono lo studente senza richiedere soglie di merito non applicabili.");

        void Check(string benefit, string scenario, Action<StudenteInfo> configure, string expectedReasons)
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
                info.InformazioniIscrizione.TipoCorso = 3;
                info.InformazioniIscrizione.AnnoCorso = 2;
                info.InformazioniIscrizione.AnnoImmatricolazione = 20252026;
                info.InformazioniIscrizione.CodCorsoLaurea = "TEST";
                info.InformazioniIscrizione.NumeroCrediti = 80m;
                info.InformazioniIscrizione.ComuneSedeStudi = "H501";
                info.InformazioniSede.Residenza.codComune = "F205";
                info.InformazioniSede.StatusSedeSuggerito = "B";
                info.InformazioniEconomiche.Calcolate.ISEEDSU = 1000m;
                info.InformazioniEconomiche.Calcolate.ISPEDSU = 1000m;
                info.EsitoBorsaFacts.BeneficiRichiesti.Add(benefit);
                context.ComuniPensionatiAttivi.Add("H501");
                configure(info);

                var result = CalcoloEsitoBorsa.CalculateBenefitResult(new EsitoBorsaStudentContext(
                    context, key, info, 2026, 20262027,
                    new EsitoBorsaRuleConfig { SogliaIsee = 20000m, SogliaIsp = 50000m }, benefit));
                if (result.EsitoCalcolato != 0 || result.EsitoCalcolatoSenzaForzature != 0
                    || result.CodiciMotivo != expectedReasons
                    || info.InformazioniIscrizione.CreditiMinimiRichiestiMerito.HasValue)
                {
                    failures.Add($"{benefit} {scenario}: esito={result.EsitoCalcolato}/{result.EsitoCalcolatoSenzaForzature}, " +
                        $"motivi={result.CodiciMotivo} (attesi {expectedReasons}), soglia={info.InformazioniIscrizione.CreditiMinimiRichiestiMerito}.");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{benefit} {scenario}: {ex.Message}");
            }
        }
    }
}
