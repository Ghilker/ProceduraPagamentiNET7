using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        internal void RaccogliEconomiciDaContesto(VerificaPipelineContext context)
        {
            void Log(int pct, string msg) => Logger.LogInfo(Math.Max(0, Math.Min(100, pct)), msg);

            if (context == null)
                throw new ArgumentNullException(nameof(context));

            Log(0, "Avvio raccolta dati economici centralizzata");

            string aa = (context.AnnoAccademico ?? "").Trim();
            if (string.IsNullOrWhiteSpace(aa) || aa.Length != 8)
                throw new ArgumentException("Anno accademico non valido (atteso char(8), es: 20242025).");

            _currentContext = context;
            ResetComuniEquiparatiState();
            Log(10, $"Studenti nel contesto: {CurrentStudents.Count}");

            ExecuteEconomiciCollectionPipeline(aa, context.TempPipelineTable, Log);
        }

        private void ExecuteEconomiciCollectionPipeline(string aa, string pipelineTableName, Action<int, string> log)
        {
            log(18, "Caricamento valori attuali da vValori_calcolati.");
            LoadValoriCalcolatiAttuali(aa, pipelineTableName);

            LoadCalcParams(aa);
            LoadNucleoFamiliare(aa, pipelineTableName);

            log(22, "Caricamento INPS e attestazioni CO.");
            LoadInpsAndAttestazioni_StoredLike(aa, pipelineTableName);

            log(30, "Lettura tipologie reddito e split per studente/domanda.");
            var split = LoadTipologieRedditiAndSplit(aa, pipelineTableName);
            ApplyEconomicSplitFlagsToPipelineTable(pipelineTableName, split);

            log(40, "Estrazione dati economici origine.");
            if (split.OrigIT_CO.Count > 0) AddDatiEconomiciItaliani_CO(aa, pipelineTableName);
            if (split.OrigIT_DO.Count > 0) AddDatiEconomiciItaliani_DOFromCert(aa, pipelineTableName);
            if (split.OrigEE.Count > 0) AddDatiEconomiciStranieri_DO(aa, pipelineTableName);

            log(60, "Estrazione dati economici integrazione.");
            if (split.IntIT_CI.Count > 0) AddDatiEconomiciItaliani_CI(aa, pipelineTableName);
            if (split.IntDI.Count > 0) AddDatiEconomiciStranieri_DI(aa, pipelineTableName);

            log(70, $"Raccolta dati economici completata. Studenti nel contesto: {CurrentStudents.Count}");
        }

        private void LoadInpsAndAttestazioni_StoredLike(string aa, string sourceTableName)
        {
            sourceTableName = ResolveTempTableName(sourceTableName);

            string sql = $@"
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
    ISNULL(sio.StatusInpsOrigine, 0) AS StatusInpsOrigine,
    ISNULL(sii.StatusInpsIntegrazione, 0) AS StatusInpsIntegrazione,
    ISNULL(att.CoAttestazioneOk, 0) AS CoAttestazioneOk
FROM {sourceTableName} t
OUTER APPLY
(
    SELECT MAX(CAST(si.status_inps AS INT)) AS StatusInpsOrigine
    FROM vStatus_INPS si
    WHERE si.anno_accademico = @AA
      AND si.cod_fiscale = t.CodFiscale
      AND si.num_domanda = t.NumDomanda
      AND si.data_fine_validita IS NULL
      AND si.tipo_certificaz NOT IN ('CI','DI')
) sio
OUTER APPLY
(
    SELECT MAX(CAST(si.status_inps AS INT)) AS StatusInpsIntegrazione
    FROM vStatus_INPS si
    WHERE si.anno_accademico = @AA
      AND si.cod_fiscale = t.CodFiscale
      AND si.num_domanda = t.NumDomanda
      AND si.data_fine_validita IS NULL
      AND si.tipo_certificaz IN ('CI','DI')
) sii
OUTER APPLY
(
    SELECT MAX(CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(cte.Cod_tipo_attestazione,''))), '') IS NULL THEN 0 ELSE 1 END) AS CoAttestazioneOk
    FROM vCertificaz_ISEE cte
    WHERE cte.Anno_accademico = @AA
      AND cte.Num_domanda = t.NumDomanda
      AND cte.tipologia_certificazione = 'CO'
) att;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);
            ReadAndMergeSingleDto(
                command,
                reader => ReadStudentKey(reader),
                reader => new InpsAttestazioneDto
                {
                    StatusInpsOrigine = reader.SafeGetInt("StatusInpsOrigine"),
                    StatusInpsIntegrazione = reader.SafeGetInt("StatusInpsIntegrazione"),
                    CoAttestazioneOk = reader.SafeGetInt("CoAttestazioneOk") != 0
                },
                static (info, dto) =>
                {
                    var raw = info.InformazioniEconomiche.Raw;
                    raw.StatusInpsOrigine = dto.StatusInpsOrigine;
                    raw.StatusInpsIntegrazione = dto.StatusInpsIntegrazione;
                    raw.CoAttestazioneOk = dto.CoAttestazioneOk;
                });
        }

        private sealed class InpsAttestazioneDto
        {
            public int StatusInpsOrigine { get; set; }
            public int StatusInpsIntegrazione { get; set; }
            public bool CoAttestazioneOk { get; set; }
        }

        private void LoadCalcParams(string aa)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadCalcParams", $"AA={aa}");
            const string sql = @"
