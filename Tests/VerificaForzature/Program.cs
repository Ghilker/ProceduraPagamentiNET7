using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using ProcedureNet7;
using ProcedureNet7.Verifica;

int checks = 0;
CoefficienteRegressionTests.Run();
EsitiImportiRegressionTests.Run();
foreach (string benefit in EsitoBorsaSupport.SupportedBenefitCodes)
{
    Check(benefit, "nessuna forzatura", (_, _) => { }, 1, 1);
    Check(benefit, "esclusione forzata", (_, info) => info.EsitoBorsaFacts.ForzatureGenerali.Add("201"), 0, 1, "GENF201");
    Check(benefit, "altro motivo di esclusione", (_, info) =>
    {
        info.EsitoBorsaFacts.ForzatureGenerali.Add("201");
        info.EsitoBorsaFacts.RinunciaBenefici = true;
    }, 0, 0, "GEN007");
}

foreach (string benefit in new[] { "BS", "PA", "CI" })
{
    Check(benefit, "rinuncia pregressa forzata", (_, info) =>
    {
        info.EsitoBorsaFacts.ForzaturaRinunciaNoEsclusione = true;
        info.EsitoBorsaFacts.BeneficiRinunciaPregressa.Add(benefit);
    }, 1, 0);
    Check(benefit, "beneficio non restituito forzato", (_, info) =>
    {
        info.EsitoBorsaFacts.ForzaturaRinunciaNoEsclusione = true;
        info.EsitoBorsaFacts.BeneficiPregressiNonRestituiti.Add(benefit);
    }, 1, 0);
    Check(benefit, "forzature con effetti opposti", (_, info) =>
    {
        info.EsitoBorsaFacts.ForzatureGenerali.Add("201");
        info.EsitoBorsaFacts.ForzaturaRinunciaNoEsclusione = true;
        info.EsitoBorsaFacts.BeneficiRinunciaPregressa.Add(benefit);
    }, 0, 0, "GENF201");
}

Check("BS", "borsa storica forzata", (context, info) =>
{
    info.EsitoBorsaFacts.ForzaturaRinunciaNoEsclusione = true;
    context.BorsaStoricaStessoAnnoConflitti.Add(context.Students.Keys.Single());
}, 1, 0);
Check("PA", "status sede forzato ammesso", (_, info) =>
{
    info.InformazioniSede.ForzaturaStatusSede = "B";
    info.InformazioniSede.StatusSedeSuggerito = "B";
    info.InformazioniSede.Residenza.codComune = "H501";
}, 1, 0);
Check("PA", "status sede forzato escluso", (_, info) =>
{
    info.InformazioniSede.ForzaturaStatusSede = "A";
    info.InformazioniSede.StatusSedeSuggerito = "A";
}, 0, 1, "PA001");
Check("PA", "vincitore PA", (_, info) =>
{
    info.InformazioniSede.ForzaturaStatusSede = "A";
    info.InformazioniBeneficio.VincitorePA = true;
}, 1, 1);

Console.WriteLine($"Superati {checks} scenari: calcolo con/senza forzature e colonne di esportazione.");

void Check(string benefit, string scenario, Action<VerificaPipelineContext, StudenteInfo> configure,
    int expected, int expectedWithout, string? expectedReason = null)
{
    using var connection = new SqlConnection();
    var context = new VerificaPipelineContext(connection)
    {
        AnnoAccademico = "20252026",
        ReferenceDate = new DateTime(2025, 9, 1),
        FaseElaborativa = VerificaFaseElaborativa.GraduatorieProvvisorie
    };
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
    configure(context, info);

    string label = $"{benefit}: {scenario}";
    string[] forzature = info.EsitoBorsaFacts.ForzatureGenerali.ToArray();
    bool rinunciaForzata = info.EsitoBorsaFacts.ForzaturaRinunciaNoEsclusione;
    string statusForzato = info.InformazioniSede.ForzaturaStatusSede;
    string statusSuggerito = info.InformazioniSede.StatusSedeSuggerito;
    var studentContext = new EsitoBorsaStudentContext(context, key, info, 2025, 20252026, new EsitoBorsaRuleConfig(), benefit);
    var result = CalcoloEsitoBorsa.CalculateBenefitResult(studentContext);
    Assert(result.EsitoCalcolato == expected, $"{label}: esito {result.EsitoCalcolato}, motivi {result.CodiciMotivo}");
    Assert(result.EsitoCalcolatoSenzaForzature == expectedWithout, $"{label}: esito senza forzature {result.EsitoCalcolatoSenzaForzature}");
    if (expectedReason != null)
        Assert(result.CodiciMotivo.Split(';').Contains(expectedReason), $"{label}: motivo originale assente");
    Assert(info.EsitoBorsaFacts.ForzatureGenerali.SetEquals(forzature)
        && info.EsitoBorsaFacts.ForzaturaRinunciaNoEsclusione == rinunciaForzata
        && info.InformazioniSede.ForzaturaStatusSede == statusForzato
        && info.InformazioniSede.StatusSedeSuggerito == statusSuggerito, $"{label}: dati originali alterati");

    context.GetOrCreateEsitiCalcolatiByBenefit(key)[benefit] = result;
    var table = (DataTable)typeof(Verifica).GetMethod("BuildOutputTable", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
    var row = table.NewRow();
    var fill = typeof(Verifica).GetMethod("FillBenefitOutcomeColumns", BindingFlags.NonPublic | BindingFlags.Static)!;
    fill.Invoke(null, new object[] { row, context, key });
    foreach (string code in EsitoBorsaSupport.SupportedBenefitCodes)
    {
        int ordinal = table.Columns[$"EsitoCalcolato_{code}"]!.Ordinal;
        Assert(table.Columns[ordinal + 1].ColumnName == $"EsitoCalcolatoSenzaForzature_{code}"
            && table.Columns[ordinal + 2].ColumnName == $"VerificaForzature_{code}", $"{label}: ordine colonne {code}");
        Assert(Equals(row[$"EsitoCalcolato_{code}"], code == benefit ? expected : DBNull.Value), $"{label}: esito esportato {code}");
        Assert(Equals(row[$"EsitoCalcolatoSenzaForzature_{code}"], code == benefit ? expectedWithout : DBNull.Value), $"{label}: esito senza forzature esportato {code}");
        string marker = code == benefit && expected != expectedWithout ? "DA CONTROLLARE" : string.Empty;
        Assert(Equals(row[$"VerificaForzature_{code}"], marker), $"{label}: segnalazione {code}");
    }

    context.GetOrCreateEsitiCalcolatiByBenefit(key).Clear();
    fill.Invoke(null, new object[] { row, context, key });
    Assert(row.IsNull($"EsitoCalcolatoSenzaForzature_{benefit}")
        && Equals(row[$"VerificaForzature_{benefit}"], string.Empty), $"{label}: risultato assente");
    checks++;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
