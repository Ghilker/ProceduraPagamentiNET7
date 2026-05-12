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
                throw new ArgumentException("Anno accademico non valido (atteso char(8), es: 20252026).");

            _currentContext = context;
            ResetComuniEquiparatiState();
            Log(10, $"Studenti nel contesto: {CurrentStudents.Count}");

            ExecuteEconomiciCollectionPipeline(aa, context.TempPipelineTable, Log);
        }

        private void ExecuteEconomiciCollectionPipeline(string aa, string pipelineTableName, Action<int, string> log)
        {
            CreateEconomicSemestreFlagsTempTable();

            try
            {
                log(18, "Caricamento valori attuali da Valori_calcolati con ultimo max data_validita.");
                LoadValoriCalcolatiAttuali(aa, pipelineTableName);

                LoadCalcParams(aa);
                LoadNucleoFamiliare(aa, pipelineTableName);

                log(22, "Caricamento INPS e attestazioni CO.");
                LoadInpsAndAttestazioni_StoredLike(aa, pipelineTableName);

                log(30, "Lettura tipologie reddito e split per studente/domanda.");
                var split = LoadTipologieRedditiAndSplit(aa, pipelineTableName);
                ApplyEconomicSplitFlagsToPipelineTable(pipelineTableName, split);

                log(40, "Estrazione dati economici origine.");
                AddDatiEconomiciItaliani_CO(aa, pipelineTableName);
                AddDatiEconomiciItaliani_DOFromCert(aa, pipelineTableName);
                AddDatiEconomiciStranieri_DO(aa, pipelineTableName);

                log(60, "Estrazione dati economici integrazione.");
                AddDatiEconomiciItaliani_CI(aa, pipelineTableName);
                AddDatiEconomiciStranieri_DI(aa, pipelineTableName);

                log(70, $"Raccolta dati economici completata. Studenti nel contesto: {CurrentStudents.Count}");
            }
            finally
            {
                DropEconomicSemestreFlagsTempTable();
            }
        }

        private const string EconomicSemestreFlagsTempTableName = "#VerificaEconomiciSemestreFlags";

        private void CreateEconomicSemestreFlagsTempTable()
        {
            const string sql = @"
IF OBJECT_ID('tempdb..#VerificaEconomiciSemestreFlags') IS NOT NULL
    DROP TABLE #VerificaEconomiciSemestreFlags;

CREATE TABLE #VerificaEconomiciSemestreFlags
(
    CodFiscale NVARCHAR(32) NOT NULL,
    NumDomanda NUMERIC(18,0) NOT NULL,
    ConfermaSemestreFiltro BIT NOT NULL,
    CONSTRAINT PK_VerificaEconomiciSemestreFlags PRIMARY KEY CLUSTERED (CodFiscale, NumDomanda)
);";

            using (var command = new SqlCommand(sql, _conn) { CommandTimeout = 9999999 })
            {
                command.ExecuteNonQuery();
            }

            var table = BuildEconomicSemestreFlagsTable();
            if (table.Rows.Count == 0)
                return;

            using var bulk = new SqlBulkCopy(_conn, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = EconomicSemestreFlagsTempTableName,
                BulkCopyTimeout = 9999999,
                BatchSize = 10000
            };

            bulk.ColumnMappings.Add("CodFiscale", "CodFiscale");
            bulk.ColumnMappings.Add("NumDomanda", "NumDomanda");
            bulk.ColumnMappings.Add("ConfermaSemestreFiltro", "ConfermaSemestreFiltro");
            bulk.WriteToServer(table);
        }

        private void DropEconomicSemestreFlagsTempTable()
        {
            const string sql = @"
IF OBJECT_ID('tempdb..#VerificaEconomiciSemestreFlags') IS NOT NULL
    DROP TABLE #VerificaEconomiciSemestreFlags;";

            using var command = new SqlCommand(sql, _conn) { CommandTimeout = 9999999 };
            command.ExecuteNonQuery();
        }

        private DataTable BuildEconomicSemestreFlagsTable()
        {
            var table = new DataTable();
            table.Columns.Add("CodFiscale", typeof(string));
            table.Columns.Add("NumDomanda", typeof(decimal));
            table.Columns.Add("ConfermaSemestreFiltro", typeof(bool));

            foreach (var info in CurrentStudents.Values)
            {
                string codFiscale = NormalizeCf(info.InformazioniPersonali.CodFiscale);
                string numDomandaText = NormalizeDomanda(info.InformazioniPersonali.NumDomanda);

                if (string.IsNullOrWhiteSpace(codFiscale) || !TryParseNumDomanda(numDomandaText, out var numDomanda))
                    continue;

                bool confermaSemestreFiltro = info.InformazioniIscrizione.ConfermaSemestreFiltro == 1;
                table.Rows.Add(codFiscale, numDomanda, confermaSemestreFiltro);
            }

            return table;
        }


        private static DateTime GetFirmataIlMaxPerAa(string aa)
        {
            string value = (aa ?? string.Empty).Trim();
            if (value.Length < 4 || !int.TryParse(value.Substring(0, 4), out int startYear))
                throw new ArgumentException($"Anno accademico non valido per il cutoff Firmata_il: {aa}", nameof(aa));

            return new DateTime(startYear, 12, 31);
        }

        private static DateTime GetScadenzaIseeBasePerAa(string aa)
        {
            string value = (aa ?? string.Empty).Trim();
            if (value.Length < 4 || !int.TryParse(value.Substring(0, 4), out int startYear))
                throw new ArgumentException($"Anno accademico non valido per la scadenza ISEE base: {aa}", nameof(aa));

            // Regola 20252026+: ISEE base, anche ordinario/corrente, firmato entro il 22 luglio dell'anno di avvio AA.
            // Per ConfermaSemestreFiltro = 1 il limite viene gestito nelle query e diventa il 31 dicembre.
            return new DateTime(startYear, 7, 22);
        }

        private static void AddScadenzaIseeBaseParameter(SqlCommand command, string aa)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            command.Parameters.Add("@ScadenzaIseeBase", SqlDbType.DateTime).Value = GetScadenzaIseeBasePerAa(aa);
        }

        private static string GetSqlPredicateAttestazioneIseeBase(string alias)
            => $"(UPPER(ISNULL({alias}.Cod_tipo_attestazione,'')) LIKE '%ORD%' OR UPPER(ISNULL({alias}.Cod_tipo_attestazione,'')) LIKE '%UNIV%' OR UPPER(ISNULL({alias}.Cod_tipo_attestazione,'')) LIKE '%RID%' OR UPPER(ISNULL({alias}.Cod_tipo_attestazione,'')) LIKE '%CORRENTE%')";

        private static string GetSqlPredicateAttestazioneUniversitaria(string alias)
            => $"(UPPER(ISNULL({alias}.Cod_tipo_attestazione,'')) LIKE '%UNIV%' OR UPPER(ISNULL({alias}.Cod_tipo_attestazione,'')) LIKE '%RID%' OR UPPER(ISNULL({alias}.Cod_tipo_attestazione,'')) LIKE '%CORRENTE%')";

        private static string GetSqlPredicateAttestazioneOrdinaria(string alias)
            => $"(UPPER(ISNULL({alias}.Cod_tipo_attestazione,'')) LIKE '%ORD%')";

        private static void AddFirmataIlMaxParameter(SqlCommand command, string aa)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            command.Parameters.Add("@FirmataIlMax", SqlDbType.DateTime).Value = GetFirmataIlMaxPerAa(aa);
        }

        private void LoadInpsAndAttestazioni_StoredLike(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadInpsAndAttestazioni", $"AA={aa}");
            sourceTableName = ResolveTempTableName(sourceTableName);

            string attestazioneBasePredicate = GetSqlPredicateAttestazioneIseeBase("cte");
            string attestazioneUniversitariaPredicate = GetSqlPredicateAttestazioneUniversitaria("cte");
            string attestazioneOrdinariaPredicate = GetSqlPredicateAttestazioneOrdinaria("cte");

            string sql = $@"
;WITH Targets AS
(
    SELECT DISTINCT
        t.CodFiscale,
        t.NumDomanda,
        ISNULL(sf.ConfermaSemestreFiltro, 0) AS ConfermaSemestreFiltro
    FROM {sourceTableName} t
    LEFT JOIN #VerificaEconomiciSemestreFlags sf
        ON sf.CodFiscale = t.CodFiscale
       AND sf.NumDomanda = t.NumDomanda
),
TipologieRanked AS
(
    SELECT
        tr.Num_domanda,
        tr.Tipo_redd_nucleo_fam_integr,
        ROW_NUMBER() OVER
        (
            PARTITION BY tr.Num_domanda
            ORDER BY tr.data_validita DESC
        ) AS rn
    FROM Tipologie_redditi tr
    INNER JOIN Targets t
        ON t.NumDomanda = tr.Num_domanda
    WHERE tr.Anno_accademico = @AA
),
UltimeTipologie AS
(
    SELECT
        Num_domanda,
        Tipo_redd_nucleo_fam_integr
    FROM TipologieRanked
    WHERE rn = 1
),
NucleiRanked AS
(
    SELECT
        nf.Num_domanda,
        nf.Cod_tipologia_nucleo,
        ROW_NUMBER() OVER
        (
            PARTITION BY nf.Num_domanda
            ORDER BY nf.data_validita DESC
        ) AS rn
    FROM Nucleo_familiare nf
    INNER JOIN Targets t
        ON t.NumDomanda = nf.Num_domanda
    WHERE nf.Anno_accademico = @AA
),
UltimoNucleo AS
(
    SELECT
        Num_domanda,
        Cod_tipologia_nucleo
    FROM NucleiRanked
    WHERE rn = 1
),
TargetEconomicFlags AS
(
    SELECT
        t.CodFiscale,
        t.NumDomanda,
        t.ConfermaSemestreFiltro,
        CASE
            WHEN UPPER(REPLACE(ISNULL(nf.Cod_tipologia_nucleo, ''), ' ', '')) = 'I'
             AND UPPER(REPLACE(ISNULL(tr.Tipo_redd_nucleo_fam_integr, ''), ' ', '')) = 'EE'
            THEN 1 ELSE 0
        END AS HasIntegrazioneRedditiEsteri
    FROM Targets t
    LEFT JOIN UltimeTipologie tr
        ON tr.Num_domanda = t.NumDomanda
    LEFT JOIN UltimoNucleo nf
        ON nf.Num_domanda = t.NumDomanda
),
BaseIsee AS
(
    SELECT
        cte.Num_domanda,
        COUNT_BIG(*) AS NumeroIseeBaseEntroScadenza
    FROM Certificaz_ISEE cte
    INNER JOIN TargetEconomicFlags t
        ON t.NumDomanda = cte.Num_domanda
    WHERE cte.Anno_accademico = @AA
      AND UPPER(ISNULL(cte.tipologia_certificazione,'')) IN ('CO','DO')
      AND cte.firmata_il IS NOT NULL
      AND cte.firmata_il <= CASE WHEN ISNULL(t.ConfermaSemestreFiltro, 0) = 1 THEN @FirmataIlMax ELSE @ScadenzaIseeBase END
      AND {attestazioneBasePredicate}
    GROUP BY cte.Num_domanda
),
CertUniversitarieRanked AS
(
    SELECT
        cte.Num_domanda,
        x.TipoCert,
        cte.Utente,
        cte.firmata_il,
        cte.data_validita,
        ROW_NUMBER() OVER
        (
            PARTITION BY cte.Num_domanda, x.TipoCert
            ORDER BY
                CASE WHEN {attestazioneUniversitariaPredicate} THEN 0 ELSE 1 END,
                cte.firmata_il DESC,
                cte.data_validita DESC
        ) AS rn
    FROM Certificaz_ISEE cte
    INNER JOIN TargetEconomicFlags t
        ON t.NumDomanda = cte.Num_domanda
    CROSS APPLY
    (
        SELECT UPPER(ISNULL(cte.tipologia_certificazione,'')) AS TipoCert
    ) x
    WHERE cte.Anno_accademico = @AA
      AND cte.firmata_il IS NOT NULL
      AND cte.firmata_il <= @FirmataIlMax
      AND
      (
          (x.TipoCert = 'CI' AND {attestazioneUniversitariaPredicate})
          OR
          (x.TipoCert = 'CO' AND ({attestazioneUniversitariaPredicate} OR ((t.HasIntegrazioneRedditiEsteri = 1 OR ISNULL(t.ConfermaSemestreFiltro, 0) = 1) AND {attestazioneOrdinariaPredicate})))
      )
),
LatestCert AS
(
    SELECT
        Num_domanda,
        TipoCert,
        Utente,
        firmata_il,
        data_validita
    FROM CertUniversitarieRanked
    WHERE rn = 1
),
LatestStatus AS
(
    SELECT
        t.CodFiscale,
        t.NumDomanda,
        c.TipoCert,
        TRY_CONVERT(INT, NULLIF(si.status_inps, '')) AS StatusInps
    FROM LatestCert c
    INNER JOIN TargetEconomicFlags t
        ON t.NumDomanda = c.Num_domanda
    OUTER APPLY
    (
        SELECT TOP (1)
            si1.status_inps,
            si1.data_validita
        FROM Status_INPS si1
        WHERE si1.anno_accademico = @AA
          AND si1.cod_fiscale = t.CodFiscale
          AND si1.num_domanda = t.NumDomanda
          AND si1.data_fine_validita IS NULL
          AND UPPER(ISNULL(si1.tipo_certificaz,'')) = c.TipoCert
          AND ISNULL(NULLIF(si1.Utente, ''), '#NULL#') = ISNULL(NULLIF(c.Utente, ''), '#NULL#')
        ORDER BY si1.data_validita DESC
    ) si
)
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
    CASE
        WHEN base.Num_domanda IS NOT NULL AND coCert.Num_domanda IS NOT NULL THEN ISNULL(coStatus.StatusInps, 0)
        ELSE 0
    END AS StatusInpsOrigine,
    CASE
        WHEN ciCert.Num_domanda IS NOT NULL THEN ISNULL(ciStatus.StatusInps, 0)
        ELSE 0
    END AS StatusInpsIntegrazione,
    CASE WHEN base.Num_domanda IS NOT NULL AND coCert.Num_domanda IS NOT NULL THEN 1 ELSE 0 END AS CoAttestazioneOk
FROM TargetEconomicFlags t
LEFT JOIN BaseIsee base
    ON base.Num_domanda = t.NumDomanda
LEFT JOIN LatestCert coCert
    ON coCert.Num_domanda = t.NumDomanda
   AND coCert.TipoCert = 'CO'
LEFT JOIN LatestStatus coStatus
    ON coStatus.CodFiscale = t.CodFiscale
   AND coStatus.NumDomanda = t.NumDomanda
   AND coStatus.TipoCert = 'CO'
LEFT JOIN LatestCert ciCert
    ON ciCert.Num_domanda = t.NumDomanda
   AND ciCert.TipoCert = 'CI'
LEFT JOIN LatestStatus ciStatus
    ON ciStatus.CodFiscale = t.CodFiscale
   AND ciStatus.NumDomanda = t.NumDomanda
   AND ciStatus.TipoCert = 'CI';";

            using var command = new SqlCommand(sql, _conn)
            {
                CommandTimeout = 9999999
            };
            AddAaParameter(command, aa);
            AddScadenzaIseeBaseParameter(command, aa);
            AddFirmataIlMaxParameter(command, aa);
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
            AddDataValiditaMaxParameter(command, aa);

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
INNER JOIN Nucleo_familiare nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.NumDomanda
   AND nf.data_validita =
   (
       SELECT MAX(nf2.data_validita)
       FROM Nucleo_familiare nf2
       WHERE nf2.Anno_accademico = nf.Anno_accademico
         AND nf2.Num_domanda = nf.Num_domanda
   );";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);
            AddDataValiditaMaxParameter(command, aa);

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

            string resetSql = $@"
UPDATE {pipelineTableName}
SET
    IsOrigIT_CO = 0,
    IsOrigIT_DO = 0,
    IsOrigEE = 0,
    IsIntIT_CI = 0,
    IsIntDI = 0;";

            using (var resetCommand = new SqlCommand(resetSql, _conn) { CommandTimeout = 9999999 })
            {
                resetCommand.ExecuteNonQuery();
            }

            if (split == null)
                return;

            var flags = BuildSplitFlagMap(split);
            if (flags.Count == 0)
                return;

            const string createSql = @"
CREATE TABLE #EconomicSplitFlags
(
    CodFiscale varchar(16) NOT NULL,
    NumDomanda numeric(18,0) NOT NULL,
    IsOrigIT_CO bit NOT NULL,
    IsOrigIT_DO bit NOT NULL,
    IsOrigEE bit NOT NULL,
    IsIntIT_CI bit NOT NULL,
    IsIntDI bit NOT NULL,
    CONSTRAINT PK_EconomicSplitFlags PRIMARY KEY CLUSTERED (CodFiscale, NumDomanda)
);";

            using (var createCommand = new SqlCommand(createSql, _conn) { CommandTimeout = 9999999 })
            {
                createCommand.ExecuteNonQuery();
            }

            using (var bulk = new SqlBulkCopy(_conn, SqlBulkCopyOptions.TableLock, null))
            {
                bulk.DestinationTableName = "#EconomicSplitFlags";
                bulk.BatchSize = 10000;
                bulk.BulkCopyTimeout = 9999999;
                bulk.ColumnMappings.Add("CodFiscale", "CodFiscale");
                bulk.ColumnMappings.Add("NumDomanda", "NumDomanda");
                bulk.ColumnMappings.Add("IsOrigIT_CO", "IsOrigIT_CO");
                bulk.ColumnMappings.Add("IsOrigIT_DO", "IsOrigIT_DO");
                bulk.ColumnMappings.Add("IsOrigEE", "IsOrigEE");
                bulk.ColumnMappings.Add("IsIntIT_CI", "IsIntIT_CI");
                bulk.ColumnMappings.Add("IsIntDI", "IsIntDI");
                bulk.WriteToServer(BuildSplitFlagTable(flags));
            }

            string updateSql = $@"
UPDATE p
SET
    p.IsOrigIT_CO = f.IsOrigIT_CO,
    p.IsOrigIT_DO = f.IsOrigIT_DO,
    p.IsOrigEE = f.IsOrigEE,
    p.IsIntIT_CI = f.IsIntIT_CI,
    p.IsIntDI = f.IsIntDI
FROM {pipelineTableName} p
INNER JOIN #EconomicSplitFlags f
    ON f.CodFiscale = p.CodFiscale
   AND f.NumDomanda = p.NumDomanda;";

            using (var updateCommand = new SqlCommand(updateSql, _conn) { CommandTimeout = 9999999 })
            {
                updateCommand.ExecuteNonQuery();
            }
        }

        private readonly record struct Target(string CodFiscale, string NumDomanda);

        private sealed class SplitFlagsDto
        {
            public bool IsOrigIT_CO { get; set; }
            public bool IsOrigIT_DO { get; set; }
            public bool IsOrigEE { get; set; }
            public bool IsIntIT_CI { get; set; }
            public bool IsIntDI { get; set; }
        }

        private static Dictionary<Target, SplitFlagsDto> BuildSplitFlagMap(SplitResult split)
        {
            var flags = new Dictionary<Target, SplitFlagsDto>();

            foreach (var target in split.OrigIT_CO)
                SetSplitFlag(flags, target, static f => f.IsOrigIT_CO = true);

            foreach (var target in split.OrigIT_DO)
                SetSplitFlag(flags, target, static f => f.IsOrigIT_DO = true);

            foreach (var target in split.OrigEE)
                SetSplitFlag(flags, target, static f => f.IsOrigEE = true);

            foreach (var target in split.IntIT_CI)
                SetSplitFlag(flags, target, static f => f.IsIntIT_CI = true);

            foreach (var target in split.IntDI)
                SetSplitFlag(flags, target, static f => f.IsIntDI = true);

            return flags;
        }

        private static void SetSplitFlag(Dictionary<Target, SplitFlagsDto> flags, Target target, Action<SplitFlagsDto> setter)
        {
            if (!flags.TryGetValue(target, out var dto))
            {
                dto = new SplitFlagsDto();
                flags[target] = dto;
            }

            setter(dto);
        }

        private static DataTable BuildSplitFlagTable(Dictionary<Target, SplitFlagsDto> flags)
        {
            var table = new DataTable();
            table.Columns.Add("CodFiscale", typeof(string));
            table.Columns.Add("NumDomanda", typeof(decimal));
            table.Columns.Add("IsOrigIT_CO", typeof(bool));
            table.Columns.Add("IsOrigIT_DO", typeof(bool));
            table.Columns.Add("IsOrigEE", typeof(bool));
            table.Columns.Add("IsIntIT_CI", typeof(bool));
            table.Columns.Add("IsIntDI", typeof(bool));

            foreach (var pair in flags)
            {
                if (!TryParseNumDomanda(pair.Key.NumDomanda, out var numDomanda))
                    continue;

                var dto = pair.Value;
                table.Rows.Add(
                    pair.Key.CodFiscale,
                    numDomanda,
                    dto.IsOrigIT_CO,
                    dto.IsOrigIT_DO,
                    dto.IsOrigEE,
                    dto.IsIntIT_CI,
                    dto.IsIntDI);
            }

            return table;
        }

        private static bool TryParseNumDomanda(string value, out decimal numDomanda)
        {
            string normalized = (value ?? string.Empty).Trim();
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out numDomanda)
                || decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out numDomanda);
        }


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
    FROM Importi_borsa_percepiti vb
    INNER JOIN Allegati a ON vb.anno_accademico = a.anno_accademico AND vb.num_domanda = a.num_domanda
    INNER JOIN Status_allegati vs ON a.id_allegato = vs.id_allegato
    WHERE vb.data_fine_validita IS NULL
      AND vb.data_validita =
      (
          SELECT MAX(vb2.data_validita)
          FROM Importi_borsa_percepiti vb2
          WHERE vb2.anno_accademico = vb.anno_accademico
            AND vb2.num_domanda = vb.num_domanda
            AND vb2.data_fine_validita IS NULL
      )
      AND a.data_fine_validita IS NULL
      AND a.data_validita =
      (
          SELECT MAX(a2.data_validita)
          FROM Allegati a2
          WHERE a2.id_allegato = a.id_allegato
            AND a2.data_fine_validita IS NULL
      )
      AND a.cod_tipo_allegato = '07'
      AND vs.data_fine_validita IS NULL
      AND vs.data_validita =
      (
          SELECT MAX(vs2.data_validita)
          FROM Status_allegati vs2
          WHERE vs2.id_allegato = vs.id_allegato
            AND vs2.data_fine_validita IS NULL
      )
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
LEFT JOIN #VerificaEconomiciSemestreFlags sf
    ON sf.CodFiscale = t.CodFiscale
   AND sf.NumDomanda = t.NumDomanda
