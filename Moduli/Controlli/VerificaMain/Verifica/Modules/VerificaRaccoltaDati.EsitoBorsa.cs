using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private const string EsitoBorsaForzatureSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CONVERT(NVARCHAR(20), f.COD_FORZATURA) AS CodForzatura
FROM {TEMP_TABLE} t
JOIN FORZATURE f
  ON f.NUM_DOMANDA = CAST(t.NumDomanda AS INT)
WHERE f.ANNO_ACCADEMICO = @AA
  AND f.DATA_FINE_VALIDITA IS NULL
  AND TRY_CONVERT(INT, f.COD_FORZATURA) > 200;";

        private const string EsitoBorsaForzatureRinunciaSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT DISTINCT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale
FROM {TEMP_TABLE} t
JOIN FORZATURE_RINUNCIA f
  ON UPPER(f.COD_FISCALE) = UPPER(t.CodFiscale)
WHERE f.ANNO_ACCADEMICO = @AA
  AND f.DATA_FINE_VALIDITA IS NULL;";

        private const string EsitoBorsaCodTipoOrdinamentoSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CAST('' AS NVARCHAR(50)) AS Cod_tipo_ordinamento
FROM {TEMP_TABLE} t
WHERE 1 = 0;";

        private const string EsitoBorsaVariazioniSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    TRY_CONVERT(INT, v.COD_TIPO_VARIAZ) AS CodTipoVariaz,
    UPPER(LTRIM(RTRIM(ISNULL(v.COD_BENEFICIO,'')))) AS CodBeneficio
FROM {TEMP_TABLE} t
JOIN VARIAZIONI v
  ON v.ANNO_ACCADEMICO = @AA
 AND v.NUM_DOMANDA = CAST(t.NumDomanda AS INT)
WHERE v.DATA_VALIDITA =
(
    SELECT MAX(v2.DATA_VALIDITA)
    FROM VARIAZIONI v2
    WHERE v2.ANNO_ACCADEMICO = v.ANNO_ACCADEMICO
      AND v2.NUM_DOMANDA = v.NUM_DOMANDA
      AND v2.COD_TIPO_VARIAZ = v.COD_TIPO_VARIAZ
      AND ISNULL(v2.COD_BENEFICIO,'') = ISNULL(v.COD_BENEFICIO,'')
);";

        private const string EsitoBorsaRinunceUfficioSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(v.Num_Domanda AS INT) AS NumDomanda,
    t.CodFiscale,
    TRY_CONVERT(INT, v.Riga_valida) AS RigaValida,
    UPPER(LTRIM(RTRIM(ISNULL(v.COD_BENEFICIO,'')))) AS CodBeneficio
FROM {TEMP_TABLE} t
JOIN RINUNCE v
  ON v.ANNO_ACCADEMICO = @AA
 AND v.NUM_DOMANDA = CAST(t.NumDomanda AS INT)
WHERE v.DATA_VALIDITA =
(
    SELECT MAX(v2.DATA_VALIDITA)
    FROM RINUNCE v2
    WHERE v2.ANNO_ACCADEMICO = v.ANNO_ACCADEMICO
      AND v2.NUM_DOMANDA = v.NUM_DOMANDA
      AND v2.Riga_valida = v.Riga_valida
      AND ISNULL(v2.COD_BENEFICIO,'') = ISNULL(v.COD_BENEFICIO,'')
);";

        private const string EsitoBorsaSlashMotiviEsclusioneSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    UPPER(LTRIM(RTRIM(ISNULL(vb.Cod_beneficio,'')))) AS CodBeneficio,
    CONVERT(NVARCHAR(MAX), dbo.SlashMotiviEsclusioneTest(CAST(t.NumDomanda AS INT), @AA, UPPER(LTRIM(RTRIM(ISNULL(vb.Cod_beneficio,'')))))) AS SlashMotiviEsclusione
