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
    internal sealed partial class ControlloStatusSede
    {
        private const string StatusSedeGetInputsSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH
DomandeRaw AS
(
    SELECT
        CAST(d.Num_domanda AS INT) AS NumDomanda,
        UPPER(LTRIM(RTRIM(d.Cod_fiscale))) AS CodFiscale,
        COALESCE(d.Tipo_bando,'') AS TipoBando,
        ROW_NUMBER() OVER
        (
            PARTITION BY d.Num_domanda
            ORDER BY d.Data_validita DESC, d.Tipo_bando
        ) AS rn
    FROM Domanda d
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando = 'lz'
),
D0 AS
(
    SELECT NumDomanda, CodFiscale, TipoBando
    FROM DomandeRaw
    WHERE rn = 1
),

-- stato compilazione / trasmissione
SC AS
(
    SELECT
        CAST(v.Num_domanda AS INT) AS NumDomanda,
        CAST(ISNULL(v.status_compilazione,0) AS INT) AS StatusCompilazione
    FROM vstatus_compilazione v
    WHERE v.anno_accademico = @AA
),

-- esiti concorsi BS (ultimo record per domanda)
BS_LAST AS
(
    SELECT
        CAST(ec.Num_domanda AS INT) AS NumDomanda,
        CAST(ec.Cod_tipo_esito AS INT) AS CodTipoEsitoBS,
        ROW_NUMBER() OVER
        (
            PARTITION BY ec.Num_domanda
            ORDER BY ec.Data_validita DESC
        ) AS rn
    FROM ESITI_CONCORSI ec
    JOIN D0 ON D0.NumDomanda = ec.Num_domanda
    WHERE ec.Anno_accademico = @AA
      AND UPPER(ec.Cod_beneficio) = 'BS'
),
BS AS
(
    SELECT NumDomanda, CodTipoEsitoBS
    FROM BS_LAST
    WHERE rn = 1
),

-- Domande filtrate da parametri (single source of truth per inclusioni)
D AS
(
    SELECT
        d0.NumDomanda,
        d0.CodFiscale,
        d0.TipoBando,
        ISNULL(bs.CodTipoEsitoBS,0) AS CodTipoEsitoBS,
        ISNULL(sc.StatusCompilazione,0) AS StatusCompilazione
    FROM D0 d0
    LEFT JOIN BS bs ON bs.NumDomanda = d0.NumDomanda
    LEFT JOIN SC sc ON sc.NumDomanda = d0.NumDomanda
    WHERE
        (@IncludeEsclusi = 1 OR ISNULL(bs.CodTipoEsitoBS,0) <> 0)
        AND
        (@IncludeNonTrasmesse = 1 OR ISNULL(sc.StatusCompilazione,0) >= 90)
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
        -- normalizzo distaccata: NULL/'' => '00000'
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

    -- utili per debug/filtri
    D.CodTipoEsitoBS,
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

    -- IN SEDE: match per cod_sede_studi OR cod_sede_distaccata (NULL/'00000' gestito)
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

    -- FUORI SEDE: match per cod_sede_studi OR cod_sede_distaccata (NULL/'00000' gestito)
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


        private sealed class SqlStatusSedeRepository
        {
            private readonly SqlConnection _conn;
            public SqlStatusSedeRepository(SqlConnection conn) => _conn = conn;

            public System.Collections.Generic.List<StatusSedeStudent> LoadInputs(
                string aa,
                bool includeEsclusi = false,
                bool includeNonTrasmesse = false,
                IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey = null)
            {
                var result = new System.Collections.Generic.List<StatusSedeStudent>();

                using var cmd = new SqlCommand(StatusSedeGetInputsSql, _conn)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 9999999,
                };

                cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;
                cmd.Parameters.Add("@IncludeEsclusi", SqlDbType.Bit).Value = includeEsclusi;
                cmd.Parameters.Add("@IncludeNonTrasmesse", SqlDbType.Bit).Value = includeNonTrasmesse;

                Logger.LogInfo(null, $"[Repo] StatusSede query (inline) | AA={aa} | IncludeEsclusi={includeEsclusi} | IncludeNonTrasmesse={includeNonTrasmesse}");

                using var reader = cmd.ExecuteReader();

                int readCount = 0;
                while (reader.Read())
                {
                    result.Add(StatusSedeStudent.FromRecord(reader, infoByKey));
                    readCount++;

                    if (readCount % 5000 == 0)
                        Logger.LogInfo(null, $"[Repo] Lettura righe... {readCount}");
                }

                Logger.LogInfo(null, $"[Repo] Lettura completata. Righe={readCount}");
                return result;
            }

            public System.Collections.Generic.List<StatusSedeStudent> LoadInputsFromTempCandidates(
                string aa,
                string tempCandidatesTable,
                IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey = null)
            {
                var result = new System.Collections.Generic.List<StatusSedeStudent>();

                string sql = BuildSqlForTempCandidates(tempCandidatesTable);

                using var cmd = new SqlCommand(sql, _conn)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 9999999,
                };

                cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;

                Logger.LogInfo(null, $"[Repo] StatusSede query (inline, temp candidates) | AA={aa} | Candidates={tempCandidatesTable}");

                using var reader = cmd.ExecuteReader();

                int readCount = 0;
                while (reader.Read())
                {
                    result.Add(StatusSedeStudent.FromRecord(reader, infoByKey));
                    readCount++;

                    if (readCount % 5000 == 0)
                        Logger.LogInfo(null, $"[Repo] Lettura righe... {readCount}");
                }

                Logger.LogInfo(null, $"[Repo] Lettura completata. Righe={readCount}");
                return result;
            }

            public HashSet<(string ComuneA, string ComuneB)> LoadComuniEquiparati()
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

                Logger.LogInfo(null, $"[Repo] Comuni equiparati caricati: {result.Count}");
                return result;
            }

            private static string BuildSqlForTempCandidates(string tempCandidatesTable)
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

                // accetta #Table o ##Table
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
}
