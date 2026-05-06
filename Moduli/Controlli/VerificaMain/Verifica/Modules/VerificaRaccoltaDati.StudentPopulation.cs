using ProcedureNet7.Verifica;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private const string EsitoBsPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT CAST(t.NumDomanda AS INT) AS NumDomanda, t.CodFiscale
    FROM {TEMP_TABLE} t
),
ESITI_LAST AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        UPPER(LTRIM(RTRIM(ISNULL(ec.Cod_beneficio,'')))) AS CodBeneficio,
        CAST(ec.Cod_tipo_esito AS INT) AS CodTipoEsito,
        TRY_CONVERT(DECIMAL(18,2), ec.Imp_beneficio) AS ImportoAssegnato,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda, UPPER(LTRIM(RTRIM(ISNULL(ec.Cod_beneficio,'')))) ORDER BY ec.Data_validita DESC) AS rn
    FROM D
    JOIN ESITI_CONCORSI ec
      ON ec.Num_domanda = D.NumDomanda
    WHERE ec.Anno_accademico = @AA
)
SELECT NumDomanda, CodFiscale, CodBeneficio, CodTipoEsito, ISNULL(ImportoAssegnato, 0) AS ImportoAssegnato
FROM ESITI_LAST
WHERE rn = 1;";

        private const string StatusCompilazionePopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CAST(ISNULL(v.STATUS_COMPILAZIONE,0) AS INT) AS StatusCompilazione
FROM {TEMP_TABLE} t
JOIN vSTATUS_COMPILAZIONE v
  ON v.Num_domanda = CAST(t.NumDomanda AS INT)
WHERE v.Anno_accademico = @AA;";

        private const string SessoStudentePopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    ISNULL(s.Sesso,'') AS StudenteSesso
FROM {TEMP_TABLE} t
JOIN Studente s
  ON UPPER(s.Cod_fiscale) = UPPER(t.CodFiscale);";

        private const string DatiGeneraliDomandaPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CAST(CASE WHEN ISNULL(dg.RIFUG_POLITICO,0) = 1 THEN 1 ELSE 0 END AS BIT) AS RifugiatoPolitico,
    CAST(CASE WHEN ISNULL(dg.INVALIDO,0) = 1 THEN 1 ELSE 0 END AS BIT) AS Invalido
FROM {TEMP_TABLE} t
JOIN DATIGENERALI_DOM dg
  ON dg.NUM_DOMANDA = CAST(t.NumDomanda AS INT)
 AND dg.ANNO_ACCADEMICO = @AA
 AND dg.DATA_VALIDITA =
 (
    SELECT MAX(dg2.DATA_VALIDITA)
    FROM DATIGENERALI_DOM dg2
    WHERE dg2.ANNO_ACCADEMICO = dg.ANNO_ACCADEMICO
      AND dg2.NUM_DOMANDA = dg.NUM_DOMANDA
 );";

        private const string MonetizzazioneMensaPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CAST(CASE WHEN ISNULL(mm.Concessa_monetizzazione,0) = 1 THEN 1 ELSE 0 END AS BIT) AS ConcessaMonetizzazione
FROM {TEMP_TABLE} t
JOIN vMonetizzazione_Mensa mm
  ON mm.Num_domanda = t.NumDomanda;";

        private const string ResidenzaPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    ISNULL(r.COD_COMUNE,'') AS ComuneResidenza,
    UPPER(ISNULL(r.PROVINCIA_RESIDENZA,'')) AS ProvinciaResidenza
FROM {TEMP_TABLE} t
JOIN vRESIDENZA r
  ON UPPER(r.COD_FISCALE) = UPPER(t.CodFiscale)
WHERE r.ANNO_ACCADEMICO = @AA
  AND (r.TIPO_BANDO = 'LZ' OR r.TIPO_BANDO IS NULL);";

        private const string StatusSedeAttualePopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    ISNULL(v.Status_sede,'') AS StatusSedeAttuale