OUTER APPLY
(
    SELECT TOP 1 *
    FROM Certificaz_ISEE cte
    WHERE cte.Anno_accademico = @AA
      AND cte.Num_domanda = t.NumDomanda
      AND UPPER(ISNULL(cte.tipologia_certificazione,'')) = 'CO'
      AND cte.firmata_il IS NOT NULL
      AND cte.firmata_il <= @FirmataIlMax
      AND ({GetSqlPredicateAttestazioneUniversitaria("cte")} OR ((t.IsIntDI = 1 OR ISNULL(sf.ConfermaSemestreFiltro, 0) = 1) AND {GetSqlPredicateAttestazioneOrdinaria("cte")}))
    ORDER BY
        CASE WHEN {GetSqlPredicateAttestazioneUniversitaria("cte")} THEN 0 ELSE 1 END,
        cte.firmata_il DESC,
        cte.data_validita DESC
) cte
LEFT JOIN sumPagamenti sp ON t.CodFiscale = sp.Cod_fiscale
LEFT JOIN impAltreBorse iab ON t.NumDomanda = iab.num_domanda AND @AA = iab.anno_accademico
WHERE t.IsOrigIT_CO = 1
  AND cte.Num_domanda IS NOT NULL;";

            var dtoMap = new Dictionary<StudentKey, EconomiciOrigineCoDto>(CurrentStudents.Count);

            using var command = new SqlCommand(sql, _conn)
            {
                CommandTimeout = 9999999
            };

            command.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;
            command.Parameters.Add("@EseFin", SqlDbType.Int).Value = eseFin;
            AddDataValiditaMaxParameter(command, aa);
            AddFirmataIlMaxParameter(command, aa);

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
INNER JOIN Nucleo_fam_stranieri nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.NumDomanda
   AND nf.Tipologia_redditi = 'DO'
   AND nf.data_validita =
   (
       SELECT MAX(nf2.data_validita)
       FROM Nucleo_fam_stranieri nf2
       WHERE nf2.Anno_accademico = nf.Anno_accademico
         AND nf2.Num_domanda = nf.Num_domanda
         AND nf2.Tipologia_redditi = nf.Tipologia_redditi
   )
