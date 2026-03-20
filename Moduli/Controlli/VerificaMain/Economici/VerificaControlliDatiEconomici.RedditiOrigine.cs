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
        private void AddDatiEconomiciItaliani_CO(string aa, List<string> codiciFiscali)
        {
            EnsureTempCfTableAndFill(codiciFiscali);

            int eseFin = GetEseFinanziario(aa);
            string filtroPagam = GetFiltroCodTipoPagam(aa);

            string sql = $@"
WITH sumPagamenti AS (
    SELECT SUM(p.imp_pagato) AS somma, d.Cod_fiscale
    FROM Pagamenti p
    INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
    WHERE p.Ritirato_azienda = 0
      AND p.Ese_finanziario = @EseFin
      AND {filtroPagam}
    GROUP BY d.Cod_fiscale
),
impAltreBorse AS (
    SELECT vb.num_domanda, vb.anno_accademico, SUM(vb.importo_borsa) AS importo_borsa
    FROM vimporti_borsa_percepiti vb
    INNER JOIN Allegati a ON vb.anno_accademico = a.anno_accademico AND vb.num_domanda = a.num_domanda
    INNER JOIN vstatus_allegati vs ON a.id_allegato = vs.id_allegato
    WHERE vb.data_fine_validita IS NULL
      AND a.data_fine_validita IS NULL
      AND a.cod_tipo_allegato = '07'
      AND vs.cod_status IN ('03','05')
      AND vb.anno_accademico = @AA
    GROUP BY vb.num_domanda, vb.anno_accademico
)
SELECT
    t.Cod_fiscale,
    ISNULL(sp.somma, 0) AS detrazioniADISU,
    ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse,

    ISNULL(cte.Somma_redditi,0) AS Somma_redditi,
    ISNULL(cte.ISR,0) AS ISR,
    ISNULL(cte.ISP,0) AS ISP,
    ISNULL(cte.Scala_equivalenza,0) AS SEQU,

    ISNULL(cte.Redd_fratelli_50,0) AS Redd_fratelli_50,
    ISNULL(cte.Patr_fratelli_50,0) AS Patr_fratelli_50,
    ISNULL(cte.Patr_frat_50_est,0) AS Patr_frat_50_est,
    ISNULL(cte.Redd_frat_50_est,0) AS Redd_frat_50_est,
    ISNULL(cte.Patr_fam_50_est,0) AS Patr_fam_50_est,
    ISNULL(cte.Metri_quadri,0) AS Metri_quadri,
    ISNULL(cte.Redd_fam_50_est,0) AS Redd_fam_50_est,
    ISNULL(cte.patr_imm_50_frat_sor,0) AS patr_imm_50_frat_sor
FROM #TargetsEconomici t
INNER JOIN #CFEstrazione cfe ON t.Cod_fiscale = cfe.Cod_fiscale
INNER JOIN Domanda d ON d.Anno_accademico = @AA AND d.Num_domanda = t.Num_domanda
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'CO'
LEFT JOIN sumPagamenti sp ON t.Cod_fiscale = sp.Cod_fiscale
LEFT JOIN impAltreBorse iab ON t.Num_domanda = iab.num_domanda AND @AA = iab.anno_accademico;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);
            command.Parameters.AddWithValue("@EseFin", eseFin);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                decimal isr = reader.SafeGetDecimal("ISR");
                decimal isp = reader.SafeGetDecimal("ISP");
                decimal seqCert = reader.SafeGetDecimal("SEQU");

                decimal reddFr50 = reader.SafeGetDecimal("Redd_fratelli_50");
                decimal patrFr50 = reader.SafeGetDecimal("Patr_fratelli_50");
                decimal patrFr50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                decimal reddFr50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                decimal patrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                decimal metri = reader.SafeGetDecimal("Metri_quadri");
                decimal reddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");
                decimal patrImm50FratSor = reader.SafeGetDecimal("patr_imm_50_frat_sor");

                decimal detrazioni = reader.SafeGetDecimal("detrazioniADISU") + reader.SafeGetDecimal("detrazioniAltreBorse");

                economicRow.SEQ_Origine = seqCert;
                economicRow.SommaRedditiStud = reader.SafeGetDecimal("Somma_redditi");

                economicRow.ISRDSU = isr - reddFr50 + reddFr50Est + reddFam50Est + economicRow.AltriMezzi
                                    + (patrFr50Est - patrFr50 + patrFam50Est) * _calc.RendPatr;

                economicRow.ISPDSU = isp - patrImm50FratSor + metri * 500m;

                economicRow.Detrazioni = detrazioni;
            }
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - EE (DO)
        // =========================

        private void AddDatiEconomiciStranieri_DO(string aa, List<string> codiciFiscali)
        {
            EnsureTempCfTableAndFill(codiciFiscali);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    nf.Numero_componenti,
    ISNULL(nf.Redd_complessivo,0) AS Redd_complessivo,
    ISNULL(nf.Patr_mobiliare,0) AS Patr_mobiliare,
    ISNULL(nf.Superf_abitaz_MQ,0) AS Superf_abitaz_MQ,
    ISNULL(nf.Sup_compl_altre_MQ,0) AS Sup_compl_altre_MQ,
    ISNULL(nf.Sup_compl_MQ,0) AS Sup_compl_MQ,
    ISNULL(nf.Redd_lordo_fratell,0) AS Redd_lordo_fratell,
    ISNULL(nf.Patr_mob_fratell,0) AS Patr_mob_fratell
FROM #TargetsEconomici t
INNER JOIN #CFEstrazione cfe ON t.Cod_fiscale = cfe.Cod_fiscale
INNER JOIN vNucleo_fam_stranieri_DO nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                int nComp = reader.SafeGetInt("Numero_componenti");
                decimal redd = reader.SafeGetDecimal("Redd_complessivo");
                decimal patrMob = reader.SafeGetDecimal("Patr_mobiliare");
                decimal superfAb = reader.SafeGetDecimal("Superf_abitaz_MQ");
                decimal supAltre = reader.SafeGetDecimal("Sup_compl_altre_MQ");
                decimal supCompl = reader.SafeGetDecimal("Sup_compl_MQ");
                decimal reddFr = reader.SafeGetDecimal("Redd_lordo_fratell");
                decimal patrFr = reader.SafeGetDecimal("Patr_mob_fratell");

                decimal patrAdj = Math.Max(patrMob + patrFr * 0.5m - _calc.FranchigiaPatMob, 0m);

                economicRow.ISRDSU = redd + reddFr * 0.5m + patrAdj * _calc.RendPatr + economicRow.AltriMezzi;

                decimal isp = Math.Max((superfAb + supAltre + supCompl * 0.5m) * 500m - _calc.Franchigia, 0m);
                economicRow.ISPDSU = isp + patrAdj;

                economicRow.SEQ_Origine = ScalaMin(nComp);
            }
        }

        private void AddDatiEconomiciItaliani_DOFromCert(string aa, List<string> codiciFiscali)
        {
            EnsureTempCfTableAndFill(codiciFiscali);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    ISNULL(cte.Somma_redditi,0) AS Somma_redditi,
    ISNULL(cte.ISR,0) AS ISR,
    ISNULL(cte.ISP,0) AS ISP,
    ISNULL(cte.Scala_equivalenza,0) AS SEQU,

    ISNULL(cte.Redd_fratelli_50,0) AS Redd_fratelli_50,
    ISNULL(cte.Patr_fratelli_50,0) AS Patr_fratelli_50,
    ISNULL(cte.Patr_frat_50_est,0) AS Patr_frat_50_est,
    ISNULL(cte.Redd_frat_50_est,0) AS Redd_frat_50_est,
    ISNULL(cte.Patr_fam_50_est,0) AS Patr_fam_50_est,
    ISNULL(cte.Metri_quadri,0) AS Metri_quadri,
    ISNULL(cte.Redd_fam_50_est,0) AS Redd_fam_50_est,
    ISNULL(cte.patr_imm_50_frat_sor,0) AS patr_imm_50_frat_sor
FROM #TargetsEconomici t
INNER JOIN #CFEstrazione cfe ON t.Cod_fiscale = cfe.Cod_fiscale
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'DO';";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                decimal isr = reader.SafeGetDecimal("ISR");
                decimal isp = reader.SafeGetDecimal("ISP");
                decimal seqCert = reader.SafeGetDecimal("SEQU");

                decimal reddFr50 = reader.SafeGetDecimal("Redd_fratelli_50");
                decimal patrFr50 = reader.SafeGetDecimal("Patr_fratelli_50");
                decimal patrFr50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                decimal reddFr50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                decimal patrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                decimal metri = reader.SafeGetDecimal("Metri_quadri");
                decimal reddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");
                decimal patrImm50FratSor = reader.SafeGetDecimal("patr_imm_50_frat_sor");

                economicRow.SEQ_Origine = seqCert;
                economicRow.SommaRedditiStud = reader.SafeGetDecimal("Somma_redditi");

                economicRow.ISRDSU = isr - reddFr50 + reddFr50Est + reddFam50Est + economicRow.AltriMezzi
                                    + (patrFr50Est - patrFr50 + patrFam50Est) * _calc.RendPatr;

                economicRow.ISPDSU = isp - patrImm50FratSor + metri * 500m;
            }
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - IT integrazione (CI)
        // =========================

    }
}