FROM {TEMP_TABLE} t
JOIN VALORI_CALCOLATI v
  ON v.NUM_DOMANDA = CAST(t.NumDomanda AS INT)
 AND v.ANNO_ACCADEMICO = @AA
 AND v.DATA_VALIDITA =
 (
    SELECT MAX(v2.DATA_VALIDITA)
    FROM VALORI_CALCOLATI v2
    WHERE v2.ANNO_ACCADEMICO = v.ANNO_ACCADEMICO
      AND v2.NUM_DOMANDA = v.NUM_DOMANDA
 );";

        private const string ForzatureStatusSedePopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    UPPER(f.Status_sede) AS ForcedStatus
FROM {TEMP_TABLE} t
JOIN Forzature_StatusSede f
  ON UPPER(f.Cod_Fiscale) = UPPER(t.CodFiscale)
WHERE f.Anno_Accademico = @AA
  AND f.Data_fine_validita IS NULL
  AND f.Status_sede IN ('A','B','C','D');";

        private const string DomicilioCorrentePopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT CAST(t.NumDomanda AS INT) AS NumDomanda, t.CodFiscale
    FROM {TEMP_TABLE} t
),
DOM_LRS AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        ISNULL(lrs.COD_COMUNE,'') AS ComuneDomicilio,
        CAST(ISNULL(lrs.TITOLO_ONEROSO,0) AS BIT) AS TitoloOneroso,
        CAST(ISNULL(lrs.TIPO_CONTRATTO_TITOLO_ONEROSO,0) AS BIT) AS ContrattoEnte,
        ISNULL(lrs.TIPO_ENTE,'') AS TipoEnte,
        ISNULL(lrs.N_SERIE_CONTRATTO,'') AS SerieContratto,
        ISNULL(lrs.DATA_REG_CONTRATTO,'') AS DataRegistrazione,
        ISNULL(lrs.DATA_DECORRENZA,'') AS DataDecorrenza,
        ISNULL(lrs.DATA_SCADENZA,'') AS DataScadenza,
        ISNULL(lrs.DURATA_CONTRATTO,0) AS DurataContratto,
        CAST(ISNULL(lrs.PROROGA,0) AS BIT) AS Prorogato,
        ISNULL(lrs.DURATA_PROROGA,0) AS DurataProroga,
        ISNULL(lrs.ESTREMI_PROROGA,'') AS SerieProroga,
        ISNULL(lrs.DENOM_ENTE,'') AS DenomEnte,
        ISNULL(lrs.IMPORTO_RATA,0) AS ImportoRataEnte,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY lrs.DATA_VALIDITA DESC) AS rn
    FROM D
    JOIN LUOGO_REPERIBILITA_STUDENTE lrs
      ON UPPER(lrs.COD_FISCALE) = UPPER(D.CodFiscale)
    WHERE lrs.ANNO_ACCADEMICO = @AA
      AND lrs.TIPO_LUOGO = 'DOM'
)
SELECT
    NumDomanda,
    CodFiscale,
    ComuneDomicilio,
    TitoloOneroso,
    ContrattoEnte,
    TipoEnte,
    SerieContratto,
    DataRegistrazione,
    DataDecorrenza,
    DataScadenza,
    DurataContratto,
    Prorogato,
    DurataProroga,
    SerieProroga,
    DenomEnte,
    ImportoRataEnte