FROM {TEMP_TABLE} t
JOIN vBenefici_richiesti vb
  ON vb.Anno_accademico = @AA
 AND vb.Num_domanda = CAST(t.NumDomanda AS INT);";

        private const string EsitoBorsaIscrizioneAlignmentSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT DISTINCT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        t.CodFiscale AS CodFiscale
    FROM {TEMP_TABLE} t
),
MER AS
(
    SELECT
        D.NumDomanda,
        TRY_CONVERT(INT, m.CARRIERA_INTERR) AS CarrieraInterr,
        TRY_CONVERT(INT, m.NUM_ANNI_INTERR) AS NumAnniInterr,
        TRY_CONVERT(DECIMAL(18,2), m.CREDITI_EXTRA_CURRICULARI) AS CreditiExtraCurriculari,
        TRY_CONVERT(INT, m.MESE_IMMATRICOLAZ) AS MeseImmatricolaz
    FROM D
    LEFT JOIN MERITO m
      ON m.ANNO_ACCADEMICO = @AA
     AND m.NUM_DOMANDA = D.NumDomanda
     AND m.DATA_VALIDITA =
     (
        SELECT MAX(m2.DATA_VALIDITA)
        FROM MERITO m2
        WHERE m2.ANNO_ACCADEMICO = m.ANNO_ACCADEMICO
          AND m2.NUM_DOMANDA = m.NUM_DOMANDA
     )
),
ISCR AS
(
    SELECT
        D.NumDomanda,
        TRY_CONVERT(INT, i.SEMESTRE) AS Semestre,
        TRY_CONVERT(INT, i.RIPETENTE) AS Ripetente
    FROM D
    LEFT JOIN ISCRIZIONI i
      ON i.COD_FISCALE = D.CodFiscale
     AND i.ANNO_ACCADEMICO = @AA
     AND i.DATA_VALIDITA =
     (
        SELECT MAX(i2.DATA_VALIDITA)
        FROM ISCRIZIONI i2
        WHERE i2.COD_FISCALE = i.COD_FISCALE
          AND i2.ANNO_ACCADEMICO = i.ANNO_ACCADEMICO
          AND (i2.TIPO_BANDO IS NULL OR i2.TIPO_BANDO LIKE 'L%')
     )
),
TSPASS AS
(
    SELECT
        D.NumDomanda,
        CAST(1 AS BIT) AS PassaggioTrasferimento,
        CAST(CASE WHEN ISNULL(ts.RIPETENTE,0) = 1 THEN 1 ELSE 0 END AS BIT) AS RipetenteDaPassaggio,
        TRY_CONVERT(INT, ts.ANNO_AVVENIMENTO) AS AnnoAvvenimentoTs,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY TRY_CONVERT(INT, ts.ANNO_AVVENIMENTO) DESC, ts.COD_FISCALE) AS rn
    FROM D
    INNER JOIN {TS_PASSAGGIO_SOURCE} ts
      ON ts.ANNO_ACCADEMICO = @AA
     AND ts.COD_FISCALE = D.CodFiscale
),
TSCP AS
(
    SELECT
        D.NumDomanda,
        TRY_CONVERT(INT, ts.PRIMA_IMMATRICOLAZ) AS PrimaImmatricolazTs,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY ts.DATA_VALIDITA DESC) AS rn
    FROM D
    INNER JOIN CARRIERA_PREGRESSA ts
      ON ts.ANNO_ACCADEMICO = @AA
     AND ts.COD_FISCALE = D.CodFiscale
     AND ts.COD_AVVENIMENTO = 'TS'
)
SELECT
    D.NumDomanda,
    D.CodFiscale,
    ISNULL(MER.CarrieraInterr, 0) AS CarrieraInterr,
    ISNULL(MER.NumAnniInterr, 0) AS NumAnniInterr,
    ISNULL(MER.CreditiExtraCurriculari, 0) AS CreditiExtraCurriculari,
    ISNULL(MER.MeseImmatricolaz, 0) AS MeseImmatricolaz,
    ISNULL(ISCR.Semestre, 0) AS Semestre,
    ISNULL(ISCR.Ripetente, 0) AS Ripetente,
    ISNULL(TSPASS.PassaggioTrasferimento, CAST(0 AS BIT)) AS PassaggioTrasferimento,
    ISNULL(TSPASS.RipetenteDaPassaggio, CAST(0 AS BIT)) AS RipetenteDaPassaggio,
    TSCP.PrimaImmatricolazTs,
    TSPASS.AnnoAvvenimentoTs
