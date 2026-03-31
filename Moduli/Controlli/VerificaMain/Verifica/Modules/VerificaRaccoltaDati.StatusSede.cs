using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private const string StatusSedeGetInputsFromTempCandidatesSqlTemplate = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH
D AS
(
    SELECT
        CAST(NumDomanda AS INT) AS NumDomanda,
        UPPER(LTRIM(RTRIM(CodFiscale))) AS CodFiscale,
        COALESCE(TipoBando,'') AS TipoBando,
        CAST(ISNULL(CodTipoEsitoBS,0) AS INT) AS CodTipoEsitoBS,
        CAST(ISNULL(ImportoAssegnato,0) AS INT) AS ImportoAssegnato,
        CAST(ISNULL(StatusCompilazione,0) AS INT) AS StatusCompilazione
    FROM {{CANDIDATES}}
),

VV AS
(
    SELECT CAST(v.Num_domanda AS INT) AS NumDomanda, v.Status_sede
    FROM vValori_calcolati v
    JOIN D ON D.NumDomanda = v.Num_domanda
    WHERE v.Anno_accademico = @AA
),

Forzature AS
(
    SELECT
        UPPER(LTRIM(RTRIM(f.Cod_Fiscale))) AS CodFiscale,
        UPPER(LTRIM(RTRIM(f.Status_sede))) AS ForcedStatus
    FROM Forzature_StatusSede f
    JOIN D ON D.CodFiscale = UPPER(LTRIM(RTRIM(f.Cod_Fiscale)))
    WHERE f.Anno_Accademico = @AA
      AND f.Data_fine_validita IS NULL
      AND f.Status_sede IN ('A','B','C','D')
),

StudenteEstrazione AS
(
    SELECT Sesso as StudenteSesso, UPPER(LTRIM(RTRIM(s.Cod_Fiscale))) AS CodFiscale
    FROM Studente S
    JOIN D ON D.CodFiscale = UPPER(LTRIM(RTRIM(S.Cod_Fiscale)))
),

MonetizzazioneMensa AS
(
    SELECT UPPER(LTRIM(RTRIM(mm.Cod_Fiscale))) AS CodFiscale, 
        CAST(CASE WHEN ISNULL(mm.Concessa_monetizzazione,0) = 1 THEN 1 ELSE 0 END AS BIT) AS ConcessaMonetizzazione
    FROM vMonetizzazione_Mensa mm 
    JOIN D on D.CodFiscale = UPPER(LTRIM(RTRIM(mm.Cod_Fiscale))) 
        AND D.NumDomanda = mm.Num_Domanda
),

DG AS
(
    SELECT
        CAST(v.Num_domanda AS INT) AS NumDomanda,
        CAST(CASE WHEN ISNULL(v.Rifug_politico,0) = 1 THEN 1 ELSE 0 END AS BIT) AS RifugiatoPolitico,
        CAST(CASE WHEN ISNULL(v.Invalido,0) = 1 THEN 1 ELSE 0 END AS BIT) AS Invalido
    FROM vDATIGENERALI_dom v
    JOIN D ON D.NumDomanda = v.Num_domanda
    WHERE v.Anno_accademico = @AA
),

NF AS
(
    SELECT
        CAST(n.Num_domanda AS INT) AS NumDomanda,
        CAST(ISNULL(n.Num_componenti,0) AS INT) AS NumComponenti,
        CAST(ISNULL(n.Numero_conviventi_estero,0) AS INT) AS NumConvEstero
    FROM VNUCLEO_FAMILIARE n
    JOIN D ON D.NumDomanda = n.Num_domanda
    WHERE n.Anno_accademico = @AA
),

RES AS
(
    SELECT
        UPPER(LTRIM(RTRIM(r.Cod_fiscale))) AS CodFiscale,
        ISNULL(r.Cod_comune,'') AS ComuneResidenza,
        ISNULL(r.provincia_residenza,'') AS ProvinciaResidenza
    FROM vResidenza r
    JOIN D ON D.CodFiscale = UPPER(LTRIM(RTRIM(r.Cod_fiscale)))
    WHERE r.Anno_accademico = @AA
),