WHERE t.IsOrigEE = 1;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);
            AddDataValiditaMaxParameter(command, aa);

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
OUTER APPLY
(
    SELECT TOP 1 *
    FROM Certificaz_ISEE cte
    WHERE cte.Anno_accademico = @AA
      AND cte.Num_domanda = t.NumDomanda
      AND UPPER(ISNULL(cte.tipologia_certificazione,'')) = 'DO'
      AND (cte.firmata_il IS NULL OR cte.firmata_il <= @FirmataIlMax)
    ORDER BY
        cte.firmata_il DESC,
        cte.data_validita DESC
) cte
WHERE t.IsOrigIT_DO = 1
  AND cte.Num_domanda IS NOT NULL;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);
            AddDataValiditaMaxParameter(command, aa);
            AddFirmataIlMaxParameter(command, aa);

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
OUTER APPLY
(
    SELECT TOP 1 *
    FROM Certificaz_ISEE cte
    WHERE cte.Anno_accademico = @AA
      AND cte.Num_domanda = t.NumDomanda
      AND UPPER(ISNULL(cte.tipologia_certificazione,'')) = 'CI'
      AND cte.firmata_il IS NOT NULL
      AND cte.firmata_il <= @FirmataIlMax
      AND {GetSqlPredicateAttestazioneUniversitaria("cte")}
    ORDER BY
        cte.firmata_il DESC,
        cte.data_validita DESC
) cte
WHERE t.IsIntIT_CI = 1
  AND cte.Num_domanda IS NOT NULL;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);
            AddDataValiditaMaxParameter(command, aa);
            AddFirmataIlMaxParameter(command, aa);

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
INNER JOIN Nucleo_fam_stranieri nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.NumDomanda
   AND nf.Tipologia_redditi = 'DI'
   AND nf.data_validita =
   (
       SELECT MAX(nf2.data_validita)
       FROM Nucleo_fam_stranieri nf2
       WHERE nf2.Anno_accademico = nf.Anno_accademico
         AND nf2.Num_domanda = nf.Num_domanda
         AND nf2.Tipologia_redditi = nf.Tipologia_redditi
   )
