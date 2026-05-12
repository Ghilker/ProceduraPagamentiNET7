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
    UPPER(ISNULL(v.COD_BENEFICIO,'')) AS CodBeneficio
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
    UPPER(ISNULL(v.COD_BENEFICIO,'')) AS CodBeneficio
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
    UPPER(ISNULL(vb.Cod_beneficio,'')) AS CodBeneficio,
    CONVERT(NVARCHAR(MAX), dbo.SlashMotiviEsclusioneTest(CAST(t.NumDomanda AS INT), @AA, UPPER(ISNULL(vb.Cod_beneficio,'')))) AS SlashMotiviEsclusione
FROM {TEMP_TABLE} t
JOIN vBenefici_richiesti vb
  ON vb.Anno_accademico = @AA
 AND vb.Num_domanda = CAST(t.NumDomanda AS INT);";

        private const string EsitoBorsaBlocchiPagamentoSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    UPPER(ISNULL(mbp.Cod_tipologia_blocco,'')) AS CodTipologiaBlocco,
    CASE WHEN ISNULL(TRY_CONVERT(INT, mbp.blocco_pagamento_attivo), 0) = 1 THEN 1 ELSE 0 END AS BloccoPagamentoAttivo
FROM {TEMP_TABLE} t
JOIN Motivazioni_blocco_pagamenti mbp
  ON mbp.Anno_accademico = @AA
 AND mbp.Num_domanda = CAST(t.NumDomanda AS INT);";

        private const string EsitoBorsaIncongruenzeSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    UPPER(ISNULL(i.Cod_incongruenza,'')) AS CodIncongruenza,
    CASE WHEN i.Data_fine_validita IS NULL THEN 1 ELSE 0 END AS IncongruenzaAttiva,
    UPPER(ISNULL(i.Cod_forzatura,'')) AS CodForzatura,
    UPPER(ISNULL(i.EliminataDa,'')) AS EliminataDa
FROM {TEMP_TABLE} t
JOIN Incongruenze i
  ON i.Anno_accademico = @AA
 AND i.Num_domanda = CAST(t.NumDomanda AS INT);";

        private static readonly string[] CarrieraTitoloAvvenimentoMarkers =
        {
            "CONSEG", "LAUR", "DIPL", "TITOLO", "ABIL"
        };

        private void LoadEsitoBorsaSupportFacts(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            foreach (var key in context.Students.Keys)
                context.GetOrCreateEsitoBorsaFacts(key);

            LoadEsitoBorsaForzature(context);
            LoadEsitoBorsaForzatureRinuncia(context);
            LoadEsitoBorsaCodTipoOrdinamento(context);
            LoadEsitoBorsaGeneralFacts(context);
            LoadEsitoBorsaBenefitRequestFacts(context);
            LoadEsitoBorsaIscrizioneAlignmentFacts(context);
            LoadEsitoBorsaVariazioni(context);
            LoadEsitoBorsaRinunceUfficio(context);
            LoadEsitoBorsaSlashMotiviEsclusione(context);
            LoadEsitoBorsaBlocchiPagamento(context);
            LoadEsitoBorsaIncongruenze(context);
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
            if (rigaValida == 0)
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
            foreach (var pair in context.Students)
            {
                var facts = GetOrCreateEsitoBorsaFacts(context, pair.Key);

                if (!context.TryGetIscrizioneEsitoFacts(pair.Key, out var raw) || raw == null)
                    continue;

                facts.CarrieraInterrotta = raw.CarrieraInterrotta;
                facts.NumAnniInterruzione = raw.NumAnniInterruzione;
                facts.CreditiExtraCurriculari = raw.CreditiExtraCurriculari;
                facts.MeseImmatricolazione = raw.MeseImmatricolazione;
                facts.Semestre = raw.Semestre;
                facts.IscrittoRipetente = raw.IscrittoRipetente;
                facts.PassaggioTrasferimento = raw.PassaggioTrasferimento ?? false;
                facts.RipetenteDaPassaggio = raw.RipetenteDaPassaggio ?? false;
                facts.PrimaImmatricolazTs = raw.PrimaImmatricolazTs;
                facts.AaTrasferimento = raw.AaTrasferimento;

                if (facts.IscrittoRipetente == true)
                    facts.RevocatoIscrittoRipetente = true;
            }
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
        TRY_CONVERT(BIT, dg.RIFUG_POLITICO) AS RifugiatoPolitico,
        TRY_CONVERT(BIT, dg.INVALIDO) AS Invalido,
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
        NubileProle,
        RifugiatoPolitico,
        Invalido
    FROM DG_RANKED
    WHERE RN = 1
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
                WHEN NULLIF(ISNULL(r.COD_COMUNE, ''), '') IS NULL THEN 0
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
)
SELECT
    D.NumDomanda,
    D.CodFiscale,
    DG.IscrizioneFuoriTermine,
    CAST(CASE WHEN ISNULL(DG.PermessoSoggiorno, CAST(0 AS BIT)) = 1 OR ISNULL(DG.PermessoSoggProvv, CAST(0 AS BIT)) = 1 THEN 1 ELSE 0 END AS BIT) AS PermessoSoggiorno,
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
    ISNULL(DG.RifugiatoPolitico, CAST(0 AS BIT)) AS RifugiatoPolitico,
    ISNULL(DG.Invalido, CAST(0 AS BIT)) AS Invalido
