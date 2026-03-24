using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaControlliDatiEconomici
    {
        private void AddDatiEconomiciItaliani_CI(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    ISNULL(cte.ISR,0) AS ISR,
    ISNULL(cte.ISP,0) AS ISP,
    ISNULL(cte.Scala_equivalenza,0) AS SEQU,
    ISNULL(cte.numero_componenti_attestazione,0) AS NumCompAtt,

    ISNULL(cte.Redd_fratelli_50,0) AS Redd_fratelli_50,
    ISNULL(cte.Patr_fratelli_50,0) AS Patr_fratelli_50,
    ISNULL(cte.Patr_frat_50_est,0) AS Patr_frat_50_est,
    ISNULL(cte.Redd_frat_50_est,0) AS Redd_frat_50_est,
    ISNULL(cte.Patr_fam_50_est,0) AS Patr_fam_50_est,
    ISNULL(cte.Metri_quadri,0) AS Metri_quadri,
    ISNULL(cte.Redd_fam_50_est,0) AS Redd_fam_50_est
FROM #TargetsEconomici t
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'CI';";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                string numDomanda = reader.SafeGetString("Num_domanda");
                if (!TryGetEconomicRow(codFiscale, numDomanda, out var economicRow)) continue;

                decimal isr = reader.SafeGetDecimal("ISR");
                decimal isp = reader.SafeGetDecimal("ISP");
                decimal seqCert = reader.SafeGetDecimal("SEQU");
                int numComponentiAtt = reader.SafeGetInt("NumCompAtt");

                decimal reddFr50 = reader.SafeGetDecimal("Redd_fratelli_50");
                decimal patrFr50 = reader.SafeGetDecimal("Patr_fratelli_50");
                decimal patrFr50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                decimal reddFr50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                decimal patrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                decimal metri = reader.SafeGetDecimal("Metri_quadri");
                decimal reddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");

                economicRow.SEQ_Integrazione = seqCert;
                economicRow.NumeroComponentiIntegrazione = numComponentiAtt;

                // Formule stored (integrazione IT): termine patrimoniale con “-” nella stored
                economicRow.ISRDSU += isr - reddFr50 + reddFr50Est + reddFam50Est
                                    - (patrFr50Est - patrFr50 + patrFam50Est) * _calc.RendPatr;

                economicRow.ISPDSU += isp + metri * 500m;
            }
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - EE integrazione (DI)
        // =========================

        private void AddDatiEconomiciStranieri_DI(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    nf.Numero_componenti,
    ISNULL(nf.Redd_complessivo,0) AS Redd_complessivo,
    ISNULL(nf.Patr_mobiliare,0) AS Patr_mobiliare,
    ISNULL(nf.Superf_abitaz_MQ,0) AS Superf_abitaz_MQ,
    ISNULL(nf.Sup_compl_altre_MQ,0) AS Sup_compl_altre_MQ,
    ISNULL(nf.Sup_compl_MQ,0) AS Sup_compl_MQ,
    ISNULL(nf.Redd_lordo_fratell,0) AS Redd_lordo_fratell,
    ISNULL(nf.Patr_mob_fratell,0) AS Patr_mob_fratell
FROM #TargetsEconomici t
INNER JOIN vNucleo_fam_stranieri_DI nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                string numDomanda = reader.SafeGetString("Num_domanda");
                if (!TryGetEconomicRow(codFiscale, numDomanda, out var economicRow)) continue;

                int nComp = reader.SafeGetInt("Numero_componenti");
                decimal redd = reader.SafeGetDecimal("Redd_complessivo");
                decimal patrMob = reader.SafeGetDecimal("Patr_mobiliare");
                decimal superfAb = reader.SafeGetDecimal("Superf_abitaz_MQ");
                decimal supAltre = reader.SafeGetDecimal("Sup_compl_altre_MQ");
                decimal supCompl = reader.SafeGetDecimal("Sup_compl_MQ");
                decimal reddFr = reader.SafeGetDecimal("Redd_lordo_fratell");
                decimal patrFr = reader.SafeGetDecimal("Patr_mob_fratell");

                economicRow.NumeroComponentiIntegrazione = nComp;
                economicRow.SEQ_Integrazione = ScalaMin(nComp);

                decimal patrAdj = Math.Max(patrMob + patrFr * 0.5m - _calc.FranchigiaPatMob, 0m);

                economicRow.ISRDSU += redd + reddFr * 0.5m + patrAdj * _calc.RendPatr;

                decimal ispAdd = Math.Max((superfAb + supAltre + supCompl * 0.5m) * 500m - _calc.Franchigia, 0m) + patrAdj;
                economicRow.ISPDSU += ispAdd;
            }
        }

        // =========================
        //  CALCOLO ISE*
        // =========================

    }
}