FROM D
LEFT JOIN MER ON MER.NumDomanda = D.NumDomanda
LEFT JOIN ISCR ON ISCR.NumDomanda = D.NumDomanda
LEFT JOIN TSPASS ON TSPASS.NumDomanda = D.NumDomanda AND TSPASS.rn = 1
LEFT JOIN TSCP ON TSCP.NumDomanda = D.NumDomanda AND TSCP.rn = 1;";

        private static readonly string[] CarrieraTitoloAvvenimentoMarkers =
        {
            "CONSEG", "LAUR", "DIPL", "TITOLO", "ABIL"
        };

        private void LoadEsitoBorsaSupportFacts(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.EsitoBorsaFactsByStudent.Clear();
            foreach (var key in context.Students.Keys)
                context.EsitoBorsaFactsByStudent[key] = new EsitoBorsaFacts();

            LoadEsitoBorsaForzature(context);
            LoadEsitoBorsaForzatureRinuncia(context);
            LoadEsitoBorsaCodTipoOrdinamento(context);
            LoadEsitoBorsaGeneralFacts(context);
            LoadEsitoBorsaBenefitRequestFacts(context);
            LoadEsitoBorsaIscrizioneAlignmentFacts(context);
            LoadEsitoBorsaVariazioni(context);
            LoadEsitoBorsaRinunceUfficio(context);
            LoadEsitoBorsaSlashMotiviEsclusione(context);
            LoadEsitoBorsaRedditoUeFacts(context);
            LoadEsitoBorsaLaureaSpecFacts(context);
            BuildEsitoBorsaPregressaFacts(context);
            BuildEsitoBorsaGeneralFactsFromCarrieraPregressa(context);
        }

        private void LoadEsitoBorsaForzature(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaForzatureSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                string code = reader.SafeGetString("CodForzatura").Trim();
                if (!string.IsNullOrWhiteSpace(code))
                    facts.ForzatureGenerali.Add(code);
            });
        }

        private void LoadEsitoBorsaForzatureRinuncia(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaForzatureRinunciaSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                facts.ForzaturaRinunciaNoEsclusione = true;
            });
        }

        private void LoadEsitoBorsaCodTipoOrdinamento(VerificaPipelineContext context)
        {
            foreach (var pair in context.Students)
            {
                var facts = GetOrCreateEsitoBorsaFacts(context, pair.Key);
                facts.CodTipoOrdinamento = (pair.Value.InformazioniIscrizione.CodTipoOrdinamentoCorso ?? string.Empty).Trim();
            }
        }

        private void LoadEsitoBorsaVariazioni(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaVariazioniSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                ApplyVariazione(facts, reader.SafeGetInt("CodTipoVariaz"), reader.SafeGetString("CodBeneficio"));
            });
        }

        private void LoadEsitoBorsaRinunceUfficio(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaRinunceUfficioSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                ApplyRinunciaUfficio(facts, reader.SafeGetInt("RigaValida"), reader.SafeGetString("CodBeneficio"));
            });
        }

        private void LoadEsitoBorsaSlashMotiviEsclusione(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaSlashMotiviEsclusioneSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                string beneficio = NormalizeUpper(reader.SafeGetString("CodBeneficio"));
                string slash = reader.SafeGetString("SlashMotiviEsclusione").Trim();
                if (beneficio.Length == 0)
                    return;

                facts.SlashMotiviEsclusioneByBenefit[beneficio] = slash;
                if (string.Equals(beneficio, "BS", StringComparison.OrdinalIgnoreCase))
                    facts.SlashMotiviEsclusioneBS = slash;
            });
        }

        private static void ApplyRinunciaUfficio(EsitoBorsaFacts facts, int rigaValida, string? codBeneficio)
        {
            if (facts == null)
                return;

            string beneficio = NormalizeUpper(codBeneficio);
            if(rigaValida == 0)
            {
                ApplyVariazione(facts, 2, beneficio);
            }
        }

        private static void ApplyVariazione(EsitoBorsaFacts facts, int codTipoVariaz, string? codBeneficio)
        {
            if (facts == null)
                return;

            string beneficio = NormalizeUpper(codBeneficio);
            switch (codTipoVariaz)
            {
                case 2:
                case 9:
                case 10:
                    ApplyRinuncia(facts, beneficio);
                    break;
                case 3:
                    if (beneficio == "00")
                        facts.Revocato = true;
                    else if (beneficio == "CI")
                        facts.RevocatoBandoCI = true;
                    break;
                case 4:
                    ApplyDecadenza(facts, beneficio);
                    break;
                case 11:
                    ApplyRevocaBando(facts, beneficio);
                    break;
                case 18:
                    if (beneficio == "PA") facts.RevocatoSedeDistaccata = true;
                    break;
                case 19:
                    if (beneficio == "00") facts.RevocatoMancataIscrizione = true;
                    break;
                case 20:
                    if (beneficio == "00") facts.RevocatoIscrittoRipetente = true;
                    break;
                case 21:
                    if (beneficio == "00") facts.RevocatoISEE = true;
                    break;
                case 22:
                    if (beneficio == "00") facts.RevocatoLaureato = true;
                    break;
                case 23:
                    if (beneficio == "00") facts.RevocatoPatrimonio = true;
                    break;
                case 24:
                    if (beneficio == "00") facts.RevocatoReddito = true;
                    break;
                case 25:
                    if (beneficio == "00") facts.RevocatoEsami = true;
                    break;
                case 26:
                    if (beneficio == "00") facts.RinunciaBenefici = true;
                    break;
                case 27:
                    if (beneficio == "00") facts.RevocatoFuoriTermine = true;
                    break;
                case 28:
                    if (beneficio == "00") facts.RevocatoIseeFuoriTermine = true;
                    break;
                case 29:
                    if (beneficio == "00") facts.RevocatoIseeNonProdotta = true;
                    break;
                case 30:
                    if (beneficio == "00") facts.RevocatoTrasmissioneIseeFuoriTermine = true;
                    break;
                case 31:
                    if (beneficio == "00") facts.RevocatoNoContrattoLocazione = true;
                    break;
            }
        }

        private static void ApplyRinuncia(EsitoBorsaFacts facts, string beneficio)
        {
            switch (beneficio)
            {
                case "BS": facts.RinunciaBS = true; break;
                case "PA": facts.RinunciaPA = true; break;
                case "CM": facts.RinunciaCM = true; break;
                case "CT": facts.RinunciaCT = true; break;
                case "CI": facts.RinunciaCI = true; break;
                case "00": facts.RinunciaBenefici = true; break;
            }
        }

        private static void ApplyDecadenza(EsitoBorsaFacts facts, string beneficio)
        {
            switch (beneficio)
            {
                case "BS": facts.DecadutoBS = true; break;
                case "PA": facts.DecadutoPA = true; break;
                case "CM": facts.DecadutoCM = true; break;
                case "CT": facts.DecadutoCT = true; break;
                case "CI": facts.DecadutoCI = true; break;
            }
        }

        private static void ApplyRevocaBando(EsitoBorsaFacts facts, string beneficio)
        {
            switch (beneficio)
            {
                case "BS": facts.RevocatoBandoBS = true; break;
                case "PA": facts.RevocatoBandoPA = true; break;
                case "CM": facts.RevocatoBandoCM = true; break;
                case "CT": facts.RevocatoBandoCT = true; break;
                case "CI": facts.RevocatoBandoCI = true; break;
                case "00": facts.Revocato = true; break;
            }
        }

        private void LoadEsitoBorsaIscrizioneAlignmentFacts(VerificaPipelineContext context)
        {
            string tsPassaggioSource = ObjectExists(context.Connection, "vCarriera_pregressa_TS")
                ? "vCarriera_pregressa_TS"
                : "(SELECT CAST(NULL AS NVARCHAR(50)) AS COD_FISCALE, CAST(NULL AS NVARCHAR(8)) AS ANNO_ACCADEMICO, CAST(NULL AS INT) AS RIPETENTE, CAST(NULL AS INT) AS ANNO_AVVENIMENTO WHERE 1=0)";

            string sql = EsitoBorsaIscrizioneAlignmentSql.Replace("{TS_PASSAGGIO_SOURCE}", tsPassaggioSource);
            using var cmd = CreatePopulationCommand(sql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                var iscr = info.InformazioniIscrizione;

                facts.CarrieraInterrotta = GetNullableBool(reader, "CarrieraInterr");
                facts.NumAnniInterruzione = GetNullableInt(reader, "NumAnniInterr");
                facts.CreditiExtraCurriculari = reader.SafeGetDecimal("CreditiExtraCurriculari");
                facts.MeseImmatricolazione = GetNullableInt(reader, "MeseImmatricolaz");
                facts.Semestre = GetNullableInt(reader, "Semestre");
                facts.IscrittoRipetente = GetNullableBool(reader, "Ripetente");
                facts.PassaggioTrasferimento = GetNullableBool(reader, "PassaggioTrasferimento") ?? false;
                facts.RipetenteDaPassaggio = GetNullableBool(reader, "RipetenteDaPassaggio") ?? false;
                facts.PrimaImmatricolazTs = GetNullableInt(reader, "PrimaImmatricolazTs");
                facts.AaTrasferimento = GetNullableInt(reader, "AnnoAvvenimentoTs");

                if (facts.IscrittoRipetente == true)
                    facts.RevocatoIscrittoRipetente = true;

                decimal rawCrediti = iscr.NumeroCrediti ?? 0m;
                decimal creditiDaRinuncia = iscr.CreditiRiconosciutiDaRinuncia ?? 0m;
                decimal creditiExtra = facts.CreditiExtraCurriculari ?? 0m;
                if (rawCrediti != 0m && (facts.PassaggioTrasferimento != true || facts.RipetenteDaPassaggio == true))
                    iscr.NumeroCrediti = rawCrediti - creditiDaRinuncia - creditiExtra;

            });
        }

        private void LoadEsitoBorsaGeneralFacts(VerificaPipelineContext context)
        {
            string sql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT DISTINCT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        t.CodFiscale AS CodFiscale
    FROM {TEMP_TABLE} t
),
D_CF AS
(
    SELECT DISTINCT
        @AA AS AnnoAccademico,
        CodFiscale
    FROM D
),
DG_RANKED AS
(
    SELECT
        dg.NUM_DOMANDA AS NumDomanda,
        TRY_CONVERT(INT, dg.TIPO_STUDENTE) AS TipoStudente,
        TRY_CONVERT(BIT, dg.PERMESSO_SOGG) AS PermessoSoggiorno,
        TRY_CONVERT(BIT, dg.PERMESSO_SOGG_PROVV) AS PermessoSoggProvv,
        TRY_CONVERT(BIT, dg.ISCRIZIONE_FUORITERMINE) AS IscrizioneFuoriTermine,
        TRY_CONVERT(BIT, dg.RINUNCIA_IN_CORSO) AS RinunciaBenefici,
        TRY_CONVERT(BIT, dg.NUBILE_PROLE) AS NubileProle,
        ROW_NUMBER() OVER
        (
            PARTITION BY dg.NUM_DOMANDA
            ORDER BY dg.DATA_VALIDITA DESC
        ) AS RN
    FROM DATIGENERALI_DOM dg
    INNER JOIN D
        ON D.NumDomanda = dg.NUM_DOMANDA
),
DG AS
(
    SELECT
        NumDomanda,
        TipoStudente,
        PermessoSoggiorno,
        PermessoSoggProvv,
        IscrizioneFuoriTermine,
        RinunciaBenefici,
        NubileProle
    FROM DG_RANKED
    WHERE RN = 1
),
SC AS
(
    SELECT
        sc.NUM_DOMANDA AS NumDomanda,
        CAST(ISNULL(sc.STATUS_COMPILAZIONE, 0) AS INT) AS StatusCompilazione
    FROM vSTATUS_COMPILAZIONE sc
    INNER JOIN D
        ON D.NumDomanda = sc.NUM_DOMANDA
    WHERE sc.ANNO_ACCADEMICO = @AA
),
CIT_RANKED AS
(
    SELECT
        c.COD_FISCALE AS CodFiscale,
        ISNULL(c.COD_CITTADINANZA, 'Z000') AS CodCittadinanza,
        ROW_NUMBER() OVER
        (
            PARTITION BY c.COD_FISCALE
            ORDER BY c.DATA_VALIDITA DESC
        ) AS RN
    FROM CITTADINANZA c
    INNER JOIN D_CF
        ON D_CF.CodFiscale = c.COD_FISCALE
),
CIT_LATEST AS
(
    SELECT
        CodFiscale,
        CodCittadinanza
    FROM CIT_RANKED
    WHERE RN = 1
),
CIT AS
(
    SELECT
        D.NumDomanda,
        CAST(CASE WHEN ISNULL(c.CodCittadinanza, 'Z000') <> 'Z000' THEN 1 ELSE 0 END AS BIT) AS Straniero,
        CAST
        (
            CASE
                WHEN ISNULL(c.CodCittadinanza, 'Z000') <> 'Z000'
                 AND EXISTS
                     (
                         SELECT 1
                         FROM CITTADINANZE_UE cue
                         WHERE cue.CODICE = c.CodCittadinanza
                     )
                THEN 1
                ELSE 0
            END
            AS BIT
        ) AS CittadinanzaUe
    FROM D
    LEFT JOIN CIT_LATEST c
        ON c.CodFiscale = D.CodFiscale
),
RES AS
(
    SELECT
        D.NumDomanda,
        CAST
        (
            CASE
                WHEN NULLIF(LTRIM(RTRIM(ISNULL(r.COD_COMUNE, ''))), '') IS NULL THEN 0
                WHEN LEFT(ISNULL(r.COD_COMUNE, ''), 1) = 'Z' THEN
                    CASE
                        WHEN EXISTS
                             (
                                 SELECT 1
                                 FROM CITTADINANZE_UE cue
                                 WHERE cue.CODICE = r.COD_COMUNE
                             )
                        THEN 1
                        ELSE 0
                    END
                ELSE 1
            END
            AS BIT
        ) AS ResidenzaUe
    FROM D
    LEFT JOIN vRESIDENZA r
        ON r.COD_FISCALE = D.CodFiscale
       AND r.ANNO_ACCADEMICO = @AA
       AND r.TIPO_BANDO = 'LZ'
),
VC_RANKED AS
(
    SELECT
        vc.NUM_DOMANDA AS NumDomanda,
        TRY_CONVERT(INT, vc.STATUS_ISEE) AS StatusIsee,
        ROW_NUMBER() OVER
        (
            PARTITION BY vc.NUM_DOMANDA
            ORDER BY vc.DATA_VALIDITA DESC
        ) AS RN
    FROM VALORI_CALCOLATI vc
    INNER JOIN D
        ON D.NumDomanda = vc.NUM_DOMANDA
),
VC AS
(
    SELECT
        NumDomanda,
        StatusIsee
    FROM VC_RANKED
    WHERE RN = 1
),
CERT AS
(
    SELECT
        D.NumDomanda,
        CASE
            WHEN MAX(CASE WHEN UPPER(ISNULL(ci.Cod_tipo_attestazione, '')) = 'UNIVERSITARIO' THEN 1 ELSE 0 END) = 1 THEN 'UNIV'
            WHEN MAX(CASE WHEN UPPER(ISNULL(ci.Cod_tipo_attestazione, '')) = 'RIDOTTO' THEN 1 ELSE 0 END) = 1 THEN 'RID'
            WHEN MAX(CASE WHEN ISNULL(ci.Cod_tipo_attestazione, '') <> '' THEN 1 ELSE 0 END) = 1 THEN 'ORD'
            ELSE ''
        END AS TipoCertificazione
    FROM D
    LEFT JOIN vCertificaz_ISEE ci
        ON ci.anno_accademico = @AA
       AND ci.num_domanda = D.NumDomanda
       AND ci.Tipologia_certificazione = 'CO'
    GROUP BY D.NumDomanda
),
CP_CD_RANKED AS
(
    SELECT
        cp.ANNO_ACCADEMICO AS AnnoAccademico,
        cp.COD_FISCALE AS CodFiscale,
        TRY_CONVERT(INT, cp.RIGA_VALIDA) AS RigaValida,
        TRY_CONVERT(INT, cp.TIPOLOGIA_CORSO) AS TipologiaCorso,
        TRY_CONVERT(INT, cp.DURATA_LEG_TITOLO_CONSEGUITO) AS DurataLegTitoloConseguito,
        ROW_NUMBER() OVER
        (
            PARTITION BY cp.ANNO_ACCADEMICO, cp.COD_FISCALE
            ORDER BY cp.DATA_VALIDITA DESC
        ) AS RN
    FROM CARRIERA_PREGRESSA cp
    INNER JOIN D_CF
        ON D_CF.AnnoAccademico = cp.ANNO_ACCADEMICO
       AND D_CF.CodFiscale = cp.COD_FISCALE
    WHERE cp.ANNO_ACCADEMICO = @AA
      AND cp.COD_AVVENIMENTO = 'CD'
),
CP_CD_LATEST AS
(
    SELECT
        AnnoAccademico,
        CodFiscale,
        RigaValida,
        TipologiaCorso,
        DurataLegTitoloConseguito
    FROM CP_CD_RANKED
    WHERE RN = 1
),
CP_CD AS
(
    SELECT
        D.NumDomanda,
        cp.RigaValida,
        cp.TipologiaCorso,
        cp.DurataLegTitoloConseguito
    FROM D
    LEFT JOIN CP_CD_LATEST cp
        ON cp.AnnoAccademico = @AA
       AND cp.CodFiscale = D.CodFiscale
),
CP_AT_RANKED AS
(
    SELECT
        cp.ANNO_ACCADEMICO AS AnnoAccademico,
        cp.COD_FISCALE AS CodFiscale,
        TRY_CONVERT(INT, cp.RIGA_VALIDA) AS RigaValida,
        TRY_CONVERT(INT, cp.TIPOLOGIA_CORSO) AS TipologiaCorso,
        ROW_NUMBER() OVER
        (
            PARTITION BY cp.ANNO_ACCADEMICO, cp.COD_FISCALE
            ORDER BY cp.DATA_VALIDITA DESC
        ) AS RN
    FROM CARRIERA_PREGRESSA cp
    INNER JOIN D_CF
        ON D_CF.AnnoAccademico = cp.ANNO_ACCADEMICO
       AND D_CF.CodFiscale = cp.COD_FISCALE
    WHERE cp.ANNO_ACCADEMICO = @AA
      AND cp.COD_AVVENIMENTO = 'AT'
),
CP_AT_LATEST AS
(
    SELECT
        AnnoAccademico,
        CodFiscale,
        RigaValida,
        TipologiaCorso
    FROM CP_AT_RANKED
    WHERE RN = 1
),
CP_AT AS
(
    SELECT
        D.NumDomanda,
        cp.RigaValida,
        cp.TipologiaCorso
    FROM D
    LEFT JOIN CP_AT_LATEST cp
        ON cp.AnnoAccademico = @AA
       AND cp.CodFiscale = D.CodFiscale
)
SELECT
    D.NumDomanda,
    D.CodFiscale,
    DG.IscrizioneFuoriTermine,
    CAST(CASE WHEN ISNULL(DG.PermessoSoggiorno, CAST(0 AS BIT)) = 1 OR ISNULL(DG.PermessoSoggProvv, CAST(0 AS BIT)) = 1 THEN 1 ELSE 0 END AS BIT) AS PermessoSoggiorno,
    CAST(CASE
            WHEN ISNULL(SC.StatusCompilazione, 0) >= 90 THEN 1
            ELSE 0 END AS BIT) AS DomandaTrasmessa,
    ISNULL(VC.StatusIsee, 0) AS StatusIsee,
    ISNULL(CERT.TipoCertificazione, '') AS TipoCertificazione,
    CAST(CASE WHEN ISNULL(CP_CD.RigaValida, 1) = 0 THEN 1 ELSE 0 END AS BIT) AS TitoloAccademicoConseguito,
    CAST(CASE WHEN ISNULL(CP_AT.RigaValida, 1) = 0 THEN 1 ELSE 0 END AS BIT) AS AttesaTitoloAccademicoConseguito,
    CASE
        WHEN ISNULL(DG.TipoStudente, 0) IN (2,3,6) THEN 2
        WHEN ISNULL(DG.TipoStudente, 0) IN (4,5) THEN 1
        ELSE ISNULL(DG.TipoStudente, 0)
    END AS TipoStudenteNormalizzato,
    CAST(CASE
            WHEN CASE
                    WHEN ISNULL(DG.TipoStudente, 0) IN (2,3,6) THEN 2
                    WHEN ISNULL(DG.TipoStudente, 0) IN (4,5) THEN 1
                    ELSE ISNULL(DG.TipoStudente, 0)
                 END = 2
            THEN 1 ELSE 0 END AS BIT) AS IsConferma,
    ISNULL(CIT.Straniero, CAST(0 AS BIT)) AS Straniero,
    ISNULL(CIT.CittadinanzaUe, CAST(0 AS BIT)) AS CittadinanzaUe,
    ISNULL(RES.ResidenzaUe, CAST(0 AS BIT)) AS ResidenzaUe,
    ISNULL(DG.NubileProle, CAST(0 AS BIT)) AS NubileProle,
    CASE
        WHEN ISNULL(CP_CD.RigaValida, 1) = 0 THEN ISNULL(CP_CD.TipologiaCorso, 0)
        WHEN ISNULL(CP_AT.RigaValida, 1) = 0 THEN ISNULL(CP_AT.TipologiaCorso, 0)
        ELSE 0
    END AS TipologiaStudiTitoloConseguito,
    CASE
        WHEN ISNULL(CP_CD.RigaValida, 1) = 0 THEN ISNULL(CP_CD.DurataLegTitoloConseguito, 0)
        ELSE 0
    END AS DurataLegTitoloConseguito