SELECT Franchigia, tasso_rendimento_pat_mobiliare, franchigia_pat_mobiliare, Importo_borsa_A, Importo_borsa_B, Importo_borsa_C, Soglia_Isee
FROM DatiGenerali_con
WHERE Anno_accademico = @AA;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);

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

        private void LoadNucleoFamiliare(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadNucleoFamiliare", $"AA={aa}");
            sourceTableName = ResolveTempTableName(sourceTableName);

            string sql = $@"
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
    ISNULL(nf.Num_componenti, 0) AS Num_componenti,
    ISNULL(nf.Cod_tipologia_nucleo, '') AS Cod_tipologia_nucleo,
    ISNULL(nf.Numero_conviventi_estero, 0) AS Numero_conviventi_estero
FROM {sourceTableName} t
INNER JOIN vNucleo_familiare nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.NumDomanda;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;

                raw.NumeroComponenti = reader.SafeGetInt("Num_componenti");
                raw.TipoNucleo = reader.SafeGetString("Cod_tipologia_nucleo");
                raw.NumeroConviventiEstero = reader.SafeGetInt("Numero_conviventi_estero");
            }
        }

        private static string ResolveTempTableName(string tableName)
        {
            string value = (tableName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Nome temp table non valido.", nameof(tableName));

            foreach (char ch in value)
            {
                bool ok = char.IsLetterOrDigit(ch) || ch == '#' || ch == '_';
                if (!ok)
                    throw new ArgumentException($"Nome temp table non valido: {tableName}", nameof(tableName));
            }

            return value;
        }

        private void ApplyEconomicSplitFlagsToPipelineTable(string pipelineTableName, SplitResult split)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.ApplyEconomicSplitFlags", $"students={CurrentStudents.Count}");
            pipelineTableName = ResolveTempTableName(pipelineTableName);

            string sql = $@"
;WITH Tipologie AS
(
    SELECT
        t.NumDomanda,
        t.CodFiscale,
        UPPER(LTRIM(RTRIM(ISNULL(tr.Tipo_redd_nucleo_fam_origine,'')))) AS TipoOrigine,
        UPPER(LTRIM(RTRIM(ISNULL(tr.Tipo_redd_nucleo_fam_integr,'')))) AS TipoIntegrazione,
        UPPER(LTRIM(RTRIM(ISNULL(nf.Cod_tipologia_nucleo,'')))) AS TipoNucleo,
        ISNULL(sio.StatusInpsOrigine, 0) AS StatusInpsOrigine,
        ISNULL(sii.StatusInpsIntegrazione, 0) AS StatusInpsIntegrazione,
        ISNULL(att.CoAttestazioneOk, 0) AS CoAttestazioneOk
    FROM {pipelineTableName} t
    LEFT JOIN vTipologie_redditi tr
        ON tr.Anno_accademico = @AA
       AND tr.Num_domanda = t.NumDomanda
    LEFT JOIN vNucleo_familiare nf
        ON nf.Anno_accademico = @AA
       AND nf.Num_domanda = t.NumDomanda
    OUTER APPLY
    (
        SELECT MAX(CAST(si.status_inps AS INT)) AS StatusInpsOrigine
        FROM vStatus_INPS si
        WHERE si.anno_accademico = @AA
          AND si.cod_fiscale = t.CodFiscale
          AND si.num_domanda = t.NumDomanda
          AND si.data_fine_validita IS NULL
          AND si.tipo_certificaz NOT IN ('CI','DI')
    ) sio
    OUTER APPLY
    (
        SELECT MAX(CAST(si.status_inps AS INT)) AS StatusInpsIntegrazione
        FROM vStatus_INPS si
        WHERE si.anno_accademico = @AA
          AND si.cod_fiscale = t.CodFiscale
          AND si.num_domanda = t.NumDomanda
          AND si.data_fine_validita IS NULL
          AND si.tipo_certificaz IN ('CI','DI')
    ) sii
    OUTER APPLY
    (
        SELECT MAX(CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(cte.Cod_tipo_attestazione,''))), '') IS NULL THEN 0 ELSE 1 END) AS CoAttestazioneOk
        FROM vCertificaz_ISEE cte
        WHERE cte.Anno_accademico = @AA
          AND cte.Num_domanda = t.NumDomanda
          AND cte.tipologia_certificazione = 'CO'
    ) att
),
Flags AS
(
    SELECT
        NumDomanda,
        CodFiscale,
        CAST(CASE WHEN TipoOrigine = 'IT' AND StatusInpsOrigine = 2 AND CoAttestazioneOk = 1 THEN 1 ELSE 0 END AS BIT) AS IsOrigIT_CO,
        CAST(CASE WHEN TipoOrigine = 'IT' AND NOT (StatusInpsOrigine = 2 AND CoAttestazioneOk = 1) THEN 1 ELSE 0 END AS BIT) AS IsOrigIT_DO,
        CAST(CASE WHEN TipoOrigine = 'EE' THEN 1 ELSE 0 END AS BIT) AS IsOrigEE,
        CAST(CASE WHEN TipoNucleo = 'I' AND TipoIntegrazione = 'IT' AND StatusInpsIntegrazione = 2 THEN 1 ELSE 0 END AS BIT) AS IsIntIT_CI,
        CAST(CASE WHEN TipoNucleo = 'I' AND ((TipoIntegrazione = 'IT' AND StatusInpsIntegrazione <> 2) OR TipoIntegrazione = 'EE') THEN 1 ELSE 0 END AS BIT) AS IsIntDI
    FROM Tipologie
)
UPDATE p
SET
    p.IsOrigIT_CO = f.IsOrigIT_CO,
    p.IsOrigIT_DO = f.IsOrigIT_DO,
    p.IsOrigEE = f.IsOrigEE,
    p.IsIntIT_CI = f.IsIntIT_CI,
    p.IsIntDI = f.IsIntDI