WHERE t.IsIntDI = 1;";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);
            AddDataValiditaMaxParameter(command, aa);

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

        private EsitoBorsaFacts GetOrCreateEsitoBorsaFacts(StudentKey key)
            => CurrentContext.GetOrCreateEsitoBorsaFacts(key);

        private SplitResult LoadTipologieRedditiAndSplit(string aa, string sourceTableName)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadTipologieRedditiAndSplit", $"AA={aa}");
            Logger.LogInfo(30, "Esecuzione query tipologie reddito set-based senza OUTER APPLY per studente.");

            sourceTableName = ResolveTempTableName(sourceTableName);
            var result = new SplitResult();

            string attestazioneBasePredicate = GetSqlPredicateAttestazioneIseeBase("cte");
            string attestazioneUniversitariaPredicate = GetSqlPredicateAttestazioneUniversitaria("cte");
            string attestazioneOrdinariaPredicate = GetSqlPredicateAttestazioneOrdinaria("cte");

            string sql = $@"
;WITH Targets AS
(
    SELECT DISTINCT
        t.CodFiscale,
        t.NumDomanda,
        ISNULL(sf.ConfermaSemestreFiltro, 0) AS ConfermaSemestreFiltro
    FROM {sourceTableName} t
    LEFT JOIN #VerificaEconomiciSemestreFlags sf
        ON sf.CodFiscale = t.CodFiscale
       AND sf.NumDomanda = t.NumDomanda
),
TipologieRanked AS
(
    SELECT
        tr.Num_domanda,
        tr.Tipo_redd_nucleo_fam_origine,
        tr.Tipo_redd_nucleo_fam_integr,
        tr.altri_mezzi,
        ROW_NUMBER() OVER
        (
            PARTITION BY tr.Num_domanda
            ORDER BY tr.data_validita DESC
        ) AS rn
    FROM Tipologie_redditi tr
    INNER JOIN Targets t
        ON t.NumDomanda = tr.Num_domanda
    WHERE tr.Anno_accademico = @AA
),
UltimeTipologie AS
(
    SELECT
        Num_domanda,
        Tipo_redd_nucleo_fam_origine,
        Tipo_redd_nucleo_fam_integr,
        altri_mezzi
    FROM TipologieRanked
    WHERE rn = 1
),
NucleiRanked AS
(
    SELECT
        nf.Num_domanda,
        nf.Cod_tipologia_nucleo,
        ROW_NUMBER() OVER
        (
            PARTITION BY nf.Num_domanda
            ORDER BY nf.data_validita DESC
        ) AS rn
    FROM Nucleo_familiare nf
    INNER JOIN Targets t
        ON t.NumDomanda = nf.Num_domanda
    WHERE nf.Anno_accademico = @AA
),
UltimoNucleo AS
(
    SELECT
        Num_domanda,
        Cod_tipologia_nucleo
    FROM NucleiRanked
    WHERE rn = 1
),
CertFlags AS
(
    SELECT
        cte.Num_domanda,
        MAX(CASE
                WHEN UPPER(ISNULL(cte.tipologia_certificazione,'')) IN ('CO','DO')
                 AND cte.firmata_il IS NOT NULL
                 AND cte.firmata_il <= CASE WHEN ISNULL(t.ConfermaSemestreFiltro, 0) = 1 THEN @FirmataIlMax ELSE @ScadenzaIseeBase END
                 AND {attestazioneBasePredicate}
                THEN 1 ELSE 0 END) AS HasIseeBaseEntroScadenza,
        MAX(CASE
                WHEN UPPER(ISNULL(cte.tipologia_certificazione,'')) = 'CO'
                 AND cte.firmata_il IS NOT NULL
                 AND cte.firmata_il <= @FirmataIlMax
                 AND {attestazioneUniversitariaPredicate}
                THEN 1 ELSE 0 END) AS HasCOUniversitarioEntroScadenza,
        MAX(CASE
                WHEN UPPER(ISNULL(cte.tipologia_certificazione,'')) = 'CO'
                 AND cte.firmata_il IS NOT NULL
                 AND cte.firmata_il <= @FirmataIlMax
                 AND {attestazioneOrdinariaPredicate}
                 AND UPPER(REPLACE(ISNULL(nf.Cod_tipologia_nucleo, ''), ' ', '')) = 'I'
                 AND UPPER(REPLACE(ISNULL(tr.Tipo_redd_nucleo_fam_integr, ''), ' ', '')) = 'EE'
                THEN 1 ELSE 0 END) AS HasCOOrdinarioConIntegrazioneEsteriEntroScadenza,
        MAX(CASE
                WHEN UPPER(ISNULL(cte.tipologia_certificazione,'')) = 'CO'
                 AND cte.firmata_il IS NOT NULL
                 AND cte.firmata_il <= @FirmataIlMax
                 AND {attestazioneOrdinariaPredicate}
                 AND ISNULL(t.ConfermaSemestreFiltro, 0) = 1
                THEN 1 ELSE 0 END) AS HasCOOrdinarioSemestreFiltroEntroScadenza,
        MAX(CASE
                WHEN UPPER(ISNULL(cte.tipologia_certificazione,'')) = 'CI'
                 AND cte.firmata_il IS NOT NULL
                 AND cte.firmata_il <= @FirmataIlMax
                 AND {attestazioneUniversitariaPredicate}
                THEN 1 ELSE 0 END) AS HasCIUniversitarioEntroScadenza,
        COUNT_BIG(*) AS NumeroCertificazioniImportate,
        SUM(CASE WHEN cte.firmata_il > CASE WHEN ISNULL(t.ConfermaSemestreFiltro, 0) = 1 THEN @FirmataIlMax ELSE @ScadenzaIseeBase END THEN 1 ELSE 0 END) AS NumeroModificheDopoScadenzaBase
    FROM Certificaz_ISEE cte
    INNER JOIN Targets t
        ON t.NumDomanda = cte.Num_domanda
    LEFT JOIN UltimeTipologie tr
        ON tr.Num_domanda = t.NumDomanda
    LEFT JOIN UltimoNucleo nf
        ON nf.Num_domanda = t.NumDomanda
    WHERE cte.Anno_accademico = @AA
      AND UPPER(ISNULL(cte.tipologia_certificazione,'')) IN ('CO','DO','CI','DI')
    GROUP BY cte.Num_domanda
)
SELECT
    t.CodFiscale AS Cod_fiscale,
    t.NumDomanda AS Num_domanda,
    ISNULL(tr.Tipo_redd_nucleo_fam_origine, '') AS Tipo_redd_nucleo_fam_origine,
    ISNULL(tr.Tipo_redd_nucleo_fam_integr, '') AS Tipo_redd_nucleo_fam_integr,
    ISNULL(tr.altri_mezzi, 0) AS altri_mezzi,
    ISNULL(cf.HasIseeBaseEntroScadenza, 0) AS HasIseeBaseEntroScadenza,
    ISNULL(cf.HasCOUniversitarioEntroScadenza, 0) AS CoAttestazioneOk,
    ISNULL(cf.HasCOOrdinarioConIntegrazioneEsteriEntroScadenza, 0) AS CoOrdinarioConIntegrazioneEsteriOk,
    ISNULL(cf.HasCOOrdinarioSemestreFiltroEntroScadenza, 0) AS CoOrdinarioSemestreFiltroOk,
    ISNULL(cf.HasCIUniversitarioEntroScadenza, 0) AS CiAttestazioneOk,
    ISNULL(cf.NumeroCertificazioniImportate, 0) AS NumeroCertificazioniImportate,
    ISNULL(cf.NumeroModificheDopoScadenzaBase, 0) AS NumeroModificheDopoScadenzaBase
