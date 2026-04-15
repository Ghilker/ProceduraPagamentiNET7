using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private const string IscrizioneCorePopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        t.CodFiscale,
        COALESCE(t.TipoBando,'') AS TipoBando
    FROM {TEMP_TABLE} t
),
MER AS
(
    SELECT
        D.NumDomanda,
        TRY_CONVERT(INT, m.ANNO_IMMATRICOLAZ) AS Anno_immatricolaz
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
        D.CodFiscale,
        D.TipoBando,
        CONVERT(NVARCHAR(50), i.COD_CORSO_LAUREA) AS Cod_corso_laurea,
        CONVERT(NVARCHAR(20), i.COD_TIPO_ORDINAMENTO) AS Cod_tipo_ordinamento,
        TRY_CONVERT(INT, cl.DURATA_LEGALE) AS Durata_legale,
        TRY_CONVERT(INT, i.ANNO_CORSO) AS Anno_corso,
        CONVERT(NVARCHAR(50), cl.COD_FACOLTA) AS Cod_facolta,
        CONVERT(NVARCHAR(20), cl.ANNO_ACCAD_INIZIO) AS Anno_accad_inizio,
        CONVERT(NVARCHAR(50), i.COD_SEDE_STUDI) AS Cod_sede_studi,
        CONVERT(NVARCHAR(50), i.COD_TIPOLOGIA_STUDI) AS Cod_tipologia_studi,
        TRY_CONVERT(DECIMAL(18,2), i.CREDITI_TIROCINIO) AS Crediti_tirocinio,
        TRY_CONVERT(DECIMAL(18,2), i.CREDITI_RICONOSCIUTI) AS Crediti_riconosciuti,
        TRY_CONVERT(INT, i.CONFERMA_SEMESTRE_FILTRO) AS Conferma_semestre_filtro,
        CAST(ISNULL(cl.CORSO_STEM,0) AS BIT) AS Stem,
        CONVERT(NVARCHAR(50), ss.COD_ENTE) AS Cod_ente,
        CASE
            WHEN NULLIF(LTRIM(RTRIM(ISNULL(cl.COD_SEDE_DISTACCATA,''))), '') IS NULL THEN '00000'
            ELSE LTRIM(RTRIM(cl.COD_SEDE_DISTACCATA))
        END AS Cod_sede_distaccata,
        CASE
            WHEN NULLIF(LTRIM(RTRIM(ISNULL(cl.COD_SEDE_DISTACCATA,''))), '') IS NULL
                 OR LTRIM(RTRIM(ISNULL(cl.COD_SEDE_DISTACCATA,''))) = '00000'
                THEN CASE
                        WHEN ISNULL(cl.COMUNE_SEDE_STUDI,'') IN ('00000','0000') THEN 'H501'
                        ELSE ISNULL(cl.COMUNE_SEDE_STUDI,'')
                     END
            ELSE ISNULL(sd.COD_COMUNE,'')
        END AS ComuneSedeStudi,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY i.DATA_VALIDITA DESC) AS rn
    FROM D
    LEFT JOIN MER m
      ON m.NumDomanda = D.NumDomanda
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
    LEFT JOIN CORSI_LAUREA cl
      ON cl.COD_CORSO_LAUREA = i.COD_CORSO_LAUREA
     AND cl.COD_TIPO_ORDINAMENTO = i.COD_TIPO_ORDINAMENTO
     AND cl.COD_FACOLTA = i.COD_FACOLTA
     AND (cl.ANNO_ACCAD_FINE >= CONVERT(NVARCHAR(20), ISNULL(m.Anno_immatricolaz,0)) OR cl.ANNO_ACCAD_FINE IS NULL)
    LEFT JOIN SEDE_STUDI ss
      ON ss.COD_SEDE_STUDI = i.COD_SEDE_STUDI
    LEFT JOIN SEDI_DISTACCATE sd
      ON sd.COD_SEDE_DISTACCATA = cl.COD_SEDE_DISTACCATA
)
SELECT
    I.NumDomanda,
    I.CodFiscale,
    I.TipoBando,
    I.Cod_corso_laurea,
    I.Cod_tipo_ordinamento,
    I.Durata_legale,
    I.Anno_corso,
    I.Cod_facolta,
    I.Anno_accad_inizio,
    I.Cod_sede_studi,
    I.Cod_tipologia_studi,
    I.Crediti_tirocinio,
    I.Crediti_riconosciuti,
    ISNULL(I.Conferma_semestre_filtro, 0) AS Conferma_semestre_filtro,
    I.Stem,
    ISNULL(I.Cod_ente,'') AS Cod_ente,
    ISNULL(I.Cod_sede_distaccata,'00000') AS Cod_sede_distaccata,
    ISNULL(I.ComuneSedeStudi,'') AS ComuneSedeStudi,
    ISNULL(c.COD_PROVINCIA,'') AS ProvinciaSede