FROM D
LEFT JOIN DG
    ON DG.NumDomanda = D.NumDomanda
LEFT JOIN SC
    ON SC.NumDomanda = D.NumDomanda
LEFT JOIN CIT
    ON CIT.NumDomanda = D.NumDomanda
LEFT JOIN RES
    ON RES.NumDomanda = D.NumDomanda
LEFT JOIN VC
    ON VC.NumDomanda = D.NumDomanda
LEFT JOIN CERT
    ON CERT.NumDomanda = D.NumDomanda
LEFT JOIN CP_CD
    ON CP_CD.NumDomanda = D.NumDomanda
LEFT JOIN CP_AT
    ON CP_AT.NumDomanda = D.NumDomanda
OPTION (RECOMPILE);";

            using var cmd = CreatePopulationCommand(sql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);

                facts.IscrizioneFuoriTermine = GetNullableBool(reader, "IscrizioneFuoriTermine");
                facts.PermessoSoggiorno = GetNullableBool(reader, "PermessoSoggiorno");
                facts.DomandaTrasmessa = GetNullableBool(reader, "DomandaTrasmessa");
                facts.StatusIsee = GetNullableInt(reader, "StatusIsee");
                facts.TipoCertificazione = reader.SafeGetString("TipoCertificazione").Trim();
                facts.TipoStudenteNormalizzato = GetNullableInt(reader, "TipoStudenteNormalizzato");
                facts.TitoloAccademicoConseguito = GetNullableBool(reader, "TitoloAccademicoConseguito");
                facts.AttesaTitoloAccademicoConseguito = GetNullableBool(reader, "AttesaTitoloAccademicoConseguito");
                facts.IsConferma = GetNullableBool(reader, "IsConferma");
                facts.Straniero = GetNullableBool(reader, "Straniero");
                facts.CittadinanzaUe = GetNullableBool(reader, "CittadinanzaUe");
                facts.ResidenzaUe = GetNullableBool(reader, "ResidenzaUe");
                facts.NubileProle = GetNullableBool(reader, "NubileProle");
                facts.TipologiaStudiTitoloConseguito = GetNullableInt(reader, "TipologiaStudiTitoloConseguito");
                facts.DurataLegTitoloConseguito = GetNullableInt(reader, "DurataLegTitoloConseguito");
            });
        }

        private void LoadEsitoBorsaBenefitRequestFacts(VerificaPipelineContext context)
        {
            const string sql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    UPPER(LTRIM(RTRIM(ISNULL(vb.Cod_beneficio,'')))) AS CodBeneficio
FROM {TEMP_TABLE} t
JOIN vBenefici_richiesti vb
  ON vb.Anno_accademico = @AA
 AND vb.Num_domanda = CAST(t.NumDomanda AS INT);";

            using var cmd = CreatePopulationCommand(sql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                string beneficio = NormalizeUpper(reader.SafeGetString("CodBeneficio"));
                if (beneficio.Length == 0)
                    return;

                facts.BeneficiRichiesti.Add(beneficio);
                if (string.Equals(beneficio, "CS", StringComparison.OrdinalIgnoreCase))
                    facts.RichiestaCS = true;
            });
        }

        private void LoadEsitoBorsaRedditoUeFacts(VerificaPipelineContext context)
        {
            if (!ObjectExists(context.Connection, "vNucleo_fam_stranieri_DO") || !ObjectExists(context.Connection, "Cittadinanze_Ue"))
                return;

            string sql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CAST(
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM vNucleo_fam_stranieri_DO n
            INNER JOIN Cittadinanze_Ue cu
                ON cu.codice = n.Cod_stato_dic
            WHERE n.Anno_accademico = @AA
              AND n.Num_domanda = CAST(t.NumDomanda AS INT)
        )
        THEN 1 ELSE 0 END
    AS BIT) AS RedditoUe