FROM Targets t
LEFT JOIN UltimeTipologie tr
    ON tr.Num_domanda = t.NumDomanda
LEFT JOIN CertFlags cf
    ON cf.Num_domanda = t.NumDomanda;";

            using var command = new SqlCommand(sql, _conn)
            {
                CommandTimeout = 9999999
            };
            AddAaParameter(command, aa);
            AddScadenzaIseeBaseParameter(command, aa);
            AddFirmataIlMaxParameter(command, aa);

            int readCount = 0;
            int origineItBaseMancanteCount = 0;
            int origineItUniversitariaMancanteCount = 0;
            int origineItOrdinariaConIntegrazioneEsteriCount = 0;
            int origineItOrdinariaSemestreFiltroCount = 0;
            int integrazioneItUniversitariaMancanteCount = 0;
            int studentiConModificheMultipleCount = 0;
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

                if (reader.SafeGetInt("NumeroModificheDopoScadenzaBase") > 0)
                    studentiConModificheMultipleCount++;

                if (tipoOrigine.Equals("it", StringComparison.OrdinalIgnoreCase))
                {
                    bool baseOk = reader.SafeGetInt("HasIseeBaseEntroScadenza") != 0;
                    bool coUniversitarioOk = reader.SafeGetInt("CoAttestazioneOk") != 0;
                    bool coOrdinarioConIntegrazioneEsteriOk = reader.SafeGetInt("CoOrdinarioConIntegrazioneEsteriOk") != 0;
                    bool coOrdinarioSemestreFiltroOk = reader.SafeGetInt("CoOrdinarioSemestreFiltroOk") != 0;
                    bool coOk = EsitoBorsaSupport.IsCoAdeguataOrigine(coUniversitarioOk, coOrdinarioConIntegrazioneEsteriOk, coOrdinarioSemestreFiltroOk);
                    bool origineEconomicaAdeguata = baseOk && coOk;
                    raw.CoAttestazioneOk = origineEconomicaAdeguata;

                    var key = CreateStudentKey(codFiscale, numDomanda);
                    var facts = GetOrCreateEsitoBorsaFacts(key);
                    facts.HasIseeBaseEntroScadenza = baseOk;
                    facts.HasCoUniversitarioEntroScadenza = coUniversitarioOk;
                    facts.HasCoOrdinarioConIntegrazioneEsteriEntroScadenza = coOrdinarioConIntegrazioneEsteriOk;
                    facts.HasCoOrdinarioSemestreFiltroEntroScadenza = coOrdinarioSemestreFiltroOk;
                    facts.OrigineEconomicaAdeguata = origineEconomicaAdeguata;
                    facts.MotivoAdeguatezzaOrigine = EsitoBorsaSupport.GetMotivoAdeguatezzaOrigine(
                        baseOk,
                        coUniversitarioOk,
                        coOrdinarioConIntegrazioneEsteriOk,
                        coOrdinarioSemestreFiltroOk);

                    // Regola 20252026+: non si usa più il fallback DO per rendere idoneo lo studente.
                    // Prima deve esistere un ISEE base firmato entro il 22/07; per ConfermaSemestreFiltro=1 entro il 31/12.
                    // Poi serve una CO UNIVERSITARIA/RIDOTTA/CORRENTE entro il 31/12.
                    // Eccezioni: CO ORDINARIA adeguata se il nucleo indipendente ha integrazione di redditi esteri oppure se lo studente è semestre filtro.
                    // Se manca una delle condizioni non viene caricata una fonte economica italiana: EsitoBorsaIncomeRules produrrà RED031.
                    if (origineEconomicaAdeguata)
                    {
                        if (!coUniversitarioOk && coOrdinarioConIntegrazioneEsteriOk)
                            origineItOrdinariaConIntegrazioneEsteriCount++;

                        if (!coUniversitarioOk && !coOrdinarioConIntegrazioneEsteriOk && coOrdinarioSemestreFiltroOk)
                            origineItOrdinariaSemestreFiltroCount++;

                        result.OrigIT_CO.Add(target);
                    }
                    else
                    {
                        if (!baseOk)
                            origineItBaseMancanteCount++;
                        else
                            origineItUniversitariaMancanteCount++;

                        raw.OrigineFonte = string.Empty;
                    }
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
                    bool ciOk = reader.SafeGetInt("CiAttestazioneOk") != 0;
                    var key = CreateStudentKey(codFiscale, numDomanda);
                    var facts = GetOrCreateEsitoBorsaFacts(key);
                    facts.HasCiUniversitarioEntroScadenza = ciOk;

                    if (ciOk)
                    {
                        result.IntIT_CI.Add(target);
                    }
                    else
                    {
                        integrazioneItUniversitariaMancanteCount++;
                        raw.IntegrazioneFonte = string.Empty;
                    }
                }
                else if (tipoIntegrazione.Equals("ee", StringComparison.OrdinalIgnoreCase))
                {
                    result.IntDI.Add(target);
                }
            }

            Logger.LogInfo(33, $"Tipologie reddito lette: {readCount} | OrigIT_CO={result.OrigIT_CO.Count} | OrigIT_DO={result.OrigIT_DO.Count} | OrigEE={result.OrigEE.Count} | IntIT_CI={result.IntIT_CI.Count} | IntDI={result.IntDI.Count} | OrigIT senza ISEE base entro scadenza effettiva={origineItBaseMancanteCount} | OrigIT senza CO adeguata entro 31/12={origineItUniversitariaMancanteCount} | OrigIT ordinario accettato per integrazione redditi esteri={origineItOrdinariaConIntegrazioneEsteriCount} | OrigIT ordinario accettato per semestre filtro={origineItOrdinariaSemestreFiltroCount} | IntIT senza UNIVERSITARIO entro 31/12={integrazioneItUniversitariaMancanteCount} | Certificazioni modificate dopo scadenza base effettiva={studentiConModificheMultipleCount}");
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
LEFT JOIN Valori_calcolati vv
    ON vv.Anno_accademico = @AA
   AND vv.Num_domanda     = t.NumDomanda
   AND vv.data_validita =
   (
       SELECT MAX(vv2.data_validita)
       FROM Valori_calcolati vv2
       WHERE vv2.Anno_accademico = vv.Anno_accademico
         AND vv2.Num_domanda = vv.Num_domanda
   );";

            using var command = new SqlCommand(sql, _conn);
            AddAaParameter(command, aa);
            AddDataValiditaMaxParameter(command, aa);

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
