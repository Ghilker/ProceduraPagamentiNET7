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
    internal sealed class ControlloStatusSede
    {
        private readonly SqlConnection _conn;

        public ControlloStatusSede(SqlConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        public DataTable OutputStatusSede { get; private set; } = BuildOutputTable();

        public IReadOnlyList<ValutazioneStatusSede> OutputStatusSedeList { get; private set; } = Array.Empty<ValutazioneStatusSede>();

        // =========================
        // OUTPUT
        // =========================
        public DataTable Compute(string aa) => Compute(aa, includeEsclusi: false, includeNonTrasmesse: false);

        public DataTable Compute(string aa, bool includeEsclusi, bool includeNonTrasmesse)
        {
            var list = ComputeList(aa, includeEsclusi, includeNonTrasmesse, iseeByKey: null);
            OutputStatusSedeList = list;
            OutputStatusSede = ToDataTable(list);
            return OutputStatusSede;
        }

        /// <summary>
        /// Variante "object-first": ritorna la lista di studenti con valutazione, da usare in Verifica e in altre strutture.
        /// </summary>
        public List<ValutazioneStatusSede> ComputeList(
            string aa,
            bool includeEsclusi,
            bool includeNonTrasmesse,
            IReadOnlyDictionary<StudentKey, ValutazioneEconomici>? iseeByKey)
        {
            ValidateSelectedAA(aa);

            var repo = new SqlStatusSedeRepository(_conn);
            var inputs = repo.LoadInputs(aa, includeEsclusi, includeNonTrasmesse, iseeByKey);

            var evaluator = new StatusSedeEvaluator();
            var (aaStart, aaEnd) = GetAaDateRange(aa);

            var results = new List<ValutazioneStatusSede>(inputs.Count);

            foreach (var inputRow in inputs)
            {
                var decision = evaluator.Evaluate(inputRow, aaStart, aaEnd);

                // Scrivo anche su StudenteInfo, così le informazioni restano nello stesso posto.
                inputRow.Info.InformazioniSede.StatusSedeSuggerito = decision.SuggestedStatus;

                results.Add(CreateResult(inputRow, decision));
            }

            OutputStatusSedeList = results;
            return results;
        }

        /// <summary>
        /// Variante Verifica-driven: gli studenti da lavorare sono gestiti da Verifica e passati tramite una temp table.
        /// La temp table deve esistere sulla stessa SqlConnection.
        /// Colonne richieste: NumDomanda (int), CodFiscale (nvarchar), TipoBando (nvarchar),
        /// CodTipoEsitoBS (int), StatusCompilazione (int).
        /// </summary>
        public List<ValutazioneStatusSede> ComputeListFromTempCandidates(
            string aa,
            string tempCandidatesTable,
            IReadOnlyDictionary<StudentKey, ValutazioneEconomici>? iseeByKey)
        {
            ValidateSelectedAA(aa);

            var repo = new SqlStatusSedeRepository(_conn);
            var inputs = repo.LoadInputsFromTempCandidates(aa, tempCandidatesTable, iseeByKey);

            var evaluator = new StatusSedeEvaluator();
            var (aaStart, aaEnd) = GetAaDateRange(aa);

            var results = new List<ValutazioneStatusSede>(inputs.Count);

            foreach (var inputRow in inputs)
            {
                var decision = evaluator.Evaluate(inputRow, aaStart, aaEnd);
                inputRow.Info.InformazioniSede.StatusSedeSuggerito = decision.SuggestedStatus;
                results.Add(CreateResult(inputRow, decision));
            }

            OutputStatusSedeList = results;
            return results;
        }


        private static ValutazioneStatusSede CreateResult(StatusSedeStudent row, StatusSedeDecision decision)
        {
            return new ValutazioneStatusSede
            {
                Info = row.Info,
                StatoSuggerito = decision.SuggestedStatus,
                Motivo = decision.Reason,
                DomicilioPresente = decision.DomicilioPresente,
                DomicilioValido = decision.DomicilioValido,
                HasAlloggio12 = row.HasAlloggio12,

                HasIstanzaDomicilio = row.HasIstanzaDomicilio,
                CodTipoIstanzaDomicilio = row.CodTipoIstanzaDomicilio,
                NumIstanzaDomicilio = row.NumIstanzaDomicilio,

                HasUltimaIstanzaChiusaDomicilio = row.HasUltimaIstanzaChiusaDomicilio,
                CodTipoUltimaIstanzaChiusaDomicilio = row.CodTipoUltimaIstanzaChiusaDomicilio,
                NumUltimaIstanzaChiusaDomicilio = row.NumUltimaIstanzaChiusaDomicilio,
                EsitoUltimaIstanzaChiusaDomicilio = row.EsitoUltimaIstanzaChiusaDomicilio,
                UtentePresaCaricoUltimaIstanzaChiusaDomicilio = row.UtentePresaCaricoUltimaIstanzaChiusaDomicilio
            };
        }

        private static DataTable ToDataTable(IReadOnlyList<ValutazioneStatusSede> items)
        {
            var dt = BuildOutputTable();

            foreach (var v in items)
            {
                var info = v.Info;
                var dom = info.InformazioniSede.Domicilio;

                var r = dt.NewRow();
                r["CodFiscale"] = info.InformazioniPersonali.CodFiscale;
                r["NumDomanda"] = info.InformazioniPersonali.NumDomanda;

                r["StatusSedeAttuale"] = info.InformazioniSede.StatusSede;
                r["StatusSedeSuggerito"] = v.StatoSuggerito;
                r["Motivo"] = v.Motivo;

                r["ComuneResidenza"] = info.InformazioniSede.Residenza.codComune;
                r["ProvinciaResidenza"] = info.InformazioniSede.Residenza.provincia;

                r["ComuneSedeStudi"] = info.InformazioniIscrizione.ComuneSedeStudi;
                r["ProvinciaSede"] = info.InformazioniIscrizione.ProvinciaSedeStudi;

                r["ComuneDomicilio"] = dom?.codComuneDomicilio ?? "";

                r["SerieContrattoDomicilio"] = dom?.codiceSerieLocazione ?? "";
                r["DataRegistrazioneDomicilio"] = FormatDateForExport(dom?.dataRegistrazioneLocazione);
                r["DataDecorrenzaDomicilio"] = FormatDateForExport(dom?.dataDecorrenzaLocazione);
                r["DataScadenzaDomicilio"] = FormatDateForExport(dom?.dataScadenzaLocazione);
                r["ProrogatoDomicilio"] = dom?.prorogatoLocazione ?? false;
                r["SerieProrogaDomicilio"] = dom?.codiceSerieProrogaLocazione ?? "";

                r["DomicilioPresente"] = v.DomicilioPresente;
                r["DomicilioValido"] = v.DomicilioValido;
                r["HasAlloggio12"] = v.HasAlloggio12;

                r["HasIstanzaDomicilio"] = v.HasIstanzaDomicilio;
                r["CodTipoIstanzaDomicilio"] = v.CodTipoIstanzaDomicilio ?? "";
                r["NumIstanzaDomicilio"] = v.NumIstanzaDomicilio > 0
                    ? v.NumIstanzaDomicilio.ToString(CultureInfo.InvariantCulture)
                    : "";

                r["HasUltimaIstanzaChiusaDomicilio"] = v.HasUltimaIstanzaChiusaDomicilio;
                r["CodTipoUltimaIstanzaChiusaDomicilio"] = v.CodTipoUltimaIstanzaChiusaDomicilio ?? "";
                r["NumUltimaIstanzaChiusaDomicilio"] = v.NumUltimaIstanzaChiusaDomicilio > 0
                    ? v.NumUltimaIstanzaChiusaDomicilio.ToString(CultureInfo.InvariantCulture)
                    : "";
                r["EsitoUltimaIstanzaChiusaDomicilio"] = v.EsitoUltimaIstanzaChiusaDomicilio ?? "";
                r["UtentePresaCaricoUltimaIstanzaChiusaDomicilio"] = v.UtentePresaCaricoUltimaIstanzaChiusaDomicilio ?? "";

                dt.Rows.Add(r);
            }

            return dt;
        }
        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("CodFiscale");
            dt.Columns.Add("NumDomanda", typeof(string));
            dt.Columns.Add("StatusSedeAttuale");
            dt.Columns.Add("StatusSedeSuggerito");
            dt.Columns.Add("Motivo");
            dt.Columns.Add("ComuneResidenza");
            dt.Columns.Add("ProvinciaResidenza");
            dt.Columns.Add("ComuneSedeStudi");
            dt.Columns.Add("ProvinciaSede");
            dt.Columns.Add("ComuneDomicilio");

            dt.Columns.Add("SerieContrattoDomicilio", typeof(string));
            dt.Columns.Add("DataRegistrazioneDomicilio", typeof(string));
            dt.Columns.Add("DataDecorrenzaDomicilio", typeof(string));
            dt.Columns.Add("DataScadenzaDomicilio", typeof(string));
            dt.Columns.Add("ProrogatoDomicilio", typeof(bool));
            dt.Columns.Add("SerieProrogaDomicilio", typeof(string));

            dt.Columns.Add("DomicilioPresente", typeof(bool));
            dt.Columns.Add("DomicilioValido", typeof(bool));
            dt.Columns.Add("HasAlloggio12", typeof(bool));

            dt.Columns.Add("HasIstanzaDomicilio", typeof(bool));
            dt.Columns.Add("CodTipoIstanzaDomicilio", typeof(string));
            dt.Columns.Add("NumIstanzaDomicilio", typeof(string));

            dt.Columns.Add("HasUltimaIstanzaChiusaDomicilio", typeof(bool));
            dt.Columns.Add("CodTipoUltimaIstanzaChiusaDomicilio", typeof(string));
            dt.Columns.Add("NumUltimaIstanzaChiusaDomicilio", typeof(string));
            dt.Columns.Add("EsitoUltimaIstanzaChiusaDomicilio", typeof(string));
            dt.Columns.Add("UtentePresaCaricoUltimaIstanzaChiusaDomicilio", typeof(string));

            return dt;
        }

        private static void AppendOutput(DataTable dt, StatusSedeStudent row, StatusSedeDecision d)
        {
            var info = row.Info;
            var dom = info.InformazioniSede.Domicilio;

            string cf = (info.InformazioniPersonali.CodFiscale ?? "").Trim().ToUpperInvariant();
            string numDomanda = (info.InformazioniPersonali.NumDomanda ?? "").Trim();

            string comuneRes = GetComuneResidenza(info);
            string provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();

            string comuneSede = (info.InformazioniIscrizione.ComuneSedeStudi ?? "").Trim();
            string provSede = (info.InformazioniIscrizione.ProvinciaSedeStudi ?? "").Trim().ToUpperInvariant();

            string comuneDom = (dom?.codComuneDomicilio ?? "").Trim();

            dt.Rows.Add(
                cf,
                numDomanda,
                (info.InformazioniSede.StatusSede ?? "").Trim().ToUpperInvariant(),
                d.SuggestedStatus,
                d.Reason,
                comuneRes,
                provRes,
                comuneSede,
                provSede,
                comuneDom,
                dom?.codiceSerieLocazione ?? "",
                FormatDateForExport(dom?.dataRegistrazioneLocazione),
                FormatDateForExport(dom?.dataDecorrenzaLocazione),
                FormatDateForExport(dom?.dataScadenzaLocazione),
                dom?.prorogatoLocazione ?? false,
                dom?.codiceSerieProrogaLocazione ?? "",
                d.DomicilioPresente,
                d.DomicilioValido,
                row.HasAlloggio12,
                row.HasIstanzaDomicilio,
                row.CodTipoIstanzaDomicilio ?? "",
                row.NumIstanzaDomicilio > 0 ? row.NumIstanzaDomicilio.ToString(CultureInfo.InvariantCulture) : "",
                row.HasUltimaIstanzaChiusaDomicilio,
                row.CodTipoUltimaIstanzaChiusaDomicilio ?? "",
                row.NumUltimaIstanzaChiusaDomicilio > 0 ? row.NumUltimaIstanzaChiusaDomicilio.ToString(CultureInfo.InvariantCulture) : "",
                row.EsitoUltimaIstanzaChiusaDomicilio ?? "",
                row.UtentePresaCaricoUltimaIstanzaChiusaDomicilio ?? ""
            );
        }
        private static string GetComuneResidenza(StudenteInfo info)
        {
            // I dati possono essere codice o nome comune: mantieni in uscita quello valorizzato.
            var c1 = (info.InformazioniSede.Residenza.codComune ?? "").Trim();
            if (c1.Length > 0) return c1;

            var c2 = (info.InformazioniSede.Residenza.nomeComune ?? "").Trim();
            if (c2.Length > 0) return c2;

            return "";
        }

        // =========================
        // REPOSITORY
        // =========================

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

DG AS
(
    SELECT
        CAST(v.Num_domanda AS INT) AS NumDomanda,
        CAST(CASE WHEN ISNULL(v.Rifug_politico,0) = 1 THEN 1 ELSE 0 END AS BIT) AS RifugiatoPolitico
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

    ISNULL(i.AlwaysA, CAST(0 AS BIT)) AS AlwaysA,

    ISNULL(dg.RifugiatoPolitico, CAST(0 AS BIT)) AS RifugiatoPolitico,
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

DG AS
(
    SELECT
        CAST(v.Num_domanda AS INT) AS NumDomanda,
        CAST(CASE WHEN ISNULL(v.Rifug_politico,0) = 1 THEN 1 ELSE 0 END AS BIT) AS RifugiatoPolitico
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

    ISNULL(i.AlwaysA, CAST(0 AS BIT)) AS AlwaysA,

    ISNULL(dg.RifugiatoPolitico, CAST(0 AS BIT)) AS RifugiatoPolitico,
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
                IReadOnlyDictionary<StudentKey, ValutazioneEconomici>? iseeByKey = null)
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
                    result.Add(StatusSedeStudent.FromRecord(reader, iseeByKey));
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
                IReadOnlyDictionary<StudentKey, ValutazioneEconomici>? iseeByKey = null)
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
                    result.Add(StatusSedeStudent.FromRecord(reader, iseeByKey));
                    readCount++;

                    if (readCount % 5000 == 0)
                        Logger.LogInfo(null, $"[Repo] Lettura righe... {readCount}");
                }

                Logger.LogInfo(null, $"[Repo] Lettura completata. Righe={readCount}");
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

        private sealed class StatusSedeStudent
        {
            public StudenteInfo Info { get; init; } = new StudenteInfo();
            public ValutazioneEconomici? IseeEconomici { get; init; }
            public bool AlwaysA { get; init; }

            public bool InSedeList { get; init; }
            public bool PendolareList { get; init; }
            public bool FuoriSedeList { get; init; }

            public bool HasAlloggio12 { get; init; }

            public int MinMesiDomicilioFuoriSede { get; init; }

            public bool HasIstanzaDomicilio { get; init; }
            public string CodTipoIstanzaDomicilio { get; init; } = "";
            public int NumIstanzaDomicilio { get; init; }
            public DomicilioSnapshot? IstanzaDomicilio { get; init; }

            public bool HasUltimaIstanzaChiusaDomicilio { get; init; }
            public string CodTipoUltimaIstanzaChiusaDomicilio { get; init; } = "";
            public int NumUltimaIstanzaChiusaDomicilio { get; init; }
            public string EsitoUltimaIstanzaChiusaDomicilio { get; init; } = "";
            public string UtentePresaCaricoUltimaIstanzaChiusaDomicilio { get; init; } = "";

            public static StatusSedeStudent FromRecord(IDataRecord record, IReadOnlyDictionary<StudentKey, ValutazioneEconomici>? iseeByKey)
            {
                int numDomanda = record.SafeGetInt("NumDomanda");
                string codFiscale = record.SafeGetString("CodFiscale").Trim().ToUpperInvariant();
                string numDomandaTxt = numDomanda <= 0
                    ? string.Empty
                    : numDomanda.ToString(CultureInfo.InvariantCulture);

                ValutazioneEconomici? iseeEconomici = null;
                StudenteInfo studenteInfo;

                var key = new StudentKey(codFiscale, numDomandaTxt);
                if (iseeByKey != null && iseeByKey.TryGetValue(key, out var foundEconomici))
                {
                    iseeEconomici = foundEconomici;
                    studenteInfo = foundEconomici.Info;
                }
                else
                {
                    studenteInfo = new StudenteInfo();
                }

                studenteInfo.InformazioniPersonali.NumDomanda = numDomandaTxt;
                studenteInfo.InformazioniPersonali.CodFiscale = codFiscale;
                studenteInfo.InformazioniSede.StatusSede = record.SafeGetString("StatusSedeAttuale").Trim().ToUpperInvariant();
                studenteInfo.InformazioniSede.ForzaturaStatusSede = record.SafeGetString("ForcedStatus").Trim().ToUpperInvariant();

                bool alwaysA = record.SafeGetBool("AlwaysA");

                bool rifugiatoPolitico = record.SafeGetBool("RifugiatoPolitico");
                studenteInfo.InformazioniPersonali.Rifugiato = rifugiatoPolitico;

                int numeroComponentiNucleo = record.SafeGetInt("NumComponenti");
                int numeroComponentiNucleoEstero = record.SafeGetInt("NumConvEstero");
                studenteInfo.SetNucleoFamiliare(numeroComponentiNucleo, numeroComponentiNucleoEstero);

                string comuneResidenza = record.SafeGetString("ComuneResidenza").Trim();
                string provinciaResidenza = record.SafeGetString("ProvinciaResidenza").Trim().ToUpperInvariant();
                studenteInfo.SetResidenza(
                    indirizzo: string.Empty,
                    codComune: comuneResidenza,
                    provincia: provinciaResidenza,
                    CAP: string.Empty,
                    nomeComune: comuneResidenza
                );

                studenteInfo.InformazioniIscrizione.CodSedeStudi = record.SafeGetString("CodSedeStudi").Trim().ToUpperInvariant();
                studenteInfo.InformazioniIscrizione.CodCorsoLaurea = record.SafeGetString("CodCorso").Trim();
                studenteInfo.InformazioniIscrizione.TipoCorso = record.SafeGetInt("CodTipoStudi");
                studenteInfo.InformazioniIscrizione.CodFacolta = record.SafeGetString("CodFacolta").Trim();
                studenteInfo.InformazioniIscrizione.ComuneSedeStudi = record.SafeGetString("ComuneSedeStudi").Trim();

                string provinciaSede = record.SafeGetString("ProvinciaSede").Trim().ToUpperInvariant();
                studenteInfo.InformazioniIscrizione.ProvinciaSedeStudi = provinciaSede;

                bool inSedeList = record.SafeGetBool("InSedeList");
                bool pendolareList = record.SafeGetBool("PendolareList");
                bool fuoriSedeList = record.SafeGetBool("FuoriSedeList");

                bool hasAlloggio12 = record.SafeGetBool("HasAlloggio12");

                string comuneDomicilio = record.SafeGetString("ComuneDomicilio").Trim();
                bool titoloOneroso = record.SafeGetBool("TitoloOneroso");
                bool contrattoEnte = record.SafeGetBool("ContrattoEnte");
                string serieContratto = record.SafeGetString("SerieContratto").Trim();

                DateTime dataRegistrazione = record.SafeGetDateTime("DataRegistrazione");
                DateTime dataDecorrenza = record.SafeGetDateTime("DataDecorrenza");
                DateTime dataScadenza = record.SafeGetDateTime("DataScadenza");

                int durataContratto = record.SafeGetInt("DurataContratto");
                bool prorogato = record.SafeGetBool("Prorogato");
                int durataProroga = record.SafeGetInt("DurataProroga");
                string serieProroga = record.SafeGetString("SerieProroga").Trim();

                string denomEnte = record.SafeGetString("DenomEnte").Trim();
                double importoRataEnte = record.SafeGetDouble("ImportoRataEnte");

                int minMesiDomicilioFuoriSede = record.SafeGetInt("MinMesiDomicilioFuoriSede");

                studenteInfo.InformazioniSede.Domicilio.codComuneDomicilio = comuneDomicilio;
                studenteInfo.InformazioniSede.Domicilio.titoloOneroso = titoloOneroso;
                studenteInfo.InformazioniSede.Domicilio.codiceSerieLocazione = serieContratto;
                studenteInfo.InformazioniSede.Domicilio.dataRegistrazioneLocazione = dataRegistrazione;
                studenteInfo.InformazioniSede.Domicilio.dataDecorrenzaLocazione = dataDecorrenza;
                studenteInfo.InformazioniSede.Domicilio.dataScadenzaLocazione = dataScadenza;
                studenteInfo.InformazioniSede.Domicilio.durataMesiLocazione = durataContratto;
                studenteInfo.InformazioniSede.Domicilio.prorogatoLocazione = prorogato;
                studenteInfo.InformazioniSede.Domicilio.durataMesiProrogaLocazione = durataProroga;
                studenteInfo.InformazioniSede.Domicilio.codiceSerieProrogaLocazione = serieProroga;

                studenteInfo.InformazioniSede.ContrattoEnte = contrattoEnte;
                studenteInfo.InformazioniSede.Domicilio.contrEnte = contrattoEnte;
                studenteInfo.InformazioniSede.Domicilio.denominazioneIstituto = denomEnte;
                studenteInfo.InformazioniSede.Domicilio.importoMensileRataIstituto = importoRataEnte;

                bool hasIstanzaDomicilio = record.SafeGetBool("HasIstanzaDomicilio");
                int numIstanzaDomicilio = record.SafeGetInt("NumIstanzaAperta");
                string codTipoIstanzaDomicilio = record.SafeGetString("CodTipoIstanzaAperta").Trim();

                DomicilioSnapshot? istanzaDomicilio = null;
                if (hasIstanzaDomicilio)
                {
                    istanzaDomicilio = new DomicilioSnapshot
                    {
                        ComuneDomicilio = record.SafeGetString("IstanzaComuneDomicilio").Trim(),
                        TitoloOneroso = record.SafeGetBool("IstanzaTitoloOneroso"),
                        ContrattoEnte = record.SafeGetBool("IstanzaContrattoEnte"),
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
                    };
                }

                bool hasUltimaIstanzaChiusaDomicilio = record.SafeGetBool("HasUltimaIstanzaChiusaDomicilio");
                int numUltimaIstanzaChiusaDomicilio = record.SafeGetInt("NumUltimaIstanzaChiusaDomicilio");
                string codTipoUltimaIstanzaChiusaDomicilio = record.SafeGetString("CodTipoUltimaIstanzaChiusaDomicilio").Trim();
                string esitoUltimaIstanzaChiusaDomicilio = record.SafeGetString("EsitoUltimaIstanzaChiusaDomicilio").Trim();
                string utentePresaCaricoUltimaIstanzaChiusaDomicilio = record.SafeGetString("UtentePresaCaricoUltimaIstanzaChiusaDomicilio").Trim();

                return new StatusSedeStudent
                {
                    Info = studenteInfo,
                    IseeEconomici = iseeEconomici,
                    AlwaysA = alwaysA,
                    InSedeList = inSedeList,
                    PendolareList = pendolareList,
                    FuoriSedeList = fuoriSedeList,
                    HasAlloggio12 = hasAlloggio12,
                    MinMesiDomicilioFuoriSede = minMesiDomicilioFuoriSede,

                    HasIstanzaDomicilio = hasIstanzaDomicilio,
                    CodTipoIstanzaDomicilio = codTipoIstanzaDomicilio,
                    NumIstanzaDomicilio = numIstanzaDomicilio,
                    IstanzaDomicilio = istanzaDomicilio,

                    HasUltimaIstanzaChiusaDomicilio = hasUltimaIstanzaChiusaDomicilio,
                    CodTipoUltimaIstanzaChiusaDomicilio = codTipoUltimaIstanzaChiusaDomicilio,
                    NumUltimaIstanzaChiusaDomicilio = numUltimaIstanzaChiusaDomicilio,
                    EsitoUltimaIstanzaChiusaDomicilio = esitoUltimaIstanzaChiusaDomicilio,
                    UtentePresaCaricoUltimaIstanzaChiusaDomicilio = utentePresaCaricoUltimaIstanzaChiusaDomicilio
                };
            }
        }
        private sealed class DomicilioSnapshot
        {
            public string ComuneDomicilio { get; init; } = "";
            public bool TitoloOneroso { get; init; }
            public bool ContrattoEnte { get; init; }
            public string SerieContratto { get; init; } = "";
            public DateTime DataRegistrazione { get; init; }
            public DateTime DataDecorrenza { get; init; }
            public DateTime DataScadenza { get; init; }
            public int DurataContratto { get; init; }
            public bool Prorogato { get; init; }
            public int DurataProroga { get; init; }
            public string SerieProroga { get; init; } = "";
            public string DenomEnte { get; init; } = "";
            public double ImportoRataEnte { get; init; }
        }
        // =========================
        // EVALUATOR (usa StudenteInfo)
        // =========================
        private sealed class StatusSedeEvaluator
        {
            public StatusSedeDecision Evaluate(StatusSedeStudent row, DateTime aaStart, DateTime aaEnd)
            {
                var info = row.Info;

                var forced = (info.InformazioniSede.ForzaturaStatusSede ?? "").Trim().ToUpperInvariant();
                if (IsValidStatus(forced))
                    return StatusSedeDecision.Fixed(forced, "Forzatura manuale (primaria)");

                if (row.AlwaysA)
                    return StatusSedeDecision.Fixed("A", "Sempre A (telematico / non in presenza) [DB]");

                if (info.InformazioniPersonali.Rifugiato)
                    return StatusSedeDecision.Fixed("B", "Rifugiato politico");

                if (IsNucleoEsteroOver50(info))
                    return StatusSedeDecision.Fixed("B", "Nucleo familiare con >50% componenti all'estero");

                var eco = row.IseeEconomici;
                if (eco != null
                    && string.Equals((eco.TipoRedditoOrigine ?? "").Trim(), "EE", StringComparison.OrdinalIgnoreCase)
                    && IsSeqOne(eco.SEQ)
                    && eco.ISR >= 9000m)
                {
                    return StatusSedeDecision.Fixed(
                        "B",
                        "Economici: TipoReddito=EE, SEQ=1, ISR>=9000 => fuori sede (B)"
                    );
                }

                var comuneRes = GetComuneResidenza(info);
                var comuneSede = (info.InformazioniIscrizione.ComuneSedeStudi ?? "").Trim();

                if (Eq(comuneRes, comuneSede))
                    return StatusSedeDecision.Fixed("A", "Comune residenza = Comune sede studi");

                if (row.HasAlloggio12)
                    return StatusSedeDecision.Fixed("B", "PA: idoneo/vincitore (1/2) => fuori sede");

                bool pendolareDefaultSameProvNoLists = false;

                if (row.InSedeList)
                    return StatusSedeDecision.Fixed("A", "COMUNI_INSEDE (stessa provincia)");

                if (row.PendolareList && !row.FuoriSedeList)
                    return StatusSedeDecision.Fixed("C", "COMUNI_PENDOLARI (stessa provincia, non in COMUNI_FUORISEDE)");

                var provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();
                var provSede = (info.InformazioniIscrizione.ProvinciaSedeStudi ?? "").Trim().ToUpperInvariant();

                if (Eq(provRes, provSede))
                {
                    if (!row.InSedeList && !row.PendolareList && !row.FuoriSedeList)
                        pendolareDefaultSameProvNoLists = true;
                }

                if (pendolareDefaultSameProvNoLists)
                    return StatusSedeDecision.Fixed("C", "Stessa provincia ma assente da COMUNI_INSEDE/COMUNI_PENDOLARI/COMUNI_FUORISEDE => pendolare default");

                var dom = DomicilioValidator.Validate(row, aaStart, aaEnd);
                if (!dom.Presente)
                    return StatusSedeDecision.WithDom("D", "Dati domicilio non presenti => pendolare calcolato (D)", dom);

                if (dom.Valido)
                {
                    if (Eq(dom.ComuneDomicilio, comuneSede))
                        return StatusSedeDecision.WithDom(
                            "B",
                            $"{dom.Source}: domicilio valido e nel comune sede studi => fuori sede (B) | {dom.Reason}",
                            dom);

                    return StatusSedeDecision.WithDom(
                        "D",
                        $"{dom.Source}: domicilio valido ma comune domicilio diverso da comune sede studi => pendolare calcolato (D) | {dom.Reason}",
                        dom);
                }

                return StatusSedeDecision.WithDom(
                    "D",
                    $"{dom.Source}: domicilio presente ma non valido => pendolare calcolato (D) | {dom.Reason}",
                    dom);
            }

            private static bool IsNucleoEsteroOver50(StudenteInfo info)
            {
                var provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();
                if (!Eq(provRes, "EE")) return false;

                int comp = info.InformazioniPersonali.NumeroComponentiNucleoFamiliare;
                if (comp <= 0) return false;

                int estero = info.InformazioniPersonali.NumeroComponentiNucleoFamiliareEstero;
                var soglia = (int)Math.Ceiling(comp / 2.0);
                return estero >= soglia;
            }

            private static bool IsValidStatus(string? s) => s is "A" or "B" or "C" or "D";
            private static bool IsSeqOne(decimal seq) => Math.Abs(seq - 1m) < 0.0001m;
            private static bool Eq(string? a, string? b)
                => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // =========================
        // DOMICILIO (usa classi informazione)
        // =========================
        private static class DomicilioValidator
        {
            public static DomResult Validate(StatusSedeStudent row, DateTime aaStart, DateTime aaEnd)
            {
                if(row.Info.InformazioniPersonali.CodFiscale == "PZZGPP05R01B180V")
                {
                    string test = "";
                }

                var corrente = ValidateSnapshot(
                    BuildCurrentSnapshot(row.Info),
                    row.MinMesiDomicilioFuoriSede,
                    aaStart,
                    aaEnd,
                    "DOMICILIO CORRENTE");

                bool hasIstanza = row.HasIstanzaDomicilio && row.IstanzaDomicilio != null;
                if (!hasIstanza)
                    return corrente;

                var istanza = ValidateSnapshot(
                    row.IstanzaDomicilio!,
                    row.MinMesiDomicilioFuoriSede,
                    aaStart,
                    aaEnd,
                    $"ISTANZA APERTA {row.NumIstanzaDomicilio}");

                // Solo istanza presente/utile
                if (!corrente.Presente && istanza.Presente)
                    return istanza;

                // Solo domicilio corrente presente/utile
                if (corrente.Presente && !istanza.Presente)
                    return corrente;

                // Nessun dato utile
                if (!corrente.Presente && !istanza.Presente)
                    return corrente;

                // Da qui in poi: sono presenti sia domicilio corrente che istanza.
                // L'istanza viene valutata come "probabile futuro" del domicilio.

                // Caso migliore: istanza valida -> prevale per la valutazione prospettica,
                // anche se il domicilio corrente non è valido o è meno affidabile.
                if (istanza.Valido)
                {
                    string comuneValutato = !string.IsNullOrWhiteSpace(istanza.ComuneDomicilio)
                        ? istanza.ComuneDomicilio
                        : corrente.ComuneDomicilio;

                    if (corrente.Valido)
                    {
                        return new DomResult(
                            Presente: true,
                            Valido: true,
                            Reason:
                                "Domicilio corrente valido e istanza aperta valida: " +
                                "usata l'istanza come probabile validità futura del domicilio" +
                                $" | Corrente: {corrente.Reason}" +
                                $" | Istanza: {istanza.Reason}",
                            ComuneDomicilio: comuneValutato,
                            Source: "ISTANZA FUTURA + DOMICILIO CORRENTE");
                    }

                    return new DomResult(
                        Presente: true,
                        Valido: true,
                        Reason:
                            "Domicilio corrente non valido, ma istanza aperta valida: " +
                            "usata l'istanza come probabile validità futura del domicilio" +
                            $" | Corrente: {corrente.Reason}" +
                            $" | Istanza: {istanza.Reason}",
                        ComuneDomicilio: comuneValutato,
                        Source: "ISTANZA FUTURA");
                }

                // Istanza presente ma non valida:
                // il corrente resta valido per lo stato attuale, ma il motivo segnala
                // che il probabile futuro del domicilio non è coerente.
                if (corrente.Valido)
                {
                    return new DomResult(
                        Presente: true,
                        Valido: true,
                        Reason:
                            "Domicilio corrente valido, ma istanza aperta non valida: " +
                            "il domicilio attuale resta valido, però l'istanza non conferma la probabile validità futura" +
                            $" | Corrente: {corrente.Reason}" +
                            $" | Istanza: {istanza.Reason}",
                        ComuneDomicilio: corrente.ComuneDomicilio,
                        Source: "DOMICILIO CORRENTE + ISTANZA FUTURA");
                }

                // Entrambi presenti ma non validi
                return new DomResult(
                    Presente: true,
                    Valido: false,
                    Reason:
                        "Domicilio corrente e istanza aperta presenti ma non validi" +
                        $" | Corrente: {corrente.Reason}" +
                        $" | Istanza: {istanza.Reason}",
                    ComuneDomicilio: !string.IsNullOrWhiteSpace(corrente.ComuneDomicilio)
                        ? corrente.ComuneDomicilio
                        : istanza.ComuneDomicilio,
                    Source: "DOMICILIO CORRENTE + ISTANZA");
            }

            private static DomResult ValidateSnapshot(
                DomicilioSnapshot dom,
                int minMesiDb,
                DateTime aaStart,
                DateTime aaEnd,
                string source)
            {
                string comuneDom = (dom.ComuneDomicilio ?? "").Trim();
                bool titoloOneroso = dom.TitoloOneroso;
                bool contrattoEnte = dom.ContrattoEnte;

                bool presente = comuneDom.Length > 0 && (titoloOneroso || contrattoEnte);
                if (!presente)
                    return new DomResult(false, false, "Dati domicilio non presenti", comuneDom, source);

                int min = minMesiDb > 0 ? minMesiDb : 10;

                if (contrattoEnte)
                {
                    if (string.IsNullOrWhiteSpace(dom.DenomEnte))
                        return new DomResult(true, false, "Contratto ente senza denominazione ente", comuneDom, source);

                    if (dom.DurataContratto < min)
                        return new DomResult(true, false, $"Durata contratto ente < minimo richiesto ({dom.DurataContratto} < {min})", comuneDom, source);

                    if (dom.ImportoRataEnte <= 0)
                        return new DomResult(true, false, "Importo rata ente nullo o negativo", comuneDom, source);

                    return new DomResult(true, true, $"Contratto ente valido (durata={dom.DurataContratto}, minimo={min})", comuneDom, source);
                }

                if (!HasValidDate(dom.DataRegistrazione))
                    return new DomResult(true, false, "Data registrazione non valida", comuneDom, source);

                if (!HasValidDate(dom.DataDecorrenza))
                    return new DomResult(true, false, "Data decorrenza non valida", comuneDom, source);

                if (!HasValidDate(dom.DataScadenza))
                    return new DomResult(true, false, "Data scadenza non valida", comuneDom, source);

                if (dom.DataScadenza < dom.DataDecorrenza)
                    return new DomResult(true, false, "Scadenza < decorrenza", comuneDom, source);

                if (dom.DataRegistrazione.Date > dom.DataScadenza.Date)
                    return new DomResult(true, false, "Data registrazione successiva alla scadenza", comuneDom, source);

                var effStart = dom.DataDecorrenza > aaStart ? dom.DataDecorrenza : aaStart;
                var effEnd = dom.DataScadenza < aaEnd ? dom.DataScadenza : aaEnd;

                if (effStart > effEnd)
                    return new DomResult(true, false, "Contratto fuori dall'intervallo AA", comuneDom, source);

                string serieContratto = (dom.SerieContratto ?? "").Trim();
                if (!DomicilioUtils.IsValidSerie(serieContratto))
                    return new DomResult(true, false, "Serie contratto non valida", comuneDom, source);

                string serieProroga = (dom.SerieProroga ?? "").Trim();
                bool hasProroga = dom.Prorogato;

                if (hasProroga)
                {
                    if (dom.DurataProroga <= 0)
                        return new DomResult(true, false, "Durata proroga non valida", comuneDom, source);

                    if (!DomicilioUtils.IsValidSerie(serieProroga))
                        return new DomResult(true, false, "Serie proroga non valida", comuneDom, source);

                    if (serieProroga.IndexOf(serieContratto, StringComparison.OrdinalIgnoreCase) >= 0)
                        return new DomResult(true, false, "Serie proroga uguale o contenente la serie del contratto", comuneDom, source);
                }

                int mesi = CoveredMonths(effStart, effEnd);
                if (mesi < min)
                {
                    var today = DateTime.Today;

                    if (today <= dom.DataScadenza.Date.AddDays(30))
                    {
                        return new DomResult(
                            true,
                            true,
                            $"Valido in finestra proroga 30 giorni (mesi coperti={mesi}, minimo={min}, scadenza={dom.DataScadenza:dd/MM/yyyy})",
                            comuneDom,
                            source);
                    }

                    return new DomResult(true, false, $"Mesi coperti {mesi} < minimo {min}", comuneDom, source);
                }

                return new DomResult(true, true, $"OK (mesi coperti={mesi}, minimo={min})", comuneDom, source);
            }

            private static DomicilioSnapshot BuildCurrentSnapshot(StudenteInfo info)
            {
                var dom = info.InformazioniSede.Domicilio ?? new Domicilio();

                return new DomicilioSnapshot
                {
                    ComuneDomicilio = (dom.codComuneDomicilio ?? "").Trim(),
                    TitoloOneroso = dom.titoloOneroso,
                    ContrattoEnte = dom.contrEnte || info.InformazioniSede.ContrattoEnte,
                    SerieContratto = (dom.codiceSerieLocazione ?? "").Trim(),
                    DataRegistrazione = dom.dataRegistrazioneLocazione,
                    DataDecorrenza = dom.dataDecorrenzaLocazione,
                    DataScadenza = dom.dataScadenzaLocazione,
                    DurataContratto = dom.durataMesiLocazione,
                    Prorogato = dom.prorogatoLocazione,
                    DurataProroga = dom.durataMesiProrogaLocazione,
                    SerieProroga = (dom.codiceSerieProrogaLocazione ?? "").Trim(),
                    DenomEnte = (dom.denominazioneIstituto ?? "").Trim(),
                    ImportoRataEnte = dom.importoMensileRataIstituto
                };
            }

            private static bool HasValidDate(DateTime dt)
            {
                if (dt == DateTime.MinValue) return false;
                if (dt.Year < 1900) return false;
                return true;
            }

            private static int CoveredMonths(DateTime start, DateTime end)
            {
                int count = 0;
                var cur = new DateTime(start.Year, start.Month, 1);

                while (cur <= end)
                {
                    var monthStart = cur;
                    var monthEnd = cur.AddMonths(1).AddDays(-1);

                    var covStart = monthStart < start ? start : monthStart;
                    var covEnd = monthEnd > end ? end : monthEnd;

                    var days = (covEnd - covStart).TotalDays + 1;
                    if (days >= 15)
                        count++;

                    cur = cur.AddMonths(1);
                }

                return count;
            }
        }

        private readonly record struct DomResult(
            bool Presente,
            bool Valido,
            string Reason,
            string ComuneDomicilio,
            string Source);

        private sealed class StatusSedeDecision
        {
            public string SuggestedStatus { get; }
            public string Reason { get; }
            public bool DomicilioPresente { get; }
            public bool DomicilioValido { get; }

            private StatusSedeDecision(string suggested, string reason, bool domPres, bool domVal)
            {
                SuggestedStatus = suggested;
                Reason = reason;
                DomicilioPresente = domPres;
                DomicilioValido = domVal;
            }

            public static StatusSedeDecision Fixed(string suggested, string reason)
                => new StatusSedeDecision(suggested, reason, domPres: false, domVal: false);

            public static StatusSedeDecision WithDom(string suggested, string reason, DomResult dom)
                => new StatusSedeDecision(suggested, reason, dom.Presente, dom.Valido);
        }

        // =========================
        // UTIL
        // =========================
        private static void ValidateSelectedAA(string aa)
        {
            if (aa.Length != 8 || !aa.All(char.IsDigit))
                throw new ArgumentException("Anno accademico non valido. Atteso formato YYYYYYYY.");

            int start = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int end = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            if (end != start + 1)
                throw new ArgumentException("Anno accademico incoerente. Fine ≠ inizio+1.");
        }

        private static string FormatDateForExport(DateTime? dt)
        {
            if (!dt.HasValue) return "";
            if (dt.Value == DateTime.MinValue) return "";
            if (dt.Value.Year < 1900) return "";
            return dt.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private static bool Eq(string? a, string? b)
        {
            return string.Equals(
                (a ?? "").Trim(),
                (b ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static (DateTime aaStart, DateTime aaEnd) GetAaDateRange(string aa)
        {
            int startYear = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int endYear = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            return (new DateTime(startYear, 10, 1), new DateTime(endYear, 9, 30));
        }
    }
}