FROM DOM_LRS
WHERE rn = 1;";

        private const string IstanzaDomicilioApertaPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT CAST(t.NumDomanda AS INT) AS NumDomanda, t.CodFiscale
    FROM {TEMP_TABLE} t
),
IST_OPEN AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        CAST(idg.Num_istanza AS INT) AS NumIstanza,
        ISNULL(CONVERT(NVARCHAR(32), idg.Cod_tipo_istanza), '') AS CodTipoIstanza,
        ISNULL(icl.COD_COMUNE,'') AS ComuneDomicilio,
        CAST(ISNULL(icl.TITOLO_ONEROSO,0) AS BIT) AS TitoloOneroso,
        CAST(ISNULL(icl.TIPO_CONTRATTO_TITOLO_ONEROSO,0) AS BIT) AS ContrattoEnte,
        ISNULL(icl.TIPO_ENTE,'') AS TipoEnte,
        ISNULL(icl.N_SERIE_CONTRATTO,'') AS SerieContratto,
        ISNULL(icl.DATA_REG_CONTRATTO,'') AS DataRegistrazione,
        ISNULL(icl.DATA_DECORRENZA,'') AS DataDecorrenza,
        ISNULL(icl.DATA_SCADENZA,'') AS DataScadenza,
        ISNULL(icl.DURATA_CONTRATTO,0) AS DurataContratto,
        CAST(ISNULL(icl.PROROGA,0) AS BIT) AS Prorogato,
        ISNULL(icl.DURATA_PROROGA,0) AS DurataProroga,
        ISNULL(icl.ESTREMI_PROROGA,'') AS SerieProroga,
        ISNULL(icl.DENOM_ENTE,'') AS DenomEnte,
        ISNULL(icl.IMPORTO_RATA,0) AS ImportoRataEnte,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY idg.Data_validita DESC, idg.Num_istanza DESC) AS rn
    FROM D
    JOIN Istanza_dati_generali idg
      ON UPPER(idg.Cod_fiscale) = UPPER(D.CodFiscale)
    JOIN Istanza_status iis
      ON iis.Num_istanza = idg.Num_istanza
     AND iis.data_fine_validita IS NULL
    JOIN Istanza_Contratto_locazione icl
      ON icl.Num_istanza = idg.Num_istanza
     AND icl.data_fine_validita IS NULL
    WHERE idg.Anno_accademico = @AA
      AND idg.Data_fine_validita IS NULL
      AND idg.Esito_istanza IS NULL
)
SELECT
    NumDomanda,
    CodFiscale,
    NumIstanza,
    CodTipoIstanza,
    ComuneDomicilio,
    TitoloOneroso,
    ContrattoEnte,
    TipoEnte,
    SerieContratto,
    DataRegistrazione,
    DataDecorrenza,
    DataScadenza,
    DurataContratto,
    Prorogato,
    DurataProroga,
    SerieProroga,
    DenomEnte,
    ImportoRataEnte
FROM IST_OPEN
WHERE rn = 1;";

        private const string UltimaIstanzaChiusaDomicilioPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT CAST(t.NumDomanda AS INT) AS NumDomanda, t.CodFiscale
    FROM {TEMP_TABLE} t
),
IST_CLOSED_LAST AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        CAST(idg.Num_istanza AS INT) AS NumIstanza,
        ISNULL(CONVERT(NVARCHAR(32), idg.Cod_tipo_istanza), '') AS CodTipoIstanza,
        ISNULL(CONVERT(NVARCHAR(64), idg.Esito_istanza), '') AS EsitoIstanza,
        ISNULL(CONVERT(NVARCHAR(256), iis.UTENTE_PRESA_CARICO), '') AS UtentePresaCarico,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY idg.Data_validita DESC, idg.Num_istanza DESC) AS rn
    FROM D
    JOIN Istanza_dati_generali idg
      ON UPPER(idg.Cod_fiscale) = UPPER(D.CodFiscale)
    JOIN Istanza_status iis
      ON iis.Num_istanza = idg.Num_istanza
     AND iis.data_fine_validita IS NOT NULL
    JOIN Istanza_Contratto_locazione icl
      ON icl.Num_istanza = idg.Num_istanza
    WHERE idg.Anno_accademico = @AA
      AND idg.Data_fine_validita IS NOT NULL
      AND idg.Esito_istanza IS NOT NULL
)
SELECT
    NumDomanda,
    CodFiscale,
    NumIstanza,
    CodTipoIstanza,
    EsitoIstanza,
    UtentePresaCarico
