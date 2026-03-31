using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        internal void RaccogliEconomiciDaContesto(string aa, IReadOnlyDictionary<StudentKey, StudenteInfo> students)
        {
            void Log(int pct, string msg) => Logger.LogInfo(Math.Max(0, Math.Min(100, pct)), msg);

            Log(0, "Avvio raccolta dati economici centralizzata");

            aa = (aa ?? "").Trim();
            if (string.IsNullOrWhiteSpace(aa) || aa.Length != 8)
                throw new ArgumentException("Anno accademico non valido (atteso char(8), es: 20232024).");

            ResetState(aa);
            InitializeStudentsFromContext(students);
            Log(10, $"Studenti inizializzati dal contesto: {_targets.Count}");

            ExecuteEconomiciCollectionPipeline(aa, Log);
        }

        private void ExecuteEconomiciCollectionPipeline(string aa, Action<int, string> log)
        {
            _targets.Clear();
            _targets.AddRange(DistinctTargets(_studentsByKey.Keys.Select(key => new Target(key.CodFiscale, key.NumDomanda))));

            log(18, "Caricamento valori attuali da vValori_calcolati.");
            LoadValoriCalcolatiAttuali(aa, _targets);

            LoadCalcParams(aa);
            LoadNucleoFamiliare(aa, _targets);

            log(19, "Caricamento esito concorso BS (ultimo record valido).");
            LoadEsitoBorsaStudio(aa, _targets);

            log(22, "Caricamento INPS e attestazioni CO.");
            LoadInpsAndAttestazioni_StoredLike(aa, _targets);

            log(30, "Lettura tipologie reddito e split per studente/domanda.");
            var split = LoadTipologieRedditiAndSplit(aa, _targets);

            log(40, "Estrazione dati economici origine.");
            if (split.OrigIT_CO.Count > 0) AddDatiEconomiciItaliani_CO(aa, split.OrigIT_CO);
            if (split.OrigIT_DO.Count > 0) AddDatiEconomiciItaliani_DOFromCert(aa, split.OrigIT_DO);
            if (split.OrigEE.Count > 0) AddDatiEconomiciStranieri_DO(aa, split.OrigEE);

            log(60, "Estrazione dati economici integrazione.");
            if (split.IntIT_CI.Count > 0) AddDatiEconomiciItaliani_CI(aa, split.IntIT_CI);
            if (split.IntDI.Count > 0) AddDatiEconomiciStranieri_DI(aa, split.IntDI);

            log(70, $"Raccolta dati economici completata. Righe in memoria: {_studentsByKey.Count}, studenti nel contesto: {_studentsByKey.Count}");
        }

        private void LoadInpsAndAttestazioni_StoredLike(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);


            const string sqlOrig = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    si.status_inps
FROM #TargetsEconomici t
LEFT JOIN vStatus_INPS si
    ON si.anno_accademico = @AA
   AND si.cod_fiscale     = t.Cod_fiscale
   AND si.num_domanda     = t.Num_domanda
   AND si.data_fine_validita IS NULL
   AND si.tipo_certificaz NOT IN ('CI','DI');";

            using (var command = new SqlCommand(sqlOrig, _conn))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                    int statusInps = reader.SafeGetInt("status_inps");
                    GetEconomicInfo(CreateStudentKey(codFiscale, reader.SafeGetString("Num_domanda"))).StatusInpsOrigine = statusInps;
                }
            }

            const string sqlInt = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    si.status_inps
FROM #TargetsEconomici t
LEFT JOIN vStatus_INPS si
    ON si.anno_accademico = @AA
   AND si.cod_fiscale     = t.Cod_fiscale
   AND si.num_domanda     = t.Num_domanda
   AND si.data_fine_validita IS NULL
   AND si.tipo_certificaz IN ('CI','DI');";

            using (var command = new SqlCommand(sqlInt, _conn))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                    int statusInps = reader.SafeGetInt("status_inps");
                    GetEconomicInfo(CreateStudentKey(codFiscale, reader.SafeGetString("Num_domanda"))).StatusInpsIntegrazione = statusInps;
                }
            }

            const string sqlAtt = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    LTRIM(RTRIM(ISNULL(cte.Cod_tipo_attestazione,''))) AS Cod_tipo_attestazione