FROM {pipelineTableName} p
INNER JOIN Flags f
    ON f.NumDomanda = p.NumDomanda
   AND f.CodFiscale = p.CodFiscale;";

            using var command = new SqlCommand(sql, _conn)
            {
                CommandTimeout = 9999999
            };
            AddAaParameter(command, CurrentContext.AnnoAccademico);
            command.ExecuteNonQuery();
        }

        private readonly record struct Target(string CodFiscale, string NumDomanda);

        private void AddDatiEconomiciItaliani_CO(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.AddDatiEconomiciItaliani_CO", $"AA={aa}");
            sourceTableName = ResolveTempTableName(sourceTableName);

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
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
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
FROM {sourceTableName} t
INNER JOIN Domanda d ON d.Anno_accademico = @AA AND d.Num_domanda = t.NumDomanda
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.NumDomanda
   AND cte.tipologia_certificazione = 'CO'
LEFT JOIN sumPagamenti sp ON t.CodFiscale = sp.Cod_fiscale
LEFT JOIN impAltreBorse iab ON t.NumDomanda = iab.num_domanda AND @AA = iab.anno_accademico
WHERE t.IsOrigIT_CO = 1;";

            var dtoMap = new Dictionary<StudentKey, EconomiciOrigineCoDto>(CurrentStudents.Count);

            using var command = new SqlCommand(sql, _conn)
            {
                CommandTimeout = 9999999
            };

            command.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;
            command.Parameters.Add("@EseFin", SqlDbType.Int).Value = eseFin;

            using var reader = command.ExecuteReader();

            int ordCf = reader.GetOrdinal("Cod_fiscale");
            int ordNumDomanda = reader.GetOrdinal("Num_domanda");
            int ordDetrazioniAdisu = reader.GetOrdinal("detrazioniADISU");
            int ordDetrazioniAltreBorse = reader.GetOrdinal("detrazioniAltreBorse");
            int ordSommaRedditi = reader.GetOrdinal("Somma_redditi");
            int ordIsr = reader.GetOrdinal("ISR");
            int ordIsp = reader.GetOrdinal("ISP");
            int ordSequ = reader.GetOrdinal("SEQU");
            int ordReddFratelli50 = reader.GetOrdinal("Redd_fratelli_50");
            int ordPatrFratelli50 = reader.GetOrdinal("Patr_fratelli_50");
            int ordPatrFrat50Est = reader.GetOrdinal("Patr_frat_50_est");
            int ordReddFrat50Est = reader.GetOrdinal("Redd_frat_50_est");
            int ordPatrFam50Est = reader.GetOrdinal("Patr_fam_50_est");
            int ordMetriQuadri = reader.GetOrdinal("Metri_quadri");
            int ordReddFam50Est = reader.GetOrdinal("Redd_fam_50_est");
            int ordPatrImm50FratSor = reader.GetOrdinal("patr_imm_50_frat_sor");

            while (reader.Read())
            {
                var key = CreateStudentKey(
                    reader.IsDBNull(ordCf) ? "" : reader.GetString(ordCf),
                    ReadDomandaAsString(reader, ordNumDomanda));

                if (!CurrentStudents.ContainsKey(key))
                    continue;

                if (!dtoMap.TryGetValue(key, out var dto))
                {
                    dto = new EconomiciOrigineCoDto();
                    dtoMap[key] = dto;
                }

                dto.DetrazioniAdisu = GetDecimalOrZero(reader, ordDetrazioniAdisu);
                dto.DetrazioniAltreBorse = GetDecimalOrZero(reader, ordDetrazioniAltreBorse);
                dto.OrigineSommaRedditi = GetDecimalOrZero(reader, ordSommaRedditi);
                dto.OrigineISR = GetDecimalOrZero(reader, ordIsr);
                dto.OrigineISP = GetDecimalOrZero(reader, ordIsp);
                dto.OrigineScalaEquivalenza = GetDecimalOrZero(reader, ordSequ);
                dto.OrigineReddFratelli50 = GetDecimalOrZero(reader, ordReddFratelli50);
                dto.OriginePatrFratelli50 = GetDecimalOrZero(reader, ordPatrFratelli50);
                dto.OriginePatrFrat50Est = GetDecimalOrZero(reader, ordPatrFrat50Est);
                dto.OrigineReddFrat50Est = GetDecimalOrZero(reader, ordReddFrat50Est);
                dto.OriginePatrFam50Est = GetDecimalOrZero(reader, ordPatrFam50Est);
                dto.OrigineMetriQuadri = GetDecimalOrZero(reader, ordMetriQuadri);
                dto.OrigineReddFam50Est = GetDecimalOrZero(reader, ordReddFam50Est);
                dto.OriginePatrImm50FratSor = GetDecimalOrZero(reader, ordPatrImm50FratSor);
            }

            MergeOrigineCoDtos(dtoMap);
        }
        private sealed class EconomiciOrigineCoDto
        {
            public string OrigineFonte { get; set; } = "CO";
            public decimal DetrazioniAdisu { get; set; }
            public decimal DetrazioniAltreBorse { get; set; }
            public decimal OrigineSommaRedditi { get; set; }
            public decimal OrigineISR { get; set; }
            public decimal OrigineISP { get; set; }
            public decimal OrigineScalaEquivalenza { get; set; }
            public decimal OrigineReddFratelli50 { get; set; }
            public decimal OriginePatrFratelli50 { get; set; }
            public decimal OriginePatrFrat50Est { get; set; }
            public decimal OrigineReddFrat50Est { get; set; }
            public decimal OriginePatrFam50Est { get; set; }
            public decimal OrigineMetriQuadri { get; set; }
            public decimal OrigineReddFam50Est { get; set; }
            public decimal OriginePatrImm50FratSor { get; set; }
        }
        private static StudentKey ReadStudentKey(SqlDataReader reader, string cfColumn = "Cod_fiscale", string domandaColumn = "Num_domanda")
            => CreateStudentKey(reader.SafeGetString(cfColumn), reader.SafeGetString(domandaColumn));
        private void MergeOrigineCoDtos(Dictionary<StudentKey, EconomiciOrigineCoDto> dtoMap)
        {
            foreach (var pair in dtoMap)
            {
                if (!TryGetStudentInfo(pair.Key, out var info) || info == null)
                    continue;

                info.InformazioniEconomiche ??= new InformazioniEconomiche();

                var dto = pair.Value;
                var raw = info.InformazioniEconomiche.Raw;

                raw.OrigineFonte = dto.OrigineFonte;
                raw.DetrazioniAdisu = dto.DetrazioniAdisu;
                raw.DetrazioniAltreBorse = dto.DetrazioniAltreBorse;
                raw.OrigineSommaRedditi = dto.OrigineSommaRedditi;
                raw.OrigineISR = dto.OrigineISR;
                raw.OrigineISP = dto.OrigineISP;
                raw.OrigineScalaEquivalenza = dto.OrigineScalaEquivalenza;
                raw.OrigineReddFratelli50 = dto.OrigineReddFratelli50;
                raw.OriginePatrFratelli50 = dto.OriginePatrFratelli50;
                raw.OriginePatrFrat50Est = dto.OriginePatrFrat50Est;
                raw.OrigineReddFrat50Est = dto.OrigineReddFrat50Est;
                raw.OriginePatrFam50Est = dto.OriginePatrFam50Est;
                raw.OrigineMetriQuadri = dto.OrigineMetriQuadri;
                raw.OrigineReddFam50Est = dto.OrigineReddFam50Est;
                raw.OriginePatrImm50FratSor = dto.OriginePatrImm50FratSor;
            }
        }
        private void AddDatiEconomiciStranieri_DO(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.AddDatiEconomiciStranieri_DO", $"AA={aa}");
            sourceTableName = ResolveTempTableName(sourceTableName);

            string sql = $@"
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
    nf.Numero_componenti,
    ISNULL(nf.Redd_complessivo,0) AS Redd_complessivo,
    ISNULL(nf.Patr_mobiliare,0) AS Patr_mobiliare,
    ISNULL(nf.Superf_abitaz_MQ,0) AS Superf_abitaz_MQ,
    ISNULL(nf.Sup_compl_altre_MQ,0) AS Sup_compl_altre_MQ,
    ISNULL(nf.Sup_compl_MQ,0) AS Sup_compl_MQ,
    ISNULL(nf.Redd_lordo_fratell,0) AS Redd_lordo_fratell,
    ISNULL(nf.Patr_mob_fratell,0) AS Patr_mob_fratell
FROM {sourceTableName} t
INNER JOIN vNucleo_fam_stranieri_DO nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.NumDomanda
WHERE t.IsOrigEE = 1;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;

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

        private void AddDatiEconomiciItaliani_DOFromCert(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.AddDatiEconomiciItaliani_DOFromCert", $"AA={aa}");
            sourceTableName = ResolveTempTableName(sourceTableName);

            string sql = $@"
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
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
FROM {sourceTableName} t
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.NumDomanda
   AND cte.tipologia_certificazione = 'DO'
WHERE t.IsOrigIT_DO = 1;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;

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

        private void AddDatiEconomiciItaliani_CI(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.AddDatiEconomiciItaliani_CI", $"AA={aa}");
            sourceTableName = ResolveTempTableName(sourceTableName);

            string sql = $@"
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
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
FROM {sourceTableName} t
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.NumDomanda
   AND cte.tipologia_certificazione = 'CI'
WHERE t.IsIntIT_CI = 1;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;

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

        private void AddDatiEconomiciStranieri_DI(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.AddDatiEconomiciStranieri_DI", $"AA={aa}");
            sourceTableName = ResolveTempTableName(sourceTableName);

            string sql = $@"
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
    nf.Numero_componenti,
    ISNULL(nf.Redd_complessivo,0) AS Redd_complessivo,
    ISNULL(nf.Patr_mobiliare,0) AS Patr_mobiliare,
    ISNULL(nf.Superf_abitaz_MQ,0) AS Superf_abitaz_MQ,
    ISNULL(nf.Sup_compl_altre_MQ,0) AS Sup_compl_altre_MQ,
    ISNULL(nf.Sup_compl_MQ,0) AS Sup_compl_MQ,
    ISNULL(nf.Redd_lordo_fratell,0) AS Redd_lordo_fratell,
    ISNULL(nf.Patr_mob_fratell,0) AS Patr_mob_fratell
FROM {sourceTableName} t
INNER JOIN vNucleo_fam_stranieri_DI nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.NumDomanda
WHERE t.IsIntDI = 1;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;

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

        private SplitResult LoadTipologieRedditiAndSplit(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadTipologieRedditiAndSplit", $"AA={aa}");
            Logger.LogInfo(30, "Esecuzione query tipologie reddito (vTipologie_redditi) + split per studente/domanda.");

            sourceTableName = ResolveTempTableName(sourceTableName);
            var result = new SplitResult();

            string sql = $@"
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
    tr.Tipo_redd_nucleo_fam_origine,
    tr.Tipo_redd_nucleo_fam_integr,
    ISNULL(tr.altri_mezzi,0) AS altri_mezzi
FROM {sourceTableName} t
INNER JOIN vTipologie_redditi tr
    ON tr.Anno_accademico = @AA
   AND tr.Num_domanda     = t.NumDomanda
;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);

            int readCount = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readCount++;
                if (!TryGetStudentInfo(reader, out var info))
                    continue;

                var eco = info.InformazioniEconomiche;
                var raw = eco.Raw;

                string codFiscale = NormalizeCf(reader.SafeGetString("Cod_fiscale"));
                string numDomanda = NormalizeDomanda(reader.SafeGetString("Num_domanda"));
                var target = new Target(codFiscale, numDomanda);
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

        private void LoadValoriCalcolatiAttuali(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadValoriCalcolatiAttuali", $"AA={aa}");
            sourceTableName = ResolveTempTableName(sourceTableName);

            string sql = $@"
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
    vv.ISPEDSU,
    vv.ISEDSU,
    vv.SEQ,
    vv.ISPDSU,
    vv.ISEEDSU
FROM {sourceTableName} t
LEFT JOIN vValori_calcolati vv
    ON vv.Anno_accademico = @AA
   AND vv.Num_domanda     = t.NumDomanda;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info)) continue;
                var eco = info.InformazioniEconomiche;
                var attuali = eco.Attuali;

                attuali.ISPEDSU = reader.SafeGetDouble("ISPEDSU");
                attuali.ISEDSU = reader.SafeGetDouble("ISEDSU");
                attuali.SEQ = reader.SafeGetDouble("SEQ");
                attuali.ISPDSU = reader.SafeGetDouble("ISPDSU");
                attuali.ISEEDSU = reader.SafeGetDouble("ISEEDSU");
            }
        }

    }
}