ISCR AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        i.Cod_sede_studi AS CodSedeStudi,
        i.Cod_corso_laurea AS CodCorso,
        i.Cod_facolta AS CodFacolta,
        i.Cod_tipologia_studi AS CodTipoStudi,
        CAST(ISNULL(cl.Corso_stem,0) AS INT) AS Stem,
        CASE
            WHEN NULLIF(LTRIM(RTRIM(ISNULL(cl.Cod_sede_distaccata,''))), '') IS NULL THEN '00000'
            ELSE LTRIM(RTRIM(cl.Cod_sede_distaccata))
        END AS CodSedeDistaccata,
        COALESCE(NULLIF(cl.comune_sede_studi_status,''), cl.Comune_Sede_studi) AS ComuneSedeStudi,
        CAST(
            CASE
                WHEN ISNULL(ss.Telematica,0) = 1 OR ISNULL(cl.corso_in_presenza,1) = 0 THEN 1
                ELSE 0
            END
        AS BIT) AS AlwaysA
    FROM D
    JOIN vIscrizioni i
      ON i.Anno_accademico = @AA
     AND UPPER(LTRIM(RTRIM(i.Cod_fiscale))) = D.CodFiscale
     AND COALESCE(i.tipo_bando,'') = COALESCE(D.TipoBando,'')
    JOIN Corsi_laurea cl
      ON i.Cod_corso_laurea     = cl.Cod_corso_laurea
     AND i.Anno_accad_inizio    = cl.Anno_accad_inizio
     AND i.Cod_tipo_ordinamento = cl.Cod_tipo_ordinamento
     AND i.Cod_facolta          = cl.Cod_facolta
     AND i.Cod_sede_studi       = cl.Cod_sede_studi
     AND i.Cod_tipologia_studi  = cl.Cod_tipologia_studi
    LEFT JOIN Sede_studi ss
      ON ss.Cod_sede_studi = i.Cod_sede_studi
),

PROV_SEDE AS
(
    SELECT
        i.NumDomanda,
        ISNULL(c.COD_PROVINCIA,'') AS ProvinciaSede
    FROM ISCR i
    LEFT JOIN COMUNI c
      ON c.COD_COMUNE = i.ComuneSedeStudi
),

PA_LAST AS
(
    SELECT
        CAST(ec.Num_domanda AS INT) AS NumDomanda,
        CAST(ec.Cod_tipo_esito AS INT) AS CodTipoEsito,
        ROW_NUMBER() OVER (PARTITION BY ec.Num_domanda ORDER BY ec.Data_validita DESC) AS rn
    FROM ESITI_CONCORSI ec
    JOIN D ON D.NumDomanda = ec.Num_domanda
    JOIN vBenefici_richiesti vb on D.NumDomanda = vb.Num_domanda and ec.Cod_beneficio = vb.Cod_beneficio and vb.Anno_accademico = ec.Anno_accademico
    WHERE ec.Anno_accademico = @AA
      AND UPPER(ec.Cod_beneficio) = 'PA'
),
PA_FLAG AS
(
    SELECT
        NumDomanda,
        CAST(CASE WHEN CodTipoEsito IN (1,2) THEN 1 ELSE 0 END AS BIT) AS HasAlloggio12
    FROM PA_LAST
    WHERE rn = 1
),

DOM_LRS AS
(
    SELECT
        UPPER(LTRIM(RTRIM(lrs.COD_FISCALE))) AS CodFiscale,
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
        ROW_NUMBER() OVER (PARTITION BY lrs.COD_FISCALE ORDER BY lrs.DATA_VALIDITA DESC) AS rn
    FROM LUOGO_REPERIBILITA_STUDENTE lrs
    JOIN D ON D.CodFiscale = UPPER(LTRIM(RTRIM(lrs.COD_FISCALE)))
    WHERE lrs.ANNO_ACCADEMICO = @AA
      AND lrs.TIPO_LUOGO = 'DOM'
),
DOM1 AS
(
    SELECT *
    FROM DOM_LRS
    WHERE rn = 1
),
IST_OPEN AS
(
    SELECT
        UPPER(LTRIM(RTRIM(idg.Cod_fiscale))) AS CodFiscale,
        CAST(idg.Num_domanda AS INT) AS NumDomanda,
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
        ROW_NUMBER() OVER
        (
            PARTITION BY UPPER(LTRIM(RTRIM(idg.Cod_fiscale)))
            ORDER BY idg.Data_validita DESC, idg.Num_istanza DESC
        ) AS rn
    FROM Istanza_dati_generali idg
    JOIN D
      ON D.CodFiscale = UPPER(LTRIM(RTRIM(idg.Cod_fiscale)))
    JOIN Istanza_status iis
      ON iis.Num_istanza = idg.Num_istanza
     AND iis.data_fine_validita IS NULL
    JOIN Istanza_Contratto_locazione icl
      ON icl.Num_istanza = idg.Num_istanza
     AND icl.data_fine_validita IS NULL
    WHERE idg.Anno_accademico = @AA
      AND idg.Data_fine_validita IS NULL
      AND idg.Esito_istanza IS NULL
),