FROM IST_CLOSED_LAST
WHERE rn = 1;";

        private void LoadEsitoBs(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadEsitiConcorso", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(EsitoBsPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                if (!context.EsitiConcorsoByStudentBenefit.TryGetValue(key, out var byBenefit) || byBenefit == null)
                {
                    byBenefit = new System.Collections.Generic.Dictionary<string, EsitoConcorsoBenefitRaw>(StringComparer.OrdinalIgnoreCase);
                    context.EsitiConcorsoByStudentBenefit[key] = byBenefit;
                }

                string codBeneficio = EsitoBorsaSupport.NormalizeUpper(reader.SafeGetString("CodBeneficio"));
                var raw = new EsitoConcorsoBenefitRaw
                {
                    CodBeneficio = codBeneficio,
                    CodTipoEsito = reader.SafeGetInt("CodTipoEsito"),
                    ImportoAssegnato = reader.SafeGetDecimal("ImportoAssegnato")
                };
                byBenefit[codBeneficio] = raw;

                if (string.Equals(codBeneficio, "BS", StringComparison.OrdinalIgnoreCase))
                {
                    info.InformazioniEconomiche.Raw.CodTipoEsitoBS = raw.CodTipoEsito ?? 0;
                    info.InformazioniEconomiche.Raw.ImportoAssegnato = Convert.ToDouble(raw.ImportoAssegnato ?? 0m, CultureInfo.InvariantCulture);
                }
            });
        }

        private void LoadStatusCompilazione(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadStatusCompilazione", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(StatusCompilazionePopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) => info.StatusCompilazione = reader.SafeGetInt("StatusCompilazione"));
        }

        private void LoadSessoStudente(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadSessoStudente", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(SessoStudentePopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) => info.InformazioniPersonali.Sesso = reader.SafeGetString("StudenteSesso").Trim().ToUpperInvariant());
        }

        private void LoadDatiGeneraliDomanda(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadDatiGeneraliDomanda", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(DatiGeneraliDomandaPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                info.InformazioniPersonali.Rifugiato = reader.SafeGetBool("RifugiatoPolitico");
                info.InformazioniPersonali.Disabile = reader.SafeGetBool("Invalido");
            });
        }

        private void LoadMonetizzazioneMensa(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadMonetizzazioneMensa", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(MonetizzazioneMensaPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) => info.InformazioniBeneficio.ConcessaMonetizzazioneMensa = reader.SafeGetBool("ConcessaMonetizzazione"));
        }

        private void LoadNucleoFamiliarePerStatusSede(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadNucleoFamiliareStatusSede", $"AA={context.AnnoAccademico}");

            foreach (var info in context.Students.Values)
            {
                var raw = info.InformazioniEconomiche.Raw;
                info.SetNucleoFamiliare(raw.NumeroComponenti, raw.NumeroConviventiEstero);
            }
        }

        private void LoadResidenza(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadResidenza", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(ResidenzaPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                string comuneResidenza = reader.SafeGetString("ComuneResidenza").Trim();
                string provinciaResidenza = reader.SafeGetString("ProvinciaResidenza").Trim().ToUpperInvariant();
                info.SetResidenza(string.Empty, comuneResidenza, provinciaResidenza, string.Empty, comuneResidenza);
            });
        }

        private void LoadStatusSedeAttuale(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadStatusSedeAttuale", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(StatusSedeAttualePopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) => info.InformazioniSede.StatusSede = reader.SafeGetString("StatusSedeAttuale").Trim().ToUpperInvariant());
        }

        private void LoadForzatureStatusSede(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadForzatureStatusSede", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(ForzatureStatusSedePopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) => info.InformazioniSede.ForzaturaStatusSede = reader.SafeGetString("ForcedStatus").Trim().ToUpperInvariant());
        }

        private void LoadStatusSedeClassificationFlags(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadStatusSedeClassificationFlags", $"AA={context.AnnoAccademico}");

            const string createSql = @"
IF OBJECT_ID('tempdb..#StatusSedeClassBase') IS NOT NULL
    DROP TABLE #StatusSedeClassBase;

CREATE TABLE #StatusSedeClassBase
(
    CodFiscale NVARCHAR(32) NOT NULL,
    NumDomanda NUMERIC(18,0) NOT NULL,
    ComuneResidenza NVARCHAR(20) NOT NULL,
    CodSedeStudi NVARCHAR(50) NOT NULL,
    CodSedeDistaccata NVARCHAR(50) NOT NULL,
    AlwaysA BIT NOT NULL,
    CONSTRAINT PK_StatusSedeClassBase PRIMARY KEY CLUSTERED (CodFiscale, NumDomanda)
);";

            using (var createCommand = new SqlCommand(createSql, context.Connection) { CommandTimeout = 9999999 })
                createCommand.ExecuteNonQuery();

            var table = BuildStatusSedeClassBaseTable(context);
            if (table.Rows.Count > 0)
            {
                using var bulk = new SqlBulkCopy(context.Connection, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = "#StatusSedeClassBase",
                    BulkCopyTimeout = 9999999,
                    BatchSize = 10000
                };

                bulk.ColumnMappings.Add("CodFiscale", "CodFiscale");
                bulk.ColumnMappings.Add("NumDomanda", "NumDomanda");
                bulk.ColumnMappings.Add("ComuneResidenza", "ComuneResidenza");
                bulk.ColumnMappings.Add("CodSedeStudi", "CodSedeStudi");
                bulk.ColumnMappings.Add("CodSedeDistaccata", "CodSedeDistaccata");
                bulk.ColumnMappings.Add("AlwaysA", "AlwaysA");
                bulk.WriteToServer(table);
            }

            const string sql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(b.NumDomanda AS INT) AS NumDomanda,
    b.CodFiscale,
    b.AlwaysA,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM COMUNI_INSEDE ci
        WHERE ci.riga_valida = 1
          AND ci.cod_comune = b.ComuneResidenza
          AND ci.cod_sede_studi = b.CodSedeStudi
          AND ISNULL(ci.cod_sede_distaccata,'00000') = ISNULL(b.CodSedeDistaccata,'00000')
    ) THEN 1 ELSE 0 END AS BIT) AS InSedeList,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM COMUNI_PENDOLARI cp
        WHERE cp.cod_comune = b.ComuneResidenza
          AND cp.cod_sede_studi = b.CodSedeStudi
          AND ISNULL(cp.cod_sede_distaccata,'00000') = ISNULL(b.CodSedeDistaccata,'00000')
    ) THEN 1 ELSE 0 END AS BIT) AS PendolareList,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM COMUNI_FUORISEDE cf
        WHERE cf.cod_comune = b.ComuneResidenza
          AND cf.cod_sede_studi = b.CodSedeStudi
          AND ISNULL(cf.cod_sede_distaccata,'00000') = ISNULL(b.CodSedeDistaccata,'00000')
    ) THEN 1 ELSE 0 END AS BIT) AS FuoriSedeList,
    CAST(10 AS INT) AS MinMesiDomicilioFuoriSede