FROM ISCR I
LEFT JOIN COMUNI c
  ON c.COD_COMUNE = I.ComuneSedeStudi
WHERE I.rn = 1;";

        private const string AppartenenzaPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CAST('00000' AS NVARCHAR(50)) AS Cod_sede_distaccata,
    CAST('' AS NVARCHAR(50)) AS Cod_ente
FROM {TEMP_TABLE} t
WHERE 1 = 0;";

        private const string MeritoPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT CAST(t.NumDomanda AS INT) AS NumDomanda, t.CodFiscale
    FROM {TEMP_TABLE} t
)
SELECT
    D.NumDomanda,
    D.CodFiscale,
    TRY_CONVERT(INT, m.ANNO_IMMATRICOLAZ) AS Anno_immatricolaz,
    TRY_CONVERT(INT, m.NUMERO_ESAMI) AS Numero_esami,
    TRY_CONVERT(DECIMAL(18,2), m.NUMERO_CREDITI) AS Numero_crediti,
    TRY_CONVERT(DECIMAL(18,2), m.SOMMA_VOTI) AS Somma_voti,
    ISNULL(TRY_CONVERT(INT, m.UTILIZZO_BONUS),0) AS Utilizzo_bonus,
    TRY_CONVERT(DECIMAL(18,2), m.CREDITI_UTILIZZATI) AS Crediti_utilizzati,
    TRY_CONVERT(DECIMAL(18,2), m.CREDITI_RIMANENTI) AS Crediti_rimanenti,
    TRY_CONVERT(DECIMAL(18,2), m.CREDITI_RICONOSCIUTI_DA_RINUNCIA) AS Crediti_riconosciuti_da_rinuncia,
    ISNULL(CONVERT(NVARCHAR(20), m.AACREDITIRICONOSCIUTI), '') AS AACreditiRiconosciuti
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
 );
";

        private const string CarrieraPregressaSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        t.CodFiscale,
        COALESCE(t.TipoBando,'') AS TipoBando
    FROM {TEMP_TABLE} t
)
SELECT
    D.NumDomanda,
    D.CodFiscale,
    D.TipoBando,
    CONVERT(NVARCHAR(50), cp.COD_AVVENIMENTO) AS Cod_avvenimento,
    TRY_CONVERT(INT, cp.ANNO_AVVENIMENTO) AS Anno_avvenimento,
    CONVERT(NVARCHAR(250), cp.UNIV_DI_CONSEGUIM) AS Univ_di_conseguim,
    CONVERT(NVARCHAR(250), cp.UNIV_PROVENIENZA) AS Univ_provenienza,
    TRY_CONVERT(INT, cp.PRIMA_IMMATRICOLAZ) AS Prima_immatricolaz,
    CONVERT(NVARCHAR(150), cp.TIPOLOGIA_CORSO) AS Tipologia_corso,
    TRY_CONVERT(INT, cp.DURATA_LEG_TITOLO_CONSEGUITO) AS Durata_leg_titolo_conseguito,
    TRY_CONVERT(INT, cp.PASSAGGIO_CORSO_ESTERO) AS Passaggio_corso_estero,
    CONVERT(NVARCHAR(250), cp.SEDE_ISTITUZIONE_UNIVERSITARIA) AS Sede_istituzione_universitaria,
    CONVERT(NVARCHAR(250), cp.BENEFICI_USUFRUITI) AS benefici_usufruiti,
    CONVERT(NVARCHAR(250), cp.IMPORTI_RESTITUITI) AS importi_restituiti,
    TRY_CONVERT(DECIMAL(18,2), cp.NUMERO_CREDITI) AS numero_crediti,
    TRY_CONVERT(INT, cp.ANNO_CORSO) AS anno_corso,
    TRY_CONVERT(INT, cp.RIPETENTE) AS ripetente,
    TRY_CONVERT(INT, cp.ISCRITTO_SEMESTRE_FILTRODI) AS Iscritto_semestre_filtroDI