IST1 AS
(
    SELECT *
    FROM IST_OPEN
    WHERE rn = 1
),
IST_CLOSED_LAST AS
(
    SELECT
        UPPER(LTRIM(RTRIM(idg.Cod_fiscale))) AS CodFiscale,
        CAST(idg.Num_domanda AS INT) AS NumDomanda,
        CAST(idg.Num_istanza AS INT) AS NumIstanza,
        ISNULL(CONVERT(NVARCHAR(32), idg.Cod_tipo_istanza), '') AS CodTipoIstanza,
        ISNULL(CONVERT(NVARCHAR(64), idg.Esito_istanza), '') AS EsitoIstanza,
        ISNULL(CONVERT(NVARCHAR(256), iis.UTENTE_PRESA_CARICO), '') AS UtentePresaCarico,
        ROW_NUMBER() OVER
        (
            PARTITION BY UPPER(LTRIM(RTRIM(idg.Cod_fiscale)))
            ORDER BY idg.Data_validita DESC, idg.Num_istanza DESC
        ) AS rn
    FROM Istanza_dati_generali idg
    JOIN D
      ON D.CodFiscale = UPPER(LTRIM(RTRIM(idg.Cod_fiscale)))
    JOIN Istanza_status iis
      ON iis.Num_istanza = idg.Num_istanza
     AND iis.data_fine_validita IS NOT NULL
    JOIN Istanza_Contratto_locazione icl
      ON icl.Num_istanza = idg.Num_istanza
    WHERE idg.Anno_accademico = @AA
      AND idg.Data_fine_validita IS NOT NULL
      AND idg.Esito_istanza IS NOT NULL
),
IST_CLOSED1 AS
(
    SELECT *
    FROM IST_CLOSED_LAST
    WHERE rn = 1
)