FROM #StatusSedeClassBase b;";

            using var cmd = new SqlCommand(sql, context.Connection) { CommandTimeout = 9999999 };
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                info.InformazioniSede.AlwaysA = reader.SafeGetBool("AlwaysA");
                info.InformazioniSede.InSedeList = reader.SafeGetBool("InSedeList");
                info.InformazioniSede.PendolareList = reader.SafeGetBool("PendolareList");
                info.InformazioniSede.FuoriSedeList = reader.SafeGetBool("FuoriSedeList");
                info.InformazioniSede.MinMesiDomicilioFuoriSede = reader.SafeGetInt("MinMesiDomicilioFuoriSede");
            });
        }

        private static DataTable BuildStatusSedeClassBaseTable(VerificaPipelineContext context)
        {
            var table = new DataTable();
            table.Columns.Add("CodFiscale", typeof(string));
            table.Columns.Add("NumDomanda", typeof(decimal));
            table.Columns.Add("ComuneResidenza", typeof(string));
            table.Columns.Add("CodSedeStudi", typeof(string));
            table.Columns.Add("CodSedeDistaccata", typeof(string));
            table.Columns.Add("AlwaysA", typeof(bool));

            foreach (var info in context.Students.Values)
            {
                string codFiscale = (info.InformazioniPersonali.CodFiscale ?? string.Empty).Trim().ToUpperInvariant();
                string numDomandaText = (info.InformazioniPersonali.NumDomanda ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(codFiscale) ||
                    (!decimal.TryParse(numDomandaText, NumberStyles.Number, CultureInfo.InvariantCulture, out var numDomanda) &&
                     !decimal.TryParse(numDomandaText, NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out numDomanda)))
                    continue;

                string comuneResidenza = GetComuneResidenzaFromContext(info);
                string codSedeStudi = (info.InformazioniIscrizione.CodSedeStudi ?? string.Empty).Trim();
                string codSedeDistaccata = (info.InformazioniIscrizione.CodSedeDistaccata ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(codSedeDistaccata))
                    codSedeDistaccata = "00000";

                table.Rows.Add(
                    codFiscale,
                    numDomanda,
                    comuneResidenza,
                    codSedeStudi,
                    codSedeDistaccata,
                    info.InformazioniSede.AlwaysA);
            }

            return table;
        }

        private static string GetComuneResidenzaFromContext(StudenteInfo info)
        {
            if (info?.InformazioniSede?.Residenza == null)
                return string.Empty;

            var codComune = (info.InformazioniSede.Residenza.codComune ?? string.Empty).Trim();
            if (codComune.Length > 0)
                return codComune;

            return (info.InformazioniSede.Residenza.nomeComune ?? string.Empty).Trim();
        }

        private void LoadEsitoPaPerAlloggio(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadEsitoPaPerAlloggio", $"AA={context.AnnoAccademico}");

            foreach (var pair in context.Students)
            {
                bool beneficioPaRichiesto = context.EsitoBorsaFactsByStudent.TryGetValue(pair.Key, out var facts)
                                           && facts.BeneficiRichiesti.Contains("PA");

                bool paAssegnato = context.EsitiConcorsoByStudentBenefit.TryGetValue(pair.Key, out var byBenefit)
                                   && byBenefit != null
                                   && byBenefit.TryGetValue("PA", out var pa)
                                   && ((pa.CodTipoEsito ?? 0) == 1 || (pa.CodTipoEsito ?? 0) == 2);

                pair.Value.InformazioniSede.HasAlloggio12 = beneficioPaRichiesto && paAssegnato;
            }
        }

        private void LoadDomicilioCorrente(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadDomicilioCorrente", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(DomicilioCorrentePopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) => ApplyCurrentDomicilioSnapshot(info, ReadDomicilioSnapshot(reader, string.Empty)));
        }

        private void LoadIstanzaDomicilioAperta(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadIstanzaDomicilioAperta", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(IstanzaDomicilioApertaPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                info.InformazioniSede.HasIstanzaDomicilio = true;
                info.InformazioniSede.CodTipoIstanzaDomicilio = reader.SafeGetString("CodTipoIstanza").Trim();
                info.InformazioniSede.NumIstanzaDomicilio = reader.SafeGetInt("NumIstanza");
                info.InformazioniSede.IstanzaDomicilio = ReadDomicilioSnapshot(reader, string.Empty);
            });
        }

        private void LoadUltimaIstanzaChiusaDomicilio(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadUltimaIstanzaChiusaDomicilio", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(UltimaIstanzaChiusaDomicilioPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                info.InformazioniSede.HasUltimaIstanzaChiusaDomicilio = true;
                info.InformazioniSede.CodTipoUltimaIstanzaChiusaDomicilio = reader.SafeGetString("CodTipoIstanza").Trim();
                info.InformazioniSede.NumUltimaIstanzaChiusaDomicilio = reader.SafeGetInt("NumIstanza");
                info.InformazioniSede.EsitoUltimaIstanzaChiusaDomicilio = reader.SafeGetString("EsitoIstanza").Trim();
                info.InformazioniSede.UtentePresaCaricoUltimaIstanzaChiusaDomicilio = reader.SafeGetString("UtentePresaCarico").Trim();
            });
        }
    }
}