FROM D
LEFT JOIN DG
    ON DG.NumDomanda = D.NumDomanda
LEFT JOIN CIT
    ON CIT.NumDomanda = D.NumDomanda
LEFT JOIN RES
    ON RES.NumDomanda = D.NumDomanda
OPTION (RECOMPILE);";

            using var cmd = CreatePopulationCommand(sql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);

                facts.IscrizioneFuoriTermine = GetNullableBool(reader, "IscrizioneFuoriTermine");
                facts.PermessoSoggiorno = GetNullableBool(reader, "PermessoSoggiorno");
                facts.DomandaTrasmessa = info.StatusCompilazione >= 90;
                facts.TipoStudenteNormalizzato = GetNullableInt(reader, "TipoStudenteNormalizzato");
                facts.IsConferma = GetNullableBool(reader, "IsConferma");
                facts.Straniero = GetNullableBool(reader, "Straniero");
                facts.CittadinanzaUe = GetNullableBool(reader, "CittadinanzaUe");
                facts.ResidenzaUe = GetNullableBool(reader, "ResidenzaUe");
                facts.NubileProle = GetNullableBool(reader, "NubileProle");

                info.InformazioniPersonali.Rifugiato = reader.SafeGetBool("RifugiatoPolitico");
                info.InformazioniPersonali.Disabile = reader.SafeGetBool("Invalido");
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
    UPPER(ISNULL(vb.Cod_beneficio,'')) AS CodBeneficio
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

        private void LoadEsitoBorsaBlocchiPagamento(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaBlocchiPagamentoSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);

                string code = reader.SafeGetString("CodTipologiaBlocco").Trim().ToUpperInvariant();
                int active = reader.SafeGetInt("BloccoPagamentoAttivo");

                if (string.IsNullOrWhiteSpace(code))
                    return;

                facts.BlocchiPagamento.Add(new BloccoPagamentoRaw
                {
                    CodTipologiaBlocco = code,
                    BloccoPagamentoAttivo = active == 1
                });

                if (code == "BIS" || code == "BST")
                {
                    if (active == 0)
                        facts.HasBloccoPagamentoBISBSTRimosso = true;
                    else
                        facts.HasBloccoPagamentoBISBSTAttivo = true;
                }
            });
        }

        private void LoadEsitoBorsaIncongruenze(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaIncongruenzeSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);

                string code = reader.SafeGetString("CodIncongruenza").Trim().ToUpperInvariant();
                int active = reader.SafeGetInt("IncongruenzaAttiva");

                if (string.IsNullOrWhiteSpace(code))
                    return;

                facts.Incongruenze.Add(new IncongruenzaRaw
                {
                    CodIncongruenza = code,
                    Attiva = active == 1,
                    CodForzatura = reader.SafeGetString("CodForzatura").Trim().ToUpperInvariant(),
                    EliminataDa = reader.SafeGetString("EliminataDa").Trim().ToUpperInvariant()
                });

                if (code == "27")
                {
                    if (active == 0)
                        facts.HasIncongruenza27NonAttiva = true;
                    else
                        facts.HasIncongruenza27Attiva = true;
                }
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
    UPPER(ISNULL(COD_ENTE,'')) AS CodEnte,
    UPPER(ISNULL(COD_CORSO_LAUREA,'')) AS CodCorsoLaurea
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
                facts.BorsaPregressaNonRestituitaConfliggente = false;
                facts.AnnoBorsaRichiestoNormalizzato = null;
                facts.AnniBorsaPregressaUsufruitiNormalizzati = string.Empty;
                facts.AnniBorsaPregressaRestituitiNormalizzati = string.Empty;
                facts.AnniBorsaPregressaNonRestituitaConfliggenti = string.Empty;
                facts.BorsaPregressaEsteraNonRichiedeRestituzione = false;
                facts.DiagnosticaBorsaPregressaRestituzioni = string.Empty;

                var items = info.InformazioniIscrizione?.CarrierePregresse;
                if (items == null || items.Count == 0)
                    continue;

                foreach (var item in items)
                {
                    string benefici = NormalizeUpper(item?.BeneficiUsufruiti);
                    string restituzioni = NormalizeUpper(item?.ImportiRestituiti);
                    string codAvvenimento = NormalizeUpper(item?.CodAvvenimento);
                    bool isCarrieraPregressaEstera = IsCarrieraPregressaEstera(item?.SedeIstituzioneUniversitaria);

                    bool hasBorsa = HasBorsaMarker(benefici);
                    bool hasRestituzione = HasMeaningfulRestitution(restituzioni);

                    // Se la carriera pregressa è stata effettuata all'estero (Sede_istituzione_universitaria = 2),
                    // il beneficio non restituito non blocca la borsa.
                    if (isCarrieraPregressaEstera && hasBorsa && !hasRestituzione)
                        facts.BorsaPregressaEsteraNonRichiedeRestituzione = true;

                    // La rinuncia (RI) con Benefici_usufruiti_LZ/Importi_restituiti_LZ viene gestita sotto con mappatura 3+2/ciclo unico.
                    // Evita il vecchio confronto secco 1° anno con 1° anno, non valido per studenti che richiedono una magistrale.
                    if (!isCarrieraPregressaEstera && codAvvenimento != "RI" && hasBorsa && !hasRestituzione)
                        facts.UsufruitoBeneficioBorsaNonRestituito = true;

                    if (IsRinunciaBorsa(codAvvenimento, benefici))
                        facts.RinunciaBorsa = true;

                    AddPregressaBenefitFacts(facts, benefici, restituzioni, codAvvenimento, isCarrieraPregressaEstera);
                }

                if (HasBeneficiRiUsufruitiNonRestituiti(context, pair.Key, facts))
                {
                    facts.UsufruitoBeneficioBorsaNonRestituito = true;
                    facts.BeneficiPregressiNonRestituiti.Add("BS");
                }
            }
        }

        private static bool HasBeneficiRiUsufruitiNonRestituiti(VerificaPipelineContext context, StudentKey key, EsitoBorsaFacts facts)
        {
            if (context == null || facts == null || !context.TryGetCarrieraPregressaBeneficiRi(key, out var rows) || rows == null || rows.Count == 0)
                return false;

            if (!context.Students.TryGetValue(key, out var info) || info?.InformazioniIscrizione == null)
                return false;

            int annoCarrieraDomanda = GetAnnoCarrieraBeneficiLz(info.InformazioniIscrizione);
            if (annoCarrieraDomanda <= 0)
                return false;

            int annoRichiestoNormalizzato = NormalizeAnnoBeneficioCorrente(info.InformazioniIscrizione, annoCarrieraDomanda);
            facts.AnnoBorsaRichiestoNormalizzato = annoRichiestoNormalizzato;

            var usufruitiNormalizzati = new HashSet<int>();
            var restituitiNormalizzati = new HashSet<int>();
            var confliggenti = new HashSet<int>();
            var diagnostica = new List<string>();

            foreach (var row in rows)
            {
                if (row == null)
                    continue;

                string codAvvenimento = NormalizeUpper(row.CodAvvenimento);
                if (codAvvenimento != "RI")
                    continue;

                if (!IsFlagOne(row.BeneficiUsufruiti))
                    continue;

                if (IsCarrieraPregressaEstera(row.SedeIstituzioneUniversitaria))
                {
                    facts.BorsaPregressaEsteraNonRichiedeRestituzione = true;
                    diagnostica.Add(BuildDiagnosticaBeneficiRiEstera(row));
                    continue;
                }

                var anniUsufruiti = ParseAnnoCarrieraSet(row.AnniBeneficiUsufruitiLz);
                if (anniUsufruiti.Count == 0)
                    continue;

                var anniRestituiti = IsFlagOne(row.ImportiRestituiti)
                    ? ParseAnnoCarrieraSet(row.AnniImportiRestituitiLz)
                    : new HashSet<int>();

                string tipoPregresso = ResolvePercorsoBeneficiPregressi(row.TipologiaCorso, row.DurataLegTitoloConseguito, row.AnnoAvvenimento);
                bool fallbackOrdinale = tipoPregresso == "UNKNOWN";

                foreach (int annoUsufruito in anniUsufruiti)
                {
                    int annoUsufruitoNormalizzato = fallbackOrdinale
                        ? annoUsufruito
                        : NormalizeAnnoBeneficioPregresso(annoUsufruito, tipoPregresso);

                    usufruitiNormalizzati.Add(annoUsufruitoNormalizzato);

                    bool restituito = false;
                    foreach (int annoRestituito in anniRestituiti)
                    {
                        int annoRestituitoNormalizzato = fallbackOrdinale
                            ? annoRestituito
                            : NormalizeAnnoBeneficioPregresso(annoRestituito, tipoPregresso);

                        restituitiNormalizzati.Add(annoRestituitoNormalizzato);
                        if (annoRestituitoNormalizzato == annoUsufruitoNormalizzato)
                            restituito = true;
                    }

                    bool confligge = fallbackOrdinale
                        ? annoUsufruito == annoCarrieraDomanda
                        : annoUsufruitoNormalizzato == annoRichiestoNormalizzato;

                    if (confligge && !restituito)
                        confliggenti.Add(annoUsufruitoNormalizzato);
                }

                diagnostica.Add(BuildDiagnosticaBeneficiRi(row, tipoPregresso, fallbackOrdinale));
            }

            facts.AnniBorsaPregressaUsufruitiNormalizzati = FormatIntSet(usufruitiNormalizzati);
            facts.AnniBorsaPregressaRestituitiNormalizzati = FormatIntSet(restituitiNormalizzati);
            facts.AnniBorsaPregressaNonRestituitaConfliggenti = FormatIntSet(confliggenti);
            facts.BorsaPregressaNonRestituitaConfliggente = confliggenti.Count > 0;
            facts.DiagnosticaBorsaPregressaRestituzioni = string.Join(" || ", diagnostica);

            return confliggenti.Count > 0;
        }

        private static string BuildDiagnosticaBeneficiRi(CarrieraPregressaBeneficiRiRaw row, string tipoPregresso, bool fallbackOrdinale)
        {
            string tipo = fallbackOrdinale ? "UNKNOWN_FALLBACK_ORDINALE" : tipoPregresso;
            return string.Concat(
                "RI;TipoPregresso=", tipo,
                ";SedeIstituzioneUniversitaria=", NormalizeUpper(row.SedeIstituzioneUniversitaria),
                ";CarrieraEstera=0",
                ";TipologiaCorso=", NormalizeUpper(row.TipologiaCorso),
                ";Durata=", row.DurataLegTitoloConseguito?.ToString(CultureInfo.InvariantCulture) ?? "",
                ";AnnoAvvenimento=", row.AnnoAvvenimento?.ToString(CultureInfo.InvariantCulture) ?? "",
                ";AnniUsufruitiRaw=", row.AnniBeneficiUsufruitiLz ?? string.Empty,
                ";AnniRestituitiRaw=", row.AnniImportiRestituitiLz ?? string.Empty);
        }

        private static string BuildDiagnosticaBeneficiRiEstera(CarrieraPregressaBeneficiRiRaw row)
            => string.Concat(
                "RI;TipoPregresso=ESTERO_NON_RICHIEDE_RESTITUZIONE",
                ";SedeIstituzioneUniversitaria=", NormalizeUpper(row.SedeIstituzioneUniversitaria),
                ";CarrieraEstera=1",
                ";Regola=beneficio_pregresso_estero_non_bloccante",
                ";TipologiaCorso=", NormalizeUpper(row.TipologiaCorso),
                ";Durata=", row.DurataLegTitoloConseguito?.ToString(CultureInfo.InvariantCulture) ?? "",
                ";AnnoAvvenimento=", row.AnnoAvvenimento?.ToString(CultureInfo.InvariantCulture) ?? "",
                ";AnniUsufruitiRaw=", row.AnniBeneficiUsufruitiLz ?? string.Empty,
                ";AnniRestituitiRaw=", row.AnniImportiRestituitiLz ?? string.Empty);

        private static int NormalizeAnnoBeneficioCorrente(InformazioniIscrizione iscr, int annoCarrieraDomanda)
        {
            if (iscr == null || annoCarrieraDomanda <= 0)
                return 0;

            string tipoCorrente = ResolvePercorsoBeneficiCorrente(iscr);
            if (tipoCorrente == "MAGISTRALE")
                return annoCarrieraDomanda + 3;

            return annoCarrieraDomanda;
        }

        private static int NormalizeAnnoBeneficioPregresso(int annoCarriera, string tipoPregresso)
        {
            if (annoCarriera <= 0)
                return 0;

            return tipoPregresso == "MAGISTRALE" ? annoCarriera + 3 : annoCarriera;
        }

        private static string ResolvePercorsoBeneficiCorrente(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return "UNKNOWN";

            if (iscr.TipoCorso == 5)
                return "MAGISTRALE";

            if (iscr.TipoCorso == 4)
                return "CICLO_UNICO";

            if (iscr.TipoCorso == 3 || iscr.TipoCorso == 6)
                return "TRIENNALE";

            int durata = EsitoBorsaSupport.GetDurataNormaleCorso(iscr);
            if (durata >= 5)
                return "CICLO_UNICO";

            if (durata == 3)
                return "TRIENNALE";

            if (durata == 2)
                return "MAGISTRALE";

            return "UNKNOWN";
        }

        private static string ResolvePercorsoBeneficiPregressi(string? tipologiaCorso, int? durataLegale, int? annoAccademicoConseguimento)
        {
            int tipologia = 0;
            int.TryParse((tipologiaCorso ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tipologia);

            tipologia = NormalizeTipologiaTitoloCarrieraPregressa(
                codAvvenimento: null,
                tipologia: tipologia,
                durataLegale: durataLegale,
                annoAccademicoConseguimento: annoAccademicoConseguimento);

            if (tipologia == 5)
                return "MAGISTRALE";

            if (tipologia == 4)
                return "CICLO_UNICO";

            if (tipologia == 3 || tipologia == 6)
                return "TRIENNALE";

            return "UNKNOWN";
        }

        private static string FormatIntSet(HashSet<int> values)
        {
            if (values == null || values.Count == 0)
                return string.Empty;

            return string.Join("|", values.OrderBy(v => v));
        }

        private static int GetAnnoCarrieraBeneficiLz(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return 0;

            int annoCorso = iscr.AnnoCorso;
            if (annoCorso > 0)
                return annoCorso;

            if (annoCorso < 0)
            {
                int durataNormale = EsitoBorsaSupport.GetDurataNormaleCorso(iscr);
                if (durataNormale > 0)
                    return durataNormale + Math.Abs(annoCorso);
            }

            return 0;
        }

        private static HashSet<int> ParseAnnoCarrieraSet(string? value)
        {
            var result = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(value))
                return result;

            string[] parts = value.Split(new[] { '|', ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int anno) && anno > 0)
                    result.Add(anno);
            }

            return result;
        }

        private static bool IsFlagOne(string? value)
        {
            string normalized = NormalizeUpper(value);
            return normalized == "1" || normalized == "TRUE" || normalized == "S" || normalized == "SI";
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
                InformazioniCarrieraPregressa? bestAtCicloUnico = null;
                int bestAtCicloUnicoYear = int.MinValue;
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

                        int? tipologiaAtScan = null;
                        if (TryParseCareerTitleType(item, out int tipoAtScan))
                            tipologiaAtScan = tipoAtScan;

                        if (IsTitoloAttesoIncompatibilePerMagistraleBiennale(tipologiaAtScan, item.DurataLegTitoloConseguito)
                            && (bestAtCicloUnico == null || year > bestAtCicloUnicoYear))
                        {
                            bestAtCicloUnico = item;
                            bestAtCicloUnicoYear = year;
                        }

                        continue;
                    }

                    if (bestGeneric == null || year > bestGenericYear)
                    {
                        bestGeneric = item;
                        bestGenericYear = year;
                    }
                }

                int aaInizio = EsitoBorsaSupport.ParseAnnoAccademicoStartFromString(context.AnnoAccademico);
                DateTime? scadenzaAttesaTitolo = EsitoBorsaSupport.GetScadenzaAttesaTitolo(aaInizio);
                bool atValida = EsitoBorsaSupport.IsAttesaTitoloValidaAllaData(context.ReferenceDate, aaInizio);
                bool atValidataDaBloccoPagamento = bestAt != null
                                     && bestCd == null
                                     && facts.HasBloccoPagamentoBISBSTRimosso
                                     && !facts.HasBloccoPagamentoBISBSTAttivo;

                bool atValidataDaIncongruenza27 = bestAt != null
                                                  && bestCd == null
                                                  && facts.HasIncongruenza27NonAttiva
                                                  && !facts.HasIncongruenza27Attiva;

                bool atValidaPerTitoloAccesso = atValida
                                                || atValidataDaBloccoPagamento
                                                || atValidataDaIncongruenza27;


                InformazioniCarrieraPregressa? best = bestCd ?? (atValidaPerTitoloAccesso ? bestAt : null) ?? bestGeneric;
                InformazioniCarrieraPregressa? titoloPerDiagnostica = bestCd ?? bestAt ?? bestGeneric;
                if (titoloPerDiagnostica == null)
                    continue;

                string bestCodAvvenimento = NormalizeUpper((best ?? titoloPerDiagnostica).CodAvvenimento);
                bool hasCd = bestCd != null;
                bool hasAt = bestAt != null;

                if (!facts.TitoloAccademicoConseguito.HasValue)
                    facts.TitoloAccademicoConseguito = hasCd;

                if (!facts.AttesaTitoloAccademicoConseguito.HasValue)
                    facts.AttesaTitoloAccademicoConseguito = hasAt && !hasCd;

                if (!facts.ScadenzaAttesaTitolo.HasValue && scadenzaAttesaTitolo.HasValue)
                    facts.ScadenzaAttesaTitolo = scadenzaAttesaTitolo.Value;

                if (!facts.AttesaTitoloValidaAllaDataValutazione.HasValue && hasAt && !hasCd)
                    facts.AttesaTitoloValidaAllaDataValutazione = atValidaPerTitoloAccesso;

                if (!facts.AttesaTitoloValidataDaBloccoPagamentoRimosso.HasValue && hasAt && !hasCd)
                    facts.AttesaTitoloValidataDaBloccoPagamentoRimosso = atValidataDaBloccoPagamento;

                if (!facts.AttesaTitoloValidataDaIncongruenza27.HasValue && hasAt && !hasCd)
                    facts.AttesaTitoloValidataDaIncongruenza27 = atValidataDaIncongruenza27;

                if (!facts.AttesaTitoloScaduta.HasValue && hasAt && !hasCd)
                    facts.AttesaTitoloScaduta = !atValidaPerTitoloAccesso;

                if (!facts.TitoloAccessoValidoPerIscrizione.HasValue)
                    facts.TitoloAccessoValidoPerIscrizione = hasCd || (hasAt && atValidaPerTitoloAccesso);

                if (string.IsNullOrWhiteSpace(facts.CodAvvenimentoTitoloAccesso))
                    facts.CodAvvenimentoTitoloAccesso = hasCd ? "CD" : hasAt ? "AT" : bestCodAvvenimento;

                int? tipologiaTitoloAccesso = null;
                int? durataTitoloAccesso = null;
                int? tipologiaTitoloAtteso = null;
                int? durataTitoloAtteso = null;
                int? tipologiaTitoloAttesoCicloUnico = null;
                int? durataTitoloAttesoCicloUnico = null;

                if (hasCd)
                {
                    if (TryParseCareerTitleType(bestCd, out int tipoCd))
                        tipologiaTitoloAccesso = tipoCd;

                    durataTitoloAccesso = bestCd.DurataLegTitoloConseguito;

                    if (!facts.AnnoAvvenimentoTitoloAccesso.HasValue && bestCd.AnnoAvvenimento.HasValue)
                        facts.AnnoAvvenimentoTitoloAccesso = bestCd.AnnoAvvenimento.Value;
                }

                if (hasAt)
                {
                    if (TryParseCareerTitleType(bestAt, out int tipoAt))
                        tipologiaTitoloAtteso = tipoAt;

                    durataTitoloAtteso = bestAt.DurataLegTitoloConseguito;

                    if (!facts.TipologiaStudiTitoloAtteso.HasValue && tipologiaTitoloAtteso.HasValue)
                        facts.TipologiaStudiTitoloAtteso = tipologiaTitoloAtteso.Value;

                    if (!facts.DurataLegTitoloAtteso.HasValue && durataTitoloAtteso.HasValue)
                        facts.DurataLegTitoloAtteso = durataTitoloAtteso.Value;

                    if (!facts.AnnoAvvenimentoTitoloAtteso.HasValue && bestAt.AnnoAvvenimento.HasValue)
                        facts.AnnoAvvenimentoTitoloAtteso = bestAt.AnnoAvvenimento.Value;
                }

                if (bestAtCicloUnico != null)
                {
                    if (TryParseCareerTitleType(bestAtCicloUnico, out int tipoAtCicloUnico))
                        tipologiaTitoloAttesoCicloUnico = tipoAtCicloUnico;

                    durataTitoloAttesoCicloUnico = bestAtCicloUnico.DurataLegTitoloConseguito;
                }

                bool titoloAccessoTriennaleConseguito = hasCd && IsTitoloAccessoTriennale(tipologiaTitoloAccesso, durataTitoloAccesso);
                bool attesaTitoloIncompatibile = bestAtCicloUnico != null
                                                   && IsTitoloAttesoIncompatibilePerMagistraleBiennale(tipologiaTitoloAttesoCicloUnico, durataTitoloAttesoCicloUnico);

                if (!facts.TitoloAccessoTriennaleConseguito.HasValue)
                    facts.TitoloAccessoTriennaleConseguito = titoloAccessoTriennaleConseguito;

                // Nome mantenuto per compatibilit�: da questa versione include AT su ciclo unico
                // e AT su magistrale biennale (tipologia 5).
                if (!facts.AttesaTitoloCicloUnicoPresente.HasValue)
                    facts.AttesaTitoloCicloUnicoPresente = attesaTitoloIncompatibile;

                if (!facts.TitoloGiaConseguitoConAttesaCicloUnico.HasValue)
                    facts.TitoloGiaConseguitoConAttesaCicloUnico = titoloAccessoTriennaleConseguito && attesaTitoloIncompatibile;

                if (best == null)
                    continue;

                if (!facts.TipologiaStudiTitoloConseguito.HasValue && TryParseCareerTitleType(best, out int tipologiaTitolo))
                    facts.TipologiaStudiTitoloConseguito = tipologiaTitolo;

                if (!facts.DurataLegTitoloConseguito.HasValue && best.DurataLegTitoloConseguito.HasValue)
                    facts.DurataLegTitoloConseguito = best.DurataLegTitoloConseguito.Value;

                if (string.IsNullOrWhiteSpace(facts.SedeIstituzioneUniversitariaTitolo))
                    facts.SedeIstituzioneUniversitariaTitolo = (best.SedeIstituzioneUniversitaria ?? string.Empty).Trim();

                if (!facts.RiconoscimentoTitoloEstero.HasValue)
                    facts.RiconoscimentoTitoloEstero = best.PassaggioCorsoEstero != 0;
            }
        }

        private static bool IsTitoloAccessoTriennale(int? tipologiaTitolo, int? durataTitolo)
        {
            if (!tipologiaTitolo.HasValue)
                return false;

            int tipo = tipologiaTitolo.Value;
            int durata = durataTitolo ?? 0;

            return (tipo == 2 && durata == 3)
                   || tipo == 3
                   || tipo == 9;
        }

        private static bool IsTitoloCicloUnico(int? tipologiaTitolo, int? durataTitolo)
        {
            int tipo = tipologiaTitolo ?? 0;
            int durata = durataTitolo ?? 0;

            return tipo == 4 || durata >= 5;
        }

        private static bool IsTitoloAttesoIncompatibilePerMagistraleBiennale(int? tipologiaTitolo, int? durataTitolo)
        {
            int tipo = tipologiaTitolo ?? 0;
            int durata = durataTitolo ?? 0;

            // Per l'iscrizione a magistrale biennale, dopo un CD triennale valido,
            // un ulteriore AT su ciclo unico o su altra magistrale biennale indica
            // titolo ulteriore gi� in corso/atteso e rende non ammissibile la richiesta.
            // AT normalmente non valorizza la durata legale: la tipologia corso � decisiva.
            return tipo == 4 || tipo == 5 || durata >= 5;
        }

        private static bool LooksLikeCareerTitleRecord(InformazioniCarrieraPregressa item)
        {
            if (item == null)
                return false;

            string code = NormalizeUpper(item.CodAvvenimento);

            // AT = attesa conseguimento titolo.
            // Nel tracciato AT normalmente non valorizza DurataLegTitoloConseguito,
            // quindi la durata legale non pu� essere usata come requisito per riconoscere il record titolo.
            if (code == "AT" || code == "CD")
                return true;

            if (item.DurataLegTitoloConseguito.HasValue && item.DurataLegTitoloConseguito.Value > 0)
                return true;

            foreach (string marker in CarrieraTitoloAvvenimentoMarkers)
            {
                if (code.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryParseCareerTitleType(InformazioniCarrieraPregressa? item, out int tipologia)
        {
            tipologia = 0;
            if (item == null)
                return false;

            if (!TryParseCareerTitleTypeRaw(item.TipologiaCorso, out tipologia))
                return false;

            tipologia = NormalizeTipologiaTitoloCarrieraPregressa(
                item.CodAvvenimento,
                tipologia,
                item.DurataLegTitoloConseguito,
                item.AnnoAvvenimento);
            return true;
        }

        private static bool TryParseCareerTitleTypeRaw(string? value, out int tipologia)
        {
            tipologia = 0;
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tipologia);
        }

        private static int NormalizeTipologiaTitoloCarrieraPregressa(string? codAvvenimento, int tipologia, int? durataLegale, int? annoAccademicoConseguimento)
        {
            int durata = durataLegale ?? 0;
            bool titoloAnte20092010 = IsAnnoAccademicoTitoloPrecedente20092010(annoAccademicoConseguimento);

            if (titoloAnte20092010)
            {
                // Regola storica per titoli conseguiti prima del 2009/2010:
                // - tipologia 1 viene valutata sempre come ciclo unico;
                // - tipologia 2 dipende dalla durata legale: 3 anni = triennale, 4/5/6 anni = ciclo unico.
                if (tipologia == 1)
                    return 4;

                if (tipologia == 2)
                {
                    if (durata == 3)
                        return 3;

                    if (durata >= 4)
                        return 4;
                }

                return tipologia;
            }

            // Dal 2009/2010 in poi la tipologia pregressa viene normalizzata solo sulla durata legale.
            if (durata == 3)
                return 3;

            if (durata >= 4)
                return 4;

            if (durata == 2)
                return 5;

            return tipologia;
        }

        private static bool IsAnnoAccademicoTitoloPrecedente20092010(int? annoAccademicoConseguimento)
        {
            if (!annoAccademicoConseguimento.HasValue || annoAccademicoConseguimento.Value <= 0)
                return false;

            int anno = annoAccademicoConseguimento.Value;

            // Formato AA compatto, es. 20082009, 20092010.
            if (anno >= 1000000)
                return anno < 20092010;

            // Formato anno iniziale, es. 2008 = 2008/2009, 2009 = 2009/2010.
            if (anno >= 1900 && anno <= 9999)
                return anno < 2009;

            return false;
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

        private static void AddPregressaBenefitFacts(EsitoBorsaFacts facts, string benefici, string restituzioni, string codAvvenimento, bool isCarrieraPregressaEstera)
        {
            foreach (string beneficio in KnownBenefitCodes)
            {
                bool hasBenefit = HasBenefitMarker(benefici, beneficio);
                bool hasRestituzione = HasMeaningfulRestitution(restituzioni);

                if (!isCarrieraPregressaEstera && hasBenefit && !hasRestituzione && !(beneficio == "BS" && codAvvenimento == "RI"))
                    facts.BeneficiPregressiNonRestituiti.Add(beneficio);

                if (IsRinunciaBenefit(codAvvenimento, benefici, beneficio))
                    facts.BeneficiRinunciaPregressa.Add(beneficio);
            }
        }

        private static bool IsCarrieraPregressaEstera(string? sedeIstituzioneUniversitaria)
            => NormalizeUpper(sedeIstituzioneUniversitaria) == "2";

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
            => context.GetOrCreateEsitoBorsaFacts(key);

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
    WHEN UPPER(CONVERT(NVARCHAR(50), {alias}.[{column}] )) IN ('1','TRUE','T','S','SI','Y','YES') THEN 1
    WHEN TRY_CONVERT(INT, {alias}.[{column}]) = 0 THEN 0
    WHEN UPPER(CONVERT(NVARCHAR(50), {alias}.[{column}] )) IN ('0','FALSE','F','N','NO') THEN 0
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

            return $"NULLIF(CONVERT(NVARCHAR(100), {alias}.[{column}] ), '')";
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