FROM {TEMP_TABLE} t;";

            using var cmd = CreatePopulationCommand(sql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                facts.RedditoUe = GetNullableBool(reader, "RedditoUe") ?? false;
            });
        }

        private void LoadEsitoBorsaLaureaSpecFacts(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (!ObjectExists(context.Connection, "CONTROLLO_SPECIALISTICA"))
                return;

            const string sql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    UPPER(LTRIM(RTRIM(ISNULL(COD_ENTE,'')))) AS CodEnte,
    UPPER(LTRIM(RTRIM(ISNULL(COD_CORSO_LAUREA,'')))) AS CodCorsoLaurea
FROM CONTROLLO_SPECIALISTICA
WHERE ANNO_ACCADEMICO = @AA
  AND DATA_FINE_VALIDITA IS NULL;";

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand(sql, context.Connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            })
            {
                AddAaParameter(cmd, context.AnnoAccademico);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string ente = reader.SafeGetString("CodEnte").Trim().ToUpperInvariant();
                    string corso = reader.SafeGetString("CodCorsoLaurea").Trim().ToUpperInvariant();
                    if (ente.Length == 0 || corso.Length == 0)
                        continue;

                    allowed.Add($"{ente}|{corso}");
                }
            }

            if (allowed.Count == 0)
                return;

            foreach (var pair in context.Students)
            {
                var iscr = pair.Value?.InformazioniIscrizione;
                if (iscr == null)
                    continue;

                string ente = (iscr.CodEnte ?? string.Empty).Trim().ToUpperInvariant();
                string corso = (iscr.CodCorsoLaurea ?? string.Empty).Trim().ToUpperInvariant();
                if (ente.Length == 0 || corso.Length == 0)
                    continue;

                if (!allowed.Contains($"{ente}|{corso}"))
                    continue;

                var facts = GetOrCreateEsitoBorsaFacts(context, pair.Key);
                facts.RichiedeControlloLaureaSpec = true;
            }
        }

        private void BuildEsitoBorsaPregressaFacts(VerificaPipelineContext context)
        {
            foreach (var pair in context.Students)
            {
                var info = pair.Value;
                var facts = GetOrCreateEsitoBorsaFacts(context, pair.Key);
                facts.UsufruitoBeneficioBorsaNonRestituito = false;
                facts.RinunciaBorsa = false;

                var items = info.InformazioniIscrizione?.CarrierePregresse;
                if (items == null || items.Count == 0)
                    continue;

                foreach (var item in items)
                {
                    string benefici = NormalizeUpper(item?.BeneficiUsufruiti);
                    string restituzioni = NormalizeUpper(item?.ImportiRestituiti);
                    string codAvvenimento = NormalizeUpper(item?.CodAvvenimento);

                    bool hasBorsa = HasBorsaMarker(benefici);
                    bool hasRestituzione = HasMeaningfulRestitution(restituzioni);

                    if (hasBorsa && !hasRestituzione)
                        facts.UsufruitoBeneficioBorsaNonRestituito = true;

                    if (IsRinunciaBorsa(codAvvenimento, benefici))
                        facts.RinunciaBorsa = true;

                    AddPregressaBenefitFacts(facts, benefici, restituzioni, codAvvenimento);
                }
            }
        }

        private void BuildEsitoBorsaGeneralFactsFromCarrieraPregressa(VerificaPipelineContext context)
        {
            foreach (var pair in context.Students)
            {
                var info = pair.Value;
                var facts = GetOrCreateEsitoBorsaFacts(context, pair.Key);
                var items = info.InformazioniIscrizione?.CarrierePregresse;
                if (items == null || items.Count == 0)
                    continue;

                InformazioniCarrieraPregressa? bestCd = null;
                int bestCdYear = int.MinValue;
                InformazioniCarrieraPregressa? bestAt = null;
                int bestAtYear = int.MinValue;
                InformazioniCarrieraPregressa? bestGeneric = null;
                int bestGenericYear = int.MinValue;

                foreach (var item in items)
                {
                    if (item == null)
                        continue;

                    bool looksLikeTitle = LooksLikeCareerTitleRecord(item);
                    if (!looksLikeTitle)
                        continue;

                    string codAvvenimento = NormalizeUpper(item.CodAvvenimento);
                    int year = item.AnnoAvvenimento ?? int.MinValue;

                    if (codAvvenimento == "CD")
                    {
                        if (bestCd == null || year > bestCdYear)
                        {
                            bestCd = item;
                            bestCdYear = year;
                        }
                        continue;
                    }

                    if (codAvvenimento == "AT")
                    {
                        if (bestAt == null || year > bestAtYear)
                        {
                            bestAt = item;
                            bestAtYear = year;
                        }
                        continue;
                    }

                    if (bestGeneric == null || year > bestGenericYear)
                    {
                        bestGeneric = item;
                        bestGenericYear = year;
                    }
                }

                InformazioniCarrieraPregressa? best = bestCd ?? bestAt ?? bestGeneric;
                if (best == null)
                    continue;

                string bestCodAvvenimento = NormalizeUpper(best.CodAvvenimento);

                if (!facts.TitoloAccademicoConseguito.HasValue)
                    facts.TitoloAccademicoConseguito = bestCodAvvenimento == "CD";

                if (!facts.AttesaTitoloAccademicoConseguito.HasValue)
                    facts.AttesaTitoloAccademicoConseguito = bestCodAvvenimento == "AT";

                if (!facts.TipologiaStudiTitoloConseguito.HasValue && TryParseCareerTitleType(best.TipologiaCorso, out int tipologiaTitolo))
                    facts.TipologiaStudiTitoloConseguito = tipologiaTitolo;

                if (!facts.DurataLegTitoloConseguito.HasValue && best.DurataLegTitoloConseguito.HasValue)
                    facts.DurataLegTitoloConseguito = best.DurataLegTitoloConseguito.Value;

                if (string.IsNullOrWhiteSpace(facts.SedeIstituzioneUniversitariaTitolo))
                    facts.SedeIstituzioneUniversitariaTitolo = (best.SedeIstituzioneUniversitaria ?? string.Empty).Trim();

                if (!facts.RiconoscimentoTitoloEstero.HasValue)
                    facts.RiconoscimentoTitoloEstero = best.PassaggioCorsoEstero != 0;
            }
        }

        private static bool LooksLikeCareerTitleRecord(InformazioniCarrieraPregressa item)
        {
            if (item == null)
                return false;

            if (item.DurataLegTitoloConseguito.HasValue && item.DurataLegTitoloConseguito.Value > 0)
                return true;

            string code = NormalizeUpper(item.CodAvvenimento);
            foreach (string marker in CarrieraTitoloAvvenimentoMarkers)
            {
                if (code.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryParseCareerTitleType(string? value, out int tipologia)
        {
            tipologia = 0;
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tipologia);
        }

        private static bool IsRinunciaBorsa(string codAvvenimento, string benefici)
        {
            bool rinuncia = codAvvenimento.Contains("RIN", StringComparison.OrdinalIgnoreCase)
                            || benefici.Contains("RINUNC", StringComparison.OrdinalIgnoreCase);
            return rinuncia && (HasBorsaMarker(benefici) || codAvvenimento.Contains("BS", StringComparison.OrdinalIgnoreCase));
        }

        private static readonly string[] KnownBenefitCodes = { "BS", "PA", "CS", "CM", "CT", "CI" };

        private static bool HasBenefitMarker(string value, string beneficio)
        {
            beneficio = NormalizeUpper(beneficio);
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(beneficio))
                return false;

            if (beneficio == "BS")
                return HasBorsaMarker(value);

            string pattern = $@"(^|[^A-Z])({Regex.Escape(beneficio)})([^A-Z]|$)";
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void AddPregressaBenefitFacts(EsitoBorsaFacts facts, string benefici, string restituzioni, string codAvvenimento)
        {
            foreach (string beneficio in KnownBenefitCodes)
            {
                bool hasBenefit = HasBenefitMarker(benefici, beneficio);
                bool hasRestituzione = HasMeaningfulRestitution(restituzioni);

                if (hasBenefit && !hasRestituzione)
                    facts.BeneficiPregressiNonRestituiti.Add(beneficio);

                if (IsRinunciaBenefit(codAvvenimento, benefici, beneficio))
                    facts.BeneficiRinunciaPregressa.Add(beneficio);
            }
        }

        private static bool IsRinunciaBenefit(string codAvvenimento, string benefici, string beneficio)
        {
            bool rinuncia = codAvvenimento.Contains("RIN", StringComparison.OrdinalIgnoreCase)
                            || benefici.Contains("RINUNC", StringComparison.OrdinalIgnoreCase);
            return rinuncia && (HasBenefitMarker(benefici, beneficio) || codAvvenimento.Contains(beneficio, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasBorsaMarker(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return Regex.IsMatch(value, @"(^|[^A-Z])(BS|BORSA)([^A-Z]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                   || value.Contains("BORSA DI STUDIO", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasMeaningfulRestitution(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (char ch in value)
            {
                if (ch >= '1' && ch <= '9')
                    return true;
            }

            return value.Contains("RESTIT", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUpper(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        private static EsitoBorsaFacts GetOrCreateEsitoBorsaFacts(VerificaPipelineContext context, StudentKey key)
        {
            if (!context.EsitoBorsaFactsByStudent.TryGetValue(key, out var facts) || facts == null)
            {
                facts = new EsitoBorsaFacts();
                context.EsitoBorsaFactsByStudent[key] = facts;
            }

            return facts;
        }

        private static HashSet<string> GetObjectColumns(SqlConnection connection, string objectName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (connection == null || string.IsNullOrWhiteSpace(objectName))
                return result;

            const string sql = @"
SELECT c.name
FROM sys.columns c
INNER JOIN sys.objects o ON o.object_id = c.object_id
WHERE o.name = @ObjectName
  AND o.type IN ('U','V');";

            using var cmd = new SqlCommand(sql, connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };
            cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 128).Value = objectName.Trim();

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(Convert.ToString(reader[0], CultureInfo.InvariantCulture) ?? string.Empty);

            return result;
        }

        private static bool ObjectExists(SqlConnection connection, string objectName)
        {
            if (connection == null || string.IsNullOrWhiteSpace(objectName))
                return false;

            const string sql = @"
SELECT TOP (1) 1
FROM sys.objects
WHERE name = @ObjectName
  AND type IN ('U','V');";

            using var cmd = new SqlCommand(sql, connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };
            cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 128).Value = objectName.Trim();
            object? value = cmd.ExecuteScalar();
            return value != null && value != DBNull.Value;
        }

        private static string BuildNullableBitExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            string? column = GetFirstAvailableColumn(columns, candidates);
            if (string.IsNullOrWhiteSpace(column))
                return "CAST(NULL AS BIT)";

            return $@"CAST(
CASE
    WHEN TRY_CONVERT(INT, {alias}.[{column}]) = 1 THEN 1
    WHEN UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), {alias}.[{column}] )))) IN ('1','TRUE','T','S','SI','Y','YES') THEN 1
    WHEN TRY_CONVERT(INT, {alias}.[{column}]) = 0 THEN 0
    WHEN UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), {alias}.[{column}] )))) IN ('0','FALSE','F','N','NO') THEN 0
    ELSE NULL
