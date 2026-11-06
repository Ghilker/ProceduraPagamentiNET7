using System.Data.SqlClient;
using ProcedureNet7;
using ProcedureNet7.Verifica;

void CheckAmount(string label, Action<StudenteInfo> configure, decimal actualExpected, decimal legacyExpected)
{
    using var connection = new SqlConnection();
    var pipeline = new VerificaPipelineContext(connection) { AnnoAccademico = "20262027" };
    pipeline.CalcParams.ImportoBorsaA = 2000m;
    pipeline.CalcParams.ImportoBorsaB = 3000m;
    pipeline.CalcParams.ImportoBorsaC = 2500m;
    pipeline.CalcParams.SogliaIsee = 20000m;
    var info = pipeline.GetOrCreateStudent(new StudentKey("TEST", "1"));
    info.InformazioniSede.StatusSedeSuggerito = "A";
    info.InformazioniIscrizione.AnnoCorso = 1;
    info.InformazioniEconomiche.Calcolate.ISEEDSU = 12000m;
    configure(info);
    new CalcoloImportoBorsa().Calculate(pipeline);
    decimal? actual = info.InformazioniImportoBorsa.ImportoFinale;
    Console.WriteLine($"{label}: attuale={actual}; atteso_legacy={legacyExpected}; differenza={actual - legacyExpected}");
    if (actual != actualExpected) throw new Exception($"Risultato inatteso: {label}");
}

CheckAmount("Controllo base", _ => { }, 2000m, 2000m);
CheckAmount("ISEE esattamente meta soglia", i => i.InformazioniEconomiche.Calcolate.ISEEDSU = 10000m, 2300m, 2300m);
CheckAmount("ISEE sotto meta soglia", i => i.InformazioniEconomiche.Calcolate.ISEEDSU = 9999m, 2300m, 2300m);
CheckAmount("Tipologia 6 con status A", i => i.InformazioniIscrizione.TipoCorso = 6, 2000m, 3000m);
CheckAmount("Monetizzazione con detrazione pasti 100", i => i.InformazioniBeneficio.ConcessaMonetizzazioneMensa = true, 2600m, 2500m);

void CheckThresholds(string label, decimal threshold, string expectedCodes)
{
    using var connection = new SqlConnection();
    var pipeline = new VerificaPipelineContext(connection) { AnnoAccademico = "20262027" };
    var key = new StudentKey("TEST", "1");
    var info = pipeline.GetOrCreateStudent(key);
    info.InformazioniEconomiche.Raw.TipoRedditoOrigine = "EE";
    info.InformazioniEconomiche.Raw.OrigineFonte = "EE";
    info.InformazioniEconomiche.Calcolate.ISEEDSU = 100000m;
    info.InformazioniEconomiche.Calcolate.ISPEDSU = 100000m;
    var config = new EsitoBorsaRuleConfig { SogliaIsee = threshold, SogliaIsp = threshold };
    var studentContext = new EsitoBorsaStudentContext(pipeline, key, info, 2026, 20262027, config, "BS");
    var evaluation = new EsitoBorsaEvaluation();
    new EsitoBorsaIncomeRules().Apply(studentContext, evaluation);
    string actualCodes = string.Join(";", evaluation.ErrorCodes);
    Console.WriteLine($"{label}: ISEE=100000; ISPE=100000; soglie={threshold}; codici={actualCodes}");
    if (actualCodes != expectedCodes) throw new Exception($"Risultato inatteso: {label}");
}

CheckThresholds("Soglie non caricate", 0m, "");
CheckThresholds("Controllo soglie configurate", 20000m, "RED012;RED013");
Console.WriteLine("Audit completato: 7 casi in memoria, nessuna connessione DB aperta.");