FROM #TargetsEconomici t
LEFT JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'CO';";

            using (var command = new SqlCommand(sqlAtt, _conn))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                    string tipoAttestazione = reader.SafeGetString("Cod_tipo_attestazione");
                    GetEconomicInfo(CreateStudentKey(codFiscale, reader.SafeGetString("Num_domanda"))).CoAttestazioneOk = !string.IsNullOrWhiteSpace(tipoAttestazione);
                }
            }
        }

        private void LoadCalcParams(string aa)
        {
            const string sql = @"
SELECT Franchigia, tasso_rendimento_pat_mobiliare, franchigia_pat_mobiliare, Importo_borsa_A, Importo_borsa_B, Importo_borsa_C, Soglia_Isee
FROM DatiGenerali_con
WHERE Anno_accademico = @AA;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                _calc.Franchigia = reader.SafeGetDecimal("Franchigia");
                _calc.RendPatr = reader.SafeGetDecimal("tasso_rendimento_pat_mobiliare");
                _calc.FranchigiaPatMob = reader.SafeGetDecimal("franchigia_pat_mobiliare");
                _calc.ImportoBorsaA = reader.SafeGetDecimal("Importo_borsa_A");
                _calc.ImportoBorsaB = reader.SafeGetDecimal("Importo_borsa_B");
                _calc.ImportoBorsaC = reader.SafeGetDecimal("Importo_borsa_C");
                _calc.SogliaIsee = reader.SafeGetDecimal("Soglia_Isee");
            }
        }

        private void LoadNucleoFamiliare(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    ISNULL(nf.Num_componenti, 0) AS Num_componenti,
    ISNULL(nf.Cod_tipologia_nucleo, '') AS Cod_tipologia_nucleo,
    ISNULL(nf.Numero_conviventi_estero, 0) AS Numero_conviventi_estero
FROM #TargetsEconomici t
INNER JOIN vNucleo_familiare nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                raw.NumeroComponenti = reader.SafeGetInt("Num_componenti");
                raw.TipoNucleo = reader.SafeGetString("Cod_tipologia_nucleo");
                raw.NumeroConviventiEstero = reader.SafeGetInt("Numero_conviventi_estero");
            }
        }

        private void EnsureTempTargetsTableAndFill(List<Target> targets)
        {
            var list = DistinctTargets(targets);

            const string ensureSql = @"
IF OBJECT_ID('tempdb..#TargetsEconomici') IS NOT NULL
BEGIN
    TRUNCATE TABLE #TargetsEconomici;
END
ELSE
BEGIN
    CREATE TABLE #TargetsEconomici
    (
        Cod_fiscale VARCHAR(16) NOT NULL,
        Num_domanda VARCHAR(20) NOT NULL
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM tempdb.sys.indexes
    WHERE name = 'ix_TargetsEconomici_CF_ND'
      AND object_id = OBJECT_ID('tempdb..#TargetsEconomici')
)
BEGIN
    CREATE INDEX ix_TargetsEconomici_CF_ND ON #TargetsEconomici (Cod_fiscale, Num_domanda);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM tempdb.sys.indexes
    WHERE name = 'ix_TargetsEconomici_ND'
      AND object_id = OBJECT_ID('tempdb..#TargetsEconomici')
)
BEGIN
    CREATE INDEX ix_TargetsEconomici_ND ON #TargetsEconomici (Num_domanda);