END
AS BIT)";
        }

        private static string BuildNullableIntExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            string? column = GetFirstAvailableColumn(columns, candidates);
            if (string.IsNullOrWhiteSpace(column))
                return "CAST(NULL AS INT)";

            return $"TRY_CONVERT(INT, {alias}.[{column}])";
        }

        private static string BuildNullableStringExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            string? column = GetFirstAvailableColumn(columns, candidates);
            if (string.IsNullOrWhiteSpace(column))
                return "CAST(NULL AS NVARCHAR(100))";

            return $"NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(100), {alias}.[{column}] ))), '')";
        }

        private static string BuildNullableDecimalExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            string? column = GetFirstAvailableColumn(columns, candidates);
            if (string.IsNullOrWhiteSpace(column))
                return "CAST(NULL AS DECIMAL(18,4))";

            return $"TRY_CONVERT(DECIMAL(18,4), {alias}.[{column}])";
        }

        private static string? GetFirstAvailableColumn(HashSet<string> columns, params string[] candidates)
        {
            if (columns == null || candidates == null)
                return null;

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && columns.Contains(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool? GetNullableBool(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            object value = reader.GetValue(ordinal);
            if (value is bool b)
                return b;

            if (value is byte by)
                return by != 0;

            if (value is short sh)
                return sh != 0;

            if (value is int i)
                return i != 0;

            if (value is long l)
                return l != 0;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("s", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("y", StringComparison.OrdinalIgnoreCase))
                return true;

            if (text.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("n", StringComparison.OrdinalIgnoreCase))
                return false;

            return null;
        }

        private static int? GetNullableInt(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            object value = reader.GetValue(ordinal);
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : null;
            }
        }
    }
}
