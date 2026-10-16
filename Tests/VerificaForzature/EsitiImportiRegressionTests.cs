using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class EsitiImportiRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        foreach (string benefit in EsitoBorsaSupport.SupportedBenefitCodes)
        {
            Check(benefit, "vincitore ancora idoneo", 2, 2, 2);
            Check(benefit, "vincitore escluso da forzatura", 2, 0, 2,
                info => info.EsitoBorsaFacts.ForzatureGenerali.Add("201"));
            Check(benefit, "vincitore escluso per rinuncia", 2, 0, 0,
                info => info.EsitoBorsaFacts.RinunciaBenefici = true);
            foreach (int? previous in new int?[] { null, 0, 1, 3, 4 })
                Check(benefit, $"idoneo con esito precedente {previous}", previous, 1, 1);
            Check(benefit, "vincitore di un altro beneficio", 1, 1, 1, info =>
                info.InformazioniBeneficio.EsitiConcorsoByBenefit[benefit == "BS" ? "PA" : "BS"] =
                    new EsitoConcorsoBenefitRaw { CodTipoEsito = 2, ImportoAssegnato = 999m });
        }

        Check("BS", "esclusione solo PA non azzera BS", 2, 2, 2, info =>
        {
            info.EsitoBorsaFacts.BeneficiRichiesti.Add("PA");
            info.EsitoBorsaFacts.BeneficiRinunciaPregressa.Add("PA");
        });
        Check("BS", "esclusione solo BS con PA idoneo", 2, 0, 0, info =>
        {
            info.EsitoBorsaFacts.BeneficiRichiesti.Add("PA");
            info.EsitoBorsaFacts.BeneficiRinunciaPregressa.Add("BS");
        });
        Check("BS", "escluso anche senza importo calcolabile", 2, 0, 0, info =>
        {
            info.EsitoBorsaFacts.RinunciaBenefici = true;
            info.InformazioniSede.StatusSedeSuggerito = "";
        }, expectedAmountBeforeOutcome: null);
        Check("BS", "idoneo senza importo calcolabile resta nullo", 1, 1, 1,
            info => info.InformazioniSede.StatusSedeSuggerito = "", expectedAmountBeforeOutcome: null);

        if (failures.Count > 0)
            throw new InvalidOperationException($"Esiti/importi: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: conferma vincitori, importi BS/PA e colonne di esportazione.");

        void Check(string benefit, string scenario, int? previous, int expected, int expectedWithout,
            Action<StudenteInfo>? configure = null, decimal? expectedAmountBeforeOutcome = 2300m)
        {
            checks++;
            try
            {
                using var connection = new SqlConnection();
                var context = new VerificaPipelineContext(connection)
                {
                    AnnoAccademico = "20252026",
                    ReferenceDate = new DateTime(2025, 9, 1),
                    FaseElaborativa = VerificaFaseElaborativa.GraduatorieProvvisorie
                };
                context.CalcParams.ImportoBorsaB = 2000m;
                context.CalcParams.SogliaIsee = 20000m;
                var key = new StudentKey("TEST", "1");
                var info = context.GetOrCreateStudent(key);
                info.StatusCompilazione = 90;
                info.InformazioniIscrizione.TipoCorso = 6;
                info.InformazioniIscrizione.AnnoCorso = 2;
                info.InformazioniIscrizione.AnnoImmatricolazione = 20242025;
                info.InformazioniIscrizione.ComuneSedeStudi = "H501";
                info.InformazioniIscrizione.ProvinciaSedeStudi = "RM";
                info.InformazioniSede.Residenza.codComune = "F205";
                info.InformazioniSede.Residenza.provincia = "MI";
                info.InformazioniSede.StatusSedeSuggerito = "B";
                info.InformazioniEconomiche.Calcolate.ISEEDSU = 1000m;
                info.EsitoBorsaFacts.BeneficiRichiesti.Add(benefit);
                context.ComuniPensionatiAttivi.Add("H501");
                if (previous.HasValue)
                    context.GetOrCreateEsitiConcorsoByBenefit(key)[benefit] = new EsitoConcorsoBenefitRaw
                    {
                        CodBeneficio = benefit, CodTipoEsito = previous, ImportoAssegnato = 999m
                    };
                configure?.Invoke(info);

                new CalcoloImportoBorsa().Calculate(context);
                Equal(info.InformazioniImportoBorsa.ImportoFinale, expectedAmountBeforeOutcome, "importo prima degli esiti");
                var results = context.GetOrCreateEsitiCalcolatiByBenefit(key);
                foreach (string code in info.EsitoBorsaFacts.BeneficiRichiesti)
                    results[code] = CalcoloEsitoBorsa.CalculateBenefitResult(new EsitoBorsaStudentContext(
                        context, key, info, 2025, 20252026, new EsitoBorsaRuleConfig(), code));
                typeof(CalcoloEsitoBorsa).GetMethod("ApplyLegacyBsEvaluation", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { info, results });
                var result = results[benefit];
                Equal(result.EsitoCalcolato, expected, "esito");
                Equal(result.EsitoCalcolatoSenzaForzature, expectedWithout, "esito senza forzature");
                if (expected == 0 && result.CodiciMotivo.Length == 0)
                    throw new InvalidOperationException("manca il motivo di esclusione");

                var table = (DataTable)typeof(Verifica).GetMethod("BuildOutputTable", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null)!;
                typeof(Verifica).GetMethod("AddOutputRow", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { table, context, key, info });
                var row = table.Rows[0];
                Equal(row[$"EsitoAttuale_{benefit}"], (object?)previous ?? DBNull.Value, "esito attuale esportato");
                Equal(row[$"EsitoCalcolato_{benefit}"], expected, "esito esportato");
                Equal(row[$"EsitoCalcolatoSenzaForzature_{benefit}"], expectedWithout, "esito senza forzature esportato");
                Equal(row[$"VerificaForzature_{benefit}"], expected == expectedWithout ? "" : "DA CONTROLLARE", "segnalazione forzature");
                foreach (string code in new[] { "BS", "PA" })
                {
                    if (!results.TryGetValue(code, out var calculated))
                    {
                        Equal(row[$"ImportoCalcolato_{code}"], DBNull.Value, $"importo {code} non richiesto");
                        continue;
                    }
                    decimal? amount = code == "PA" || calculated.EsitoCalcolato == 0 ? 0m : expectedAmountBeforeOutcome;
                    Equal(calculated.ImportoCalcolato, amount, $"importo {code}");
                    Equal(row[$"ImportoCalcolato_{code}"], (object?)amount ?? DBNull.Value, $"importo {code} esportato");
                    if (code == "BS")
                    {
                        Equal(info.InformazioniImportoBorsa.ImportoFinale, amount, "importo finale BS");
                        Equal(row["ImportoFinaleBorsa"], (object?)amount ?? DBNull.Value, "importo finale BS esportato");
                        Equal(info.InformazioniImportoBorsa.CalcoloEseguito, amount.HasValue, "calcolo importo BS eseguito");
                    }
                }
                if (previous.HasValue)
                {
                    var original = context.GetOrCreateEsitiConcorsoByBenefit(key)[benefit];
                    Equal(original.CodTipoEsito, previous, "esito originale invariato");
                    Equal(original.ImportoAssegnato, 999m, "importo assegnato originale invariato");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{benefit}: {scenario}: {ex.GetBaseException().Message}");
            }
        }
    }

    private static void Equal(object? actual, object? expected, string label)
    {
        if (!Equals(actual, expected))
            throw new InvalidOperationException($"{label}: atteso {expected ?? "NULL"}, ottenuto {actual ?? "NULL"}");
    }
}