END;";

            using (var command = new SqlCommand(ensureSql, _conn))
                command.ExecuteNonQuery();

            if (list.Count == 0)
            {
                using var statsCommand = new SqlCommand("UPDATE STATISTICS #TargetsEconomici;", _conn);
                statsCommand.ExecuteNonQuery();
                return;
            }

            using (var dataTable = new DataTable())
            {
                dataTable.Columns.Add("Cod_fiscale", typeof(string));
                dataTable.Columns.Add("Num_domanda", typeof(string));

                foreach (var target in list)
                    dataTable.Rows.Add(target.CodFiscale, target.NumDomanda);

                using var bulkCopy = new SqlBulkCopy(_conn, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = TempTargetsTable,
                    BatchSize = 10000,
                    BulkCopyTimeout = 600
                };
                bulkCopy.WriteToServer(dataTable);
            }

            using (var statsCommand = new SqlCommand("UPDATE STATISTICS #TargetsEconomici;", _conn))
                statsCommand.ExecuteNonQuery();
        }

        private readonly record struct Target(string CodFiscale, string NumDomanda);

        private void AddDatiEconomiciItaliani_CO(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            int eseFin = EconomiciFormulaSupport.GetEseFinanziario(aa);
            string filtroPagam = EconomiciFormulaSupport.GetFiltroCodTipoPagam(aa);

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
    t.Num_domanda,
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
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                raw.OrigineFonte = "CO";
                raw.DetrazioniAdisu = reader.SafeGetDecimal("detrazioniADISU");
                raw.DetrazioniAltreBorse = reader.SafeGetDecimal("detrazioniAltreBorse");
                raw.OrigineSommaRedditi = reader.SafeGetDecimal("Somma_redditi");
                raw.OrigineISR = reader.SafeGetDecimal("ISR");
                raw.OrigineISP = reader.SafeGetDecimal("ISP");
                raw.OrigineScalaEquivalenza = reader.SafeGetDecimal("SEQU");
                raw.OrigineReddFratelli50 = reader.SafeGetDecimal("Redd_fratelli_50");
                raw.OriginePatrFratelli50 = reader.SafeGetDecimal("Patr_fratelli_50");
                raw.OriginePatrFrat50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                raw.OrigineReddFrat50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                raw.OriginePatrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                raw.OrigineMetriQuadri = reader.SafeGetDecimal("Metri_quadri");
                raw.OrigineReddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");
                raw.OriginePatrImm50FratSor = reader.SafeGetDecimal("patr_imm_50_frat_sor");
            }
        }

        private void AddDatiEconomiciStranieri_DO(string aa, List<Target> targets)
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
INNER JOIN vNucleo_fam_stranieri_DO nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                raw.OrigineFonte = "EE";
                raw.OrigineNumeroComponenti = reader.SafeGetInt("Numero_componenti");
                raw.OrigineRedditoComplessivo = reader.SafeGetDecimal("Redd_complessivo");
                raw.OriginePatrMobiliare = reader.SafeGetDecimal("Patr_mobiliare");
                raw.OrigineSuperfAbitazMq = reader.SafeGetDecimal("Superf_abitaz_MQ");
                raw.OrigineSupComplAltreMq = reader.SafeGetDecimal("Sup_compl_altre_MQ");
                raw.OrigineSupComplMq = reader.SafeGetDecimal("Sup_compl_MQ");
                raw.OrigineReddLordoFratell = reader.SafeGetDecimal("Redd_lordo_fratell");
                raw.OriginePatrMobFratell = reader.SafeGetDecimal("Patr_mob_fratell");
            }
        }

        private void AddDatiEconomiciItaliani_DOFromCert(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
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
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'DO';";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                raw.OrigineFonte = "DO";
                raw.OrigineSommaRedditi = reader.SafeGetDecimal("Somma_redditi");
                raw.OrigineISR = reader.SafeGetDecimal("ISR");
                raw.OrigineISP = reader.SafeGetDecimal("ISP");
                raw.OrigineScalaEquivalenza = reader.SafeGetDecimal("SEQU");
                raw.OrigineReddFratelli50 = reader.SafeGetDecimal("Redd_fratelli_50");
                raw.OriginePatrFratelli50 = reader.SafeGetDecimal("Patr_fratelli_50");
                raw.OriginePatrFrat50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                raw.OrigineReddFrat50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                raw.OriginePatrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                raw.OrigineMetriQuadri = reader.SafeGetDecimal("Metri_quadri");
                raw.OrigineReddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");
                raw.OriginePatrImm50FratSor = reader.SafeGetDecimal("patr_imm_50_frat_sor");
            }
        }

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
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                raw.IntegrazioneFonte = "CI";
                raw.IntegrazioneISR = reader.SafeGetDecimal("ISR");
                raw.IntegrazioneISP = reader.SafeGetDecimal("ISP");
                raw.IntegrazioneScalaEquivalenza = reader.SafeGetDecimal("SEQU");
                raw.IntegrazioneNumeroComponenti = reader.SafeGetInt("NumCompAtt");
                raw.IntegrazioneReddFratelli50 = reader.SafeGetDecimal("Redd_fratelli_50");
                raw.IntegrazionePatrFratelli50 = reader.SafeGetDecimal("Patr_fratelli_50");
                raw.IntegrazionePatrFrat50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                raw.IntegrazioneReddFrat50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                raw.IntegrazionePatrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                raw.IntegrazioneMetriQuadri = reader.SafeGetDecimal("Metri_quadri");
                raw.IntegrazioneReddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");
            }
        }

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
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                raw.IntegrazioneFonte = "DI";
                raw.IntegrazioneNumeroComponenti = reader.SafeGetInt("Numero_componenti");
                raw.IntegrazioneRedditoComplessivo = reader.SafeGetDecimal("Redd_complessivo");
                raw.IntegrazionePatrMobiliare = reader.SafeGetDecimal("Patr_mobiliare");
                raw.IntegrazioneSuperfAbitazMq = reader.SafeGetDecimal("Superf_abitaz_MQ");
                raw.IntegrazioneSupComplAltreMq = reader.SafeGetDecimal("Sup_compl_altre_MQ");
                raw.IntegrazioneSupComplMq = reader.SafeGetDecimal("Sup_compl_MQ");
                raw.IntegrazioneReddLordoFratell = reader.SafeGetDecimal("Redd_lordo_fratell");
                raw.IntegrazionePatrMobFratell = reader.SafeGetDecimal("Patr_mob_fratell");
            }
        }

        private sealed class SplitResult
        {
            public List<Target> OrigIT_CO { get; } = new();
            public List<Target> OrigIT_DO { get; } = new();
            public List<Target> OrigEE { get; } = new();
            public List<Target> IntIT_CI { get; } = new();
            public List<Target> IntDI { get; } = new();
        }

        private SplitResult LoadTipologieRedditiAndSplit(string aa, List<Target> targets)
        {
            Logger.LogInfo(30, "Esecuzione query tipologie reddito (vTipologie_redditi) + split per studente/domanda.");

            EnsureTempTargetsTableAndFill(targets);
            var result = new SplitResult();

            const string sql = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    tr.Tipo_redd_nucleo_fam_origine,
    tr.Tipo_redd_nucleo_fam_integr,
    ISNULL(tr.altri_mezzi,0) AS altri_mezzi
FROM #TargetsEconomici t
INNER JOIN vTipologie_redditi tr
    ON tr.Anno_accademico = @AA
   AND tr.Num_domanda     = t.Num_domanda
ORDER BY t.Cod_fiscale, t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            int readCount = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readCount++;
                if (!TryGetStudentInfo(reader, out var info))
                    continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                string codFiscale = NormalizeCf(reader.SafeGetString("Cod_fiscale"));
                string numDomanda = NormalizeDomanda(reader.SafeGetString("Num_domanda"));
                var target = new Target(codFiscale, numDomanda);
                var key = CreateStudentKey(codFiscale, numDomanda);
                string tipoOrigine = Utilities.RemoveAllSpaces(reader.SafeGetString("Tipo_redd_nucleo_fam_origine"));
                string tipoIntegrazione = Utilities.RemoveAllSpaces(reader.SafeGetString("Tipo_redd_nucleo_fam_integr"));

                raw.TipoRedditoOrigine = tipoOrigine;
                raw.TipoRedditoIntegrazione = tipoIntegrazione;
                raw.AltriMezzi = reader.SafeGetDecimal("altri_mezzi");


                if (tipoOrigine.Equals("it", StringComparison.OrdinalIgnoreCase))
                {
                    int statusInps = raw.StatusInpsOrigine;
                    bool coOk = statusInps == 2 && raw.CoAttestazioneOk;
                    if (coOk) result.OrigIT_CO.Add(target);
                    else result.OrigIT_DO.Add(target);
                }
                else if (tipoOrigine.Equals("ee", StringComparison.OrdinalIgnoreCase))
                {
                    result.OrigEE.Add(target);
                }

                bool doIntegrazione = string.Equals(raw.TipoNucleo, "I", StringComparison.OrdinalIgnoreCase)
                                      && !string.IsNullOrWhiteSpace(tipoIntegrazione);
                if (!doIntegrazione)
                    continue;

                if (tipoIntegrazione.Equals("it", StringComparison.OrdinalIgnoreCase))
                {
                    int statusInpsI = raw.StatusInpsIntegrazione;
                    if (statusInpsI == 2) result.IntIT_CI.Add(target);
                    else result.IntDI.Add(target);
                }
                else if (tipoIntegrazione.Equals("ee", StringComparison.OrdinalIgnoreCase))
                {
                    result.IntDI.Add(target);
                }
            }

            Logger.LogInfo(33, $"Tipologie reddito lette: {readCount}");
            return result;
        }

        private void LoadValoriCalcolatiAttuali(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    vv.ISPEDSU,
    vv.ISEDSU,
    vv.SEQ,
    vv.ISPDSU,
    vv.ISEEDSU
FROM #TargetsEconomici t
LEFT JOIN vValori_calcolati vv
    ON vv.Anno_accademico = @AA
   AND vv.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                attuali.ISPEDSU = reader.SafeGetDouble("ISPEDSU");
                attuali.ISEDSU = reader.SafeGetDouble("ISEDSU");
                attuali.SEQ = reader.SafeGetDouble("SEQ");
                attuali.ISPDSU = reader.SafeGetDouble("ISPDSU");
                attuali.ISEEDSU = reader.SafeGetDouble("ISEEDSU");
            }
        }

        private void LoadEsitoBorsaStudio(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
WITH EsitoBS AS
(
    SELECT
        ec.Anno_accademico,
        ec.Num_domanda,
        CAST(ec.Cod_tipo_esito AS INT) AS Cod_tipo_esito,
        ec.imp_beneficio AS imp_assegnato,
        ROW_NUMBER() OVER
        (
            PARTITION BY ec.Anno_accademico, ec.Num_domanda
            ORDER BY ec.Data_validita DESC, ec.Cod_tipo_esito DESC
        ) AS rn
    FROM vEsiti_concorsi ec
    WHERE ec.Anno_accademico = @AA
      AND ec.Cod_beneficio = 'BS'
)
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    e.Cod_tipo_esito,
    e.imp_assegnato
FROM #TargetsEconomici t
LEFT JOIN EsitoBS e
    ON e.Anno_accademico = @AA
   AND e.Num_domanda     = t.Num_domanda
   AND e.rn = 1;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;
                var attuali = eco.Attuali;

                object rawEsito = reader["Cod_tipo_esito"];
                int? codTipoEsito = rawEsito is DBNull or null ? (int?)null : Convert.ToInt32(rawEsito, CultureInfo.InvariantCulture);
                raw.CodTipoEsitoBS = codTipoEsito;
                raw.ImportoAssegnato = Utilities.SafeGetDouble(reader, "imp_assegnato");
            }
        }
    }
}
