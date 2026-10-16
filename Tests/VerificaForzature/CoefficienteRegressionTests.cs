using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class CoefficienteRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();

        CheckCfn("CFN negativo", 20m, 25m, 60m, 0m, -0.14m);
        CheckCfn("CFN superiore a uno", 70m, 25m, 60m, 0m, 1.29m);
        CheckCfn("bonus escluso dal punteggio come nel VB", 20m, 25m, 60m, 5m, -0.14m);
        CheckCfn("precisione della colonna CFN", 50m, 25m, 60m, 0m, 0.71m);
        CheckCfn("arrotondamento positivo a meta centesimo", 1.5m, 1m, 101m, 0m, 0.01m);
        CheckCfn("arrotondamento negativo a meta centesimo", 0.5m, 1m, 101m, 0m, -0.01m);
        CheckCfn("massimo uguale a minimo non nullo", 30m, 30m, 30m, 0m, 1m);
        CheckCfn("minimo zero non calcolato nel VB", 30m, 0m, 60m, 0m, null);
        CheckCfn("massimo assente", 30m, 25m, null, 0m, null);
        CheckCfn("merito assente", null, 25m, 60m, 0m, null);
        CheckCfn("minimo storico in assenza di soglia nuova", 50m, null, 60m, 0m, 0.71m);

        Check("sistema esami separato dai crediti", (context, info) =>
        {
            var iscr = info.InformazioniIscrizione;
            iscr.CoefficienteCongiunto.SistemaValutazione = " e ";
            iscr.NumeroEsami = 15;
            iscr.NumeroCrediti = 100m;
            iscr.CreditiMinimiRichiestiMerito = 25m;
            iscr.EsamiMinimiRichiestiMerito = 10m;
            iscr.CoefficienteCongiunto.MeritoMassimoConseguibile = 20m;
        }, row =>
        {
            Equal(row, "MeritoConseguito_Calcolato", 15m);
            Equal(row, "MeritoMinimoPrevisto_Calcolato", 10m);
            Equal(row, "CFN_Calcolato", 0.50m);
        });

        Check("soglia esami storica", (_, info) =>
        {
            var iscr = info.InformazioniIscrizione;
            iscr.CoefficienteCongiunto.SistemaValutazione = "E";
            iscr.NumeroEsami = 15;
            iscr.CreditiMinimiRichiestiMerito = 25m;
            iscr.CoefficienteCongiunto.MeritoMinimoPrevisto = 10m;
            iscr.CoefficienteCongiunto.MeritoMassimoConseguibile = 20m;
        }, row => Equal(row, "CFN_Calcolato", 0.50m));

        CheckIsee("anno precedente senza minimo estero", "20252026", "EE", 2, 4000m, 4000m);
        CheckIsee("minimo estero dal 20262027", "20262027", "EE", 2, 4000m, 7000m);
        CheckIsee("tipo comune 1 escluso dal minimo", "20262027", "EE", 1, 4000m, 4000m);
        CheckIsee("tipo comune 3 escluso dal minimo", "20262027", "EE", 3, 4000m, 4000m);
        CheckIsee("tipo comune zero valido e diverso da 1 e 3", "20262027", "EE", 0, 4000m, 7000m);
        CheckIsee("tipo comune assente non soddisfa condizione SQL", "20262027", "EE", null, 4000m, 4000m);
        CheckIsee("residente italiano", "20262027", "RM", 2, 4000m, 4000m);
        CheckIsee("anni successivi", "20272028", "EE", 2, 4000m, 7000m);
        CheckIsee("provincia normalizzata", "20262027", " ee ", 2, 4000m, 7000m);
        CheckIsee("ISEE esattamente al minimo", "20262027", "EE", 2, 7000m, 7000m);
        CheckIsee("ISEE sopra il minimo", "20262027", "EE", 2, 7000.01m, 7000.01m);
        CheckIsee("ISEE nullo", "20262027", "EE", 2, null, null);
        CheckIsee("ISEE zero", "20262027", "EE", 2, 0m, 7000m);
        CheckIsee("anno non disponibile", "", "EE", 2, 4000m, 4000m);

        if (failures.Count > 0)
            throw new InvalidOperationException($"Falliti {failures.Count}/{checks} scenari coefficienti:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: CFN legacy e ISEE estero. Nessuna connessione DB aperta.");

        void CheckCfn(string name, decimal? merit, decimal? minimum, decimal? maximum, decimal bonus, decimal? expected)
        {
            Check(name, (_, info) =>
            {
                var iscr = info.InformazioniIscrizione;
                iscr.NumeroCrediti = merit;
                iscr.CreditiUtilizzati = bonus;
                iscr.CreditiMinimiRichiestiMerito = minimum;
                iscr.CoefficienteCongiunto.MeritoMassimoConseguibile = maximum;
            }, row =>
            {
                Equal(row, "MeritoConseguito_Calcolato", merit);
                Equal(row, "CFN_Calcolato", expected);
                // Con media 24 e ISEE uguale al limite, resta 0,70 * CFN + 0,02.
                Equal(row, "CoefficienteCongiunto_Calcolato", expected.HasValue ? 0.70m * expected.Value + 0.02m : null);
            });
        }

        void CheckIsee(string name, string aa, string provincia, int? tipoComune, decimal? isee, decimal? expected)
        {
            Check(name, (context, info) =>
            {
                context.AnnoAccademico = aa;
                info.InformazioniSede.Residenza.provincia = provincia;
                info.InformazioniSede.Residenza.TipoComune = tipoComune;
                info.InformazioniEconomiche.Calcolate.ISEEDSU = isee;
            }, row =>
            {
                Equal(row, "ISEEDSU_Coefficiente", expected);
                Equal(row, "ISEEN_Calcolato", expected.HasValue
                    ? decimal.Round(1m - expected.Value / 28000m, 6, MidpointRounding.AwayFromZero)
                    : null);
            });
        }

        void Check(string name, Action<VerificaPipelineContext, StudenteInfo> configure, Action<DataRow> verify)
        {
            checks++;
            try
            {
                using var connection = new SqlConnection();
                var context = new VerificaPipelineContext(connection) { AnnoAccademico = "20252026" };
                context.CalcParams.SogliaIsee = 28000m;
                var key = new StudentKey("TEST-CFN", "1");
                var info = context.GetOrCreateStudent(key);
                var iscr = info.InformazioniIscrizione;
                iscr.NumeroCrediti = 50m;
                iscr.NumeroEsami = 10;
                iscr.SommaVoti = 240m;
                iscr.CreditiMinimiRichiestiMerito = 25m;
                iscr.CoefficienteCongiunto.MeritoMinimoPrevisto = 25m;
                iscr.CoefficienteCongiunto.MeritoMassimoConseguibile = 60m;
                iscr.CoefficienteCongiunto.SistemaValutazione = "C";
                info.InformazioniSede.Residenza.provincia = "RM";
                info.InformazioniEconomiche.Calcolate.ISEEDSU = 28000m;
                context.GetOrCreateEsitiConcorsoByBenefit(key)["BS"] = new EsitoConcorsoBenefitRaw { CodTipoEsito = 1 };
                configure(context, info);
                var table = (DataTable)typeof(Verifica).GetMethod("BuildOutputTable", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
                var row = table.NewRow();
                typeof(Verifica).GetMethod("FillCoefficienteCongiuntoColumns", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { row, context, key, info });
                verify(row);
                if (connection.State != ConnectionState.Closed)
                    throw new InvalidOperationException("Il test ha aperto la connessione DB.");
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.GetBaseException().Message}");
            }
        }
    }

    private static void Equal(DataRow row, string column, decimal? expected)
    {
        object actual = row[column];
        object expectedValue = expected.HasValue ? expected.Value : DBNull.Value;
        if (!Equals(actual, expectedValue))
            throw new InvalidOperationException($"{column}: atteso {expectedValue}, ottenuto {actual}");
    }
}