SELECT
    D.NumDomanda,
    D.CodFiscale,
    D.TipoBando,

    D.CodTipoEsitoBS,
    D.ImportoAssegnato,
    D.StatusCompilazione,

    ISNULL(vv.Status_sede,'') AS StatusSedeAttuale,
    ISNULL(f.ForcedStatus,'') AS ForcedStatus,
    ISNULL(s.StudenteSesso,'') AS StudenteSesso,

    ISNULL(i.AlwaysA, CAST(0 AS BIT)) AS AlwaysA,

    ISNULL(dg.RifugiatoPolitico, CAST(0 AS BIT)) AS RifugiatoPolitico,
    ISNULL(mm.ConcessaMonetizzazione, CAST(0 AS BIT)) AS ConcessaMonetizzazione,
    ISNULL(dg.Invalido, CAST(0 AS BIT)) AS Invalido,
    ISNULL(nf.NumComponenti,0) AS NumComponenti,
    ISNULL(nf.NumConvEstero,0) AS NumConvEstero,

    ISNULL(r.ComuneResidenza,'') AS ComuneResidenza,
    UPPER(ISNULL(r.ProvinciaResidenza,'')) AS ProvinciaResidenza,

    ISNULL(i.CodSedeStudi,'') AS CodSedeStudi,
    ISNULL(i.CodSedeDistaccata,'00000') AS CodSedeDistaccata,
    ISNULL(i.CodCorso,'') AS CodCorso,
    ISNULL(i.CodFacolta,'') AS CodFacolta,
    ISNULL(i.ComuneSedeStudi,'') AS ComuneSedeStudi,
    ISNULL(i.CodTipoStudi, '') AS CodTipoStudi,
    ISNULL(i.Stem, CAST(0 AS BIT)) AS Stem,
    UPPER(ISNULL(ps.ProvinciaSede,'')) AS ProvinciaSede,

    CAST(
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM COMUNI_INSEDE ci
            WHERE ci.riga_valida = 1 and ci.cod_comune = r.ComuneResidenza
              AND
              (
                  (ci.cod_sede_studi = i.CodSedeStudi)
                  AND
                  (ISNULL(ci.cod_sede_distaccata,'00000') = ISNULL(i.CodSedeDistaccata,'00000'))
              )
        )
        THEN 1 ELSE 0 END
    AS BIT) AS InSedeList,

    CAST(
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM COMUNI_PENDOLARI cp
            WHERE cp.cod_sede_studi = i.CodSedeStudi
              AND cp.cod_comune = r.ComuneResidenza
              AND ISNULL(cp.cod_sede_distaccata,'00000') = ISNULL(i.CodSedeDistaccata,'00000')
        )
        THEN 1 ELSE 0 END
    AS BIT) AS PendolareList,

    CAST(
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM COMUNI_FUORISEDE cf
            WHERE cf.cod_comune = r.ComuneResidenza
              AND
              (
                  (cf.cod_sede_studi = i.CodSedeStudi)
                  AND
                  (ISNULL(cf.cod_sede_distaccata,'00000') = ISNULL(i.CodSedeDistaccata,'00000'))
              )
        )
        THEN 1 ELSE 0 END
    AS BIT) AS FuoriSedeList,

    ISNULL(pf.HasAlloggio12, CAST(0 AS BIT)) AS HasAlloggio12,

    ISNULL(dom.ComuneDomicilio,'') AS ComuneDomicilio,
    ISNULL(dom.TitoloOneroso, CAST(0 AS BIT)) AS TitoloOneroso,
    ISNULL(dom.ContrattoEnte, CAST(0 AS BIT)) AS ContrattoEnte,
    ISNULL(dom.TipoEnte, '') AS TipoEnte,
    ISNULL(dom.SerieContratto,'') AS SerieContratto,
    ISNULL(dom.DataRegistrazione,'') AS DataRegistrazione,
    ISNULL(dom.DataDecorrenza,'') AS DataDecorrenza,
    ISNULL(dom.DataScadenza,'') AS DataScadenza,
    ISNULL(dom.DurataContratto,0) AS DurataContratto,
    ISNULL(dom.Prorogato, CAST(0 AS BIT)) AS Prorogato,
    ISNULL(dom.DurataProroga,0) AS DurataProroga,
    ISNULL(dom.SerieProroga,'') AS SerieProroga,
    ISNULL(dom.DenomEnte,'') AS DenomEnte,
    ISNULL(dom.ImportoRataEnte,0) AS ImportoRataEnte,

    ISNULL(ist.NumIstanza,0) AS NumIstanzaAperta,
    CAST(CASE WHEN ist.NumIstanza IS NULL THEN 0 ELSE 1 END AS BIT) AS HasIstanzaDomicilio,
    ISNULL(ist.CodTipoIstanza,'') AS CodTipoIstanzaAperta,

    ISNULL(ist.ComuneDomicilio,'') AS IstanzaComuneDomicilio,
    ISNULL(ist.TitoloOneroso, CAST(0 AS BIT)) AS IstanzaTitoloOneroso,
    ISNULL(ist.ContrattoEnte, CAST(0 AS BIT)) AS IstanzaContrattoEnte,
    ISNULL(ist.TipoEnte,'') AS IstanzaTipoEnte,
    ISNULL(ist.SerieContratto,'') AS IstanzaSerieContratto,
    ISNULL(ist.DataRegistrazione,'') AS IstanzaDataRegistrazione,
    ISNULL(ist.DataDecorrenza,'') AS IstanzaDataDecorrenza,
    ISNULL(ist.DataScadenza,'') AS IstanzaDataScadenza,
    ISNULL(ist.DurataContratto,0) AS IstanzaDurataContratto,
    ISNULL(ist.Prorogato, CAST(0 AS BIT)) AS IstanzaProrogato,
    ISNULL(ist.DurataProroga,0) AS IstanzaDurataProroga,
    ISNULL(ist.SerieProroga,'') AS IstanzaSerieProroga,
    ISNULL(ist.DenomEnte,'') AS IstanzaDenomEnte,
    ISNULL(ist.ImportoRataEnte,0) AS IstanzaImportoRataEnte,

    CAST(CASE WHEN istc.NumIstanza IS NULL THEN 0 ELSE 1 END AS BIT) AS HasUltimaIstanzaChiusaDomicilio,
    ISNULL(istc.NumIstanza,0) AS NumUltimaIstanzaChiusaDomicilio,
    ISNULL(istc.CodTipoIstanza,'') AS CodTipoUltimaIstanzaChiusaDomicilio,
    ISNULL(istc.EsitoIstanza,'') AS EsitoUltimaIstanzaChiusaDomicilio,
    ISNULL(istc.UtentePresaCarico,'') AS UtentePresaCaricoUltimaIstanzaChiusaDomicilio,

    CAST(10 AS INT) AS MinMesiDomicilioFuoriSede

FROM D
LEFT JOIN VV vv
       ON vv.NumDomanda = D.NumDomanda
LEFT JOIN Forzature f
       ON f.CodFiscale = D.CodFiscale
LEFT JOIN StudenteEstrazione s
       ON s.CodFiscale = D.CodFiscale
LEFT JOIN MonetizzazioneMensa mm
       ON mm.CodFiscale = D.CodFiscale
LEFT JOIN DG dg
       ON dg.NumDomanda = D.NumDomanda
