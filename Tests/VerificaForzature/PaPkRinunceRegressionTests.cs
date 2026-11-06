using System.Data.SqlClient;
using System.Reflection;
using System.Text.RegularExpressions;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class PaPkRinunceRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        foreach (string benefit in new[] { "PA", "PK" })
        {
            Check(benefit, "idoneo", (_, _) => { }, 1, 1);
            Check(benefit, "vincitore dello stesso beneficio", (context, _) => SetWinner(context, benefit), 2, 2);
            Check(benefit, "vincitore di un altro beneficio", (context, _) => SetWinner(context, benefit == "PA" ? "PK" : "PA"), 1, 1);
            Check(benefit, "status sede A", (_, info) => info.InformazioniSede.StatusSedeSuggerito = "A", 0, 0, "PA001");
            Check(benefit, "status sede D ammesso", (_, info) => info.InformazioniSede.StatusSedeSuggerito = "D", 1, 1);
            Check(benefit, "comune senza pensionato", (context, _) => context.ComuniPensionatiAttivi.Clear(), 0, 0, "PA002");
            Check(benefit, "fuori corso oltre il limite", (context, info) =>
            {
                info.InformazioniIscrizione.TipoCorso = 3;
                info.InformazioniIscrizione.AnnoCorso = -3;
                info.InformazioniIscrizione.AnnoImmatricolazione = 20212022;
                info.InformazioniIscrizione.CodCorsoLaurea = "TEST";
                info.InformazioniIscrizione.NumeroCrediti = 160m;
                context.CreditiRichiestiCatalog.Add(3, -3, "0", 135m, false, 1);
            }, 0, 0, "PA003");
            Check(benefit, "beneficio pregresso non restituito", (_, info) => info.EsitoBorsaFacts.BeneficiPregressiNonRestituiti.Add(benefit), 0, 0, "PA004");
            Check(benefit, "rinuncia pregressa", (_, info) => info.EsitoBorsaFacts.BeneficiRinunciaPregressa.Add(benefit), 0, 0, "PA005");
            Check(benefit, "rinuncia corrente", (_, info) => SetRinuncia(info.EsitoBorsaFacts, benefit), 0, 0, "PA005");
            Check(benefit, "decadenza", (_, info) => SetDecadenza(info.EsitoBorsaFacts, benefit), 0, 0, "PA006");
            Check(benefit, "revoca bando", (_, info) => SetRevocaBando(info.EsitoBorsaFacts, benefit), 0, 0, "PA007");
            Check(benefit, "revoca sede distaccata", (_, info) => info.EsitoBorsaFacts.RevocatoSedeDistaccata = true, 0, 0, "PA008");
            Check(benefit, "variazione 18 del beneficio", (_, info) => ApplyVariazione(info.EsitoBorsaFacts, 18, benefit), 0, 0, "PA008");
            Check(benefit, "revoca totale", (_, info) => info.EsitoBorsaFacts.Revocato = true, 0, 0, "VAR003");
            Check(benefit, "rinuncia pregressa con forzatura", (_, info) =>
            {
                info.EsitoBorsaFacts.BeneficiRinunciaPregressa.Add(benefit);
                info.EsitoBorsaFacts.ForzaturaRinunciaNoEsclusione = true;
            }, 1, 0);
            Check(benefit, "forzatura status sede", (_, info) =>
            {
                info.InformazioniSede.Residenza.codComune = "H501";
                info.InformazioniSede.Residenza.provincia = "RM";
                info.InformazioniSede.ForzaturaStatusSede = "B";
            }, 1, 0);
            Check(benefit, "vincitore escluso da rinuncia", (context, info) =>
            {
                SetWinner(context, benefit);
                SetRinuncia(info.EsitoBorsaFacts, benefit);
            }, 0, 0, "PA005");
        }

        Check("PK", "rinuncia PA collegata", (_, info) => info.EsitoBorsaFacts.RinunciaPA = true, 0, 0, "PA005");
        Check("PK", "decadenza PA collegata", (_, info) => info.EsitoBorsaFacts.DecadutoPA = true, 0, 0, "PA006");
        Check("PK", "revoca PA collegata", (_, info) => info.EsitoBorsaFacts.RevocatoBandoPA = true, 0, 0, "PA007");
        Check("PK", "beneficio pregresso PA collegato", (_, info) => info.EsitoBorsaFacts.BeneficiPregressiNonRestituiti.Add("PA"), 0, 0, "PA004");
        Check("PK", "rinuncia pregressa PA collegata", (_, info) => info.EsitoBorsaFacts.BeneficiRinunciaPregressa.Add("PA"), 0, 0, "PA005");
        Check("PK", "variazione 18 PA collegata", (_, info) => ApplyVariazione(info.EsitoBorsaFacts, 18, "PA"), 0, 0, "PA008");
        Check("PA", "variazione PK indipendente", (_, info) => info.EsitoBorsaFacts.RinunciaPK = true, 1, 1);
        foreach (string benefit in new[] { "BS", "PA", "CI" })
            Check(benefit, "variazione 18 PK indipendente", (_, info) => ApplyVariazione(info.EsitoBorsaFacts, 18, "PK"), 1, 1);

        checks++;
        string rinunceSql = (string)typeof(VerificaRaccoltaDati)
            .GetField("EsitoBorsaRinunceUfficioSql", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        // Riga_valida è lo stato storico da sostituire: includerlo nella chiave
        // mantiene selezionata una vecchia rinuncia anche dopo il ripristino.
        if (Regex.IsMatch(rinunceSql, @"\bv2\.Riga_valida\s*=", RegexOptions.IgnoreCase)
            || !rinunceSql.Contains("v2.ANNO_ACCADEMICO = v.ANNO_ACCADEMICO", StringComparison.OrdinalIgnoreCase)
            || !rinunceSql.Contains("v2.NUM_DOMANDA = v.NUM_DOMANDA", StringComparison.OrdinalIgnoreCase)
            || !rinunceSql.Contains("ISNULL(v2.COD_BENEFICIO,'') = ISNULL(v.COD_BENEFICIO,'')", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Rinunce: l'ultima riga deve essere selezionata per anno/domanda/beneficio, indipendentemente dallo stato Riga_valida.");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException($"PA/PK e rinunce: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: parità PA/PK, variazioni e selezione delle rinunce ripristinate.");

        void Check(string benefit, string scenario, Action<VerificaPipelineContext, StudenteInfo> configure,
            int expected, int expectedWithout, string? expectedReason = null)
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
                info.InformazioniIscrizione.ProvinciaSedeStudi = "RM";
                info.InformazioniSede.Residenza.codComune = "F205";
                info.InformazioniSede.Residenza.provincia = "MI";
                info.InformazioniSede.StatusSedeSuggerito = "B";
                info.InformazioniEconomiche.Calcolate.ISEEDSU = 1000m;
                info.InformazioniEconomiche.Calcolate.ISPEDSU = 1000m;
                info.EsitoBorsaFacts.BeneficiRichiesti.Add(benefit);
                context.ComuniPensionatiAttivi.Add("H501");
                configure(context, info);

                var result = CalcoloEsitoBorsa.CalculateBenefitResult(new EsitoBorsaStudentContext(
                    context, key, info, 2026, 20262027,
                    new EsitoBorsaRuleConfig { SogliaIsee = 20000m, SogliaIsp = 50000m }, benefit));
                if (result.EsitoCalcolato != expected || result.EsitoCalcolatoSenzaForzature != expectedWithout
                    || (expectedReason != null && !result.CodiciMotivo.Split(';').Contains(expectedReason)))
                {
                    failures.Add($"{benefit} {scenario}: esito={result.EsitoCalcolato}/{result.EsitoCalcolatoSenzaForzature}, atteso={expected}/{expectedWithout}, motivi={result.CodiciMotivo}.");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{benefit} {scenario}: {ex.Message}");
            }
        }
    }

    private static void SetWinner(VerificaPipelineContext context, string benefit)
        => context.GetOrCreateEsitiConcorsoByBenefit(context.Students.Keys.Single())[benefit] =
            new EsitoConcorsoBenefitRaw { CodBeneficio = benefit, CodTipoEsito = 2 };

    private static void ApplyVariazione(EsitoBorsaFacts facts, int codice, string benefit)
        => typeof(VerificaRaccoltaDati)
            .GetMethod("ApplyVariazione", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { facts, codice, benefit });

    private static void SetRinuncia(EsitoBorsaFacts facts, string benefit)
    {
        if (benefit == "PA") facts.RinunciaPA = true;
        else facts.RinunciaPK = true;
    }

    private static void SetDecadenza(EsitoBorsaFacts facts, string benefit)
    {
        if (benefit == "PA") facts.DecadutoPA = true;
        else facts.DecadutoPK = true;
    }

    private static void SetRevocaBando(EsitoBorsaFacts facts, string benefit)
    {
        if (benefit == "PA") facts.RevocatoBandoPA = true;
        else facts.RevocatoBandoPK = true;
    }
}