FROM D
JOIN CARRIERA_PREGRESSA cp
  ON cp.ANNO_ACCADEMICO = @AA
 AND cp.COD_FISCALE = D.CodFiscale
 AND cp.DATA_VALIDITA =
 (
    SELECT MAX(cp2.DATA_VALIDITA)
    FROM CARRIERA_PREGRESSA cp2
    WHERE cp2.COD_FISCALE = cp.COD_FISCALE
      AND cp2.ANNO_ACCADEMICO = cp.ANNO_ACCADEMICO
      AND cp2.COD_AVVENIMENTO = cp.COD_AVVENIMENTO
 );
";

        private const string MeritoDurataLegaleCorsoColumn = "Durata_legale";
        private const string MeritoCodTipoOrdinamentoCorsoColumn = "Cod_tipo_ordinamento";

        private static void ResetIscrizioneState(VerificaPipelineContext context)
        {
            foreach (var info in context.Students.Values)
            {
                var iscr = info.InformazioniIscrizione;
                iscr.TipoBando = string.Empty;
                iscr.AnnoCorso = 0;
                iscr.TipoCorso = 0;
                iscr.CodCorsoLaurea = string.Empty;
                iscr.CodSedeStudi = string.Empty;
                iscr.CodFacolta = string.Empty;
                iscr.AnnoAccadInizioCorso = string.Empty;
                iscr.CodEnte = string.Empty;
                iscr.CodSedeDistaccata = string.Empty;
                iscr.ComuneSedeStudi = string.Empty;
                iscr.ProvinciaSedeStudi = string.Empty;
                iscr.CorsoStem = false;
                iscr.CreditiTirocinio = null;
                iscr.CreditiRiconosciuti = null;
                iscr.ConfermaSemestreFiltro = 0;
                iscr.AnnoImmatricolazione = null;
                iscr.NumeroEsami = null;
                iscr.NumeroCrediti = null;
                iscr.SommaVoti = null;
                iscr.UtilizzoBonus = 0;
                iscr.CreditiUtilizzati = null;
                iscr.CreditiRimanenti = null;
                iscr.CreditiRiconosciutiDaRinuncia = null;
                iscr.AACreditiRiconosciuti = string.Empty;
                iscr.DurataLegaleCorso = null;
                iscr.CodTipoOrdinamentoCorso = string.Empty;
                iscr.EsamiMinimiRichiestiMerito = null;
                iscr.CreditiMinimiRichiestiMerito = null;
                iscr.EsamiMinimiRichiestiPassaggio = null;
                iscr.CreditiMinimiRichiestiPassaggio = null;
                iscr.RegolaMeritoApplicata = string.Empty;
                iscr.NumeroEventiCarrieraPregressa = 0;
                iscr.UltimoAnnoAvvenimentoCarrieraPregressa = null;
                iscr.TotaleCreditiCarrieraPregressa = 0m;
                iscr.HaPassaggioCorsoEsteroCarrieraPregressa = 0;
                iscr.HaRipetenzaCarrieraPregressa = 0;
                iscr.CodiciAvvenimentoCarrieraPregressa = string.Empty;
                iscr.CarrierePregresse.Clear();
            }
        }

        private void LoadBaseIscrizione(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadBaseIscrizione", $"AA={context.AnnoAccademico}");
            LoadIscrizioneCore(context);
            LoadMerito(context);
        }

        private void LoadIscrizioneCore(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(IscrizioneCorePopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var iscr = info.InformazioniIscrizione;
                iscr.TipoBando = reader.SafeGetString("TipoBando");
                iscr.AnnoCorso = reader.SafeGetInt("Anno_corso");
                iscr.CodCorsoLaurea = reader.SafeGetString("Cod_corso_laurea");
                iscr.CodTipoOrdinamentoCorso = reader.SafeGetString("Cod_tipo_ordinamento");
                iscr.DurataLegaleCorso = reader.SafeGetInt("Durata_legale");
                iscr.CorsoMedicina = reader.SafeGetInt("Durata_legale") == 6;
                iscr.CodFacolta = reader.SafeGetString("Cod_facolta");
                iscr.AnnoAccadInizioCorso = reader.SafeGetString("Anno_accad_inizio");
                iscr.CodSedeStudi = reader.SafeGetString("Cod_sede_studi");
                iscr.CodEnte = reader.SafeGetString("Cod_ente");
                iscr.CodSedeDistaccata = reader.SafeGetString("Cod_sede_distaccata");
                iscr.ComuneSedeStudi = reader.SafeGetString("ComuneSedeStudi").Trim();
                iscr.ProvinciaSedeStudi = reader.SafeGetString("ProvinciaSede").Trim().ToUpperInvariant();
                iscr.CorsoStem = reader.SafeGetBool("Stem");
                iscr.CreditiTirocinio = reader.SafeGetDecimal("Crediti_tirocinio");
                iscr.CreditiRiconosciuti = reader.SafeGetDecimal("Crediti_riconosciuti");
                iscr.ConfermaSemestreFiltro = reader.SafeGetInt("Conferma_semestre_filtro");

                if (TryParseInt(reader.SafeGetString("Cod_tipologia_studi"), out var tipoCorso))
                    iscr.TipoCorso = tipoCorso;
            });
        }

        private void LoadMerito(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(MeritoPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var iscr = info.InformazioniIscrizione;
                iscr.AnnoImmatricolazione = reader.SafeGetInt("Anno_immatricolaz");
                iscr.NumeroEsami = reader.SafeGetInt("Numero_esami");
                iscr.NumeroCrediti = reader.SafeGetDecimal("Numero_crediti");
                iscr.SommaVoti = reader.SafeGetDecimal("Somma_voti");
                iscr.UtilizzoBonus = reader.SafeGetInt("Utilizzo_bonus");
                iscr.CreditiUtilizzati = reader.SafeGetDecimal("Crediti_utilizzati");
                iscr.CreditiRimanenti = reader.SafeGetDecimal("Crediti_rimanenti");
                iscr.CreditiRiconosciutiDaRinuncia = reader.SafeGetDecimal("Crediti_riconosciuti_da_rinuncia");
                iscr.AACreditiRiconosciuti = reader.SafeGetString("AACreditiRiconosciuti");
            });
        }


        private void LoadCarrieraPregressa(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadCarrieraPregressa", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(CarrieraPregressaSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                info.InformazioniIscrizione.CarrierePregresse.Add(new InformazioniCarrieraPregressa
                {
                    CodAvvenimento = reader.SafeGetString("Cod_avvenimento"),
                    AnnoAvvenimento = reader.SafeGetInt("Anno_avvenimento"),
                    UnivDiConseguim = reader.SafeGetString("Univ_di_conseguim"),
                    UnivProvenienza = reader.SafeGetString("Univ_provenienza"),
                    PrimaImmatricolaz = reader.SafeGetInt("Prima_immatricolaz"),
                    TipologiaCorso = reader.SafeGetString("Tipologia_corso"),
                    DurataLegTitoloConseguito = reader.SafeGetInt("Durata_leg_titolo_conseguito"),
                    PassaggioCorsoEstero = reader.SafeGetInt("Passaggio_corso_estero"),
                    SedeIstituzioneUniversitaria = reader.SafeGetString("Sede_istituzione_universitaria"),
                    BeneficiUsufruiti = reader.SafeGetString("benefici_usufruiti"),
                    ImportiRestituiti = reader.SafeGetString("importi_restituiti"),
                    NumeroCrediti = reader.SafeGetDecimal("numero_crediti"),
                    AnnoCorso = reader.SafeGetInt("anno_corso"),
                    Ripetente = reader.SafeGetInt("ripetente"),
                    ConfermaSemestreFiltroDi = reader.SafeGetInt("Iscritto_semestre_filtroDI")
                });
            });
        }

        private static void BuildCarrieraPregressaAggregate(VerificaPipelineContext context)
        {
            foreach (var info in context.Students.Values)
            {
                var iscr = info.InformazioniIscrizione;
                var items = iscr.CarrierePregresse;

                int count = 0;
                int? lastYear = null;
                decimal totalCredits = 0m;
                int hasPassaggioEstero = 0;
                int hasRipetenza = 0;
                var codici = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in items)
                {
                    count++;

                    if (item.AnnoAvvenimento.HasValue && (!lastYear.HasValue || item.AnnoAvvenimento.Value > lastYear.Value))
                        lastYear = item.AnnoAvvenimento.Value;

                    totalCredits += item.NumeroCrediti ?? 0m;

                    if (item.PassaggioCorsoEstero != 0)
                        hasPassaggioEstero = 1;

                    if (item.Ripetente != 0)
                        hasRipetenza = 1;

                    if (!string.IsNullOrWhiteSpace(item.CodAvvenimento))
                        codici.Add(item.CodAvvenimento);
                }

                iscr.NumeroEventiCarrieraPregressa = count;
                iscr.UltimoAnnoAvvenimentoCarrieraPregressa = count == 0 ? null : lastYear;
                iscr.TotaleCreditiCarrieraPregressa = totalCredits;
                iscr.HaPassaggioCorsoEsteroCarrieraPregressa = hasPassaggioEstero;
                iscr.HaRipetenzaCarrieraPregressa = hasRipetenza;
                iscr.CodiciAvvenimentoCarrieraPregressa = string.Join("|", codici);
            }
        }

        private const string EsamiCatalogSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CONVERT(NVARCHAR(50), e.COD_CORSO_LAUREA) AS Cod_corso_laurea,
    CONVERT(NVARCHAR(20), e.COD_TIPO_ORDINAMENTO) AS Cod_tipo_ordinamento,
    CONVERT(NVARCHAR(20), e.Anno_Accad_Inizio) AS Anno_accad_inizio,
    TRY_CONVERT(INT, e.ANNO_CORSO) AS Anno_corso,
    SUM(TRY_CONVERT(DECIMAL(18,2), e.NUM_ESAMI)) AS Numero_esami
FROM ESAMI e
WHERE e.ANNO_ACCADEMICO = @AA
GROUP BY
    CONVERT(NVARCHAR(50), e.COD_CORSO_LAUREA),
    CONVERT(NVARCHAR(20), e.COD_TIPO_ORDINAMENTO),
    CONVERT(NVARCHAR(20), e.Anno_Accad_Inizio),
    TRY_CONVERT(INT, e.ANNO_CORSO);";

        private void LoadEsamiCatalog(VerificaPipelineContext context)
        {
            context.EsamiCatalog.Clear();

            if (!ObjectExists(context.Connection, "ESAMI"))
                return;

            using var cmd = new SqlCommand(EsamiCatalogSql, context.Connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };
            cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = context.AnnoAccademico;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                context.EsamiCatalog.Add(
                    reader.SafeGetString("Cod_corso_laurea"),
                    reader.SafeGetString("Cod_tipo_ordinamento"),
                    reader.SafeGetString("Anno_accad_inizio"),
                    reader.SafeGetInt("Anno_corso"),
                    reader.SafeGetDecimal("Numero_esami"));
            }
        }

        private static bool TryParseInt(string? value, out int parsed)
            => int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }
}