LEFT JOIN NF nf
       ON nf.NumDomanda = D.NumDomanda
LEFT JOIN RES r
       ON r.CodFiscale = D.CodFiscale
LEFT JOIN ISCR i
       ON i.NumDomanda = D.NumDomanda
LEFT JOIN PROV_SEDE ps
       ON ps.NumDomanda = D.NumDomanda
LEFT JOIN PA_FLAG pf
       ON pf.NumDomanda = D.NumDomanda
LEFT JOIN DOM1 dom
       ON dom.CodFiscale = D.CodFiscale
LEFT JOIN IST1 ist
       ON ist.CodFiscale = D.CodFiscale
LEFT JOIN IST_CLOSED1 istc
       ON istc.CodFiscale = D.CodFiscale;
";

        private void LoadStatusSedeFromTempCandidates(
            string aa,
            string tempCandidatesTable,
            IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey = null)
        {
            string sql = BuildStatusSedeSqlForTempCandidates(tempCandidatesTable);

            using var cmd = new SqlCommand(sql, _conn)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999,
            };

            cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;

            Logger.LogInfo(null, $"[VerificaRaccoltaDati] StatusSede query (temp candidates) | AA={aa} | Candidates={tempCandidatesTable}");

            Func<StudentKey, bool>? keyFilter = infoByKey == null
                ? null
                : key => infoByKey.ContainsKey(key);

            var dtoMap = ReadDtoMap(
                cmd,
                ReadStatusSedeKey,
                static () => new StatusSedeDto(),
                ReadStatusSedeDto,
                out var readCount,
                keyFilter,
                infoByKey?.Count ?? 0);

            MergeDtoMap(dtoMap, infoByKey ?? _studentsByKey, ApplyStatusSedeDtoToStudent);

            Logger.LogInfo(null, $"[VerificaRaccoltaDati] StatusSede completato. Righe={readCount}, studenti aggiornati={dtoMap.Count}");
        }

        private static StudentKey ReadStatusSedeKey(SqlDataReader reader)
        {
            int numDomanda = reader.SafeGetInt("NumDomanda");
            string codFiscale = NormalizeCf(reader.SafeGetString("CodFiscale"));
            string numDomandaTxt = numDomanda <= 0 ? string.Empty : numDomanda.ToString(CultureInfo.InvariantCulture);
            return new StudentKey(codFiscale, numDomandaTxt);
        }

        private static void ReadStatusSedeDto(SqlDataReader record, StatusSedeDto dto)
        {
            dto.NumDomanda = record.SafeGetInt("NumDomanda");
            dto.CodFiscale = NormalizeCf(record.SafeGetString("CodFiscale"));
            dto.StatusSedeAttuale = record.SafeGetString("StatusSedeAttuale").Trim().ToUpperInvariant();
            dto.ForcedStatus = record.SafeGetString("ForcedStatus").Trim().ToUpperInvariant();
            dto.StudenteSesso = record.SafeGetString("StudenteSesso").Trim().ToUpperInvariant();
            dto.RifugiatoPolitico = record.SafeGetBool("RifugiatoPolitico");
            dto.ConcessaMonetizzazione = record.SafeGetBool("ConcessaMonetizzazione");
            dto.Invalido = record.SafeGetBool("Invalido");
            dto.NumComponenti = record.SafeGetInt("NumComponenti");
            dto.NumConvEstero = record.SafeGetInt("NumConvEstero");
            dto.ComuneResidenza = record.SafeGetString("ComuneResidenza").Trim();
            dto.ProvinciaResidenza = record.SafeGetString("ProvinciaResidenza").Trim().ToUpperInvariant();
            dto.CodSedeStudi = record.SafeGetString("CodSedeStudi").Trim().ToUpperInvariant();
            dto.CodCorso = record.SafeGetString("CodCorso").Trim();
            dto.CodTipoStudi = record.SafeGetInt("CodTipoStudi");
            dto.CodFacolta = record.SafeGetString("CodFacolta").Trim();
            dto.ComuneSedeStudi = record.SafeGetString("ComuneSedeStudi").Trim();
            dto.ProvinciaSede = record.SafeGetString("ProvinciaSede").Trim().ToUpperInvariant();
            dto.Stem = record.SafeGetBool("Stem");
            dto.AlwaysA = record.SafeGetBool("AlwaysA");
            dto.InSedeList = record.SafeGetBool("InSedeList");
            dto.PendolareList = record.SafeGetBool("PendolareList");
            dto.FuoriSedeList = record.SafeGetBool("FuoriSedeList");
            dto.HasAlloggio12 = record.SafeGetBool("HasAlloggio12");
            dto.MinMesiDomicilioFuoriSede = record.SafeGetInt("MinMesiDomicilioFuoriSede");
            dto.ComuneDomicilio = record.SafeGetString("ComuneDomicilio").Trim();
            dto.TitoloOneroso = record.SafeGetBool("TitoloOneroso");
            dto.ContrattoEnte = record.SafeGetBool("ContrattoEnte");
            dto.TipoEnte = record.SafeGetString("TipoEnte").Trim().ToUpperInvariant();
            dto.SerieContratto = record.SafeGetString("SerieContratto").Trim();
            dto.DataRegistrazione = record.SafeGetDateTime("DataRegistrazione");
            dto.DataDecorrenza = record.SafeGetDateTime("DataDecorrenza");
            dto.DataScadenza = record.SafeGetDateTime("DataScadenza");
            dto.DurataContratto = record.SafeGetInt("DurataContratto");
            dto.Prorogato = record.SafeGetBool("Prorogato");
            dto.DurataProroga = record.SafeGetInt("DurataProroga");
            dto.SerieProroga = record.SafeGetString("SerieProroga").Trim();
            dto.DenomEnte = record.SafeGetString("DenomEnte").Trim();
            dto.ImportoRataEnte = record.SafeGetDouble("ImportoRataEnte");
            dto.HasIstanzaDomicilio = record.SafeGetBool("HasIstanzaDomicilio");
            dto.CodTipoIstanzaAperta = record.SafeGetString("CodTipoIstanzaAperta").Trim();
            dto.NumIstanzaAperta = record.SafeGetInt("NumIstanzaAperta");
            dto.HasUltimaIstanzaChiusaDomicilio = record.SafeGetBool("HasUltimaIstanzaChiusaDomicilio");
            dto.CodTipoUltimaIstanzaChiusaDomicilio = record.SafeGetString("CodTipoUltimaIstanzaChiusaDomicilio").Trim();
            dto.NumUltimaIstanzaChiusaDomicilio = record.SafeGetInt("NumUltimaIstanzaChiusaDomicilio");
            dto.EsitoUltimaIstanzaChiusaDomicilio = record.SafeGetString("EsitoUltimaIstanzaChiusaDomicilio").Trim();
            dto.UtentePresaCaricoUltimaIstanzaChiusaDomicilio = record.SafeGetString("UtentePresaCaricoUltimaIstanzaChiusaDomicilio").Trim();

            dto.IstanzaDomicilio = dto.HasIstanzaDomicilio
                ? new DomicilioSnapshot
                {
                    ComuneDomicilio = record.SafeGetString("IstanzaComuneDomicilio").Trim(),
                    TitoloOneroso = record.SafeGetBool("IstanzaTitoloOneroso"),
                    ContrattoEnte = record.SafeGetBool("IstanzaContrattoEnte"),
                    TipoEnte = record.SafeGetString("IstanzaTipoEnte").Trim().ToUpperInvariant(),
                    SerieContratto = record.SafeGetString("IstanzaSerieContratto").Trim(),
                    DataRegistrazione = record.SafeGetDateTime("IstanzaDataRegistrazione"),
                    DataDecorrenza = record.SafeGetDateTime("IstanzaDataDecorrenza"),
                    DataScadenza = record.SafeGetDateTime("IstanzaDataScadenza"),
                    DurataContratto = record.SafeGetInt("IstanzaDurataContratto"),
                    Prorogato = record.SafeGetBool("IstanzaProrogato"),
                    DurataProroga = record.SafeGetInt("IstanzaDurataProroga"),
                    SerieProroga = record.SafeGetString("IstanzaSerieProroga").Trim(),
                    DenomEnte = record.SafeGetString("IstanzaDenomEnte").Trim(),
                    ImportoRataEnte = record.SafeGetDouble("IstanzaImportoRataEnte")
                }
                : null;
        }

        private static void ApplyStatusSedeDtoToStudent(StudenteInfo info, StatusSedeDto dto)
        {
            string numDomandaTxt = dto.NumDomanda <= 0 ? string.Empty : dto.NumDomanda.ToString(CultureInfo.InvariantCulture);

            info.InformazioniPersonali.NumDomanda = numDomandaTxt;
            info.InformazioniPersonali.CodFiscale = dto.CodFiscale;
            info.InformazioniSede.StatusSede = dto.StatusSedeAttuale;
            info.InformazioniSede.ForzaturaStatusSede = dto.ForcedStatus;
            info.InformazioniPersonali.Sesso = dto.StudenteSesso;
            info.InformazioniPersonali.Rifugiato = dto.RifugiatoPolitico;
            info.InformazioniBeneficio.ConcessaMonetizzazioneMensa = dto.ConcessaMonetizzazione;
            info.InformazioniPersonali.Disabile = dto.Invalido;
            info.SetNucleoFamiliare(dto.NumComponenti, dto.NumConvEstero);
            info.SetResidenza(string.Empty, dto.ComuneResidenza, dto.ProvinciaResidenza, string.Empty, dto.ComuneResidenza);

            info.InformazioniIscrizione.CodSedeStudi = dto.CodSedeStudi;
            info.InformazioniIscrizione.CodCorsoLaurea = dto.CodCorso;
            info.InformazioniIscrizione.TipoCorso = dto.CodTipoStudi;
            info.InformazioniIscrizione.CodFacolta = dto.CodFacolta;
            info.InformazioniIscrizione.ComuneSedeStudi = dto.ComuneSedeStudi;
            info.InformazioniIscrizione.ProvinciaSedeStudi = dto.ProvinciaSede;
            info.InformazioniIscrizione.CorsoStem = dto.Stem;

            info.InformazioniSede.AlwaysA = dto.AlwaysA;
            info.InformazioniSede.InSedeList = dto.InSedeList;
            info.InformazioniSede.PendolareList = dto.PendolareList;
            info.InformazioniSede.FuoriSedeList = dto.FuoriSedeList;
            info.InformazioniSede.HasAlloggio12 = dto.HasAlloggio12;
            info.InformazioniSede.MinMesiDomicilioFuoriSede = dto.MinMesiDomicilioFuoriSede;

            info.InformazioniSede.Domicilio.codComuneDomicilio = dto.ComuneDomicilio;
            info.InformazioniSede.Domicilio.titoloOneroso = dto.TitoloOneroso;
            info.InformazioniSede.Domicilio.contrEnte = dto.ContrattoEnte;
            info.InformazioniSede.Domicilio.TipoEnte = dto.TipoEnte;
            info.InformazioniSede.Domicilio.codiceSerieLocazione = dto.SerieContratto;
            info.InformazioniSede.Domicilio.dataRegistrazioneLocazione = dto.DataRegistrazione;
            info.InformazioniSede.Domicilio.dataDecorrenzaLocazione = dto.DataDecorrenza;
            info.InformazioniSede.Domicilio.dataScadenzaLocazione = dto.DataScadenza;
            info.InformazioniSede.Domicilio.durataMesiLocazione = dto.DurataContratto;
            info.InformazioniSede.Domicilio.prorogatoLocazione = dto.Prorogato;
            info.InformazioniSede.Domicilio.durataMesiProrogaLocazione = dto.DurataProroga;
            info.InformazioniSede.Domicilio.codiceSerieProrogaLocazione = dto.SerieProroga;
            info.InformazioniSede.ContrattoEnte = dto.ContrattoEnte;
            info.InformazioniSede.Domicilio.denominazioneIstituto = dto.DenomEnte;
            info.InformazioniSede.Domicilio.importoMensileRataIstituto = dto.ImportoRataEnte;

            info.InformazioniSede.HasIstanzaDomicilio = dto.HasIstanzaDomicilio;
            info.InformazioniSede.CodTipoIstanzaDomicilio = dto.CodTipoIstanzaAperta;
            info.InformazioniSede.NumIstanzaDomicilio = dto.NumIstanzaAperta;
            info.InformazioniSede.HasUltimaIstanzaChiusaDomicilio = dto.HasUltimaIstanzaChiusaDomicilio;
            info.InformazioniSede.CodTipoUltimaIstanzaChiusaDomicilio = dto.CodTipoUltimaIstanzaChiusaDomicilio;
            info.InformazioniSede.NumUltimaIstanzaChiusaDomicilio = dto.NumUltimaIstanzaChiusaDomicilio;
            info.InformazioniSede.EsitoUltimaIstanzaChiusaDomicilio = dto.EsitoUltimaIstanzaChiusaDomicilio;
            info.InformazioniSede.UtentePresaCaricoUltimaIstanzaChiusaDomicilio = dto.UtentePresaCaricoUltimaIstanzaChiusaDomicilio;
            info.InformazioniSede.IstanzaDomicilio = dto.IstanzaDomicilio;
        }

        private sealed class StatusSedeDto
        {
            public int NumDomanda { get; set; }
            public string CodFiscale { get; set; } = string.Empty;
            public string StatusSedeAttuale { get; set; } = string.Empty;
            public string ForcedStatus { get; set; } = string.Empty;
            public string StudenteSesso { get; set; } = string.Empty;
            public bool RifugiatoPolitico { get; set; }
            public bool ConcessaMonetizzazione { get; set; }
            public bool Invalido { get; set; }
            public int NumComponenti { get; set; }
            public int NumConvEstero { get; set; }
            public string ComuneResidenza { get; set; } = string.Empty;
            public string ProvinciaResidenza { get; set; } = string.Empty;
            public string CodSedeStudi { get; set; } = string.Empty;
            public string CodCorso { get; set; } = string.Empty;
            public int CodTipoStudi { get; set; }
            public string CodFacolta { get; set; } = string.Empty;
            public string ComuneSedeStudi { get; set; } = string.Empty;
            public string ProvinciaSede { get; set; } = string.Empty;
            public bool Stem { get; set; }
            public bool AlwaysA { get; set; }
            public bool InSedeList { get; set; }
            public bool PendolareList { get; set; }
            public bool FuoriSedeList { get; set; }
            public bool HasAlloggio12 { get; set; }
            public int MinMesiDomicilioFuoriSede { get; set; }
            public string ComuneDomicilio { get; set; } = string.Empty;
            public bool TitoloOneroso { get; set; }
            public bool ContrattoEnte { get; set; }
            public string TipoEnte { get; set; } = string.Empty;
            public string SerieContratto { get; set; } = string.Empty;
            public DateTime DataRegistrazione { get; set; }
            public DateTime DataDecorrenza { get; set; }
            public DateTime DataScadenza { get; set; }
            public int DurataContratto { get; set; }
            public bool Prorogato { get; set; }
            public int DurataProroga { get; set; }
            public string SerieProroga { get; set; } = string.Empty;
            public string DenomEnte { get; set; } = string.Empty;
            public double ImportoRataEnte { get; set; }
            public bool HasIstanzaDomicilio { get; set; }
            public string CodTipoIstanzaAperta { get; set; } = string.Empty;
            public int NumIstanzaAperta { get; set; }
            public bool HasUltimaIstanzaChiusaDomicilio { get; set; }
            public string CodTipoUltimaIstanzaChiusaDomicilio { get; set; } = string.Empty;
            public int NumUltimaIstanzaChiusaDomicilio { get; set; }
            public string EsitoUltimaIstanzaChiusaDomicilio { get; set; } = string.Empty;
            public string UtentePresaCaricoUltimaIstanzaChiusaDomicilio { get; set; } = string.Empty;
            public DomicilioSnapshot? IstanzaDomicilio { get; set; }
        }

        private HashSet<(string ComuneA, string ComuneB)> LoadComuniEquiparatiFromDb()
        {
            const string sql = @"
SELECT
    UPPER(LTRIM(RTRIM(Cod_Comune_A))) AS Cod_Comune_A,
    UPPER(LTRIM(RTRIM(Cod_Comune_B))) AS Cod_Comune_B
FROM dbo.STATUS_SEDE_COMUNI_EQUIVALENTI
WHERE Data_Fine_Validita IS NULL;";

            var result = new HashSet<(string ComuneA, string ComuneB)>();

            using var cmd = new SqlCommand(sql, _conn)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999,
            };

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string comuneA = reader.SafeGetString("Cod_Comune_A").Trim().ToUpperInvariant();
                string comuneB = reader.SafeGetString("Cod_Comune_B").Trim().ToUpperInvariant();

                if (comuneA.Length == 0 || comuneB.Length == 0)
                    continue;

                result.Add(NormalizeComunePair(comuneA, comuneB));
            }

            Logger.LogInfo(null, $"[VerificaRaccoltaDati] Comuni equiparati caricati: {result.Count}");
            return result;
        }

        private static string BuildStatusSedeSqlForTempCandidates(string tempCandidatesTable)
        {
            var t = (tempCandidatesTable ?? "").Trim();

            if (!IsValidTempTableName(t))
                throw new ArgumentException($"Nome temp table non valido: '{tempCandidatesTable}'", nameof(tempCandidatesTable));

            return StatusSedeGetInputsFromTempCandidatesSqlTemplate.Replace("{{CANDIDATES}}", t);
        }

        private static bool IsValidTempTableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            name = name.Trim();
            if (!name.StartsWith("#", StringComparison.Ordinal)) return false;

            int start = name.StartsWith("##", StringComparison.Ordinal) ? 2 : 1;
            if (name.Length <= start) return false;

            for (int i = start; i < name.Length; i++)
            {
                char ch = name[i];
                bool ok = char.IsLetterOrDigit(ch) || ch == '_';
                if (!ok) return false;
            }

            return true;
        }
    }
}
