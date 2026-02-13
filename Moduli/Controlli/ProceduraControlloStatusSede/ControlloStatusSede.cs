using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal class ControlloStatusSede : BaseProcedure<ArgsControlloStatusSede>
    {
        public ControlloStatusSede(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public string selectedAA = string.Empty;
        public string folderPath = string.Empty;

        public DataTable studentiPendolari = new();

        private readonly Dictionary<string, string> _provCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _forcedStatusByCF = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DomicilioEvaluator.ContractInput> _openIstanzaByCf =
    new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DomicilioEvaluator.LastWorkedIstanza> _lastRejectByCf =
    new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, int> _comuneCompatGroup = new()
        {
            ["G954"] = 1,
            ["L725"] = 1,
            ["G698"] = 2,
            ["E472"] = 2,
            ["D708"] = 3,
            ["D843"] = 3,
            ["A323"] = 4,
            ["F880"] = 4
        };

        // ===== Calcolo_ComuneSedeStudi (VB) =====
        private static readonly Dictionary<string, string> _distCassino = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CL17"] = "L120",
            ["CL35"] = "L120",
            ["IL211"] = "D810",
            ["IL210"] = "D810",
            ["CLN01"] = "D810",
            ["CLN50"] = "I838"
        };
        private static readonly Dictionary<string, string> _distTorVergata = new(StringComparer.OrdinalIgnoreCase)
        {
            ["04UUTI60"] = "C858",
            ["04UUTI8"] = "C858",
            ["04UUTI10"] = "C858",
            ["04UUTI9"] = "C858"
        };
        private static readonly Dictionary<string, string> _distSanPioV = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VL333"] = "D643",
            ["SVEC1"] = "D643",
            ["UUPB14"] = "D643",
            ["UUPB3"] = "D643",
            ["SVEC2"] = "C351",
            ["UUPB18"] = "C351",
            ["UUPB7"] = "C351",
            ["SVEC3"] = "G273",
            ["SVEC4"] = "L840",
            ["UUPB10"] = "G288",
            ["UUPB21"] = "G288",
            ["UUPB11"] = "D612",
            ["UUPB22"] = "D612",
            ["UUPB13"] = "A783",
            ["UUPB2"] = "A783",
            ["UUPB15"] = "F839",
            ["UUPB4"] = "F839",
            ["UUPB16"] = "H579",
            ["UUPB5"] = "H579",
            ["UUPB17"] = "A091",
            ["UUPB6"] = "A091",
            ["UUPB19"] = "B157",
            ["UUPB8"] = "B157",
            ["UUPB20"] = "D086",
            ["UUPB9"] = "D086"
        };
        private static readonly Dictionary<string, string> _distLumsa = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ZD073"] = "L049",
            ["ZD085_T"] = "L049",
            ["ZL159_G"] = "E256",
            ["ZD081"] = "E256"
        };

        private static readonly HashSet<string> _forceAByCorso = new(StringComparer.OrdinalIgnoreCase)
        {
            "29386","29400","129618","129618_1","12961_1","DPTEA_57","ECOLUISS_52","04UUTK42",
            // nuovi dal VB (>= 20252026)
            "V89_1","V86"
        };

        private static readonly string[] _dateFormats = new[]
        {
            "yyyy-MM-dd","yyyyMMdd","dd/MM/yyyy","d/M/yyyy","dd-MM-yyyy","d-M-yyyy",
            "yyyy-MM-dd HH:mm:ss","dd/MM/yyyy HH:mm:ss"
        };

        public override void RunProcedure(ArgsControlloStatusSede args)
        {
            selectedAA = args._selectedAA?.Trim() ?? "";
            folderPath = args._folderPath?.Trim() ?? "";

            ValidateSelectedAA(selectedAA);

            studentiPendolari.Columns.Add("CodFiscale");
            studentiPendolari.Columns.Add("TitoloOneroso", typeof(bool));
            studentiPendolari.Columns.Add("SerieContratto");
            studentiPendolari.Columns.Add("DataRegistrazione");
            studentiPendolari.Columns.Add("DataDecorrenza");
            studentiPendolari.Columns.Add("DataScadenza");
            studentiPendolari.Columns.Add("DurataContratto", typeof(int));
            studentiPendolari.Columns.Add("Prorogato", typeof(bool));
            studentiPendolari.Columns.Add("DurataProroga", typeof(int));
            studentiPendolari.Columns.Add("SerieProroga");
            studentiPendolari.Columns.Add("ContrattoEnte", typeof(bool));
            studentiPendolari.Columns.Add("DenomEnte");
            studentiPendolari.Columns.Add("ImportoRataEnte", typeof(double));
            studentiPendolari.Columns.Add("Motivo");
            studentiPendolari.Columns.Add("StatoAttuale");
            studentiPendolari.Columns.Add("StatoSuggerito");
            studentiPendolari.Columns.Add("ComuneDom");
            studentiPendolari.Columns.Add("ComuneRes");
            studentiPendolari.Columns.Add("ComuneSede");
            studentiPendolari.Columns.Add("CodSede");
            studentiPendolari.Columns.Add("CodCorso");

            ExecuteMultiQueryAndCompute();

            Utilities.ExportDataTableToExcel(studentiPendolari, folderPath);
            Logger.LogInfo(100, "Fine lavorazione");
        }

        private void ExecuteMultiQueryAndCompute()
        {
            var (aaStart, aaEnd) = GetAaDateRange(selectedAA);

            var domande = new List<DomandaRow>();
            var vv = new Dictionary<int, string>();
            var esitiBS = new HashSet<int>();
            var esitiPA = new HashSet<int>();
            var dg = new Dictionary<int, (bool Rifugiato, bool Detenuto)>();
            var iscr = new Dictionary<int, IscrRow>();
            var resComune = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var resProv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lrs = new Dictionary<string, LrsDomRow>(StringComparer.OrdinalIgnoreCase);
            var codBlocchi = new Dictionary<int, string>();
            var nucleo = new Dictionary<int, (int NumComp, int ConvEstero, string TipoNucleo)>();
            var tipRed = new Dictionary<int, (string? Integr, string? Origine)>();
            var paRichiestaAttiva = new HashSet<int>();
            // tabelle comuni
            var inSedeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var pendolariMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            // fuori sede (prioritario) — VB usa sia globale che per sede/distaccata
            var fuoriSedeGlobal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fuoriSedeBySede = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var fuoriSedeByDist = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            // Estrai_ComuniLimitrofi (VB)
            var inSedeDistPairs = new HashSet<(string CodSedeDistaccata, string ComuneRes)>(new DistPairComparer());

            // posto alloggio / PA
            var prolungamentiCF = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var terminePAByEnte = new Dictionary<string, DateTime>();
            var enteByCF = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var richiestePA = new HashSet<int>();
            var rinunciaPA = new HashSet<int>();
            var idoneoPA2Assegn = new HashSet<int>();
            var vincitorePA = new HashSet<int>();
            var vincitorePANoAss = new HashSet<int>();

            using (var cmd = new SqlCommand("", CONNECTION))
            {
                cmd.CommandTimeout = 600;
                cmd.CommandText = "SET NOCOUNT ON; SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
WITH d AS (
  SELECT CAST(Num_domanda AS INT) AS Num_domanda,
         Cod_fiscale,
         Tipo_bando,
         ROW_NUMBER() OVER (PARTITION BY Num_domanda ORDER BY Data_validita DESC, Tipo_bando) AS rn
  FROM Domanda
  WHERE Anno_accademico = @AA
)
SELECT Num_domanda, Cod_fiscale, Tipo_bando
FROM d
WHERE rn = 1;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var tipo = Utilities.SafeGetString(rd, 2);
                        domande.Add(new DomandaRow
                        {
                            NumDomanda = rd.GetInt32(0),
                            CodFiscale = Utilities.SafeGetString(rd, 1),
                            TipoBando = tipo,
                            IsRichiedentePA = string.Equals(tipo, "PA", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
                if (domande.Count == 0) return;

                cmd.CommandText = @"
IF OBJECT_ID('tempdb..#domande') IS NOT NULL DROP TABLE #domande;
CREATE TABLE #domande (
  Num_domanda INT NOT NULL PRIMARY KEY,
  Cod_fiscale VARCHAR(32) NULL,
  Tipo_bando  VARCHAR(8)  NULL
);";
                cmd.Parameters.Clear();
                cmd.ExecuteNonQuery();

                using (var bulk = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.CheckConstraints, null)
                { DestinationTableName = "#domande", BulkCopyTimeout = 600 })
                {
                    var dt = new DataTable();
                    dt.Columns.Add("Num_domanda", typeof(int));
                    dt.Columns.Add("Cod_fiscale", typeof(string));
                    dt.Columns.Add("Tipo_bando", typeof(string));
                    foreach (var d in domande)
                        dt.Rows.Add(d.NumDomanda, d.CodFiscale?.Trim(), d.TipoBando?.Trim());
                    bulk.ColumnMappings.Add("Num_domanda", "Num_domanda");
                    bulk.ColumnMappings.Add("Cod_fiscale", "Cod_fiscale");
                    bulk.ColumnMappings.Add("Tipo_bando", "Tipo_bando");
                    bulk.WriteToServer(dt);
                }

                cmd.CommandText = @"
SELECT CAST(v.Num_domanda AS INT) AS Num_domanda, v.Status_sede
FROM vValori_calcolati v
JOIN #domande t ON t.Num_domanda = v.Num_domanda
WHERE v.Anno_accademico = @AA AND v.Status_sede IN ('A','B','C','D');";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) vv[rd.GetInt32(0)] = Utilities.SafeGetString(rd, 1);

                cmd.CommandText = @"
SELECT CAST(e.Num_domanda AS INT) AS Num_domanda
FROM vEsiti_concorsiBS e
JOIN #domande t ON t.Num_domanda = e.Num_domanda
WHERE e.Anno_accademico = @AA AND e.Cod_tipo_esito <> 0;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) esitiBS.Add(rd.GetInt32(0));

                cmd.CommandText = @"
SELECT CAST(e.Num_domanda AS INT) AS Num_domanda
FROM vEsiti_concorsiPA e
JOIN #domande t ON t.Num_domanda = e.Num_domanda
WHERE e.Anno_accademico = @AA AND e.Cod_tipo_esito <> 0;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) esitiPA.Add(rd.GetInt32(0));

                cmd.CommandText = @"
SELECT CAST(d.Num_domanda AS INT) AS Num_domanda,
       COALESCE(v.Rifug_politico,0) AS Rifugiato,
       0
FROM #domande d
JOIN vDATIGENERALI_dom v ON v.Anno_accademico = @AA AND v.Num_domanda = d.Num_domanda;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        dg[rd.GetInt32(0)] = (Utilities.SafeGetInt(rd, 1) == 1, Utilities.SafeGetInt(rd, 2) == 1);

                cmd.CommandText = @"
SELECT CAST(d.Num_domanda AS INT) AS Num_domanda,
       i.Cod_tipologia_studi,
       cl.Comune_Sede_studi,
       i.Cod_sede_studi,
       i.Cod_corso_laurea,
       i.Cod_facolta,
       ISNULL(cl.Cod_sede_distaccata,'') AS Cod_sede_distaccata
FROM #domande d
JOIN vIscrizioni i
  ON i.Anno_accademico = @AA AND i.Cod_fiscale = d.Cod_fiscale AND i.tipo_bando = d.Tipo_bando
JOIN Corsi_laurea cl
  ON i.Cod_corso_laurea     = cl.Cod_corso_laurea
 AND i.Anno_accad_inizio    = cl.Anno_accad_inizio
 And i.Cod_tipo_ordinamento = cl.Cod_tipo_ordinamento
 And i.Cod_facolta          = cl.Cod_facolta
 And i.Cod_sede_studi       = cl.Cod_sede_studi
 And i.Cod_tipologia_studi  = cl.Cod_tipologia_studi
WHERE i.Cod_tipologia_studi <> '06';";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var num = rd.GetInt32(0);
                        iscr[num] = new IscrRow
                        {
                            CodTipologia = Utilities.SafeGetString(rd, 1),
                            ComuneSede = Utilities.SafeGetString(rd, 2),
                            CodSedeStudi = Utilities.SafeGetString(rd, 3),
                            CodCorso = Utilities.SafeGetString(rd, 4),
                            CodFacolta = Utilities.SafeGetString(rd, 5),
                            CodSedeDistaccata = Utilities.SafeGetString(rd, 6),
                        };
                    }
                }

                cmd.CommandText = @"
SELECT DISTINCT d.Cod_fiscale, r.Cod_comune, r.provincia_residenza
FROM #domande d
JOIN vResidenza r ON r.Cod_fiscale = d.Cod_fiscale AND r.Anno_accademico = @AA;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var cf = Utilities.SafeGetString(rd, 0);
                        resComune[cf] = Utilities.SafeGetString(rd, 1);
                        resProv[cf] = Utilities.SafeGetString(rd, 2);
                    }
                }

                cmd.CommandText = @"
SELECT DISTINCT f.Cod_Fiscale, f.Status_sede
FROM Forzature_StatusSede f
JOIN #domande d ON d.Cod_fiscale = f.Cod_Fiscale
WHERE f.Anno_Accademico = @AA
  AND f.Data_fine_validita IS NULL
  AND f.Status_sede IN ('A','B','C','D');";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        _forcedStatusByCF[Utilities.SafeGetString(rd, 0)] = Utilities.SafeGetString(rd, 1);

                cmd.CommandText = @"
WITH x AS (
  SELECT LRS.COD_FISCALE, LRS.TITOLO_ONEROSO, LRS.N_SERIE_CONTRATTO, LRS.DATA_REG_CONTRATTO,
         LRS.DATA_DECORRENZA, LRS.DATA_SCADENZA, LRS.DURATA_CONTRATTO, LRS.PROROGA, LRS.DURATA_PROROGA,
         LRS.ESTREMI_PROROGA, LRS.TIPO_CONTRATTO_TITOLO_ONEROSO, LRS.DENOM_ENTE, LRS.IMPORTO_RATA,
         LRS.COD_COMUNE AS CodComuneDom,
         ROW_NUMBER() OVER (PARTITION BY LRS.COD_FISCALE ORDER BY LRS.DATA_VALIDITA DESC) AS rn
  FROM #domande d
  JOIN LUOGO_REPERIBILITA_STUDENTE LRS
    ON LRS.ANNO_ACCADEMICO = @AA AND LRS.COD_FISCALE = d.Cod_fiscale
  WHERE LRS.TIPO_LUOGO = 'DOM' and titolo_oneroso is not null
)
SELECT * FROM x WHERE rn = 1;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var cf = Utilities.SafeGetString(rd, "COD_FISCALE");
                        lrs[cf] = new LrsDomRow
                        {
                            CodFiscale = cf,
                            TitoloOneroso = Utilities.SafeGetInt(rd, "TITOLO_ONEROSO") == 1,
                            SerieContratto = Utilities.SafeGetString(rd, "N_SERIE_CONTRATTO"),
                            DataRegistrazione = Utilities.SafeGetString(rd, "DATA_REG_CONTRATTO"),
                            DataDecorrenza = Utilities.SafeGetString(rd, "DATA_DECORRENZA"),
                            DataScadenza = Utilities.SafeGetString(rd, "DATA_SCADENZA"),
                            DurataContratto = Utilities.SafeGetInt(rd, "DURATA_CONTRATTO"),
                            Proroga = Utilities.SafeGetInt(rd, "PROROGA") == 1,
                            DurataProroga = Utilities.SafeGetInt(rd, "DURATA_PROROGA"),
                            SerieProroga = Utilities.SafeGetString(rd, "ESTREMI_PROROGA"),
                            ContrattoEnte = Utilities.SafeGetInt(rd, "TIPO_CONTRATTO_TITOLO_ONEROSO") == 1,
                            DenomEnte = Utilities.SafeGetString(rd, "DENOM_ENTE"),
                            ImportoRata = Utilities.SafeGetDouble(rd, "IMPORTO_RATA"),
                            CodComuneDom = Utilities.SafeGetString(rd, "CodComuneDom")
                        };
                    }
                }

                cmd.CommandText = @"
SELECT CAST(v.Num_domanda AS INT) AS Num_domanda,
       dbo.SlashBlocchi(v.Num_domanda, v.Anno_accademico, '') as cod_blocchi
FROM vValori_calcolati v
JOIN #domande t ON t.Num_domanda = v.Num_domanda
WHERE v.Anno_accademico = @AA;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) codBlocchi[rd.GetInt32(0)] = Utilities.SafeGetString(rd, 1);

                cmd.CommandText = @"
SELECT CAST(Num_domanda AS INT) AS Num_domanda,
       CAST(Num_componenti AS INT) AS Num_componenti,
       CAST(Numero_conviventi_estero AS INT) AS Numero_conviventi_estero,
       COALESCE(cod_TIPOLOGIA_NUCLEO,'') as TipoNucleo
FROM VNUCLEO_FAMILIARE
WHERE Anno_accademico = @AA
  AND Num_domanda IN (SELECT Num_domanda FROM #domande);";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        nucleo[rd.GetInt32(0)] = (Utilities.SafeGetInt(rd, 1),
                                                  Utilities.SafeGetInt(rd, 2),
                                                  Utilities.SafeGetString(rd, 3));

                cmd.CommandText = @"
WITH x AS (
  SELECT CAST(tr.Num_domanda AS INT) AS Num_domanda,
         tr.TIPO_REDD_NUCLEO_FAM_INTEGR,
         tr.TIPO_REDD_NUCLEO_FAM_ORIGINE,
         tr.DATA_VALIDITA,
         ROW_NUMBER() OVER (PARTITION BY tr.Num_domanda ORDER BY tr.DATA_VALIDITA DESC) rn
  FROM TIPOLOGIE_REDDITI tr
  WHERE tr.Anno_accademico = @AA
    AND tr.Num_domanda IN (SELECT Num_domanda FROM #domande)
)
SELECT Num_domanda, TIPO_REDD_NUCLEO_FAM_INTEGR, TIPO_REDD_NUCLEO_FAM_ORIGINE
FROM x WHERE rn = 1;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        tipRed[rd.GetInt32(0)] = (Utilities.SafeGetString(rd, 1), Utilities.SafeGetString(rd, 2));

                cmd.CommandText = @"SELECT cod_sede_studi, Cod_comune FROM COMUNI_INSEDE;";
                cmd.Parameters.Clear();
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        AddToMap(inSedeMap, Utilities.SafeGetString(rd, 0), Utilities.SafeGetString(rd, 1));

                cmd.CommandText = @"SELECT cod_sede_studi, Cod_comune FROM COMUNI_PENDOLARI;";
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        AddToMap(pendolariMap, Utilities.SafeGetString(rd, 0), Utilities.SafeGetString(rd, 1));

                cmd.CommandText = @"SELECT ISNULL(cod_sede_studi,'') as cod_sede_studi,
                                           ISNULL(cod_sede_distaccata,'') as cod_sede_distaccata,
                                           cod_comune
                                    FROM COMUNI_FUORISEDE;";
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var sede = (Utilities.SafeGetString(rd, 0) ?? "").Trim();
                        var dist = (Utilities.SafeGetString(rd, 1) ?? "").Trim();
                        var comune = (Utilities.SafeGetString(rd, 2) ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(comune)) continue;

                        if (string.IsNullOrWhiteSpace(sede) && string.IsNullOrWhiteSpace(dist))
                        {
                            fuoriSedeGlobal.Add(comune);
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(sede))
                            AddToMap(fuoriSedeBySede, sede, comune);

                        if (!string.IsNullOrWhiteSpace(dist))
                            AddToMap(fuoriSedeByDist, dist, comune);
                    }
                }

                // Estrai_ComuniLimitrofi (VB)
                cmd.CommandText = @"
SELECT
    C.COD_SEDE_DISTACCATA,
    C.COD_COMUNE as ComuneRes
FROM COMUNI_INSEDE C
WHERE C.COD_SEDE_DISTACCATA <> '00000';";
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var dist = (Utilities.SafeGetString(rd, 0) ?? "").Trim();
                        var res = (Utilities.SafeGetString(rd, 1) ?? "").Trim();
                        if (dist.Length == 0 || res.Length == 0) continue;
                        inSedeDistPairs.Add((dist, res));
                    }
                }

                cmd.CommandText = @"
SELECT DISTINCT Cod_Fiscale
FROM Prolungamenti_posto_alloggio
WHERE anno_accademico = @AA AND data_fine_validita IS NULL;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) prolungamentiCF.Add(Utilities.SafeGetString(rd, 0));

                cmd.CommandText = @"
SELECT cod_ente, data_termine_ass_pa
FROM termine_assegnazione_pa
WHERE anno_accademico = @AA;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                    {
                        var ente = Utilities.SafeGetString(rd, 0);
                        if (DateTime.TryParse(Utilities.SafeGetString(rd, 1), out var dt))
                            terminePAByEnte[ente] = dt;
                    }

                // ESITI_CONCORSI (PA) — ultimo per domanda+beneficio
                cmd.CommandText = @"
WITH x AS (
  SELECT
    CAST(ec.Num_domanda AS INT) AS Num_domanda,
    UPPER(ec.Cod_beneficio) AS Cod_beneficio,
    CAST(ec.Cod_tipo_esito AS INT) AS Cod_tipo_esito,
    ROW_NUMBER() OVER (
      PARTITION BY ec.Num_domanda, UPPER(ec.Cod_beneficio)
      ORDER BY ec.Data_validita DESC
    ) AS rn
  FROM ESITI_CONCORSI ec
  JOIN #domande d ON d.Num_domanda = ec.Num_domanda
  WHERE ec.Anno_accademico = @AA
    AND UPPER(ec.Cod_beneficio) IN ('PA')
)
SELECT Num_domanda, Cod_beneficio, Cod_tipo_esito
FROM x
WHERE rn = 1;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        int num = rd.GetInt32(0);
                        string ben = Utilities.SafeGetString(rd, 1).Trim().ToUpperInvariant();
                        int esito = Utilities.SafeGetInt(rd, 2);

                        if (ben == "PA")
                        {
                            richiestePA.Add(num);
                            if (esito == 1) idoneoPA2Assegn.Add(num);
                            if (esito == 2) vincitorePA.Add(num);
                            if (esito == 4) vincitorePANoAss.Add(num);
                        }
                    }
                }

                // VARIAZIONI — rinuncia PA (cod_tipo_variaz 2/9/10)
                cmd.CommandText = @"
SELECT DISTINCT CAST(v.Num_domanda AS INT) AS Num_domanda
FROM VARIAZIONI v
JOIN #domande d ON d.Num_domanda = v.Num_domanda
WHERE v.Anno_accademico = @AA
  AND UPPER(v.Cod_beneficio) = 'PA'
  AND v.Cod_tipo_variaz IN (2,9,10)
  AND v.Data_validita = (
      SELECT MAX(v2.Data_validita)
      FROM VARIAZIONI v2
      WHERE v2.Anno_accademico = v.Anno_accademico
        AND v2.Num_domanda = v.Num_domanda
        AND UPPER(v2.Cod_beneficio) = UPPER(v.Cod_beneficio)
        AND v2.Cod_tipo_variaz = v.Cod_tipo_variaz
  );";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        rinunciaPA.Add(rd.GetInt32(0));

                // Appartenenza → ente (per termine assegnazione)
                cmd.CommandText = @"
WITH x AS (
  SELECT
    a.Cod_fiscale,
    a.Cod_ente,
    ROW_NUMBER() OVER (
      PARTITION BY a.Anno_accademico, a.Cod_fiscale, COALESCE(a.tipo_bando,'LZ')
      ORDER BY a.Data_validita DESC
    ) rn
  FROM Appartenenza a
  JOIN #domande d
    ON d.Cod_fiscale = a.Cod_fiscale
   AND COALESCE(a.tipo_bando,'LZ') = COALESCE(d.Tipo_bando,'LZ')
  WHERE a.Anno_accademico = @AA
)
SELECT Cod_fiscale, Cod_ente
FROM x
WHERE rn = 1;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var cf = Utilities.SafeGetString(rd, 0);
                        var ente = Utilities.SafeGetString(rd, 1);
                        if (!string.IsNullOrWhiteSpace(cf) && !string.IsNullOrWhiteSpace(ente))
                            enteByCF[cf] = ente;
                    }
                }

                cmd.CommandText = @"
;WITH q AS (
    SELECT
        idg.Cod_fiscale,
        icl.COD_COMUNE,
        icl.TITOLO_ONEROSO,
        icl.TIPO_CONTRATTO_TITOLO_ONEROSO,
        icl.N_SERIE_CONTRATTO,
        icl.DATA_REG_CONTRATTO,
        icl.DATA_DECORRENZA,
        icl.DATA_SCADENZA,
        icl.DURATA_CONTRATTO,
        icl.PROROGA,
        icl.DURATA_PROROGA,
        icl.ESTREMI_PROROGA,
        icl.DENOM_ENTE,
        icl.IMPORTO_RATA,
        ROW_NUMBER() OVER (
            PARTITION BY idg.Cod_fiscale
            ORDER BY idg.Data_validita DESC, idg.Num_istanza DESC
        ) rn
    FROM Istanza_dati_generali idg
    INNER JOIN Istanza_status iis
        ON idg.Num_istanza = iis.Num_istanza
       AND iis.data_fine_validita IS NULL
    INNER JOIN Istanza_Contratto_locazione icl
        ON idg.Num_istanza = icl.Num_istanza
       AND icl.data_fine_validita IS NULL
    INNER JOIN #domande d
        ON d.Cod_fiscale = idg.Cod_fiscale
    WHERE idg.Anno_accademico = @AA
      AND idg.Data_fine_validita IS NULL
      AND idg.Esito_istanza IS NULL
)
SELECT *
FROM q
WHERE rn = 1;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;

                _openIstanzaByCf.Clear();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var cf = (Utilities.SafeGetString(rd, "Cod_fiscale") ?? "").Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(cf)) continue;

                        var input = new DomicilioEvaluator.ContractInput(
                            CodFiscale: cf,
                            ComuneDomicilio: Utilities.SafeGetString(rd, "COD_COMUNE"),
                            TitoloOneroso: Utilities.SafeGetInt(rd, "TITOLO_ONEROSO") == 1,
                            ContrattoEnte: Utilities.SafeGetInt(rd, "TIPO_CONTRATTO_TITOLO_ONEROSO") == 1,
                            SerieContratto: Utilities.SafeGetString(rd, "N_SERIE_CONTRATTO"),
                            DataRegistrazioneString: Utilities.SafeGetString(rd, "DATA_REG_CONTRATTO"),
                            DataDecorrenzaString: Utilities.SafeGetString(rd, "DATA_DECORRENZA"),
                            DataScadenzaString: Utilities.SafeGetString(rd, "DATA_SCADENZA"),
                            DurataContratto: Utilities.SafeGetInt(rd, "DURATA_CONTRATTO"),
                            Prorogato: Utilities.SafeGetInt(rd, "PROROGA") == 1,
                            DurataProroga: Utilities.SafeGetInt(rd, "DURATA_PROROGA"),
                            SerieProroga: Utilities.SafeGetString(rd, "ESTREMI_PROROGA"),
                            DenominazioneEnte: Utilities.SafeGetString(rd, "DENOM_ENTE"),
                            ImportoRataEnte: Utilities.SafeGetDouble(rd, "IMPORTO_RATA")
                        );

                        _openIstanzaByCf[cf] = input;
                    }
                }
                cmd.CommandText = @"
SELECT CAST(br.Num_domanda AS INT) AS Num_domanda
FROM Benefici_richiesti br
JOIN #domande d ON d.Num_domanda = br.Num_domanda
WHERE br.Anno_accademico = @AA
  AND UPPER(br.Cod_beneficio) = 'PA'
  AND br.Data_fine_validita IS NULL;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;

                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        paRichiestaAttiva.Add(rd.GetInt32(0));


                cmd.CommandText = @"
;WITH lastWorked AS (
    SELECT
        idg.Cod_fiscale,
        CAST(idg.Esito_istanza AS INT) AS Esito,
        idg.Data_validita AS DataCreazione,
        icl.DATA_SCADENZA AS ScadenzaContrattoIstanza,
        MAX(iis.data_fine_validita) AS DataEsito,
        ROW_NUMBER() OVER (
            PARTITION BY idg.Cod_fiscale
            ORDER BY MAX(iis.data_fine_validita) DESC, idg.Data_validita DESC
        ) AS rn
    FROM Istanza_dati_generali idg
    JOIN Istanza_status iis
        ON idg.Num_istanza = iis.Num_istanza
    JOIN Istanza_Contratto_locazione icl
        ON idg.Num_istanza = icl.Num_istanza
       AND icl.data_fine_validita IS NOT NULL
    JOIN #domande d
        ON d.Cod_fiscale = idg.Cod_fiscale
    WHERE idg.Anno_accademico = @AA
      AND idg.Esito_istanza IS NOT NULL
    GROUP BY
        idg.Cod_fiscale,
        CAST(idg.Esito_istanza AS INT),
        idg.Data_validita,
        icl.DATA_SCADENZA
)
SELECT Cod_fiscale, Esito, DataCreazione, ScadenzaContrattoIstanza, DataEsito
FROM lastWorked
WHERE rn = 1 AND Esito = 0 AND DataEsito IS NOT NULL;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", SqlDbType.VarChar, 8).Value = selectedAA;

                _lastRejectByCf.Clear();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var cf = (Utilities.SafeGetString(rd, 0) ?? "").Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(cf)) continue;

                        var esito = Utilities.SafeGetInt(rd, 1);
                        var dataCreazione = rd.GetDateTime(2);
                        DateTime? scad = rd.IsDBNull(3) ? (DateTime?)null : rd.GetDateTime(3);
                        DateTime? dataEsito = rd.IsDBNull(4) ? (DateTime?)null : rd.GetDateTime(4);

                        _lastRejectByCf[cf] = new DomicilioEvaluator.LastWorkedIstanza(esito, dataCreazione, scad, dataEsito);
                    }
                }
                // Preload province
                var comuniNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in resComune.Values) if (!string.IsNullOrWhiteSpace(v)) comuniNeeded.Add(v);
                foreach (var v in iscr.Values) if (!string.IsNullOrWhiteSpace(v.ComuneSede)) comuniNeeded.Add(v.ComuneSede);
                PreloadProvinces(comuniNeeded, CONNECTION);

                cmd.CommandText = "DROP TABLE #domande;";
                cmd.Parameters.Clear();
                cmd.ExecuteNonQuery();

                foreach (var d in domande)
                {
                    if (!vv.TryGetValue(d.NumDomanda, out var statusSede)) statusSede = "0";
                    if (!esitiBS.Contains(d.NumDomanda)) continue; // solo BS
                    if (esitiPA.Contains(d.NumDomanda)) continue;  // escludi se ha esito PA
                    if (!iscr.TryGetValue(d.NumDomanda, out var iscrRow)) continue;
                    if(d.CodFiscale == "TRNSLV03A60F839Y")
                    {
                        string test = "";
                    }
                    var outRow = new OutputRow(d, statusSede)
                    {
                        ComuneResidenza = (resComune.TryGetValue(d.CodFiscale, out var cr) ? cr : "").Trim(),
                        ProvinciaRes = (resProv.TryGetValue(d.CodFiscale, out var pr) ? pr : "").Trim(),
                        ComuneSedeStudi = iscrRow.ComuneSede?.Trim() ?? "",
                        CodSedeStudi = iscrRow.CodSedeStudi?.Trim() ?? "",
                        CodCorso = iscrRow.CodCorso?.Trim() ?? "",
                        CodFacolta = iscrRow.CodFacolta?.Trim() ?? "",
                        CodSedeDistaccata = iscrRow.CodSedeDistaccata?.Trim() ?? "",
                        CodBlocchi = codBlocchi.TryGetValue(d.NumDomanda, out var cb) ? cb : "",
                        ProlungamentoPA = prolungamentiCF.Contains(d.CodFiscale),
                        AnnoAccademico = selectedAA,

                        // Richiesta PA = beneficio PA (VB), non solo tipo_bando
                        PaRichiestaAttiva = paRichiestaAttiva.Contains(d.NumDomanda),
                        RichiedentePA = paRichiestaAttiva.Contains(d.NumDomanda),

                        // flag PA
                        IdoneoPA_Attesa2Assegn = idoneoPA2Assegn.Contains(d.NumDomanda),
                        VincitorePA = vincitorePA.Contains(d.NumDomanda),
                        VincitorePANoAssegn = vincitorePANoAss.Contains(d.NumDomanda),
                        RinunciaPA = rinunciaPA.Contains(d.NumDomanda),

                        EnteGestione = enteByCF.TryGetValue(d.CodFiscale, out var eg) ? eg : ""
                    };

                    if (dg.TryGetValue(d.NumDomanda, out var flags))
                    {
                        outRow.Detenuto = flags.Detenuto;
                        outRow.RifugiatoPolitico = flags.Rifugiato;
                    }

                    if (lrs.TryGetValue(d.CodFiscale, out var dom))
                    {
                        outRow.TitoloOneroso = dom.TitoloOneroso;
                        outRow.SerieContratto = dom.SerieContratto;
                        outRow.DataRegistrazione = dom.DataRegistrazione;
                        outRow.DataDecorrenza = dom.DataDecorrenza;
                        outRow.DataScadenza = dom.DataScadenza;
                        outRow.DurataContratto = dom.DurataContratto;
                        outRow.Prorogato = dom.Proroga;
                        outRow.DurataProroga = dom.DurataProroga;
                        outRow.SerieProroga = dom.SerieProroga;
                        outRow.ContrattoEnte = dom.ContrattoEnte;
                        outRow.DenomEnte = dom.DenomEnte;
                        outRow.ImportoRataEnte = dom.ImportoRata;
                        outRow.ComuneDomicilio = dom.CodComuneDom;
                    }

                    bool famigliaItalia = true;
                    if (nucleo.TryGetValue(d.NumDomanda, out var n))
                    {
                        if (outRow.ProvinciaRes == "EE")
                        {
                            if (n.ConvEstero >= Math.Ceiling(n.NumComp / 2.0)) famigliaItalia = false;
                        }
                    }
                   
                    outRow.FamigliaResidenteItalia = famigliaItalia;

                    // ===== ALLINEAMENTO DISTACCATE VB: prima calcolo comune sede studi =====
                    ApplySedeOverrides_VB(outRow);

                    EvaluateAndAppend(
                        outRow,
                        aaStart,
                        aaEnd,
                        inSedeMap,
                        pendolariMap,
                        fuoriSedeGlobal,
                        fuoriSedeBySede,
                        fuoriSedeByDist,
                        inSedeDistPairs,
                        terminePAByEnte
                    );
                }
            }
        }


        private void EvaluateAndAppend(
            OutputRow row,
            DateTime aaStart,
            DateTime aaEnd,
            Dictionary<string, HashSet<string>> inSedeMap,
            Dictionary<string, HashSet<string>> pendolariMap,
            HashSet<string> fuoriSedeGlobal,
            Dictionary<string, HashSet<string>> fuoriSedeBySede,
            Dictionary<string, HashSet<string>> fuoriSedeByDist,
            HashSet<(string ComuneSede, string ComuneRes)> inSedeDistPairs,
            Dictionary<string, DateTime> terminePAByEnte)
        {

            if(row.CodFiscale == "TRNSLV03A60F839Y")
            {
                string test = "";
            }

            // 0) forzatura tabellare
            if (_forcedStatusByCF.TryGetValue(row.CodFiscale, out var forced))
            {
                AppendDecision(row, forced, "Forzatura tabellare");
                return;
            }

            // 1) telematici/corsi forzati → A (precedenza, VB)
            if (row.ForcedStatus == "A" || IsTelematicoOrForcedA(row.AnnoAccademico, row.CodSedeStudi, row.CodFacolta, row.CodCorso))
            {
                AppendDecision(row, "A", "Telematico/corso forzato");
                return;
            }

            // 2) detenuto → A
            if (row.Detenuto)
            {
                AppendDecision(row, "A", "Speciale: detenuto");
                return;
            }

            // 3) rifugiato → B (come prima)
            if (row.RifugiatoPolitico)
            {
                AppendDecision(row, "B", "Speciale: rifugiato politico");
                return;
            }

            // 5) residenza == sede → A
            if (!string.IsNullOrEmpty(row.ComuneResidenza) &&
                row.ComuneResidenza.Equals(row.ComuneSedeStudi, StringComparison.OrdinalIgnoreCase))
            {
                AppendDecision(row, "A", "res==sede|prio1");
                return;
            }

            // 4) nucleo estero prevalente e provincia estera → B
            if (!row.FamigliaResidenteItalia && row.ProvinciaRes == "EE")
            {
                AppendDecision(row, "B", "Nucleo estero prevalente");
                return;
            }

            // Da qui in poi: studente “italiano” ai fini logica status.
            // Domicilio: serve per qualificare "fuori sede" (B) anche se comune è in lista prioritaria.
            var domicilioValido = ComputeDomicilioValido_WithIstanze(row, aaStart, aaEnd, out var domicileReason);

            // 7) COMUNI_FUORISEDE prioritari
            // MODIFICA: per italiani -> B solo con domicilio valido, altrimenti NON assegno B qui (si prosegue)
            if (IsComuneFuoriSedePrioritario_VB(row, fuoriSedeGlobal, fuoriSedeBySede, fuoriSedeByDist))
            {
                if (domicilioValido)
                {
                    var sug = "B";
                    MaybeDowngradePA(ref sug, row, terminePAByEnte);
                    AppendDecision(row, sug, $"COMUNI_FUORISEDE(prio) + domicilio valido → {sug} ({domicileReason})");
                    return;
                }

                // comune “fuori sede” ma senza domicilio valido: non concedere B
                // continua nelle regole successive (PA / pendolare / fallback)
            }

            // 8) sede distaccata speciale (VB: F061/ISP, A462/TW8)
            if (IsSpecialDistaccata_VB(row))
            {
                var sug = EvaluateSpecialDistaccata_VB(row, aaStart, aaEnd, terminePAByEnte);
                AppendDecision(row, sug, "Sede distaccata speciale");
                return;
            }

            // 6) COMUNI_INSEDE + Estrai_ComuniLimitrofi (VB)
            if (IsInSede_VB(row, inSedeMap, inSedeDistPairs))
            {
                AppendDecision(row, "A", "COMUNI_INSEDE/limitrofi|prio2");
                return;
            }

            // 9) PA: consentita solo se richiesta attiva e (idoneo/vincitore/noAssegn) e non rinuncia
            if (row.PaRichiestaAttiva && HasPaAlloggioStatus(row) && IsSedePAConsentita(row.AnnoAccademico, row.ComuneSedeStudi))
            {
                var sugPA = "B";
                MaybeDowngradePA(ref sugPA, row, terminePAByEnte);
                AppendDecision(row, sugPA, "PA consentita (idoneo/vincitore) + richiesta attiva");
                return;
            }

            // 10) pendolare VB (lista + provincia) — fuori sede tabellare già gestito prima
            if (IsPendolare_VB(row, pendolariMap))
            {
                if (domicilioValido)
                {
                    var sug = "B";
                    MaybeDowngradePA(ref sug, row, terminePAByEnte);
                    AppendDecision(row, sug, $"Pendolare VB + domicilio valido → {sug} ({domicileReason})");
                }
                else
                {
                    AppendDecision(row, "C", "Pendolare VB (lista/provincia) → C");
                }
                return;
            }

            // 11) fallback
            if (domicilioValido)
            {
                var sug = "B";
                MaybeDowngradePA(ref sug, row, terminePAByEnte);
                AppendDecision(row, sug, $"Fallback: domicilio valido → {sug} ({domicileReason})");
            }
            else
            {
                AppendDecision(row, "D", $"Fallback: domicilio NON valido → D ({domicileReason})");
            }
        }


        // ======== ALLINEAMENTO “SEDI DISTACCATE” AL VB ========

        // Calcolo_ComuneSedeStudi (VB) con exit/return per blocchi sede
        private void ApplySedeOverrides_VB(OutputRow row)
        {
            var codSede = (row.CodSedeStudi ?? "").Trim().ToUpperInvariant();
            var corso = (row.CodCorso ?? "").Trim();

            // Cassino: A / O
            if (codSede == "A" || codSede == "O")
            {
                if (_distCassino.TryGetValue(corso, out var com))
                    row.ComuneSedeStudi = com;
                // VB: Exit Sub
                goto End;
            }

            // Tor Vergata: C
            if (codSede == "C")
            {
                if (_distTorVergata.TryGetValue(corso, out var com))
                    row.ComuneSedeStudi = com;
                // VB: Exit Sub
                goto End;
            }

            // San Pio V: W
            if (codSede == "W")
            {
                if (_distSanPioV.TryGetValue(corso, out var com))
                    row.ComuneSedeStudi = com;
                // VB: Exit Sub
                goto End;
            }

            // LUMSA: K
            if (codSede == "K")
            {
                if (_distLumsa.TryGetValue(corso, out var com))
                    row.ComuneSedeStudi = com;
                // VB: Exit Sub
                goto End;
            }

        End:
            // Forzature A (VB) — applicate dopo il calcolo comune sede
            if (IsTelematicoOrForcedA(row.AnnoAccademico, row.CodSedeStudi, row.CodFacolta, row.CodCorso))
                row.ForcedStatus = "A";
        }

        // Estrai_ComuniLimitrofi + COMUNI_INSEDE (VB)
        private bool IsInSede_VB(
            OutputRow row,
            Dictionary<string, HashSet<string>> inSedeMap,
            HashSet<(string CodSedeDistaccata, string ComuneRes)> inSedeDistPairs)
        {
            var comuneRes = (row.ComuneResidenza ?? "").Trim();
            var comuneSede = (row.ComuneSedeStudi ?? "").Trim();
            var codSede = (row.CodSedeStudi ?? "").Trim().ToUpperInvariant();
            var codDist = (row.CodSedeDistaccata ?? "").Trim();

            if (string.IsNullOrWhiteSpace(comuneRes) || string.IsNullOrWhiteSpace(comuneSede))
                return false;

            // VB: A/O/P/D usano lista propria; tutti gli altri usano B.
            var key = (codSede is "A" or "O" or "P" or "D") ? codSede : "B";

            // Estrai_ComuniLimitrofi SOLO se:
            // - la sede è tra A/O/P/D/B (come prima)
            // - lo studente studia in sede distaccata (codDist valorizzato e != '00000')
            if (codSede is "A" or "O" or "P" or "D" or "B")
            {
                if (!string.IsNullOrWhiteSpace(codDist) && codDist != "00000")
                {
                    if (inSedeDistPairs.Contains((codDist, comuneRes)))
                        return true;
                }
            }

            return inSedeMap.TryGetValue(key, out var set) && set.Contains(comuneRes);
        }

        // COMUNI_FUORISEDE prioritario (VB) con match anche su cod_sede_distaccata
        private bool IsComuneFuoriSedePrioritario_VB(
            OutputRow row,
            HashSet<string> fuoriSedeGlobal,
            Dictionary<string, HashSet<string>> fuoriSedeBySede,
            Dictionary<string, HashSet<string>> fuoriSedeByDist)
        {
            var comuneRes = (row.ComuneResidenza ?? "").Trim();
            if (string.IsNullOrWhiteSpace(comuneRes)) return false;

            if (fuoriSedeGlobal.Contains(comuneRes)) return true;

            var codSede = (row.CodSedeStudi ?? "").Trim().ToUpperInvariant();
            var dist = (row.CodSedeDistaccata ?? "").Trim();

            // VB: (cod_sede_studi = Cod_SedeStudi) OR (cod_sede_distaccata = cod_sede_distaccata)
            if (!string.IsNullOrWhiteSpace(codSede) &&
                fuoriSedeBySede.TryGetValue(codSede, out var setSede) &&
                setSede.Contains(comuneRes))
                return true;

            if (!string.IsNullOrWhiteSpace(dist) &&
                fuoriSedeByDist.TryGetValue(dist, out var setDist) &&
                setDist.Contains(comuneRes))
                return true;

            // VB: default 'B' per sedi non A/O/P/D
            if (!(codSede is "A" or "O" or "P" or "D") &&
                fuoriSedeBySede.TryGetValue("B", out var setB) &&
                setB.Contains(comuneRes))
                return true;

            // Latina (chiave LT) dal 20212022 in poi (VB)
            if (string.Compare(row.AnnoAccademico, "20212022", StringComparison.Ordinal) >= 0 &&
                row.ComuneSedeStudi == "E472")
            {
                if (fuoriSedeBySede.TryGetValue("LT", out var setLt) && setLt.Contains(comuneRes))
                    return true;
            }

            // Viterbo dal 20232024: esclusi per cod_sede_studi='D' (VB)
            if (string.Compare(row.AnnoAccademico, "20232024", StringComparison.Ordinal) >= 0 &&
                row.ComuneSedeStudi == "H501")
            {
                if (fuoriSedeBySede.TryGetValue("D", out var setVt) && setVt.Contains(comuneRes))
                    return true;
            }

            return false;
        }

        // Pendolare VB (lista + provincia) — distaccate incluse
        private bool IsPendolare_VB(OutputRow row, Dictionary<string, HashSet<string>> pendolariMap)
        {
            var comuneRes = (row.ComuneResidenza ?? "").Trim();
            var comuneSede = (row.ComuneSedeStudi ?? "").Trim();
            if (string.IsNullOrWhiteSpace(comuneRes) || string.IsNullOrWhiteSpace(comuneSede))
                return false;

            var codSede = (row.CodSedeStudi ?? "").Trim().ToUpperInvariant();

            // VB: A/O/P/D lista propria; altrimenti 'B'
            var keyPend = (codSede is "A" or "O" or "P" or "D") ? codSede : "B";
            if (pendolariMap.TryGetValue(keyPend, out var setPend) && setPend.Contains(comuneRes))
                return true;

            return false;
        }

        // sede distaccata speciale (VB)
        private static bool IsSpecialDistaccata_VB(OutputRow row)
        {
            var corso = (row.CodCorso ?? "").Trim().ToUpperInvariant();
            var comuneSede = (row.ComuneSedeStudi ?? "").Trim().ToUpperInvariant();

            return (comuneSede == "F061" && corso == "ISP") ||
                   (comuneSede == "A462" && corso == "TW8");
        }

        // CalcoloSedeDistaccata (VB) — per anni recenti: TP/AP => C, altrimenti domicilio valido => B, altrimenti D
        private string EvaluateSpecialDistaccata_VB(OutputRow row, DateTime aaStart, DateTime aaEnd, Dictionary<string, DateTime> terminePAByEnte)
        {
            if (!string.IsNullOrEmpty(row.ComuneResidenza) &&
                row.ComuneResidenza.Equals(row.ComuneSedeStudi, StringComparison.OrdinalIgnoreCase))
                return "A";

            var provRes = _provCache.TryGetValue(row.ComuneResidenza ?? "", out var pr) ? pr : "";
            if (provRes is "TP" or "AP")
                return "C";

            var domicilioValido = ComputeDomicilioValido_WithIstanze(row, aaStart, aaEnd, out var _);
            if (domicilioValido)
            {
                var sug = "B";
                MaybeDowngradePA(ref sug, row, terminePAByEnte);
                return sug;
            }

            return "D";
        }

        private static bool HasPaAlloggioStatus(OutputRow r)
        {
            if (r.RinunciaPA) return false;
            return r.IdoneoPA_Attesa2Assegn || r.VincitorePA || r.VincitorePANoAssegn;
        }

        private bool ComputeDomicilioValido_WithIstanze(
    OutputRow row,
    DateTime aaStart,
    DateTime aaEnd,
    out string reason)
        {
            // normalizzazioni VB già presenti nel tuo codice (le lasci identiche)
            if ((row.CodCorso is "29868" or "29868_1") && row.ComuneDomicilio == "D708")
                row.ComuneDomicilio = row.ComuneSedeStudi;
            if (row.ComuneSedeStudi == "G954" && row.ComuneDomicilio == "L725")
                row.ComuneDomicilio = row.ComuneSedeStudi;

            if (row.CodCorso is "QD132" or "QD133")
            {
                if (row.ComuneSedeStudi == "D539" && row.ComuneDomicilio == "D810")
                    row.ComuneDomicilio = "D539";
            }

            // main (LRS) → ContractInput
            var main = new DomicilioEvaluator.ContractInput(
                CodFiscale: row.CodFiscale,
                ComuneDomicilio: row.ComuneDomicilio,
                TitoloOneroso: row.TitoloOneroso,
                ContrattoEnte: row.ContrattoEnte,
                SerieContratto: row.SerieContratto,
                DataRegistrazioneString: row.DataRegistrazione,
                DataDecorrenzaString: row.DataDecorrenza,
                DataScadenzaString: row.DataScadenza,
                DurataContratto: row.DurataContratto,
                Prorogato: row.Prorogato,
                DurataProroga: row.DurataProroga,
                SerieProroga: row.SerieProroga,
                DenominazioneEnte: row.DenomEnte,
                ImportoRataEnte: row.ImportoRataEnte
            );

            _openIstanzaByCf.TryGetValue(row.CodFiscale, out var istanza);
            _lastRejectByCf.TryGetValue(row.CodFiscale, out var lastWorked);

            // stessa data del controller (se vuoi renderla per-AA, sostituisci qui)
            var deadlineBase = new DateTime(2025, 12, 30, 23, 59, 59, DateTimeKind.Local);

            var eval = DomicilioEvaluator.EvaluateForStatus(
                mainLrs: main,
                openIstanza: istanza,
                aaStart: aaStart,
                aaEnd: aaEnd,
                now: DateTime.Now,
                deadlineBase: deadlineBase,
                lastWorked: lastWorked,
                comuneResidenza: row.ComuneResidenza,
                comuneSedeStudi: row.ComuneSedeStudi,
                areComuniCompatible: AreComuniCompatible,
                requireGeoForStatus: true
            );

            reason = eval.Reason;
            return eval.DomicilioValidoPerStatus;
        }

        private static void AddToMap(Dictionary<string, HashSet<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[key] = set;
            }
            set.Add(value);
        }

        private static string NormStatus(string? s)
        {
            var x = (s ?? "").Trim().ToUpperInvariant();
            return (x is "A" or "B" or "C" or "D") ? x : x;
        }

        private void AppendDecision(OutputRow row, string suggestedStatus, string reason)
        {
            var cur = NormStatus(row.StatusSede);
            var sug = NormStatus(suggestedStatus);

            var isChange = !string.Equals(cur, sug, StringComparison.OrdinalIgnoreCase);
            var prefix = isChange ? "MODIFICA" : "OK";
            var tag = isChange ? $"[{cur}→{sug}]" : $"[{cur}]";

            AppendData($"{prefix} | {tag} {reason}", row, cur, sug);
        }

        private void AppendData(string motivo, OutputRow row, string statoAttuale, string statoSuggerito)
        {
            studentiPendolari.Rows.Add(
                row.CodFiscale,
                row.TitoloOneroso,
                row.SerieContratto,
                row.DataRegistrazione,
                row.DataDecorrenza,
                row.DataScadenza,
                row.DurataContratto,
                row.Prorogato,
                row.DurataProroga,
                row.SerieProroga,
                row.ContrattoEnte,
                row.DenomEnte,
                row.ImportoRataEnte,
                motivo,
                statoAttuale,
                statoSuggerito,
                row.ComuneDomicilio,
                row.ComuneResidenza,
                row.ComuneSedeStudi,
                row.CodSedeStudi,
                row.CodCorso
            );
        }

        private void PreloadProvinces(HashSet<string> comuniNeeded, SqlConnection conn)
        {
            using var cmd = new SqlCommand(@"
IF OBJECT_ID('tempdb..#need') IS NOT NULL DROP TABLE #need;
CREATE TABLE #need (COD_COMUNE VARCHAR(8) NOT NULL PRIMARY KEY);
", conn);
            cmd.ExecuteNonQuery();

            var dt = new DataTable();
            dt.Columns.Add("COD_COMUNE", typeof(string));
            foreach (var c in comuniNeeded)
                if (!string.IsNullOrWhiteSpace(c)) dt.Rows.Add(c.Trim());

            using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.CheckConstraints, null)
            {
                DestinationTableName = "#need",
                BulkCopyTimeout = 600
            })
            {
                bulk.ColumnMappings.Add("COD_COMUNE", "COD_COMUNE");
                bulk.WriteToServer(dt);
            }

            using var read = new SqlCommand(@"
SELECT c.COD_COMUNE, c.COD_PROVINCIA
FROM COMUNI c
JOIN #need n ON n.COD_COMUNE = c.COD_COMUNE;
DROP TABLE #need;
", conn).ExecuteReader();

            while (read.Read())
            {
                var cod = read.GetString(0);
                var prov = read.GetString(1);
                _provCache[cod] = prov;
            }
        }

        private static void ValidateSelectedAA(string aa)
        {
            if (aa.Length != 8 || !aa.All(char.IsDigit))
                throw new ArgumentException("Anno accademico non valido. Atteso formato YYYYYYYY.");
            int start = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int end = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            if (end != start + 1)
                throw new ArgumentException("Anno accademico incoerente. Fine ≠ inizio+1.");
        }

        private static (DateTime aaStart, DateTime aaEnd) GetAaDateRange(string aa)
        {
            int startYear = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int endYear = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            var aaStart = new DateTime(startYear, 10, 1);
            var aaEnd = new DateTime(endYear, 9, 30);
            return (aaStart, aaEnd);
        }

        private static int ComputeCoveredMonths(DateTime dec, DateTime scad, DateTime aaStart, DateTime aaEnd)
        {
            if (dec == default || scad == default) return 0;
            DateTime effectiveStart = dec > aaStart ? dec : aaStart;
            DateTime effectiveEnd = scad < aaEnd ? scad : aaEnd;
            if (effectiveStart > effectiveEnd) return 0;

            int monthsCovered = 0;
            var current = new DateTime(effectiveStart.Year, effectiveStart.Month, 1);
            while (current <= effectiveEnd)
            {
                var monthStart = current;
                var monthEnd = current.AddMonths(1).AddDays(-1);
                var coverageStart = monthStart < effectiveStart ? effectiveStart : monthStart;
                var coverageEnd = monthEnd > effectiveEnd ? effectiveEnd : monthEnd;
                double days = (coverageEnd - coverageStart).TotalDays + 1;
                if (days >= 15) monthsCovered++;
                current = current.AddMonths(1);
            }
            return monthsCovered;
        }

        private static bool ParseDate(string? s, out DateTime dt)
        {
            if (string.IsNullOrWhiteSpace(s)) { dt = default; return false; }
            return DateTime.TryParseExact(s.Trim(), _dateFormats, CultureInfo.InvariantCulture,
                                          DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out dt);
        }

        private static bool AreComuniCompatible(string? c1, string? c2)
        {
            if (string.Equals(c1, c2, StringComparison.OrdinalIgnoreCase)) return true;
            return _comuneCompatGroup.TryGetValue(c1 ?? string.Empty, out int g1) &&
                   _comuneCompatGroup.TryGetValue(c2 ?? string.Empty, out int g2) &&
                   g1 == g2;
        }

        private static bool SerieMatch(string a, string b)
        {
            a = (a ?? "").Trim();
            b = (b ?? "").Trim();
            if (a.Length == 0 || b.Length == 0) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // Telematici / corsi forzati (VB) inclusi nuovi casi >= 20252026
        private static bool IsTelematicoOrForcedA(string aa, string codSede, string fac, string corso)
        {
            aa = (aa ?? "").Trim();
            codSede = (codSede ?? "").Trim().ToUpperInvariant();
            fac = (fac ?? "").Trim().ToUpperInvariant();
            corso = (corso ?? "").Trim().ToUpperInvariant();

            if (codSede is "TM" or "TGM" or "TUN" or "UTU" or "UTM" or "TSR" or "TUM" or "TU" or "TNC")
                return true;

            if (fac == "I" && (corso == "IID" || corso == "IIF" || corso == "IAJ")) return true;
            if (fac == "P" && corso == "ZAM") return true;
            if (fac == "X" && corso == "04301") return true;

            if (codSede == "B" && (corso == "29386" || corso == "29400")) return true;
            if (codSede == "E" && (corso == "129618" || corso == "129618_1" || corso == "12961_1")) return true;
            if (codSede == "J" && (corso == "DPTEA_57" || corso == "ECOLUISS_52")) return true;

            // VB: Cod_SedeStudi="C" AND Cod_Corso_Laurea="04UUTK42" => A
            if (codSede == "C" && corso == "04UUTK42") return true;

            // VB (nuovo): da 20252026
            if (string.Compare(aa, "20252026", StringComparison.Ordinal) >= 0)
            {
                if (codSede == "C" && (corso == "V89_1" || corso == "V86")) return true;
                if (fac == "UUTK" && corso == "V86") return true;
            }

            if (_forceAByCorso.Contains(corso)) return true;

            return false;
        }

        private static bool IsSedePAConsentita(string aa, string comuneSede)
        {
            int y = int.Parse(aa.AsSpan(0, 4), CultureInfo.InvariantCulture);
            var ok = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "H501", "E472" };
            if (y >= 2009) { ok.Add("M082"); ok.Add("C034"); ok.Add("D810"); ok.Add("I838"); }
            return ok.Contains(comuneSede ?? "");
        }

        private static void MaybeDowngradePA(ref string sug, OutputRow r, Dictionary<string, DateTime> terminePAByEnte)
        {
            if (sug != "B") return;
            if (!r.IdoneoPA_Attesa2Assegn) return;
            if (string.IsNullOrWhiteSpace(r.EnteGestione)) return;
            if (!terminePAByEnte.TryGetValue(r.EnteGestione, out var t)) return;
            if (DateTime.Today > t) sug = "D";
        }

        private sealed class DistPairComparer : IEqualityComparer<(string CodSedeDistaccata, string ComuneRes)>
        {
            public bool Equals((string CodSedeDistaccata, string ComuneRes) x, (string CodSedeDistaccata, string ComuneRes) y)
                => string.Equals(x.CodSedeDistaccata, y.CodSedeDistaccata, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ComuneRes, y.ComuneRes, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string CodSedeDistaccata, string ComuneRes) obj)
                => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CodSedeDistaccata) ^
                   StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ComuneRes);
        }
        private sealed class PairComparer : IEqualityComparer<(string ComuneSede, string ComuneRes)>
        {
            public bool Equals((string ComuneSede, string ComuneRes) x, (string ComuneSede, string ComuneRes) y)
                => string.Equals(x.ComuneSede, y.ComuneSede, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ComuneRes, y.ComuneRes, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string ComuneSede, string ComuneRes) obj)
                => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ComuneSede) ^
                   StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ComuneRes);
        }

        private sealed class DomandaRow
        {
            public int NumDomanda { get; set; }
            public string CodFiscale { get; set; } = "";
            public string TipoBando { get; set; } = "";
            public bool IsRichiedentePA { get; set; }
        }

        private sealed class IscrRow
        {
            public string CodTipologia { get; set; } = "";
            public string ComuneSede { get; set; } = "";
            public string CodSedeStudi { get; set; } = "";
            public string CodCorso { get; set; } = "";
            public string CodFacolta { get; set; } = "";
            public string CodSedeDistaccata { get; set; } = "";
        }

        private sealed class LrsDomRow
        {
            public string CodFiscale { get; set; } = "";
            public bool TitoloOneroso { get; set; }
            public string SerieContratto { get; set; } = "";
            public string DataRegistrazione { get; set; } = "";
            public string DataDecorrenza { get; set; } = "";
            public string DataScadenza { get; set; } = "";
            public int DurataContratto { get; set; }
            public bool Proroga { get; set; }
            public int DurataProroga { get; set; }
            public string SerieProroga { get; set; } = "";
            public bool ContrattoEnte { get; set; }
            public string DenomEnte { get; set; } = "";
            public double ImportoRata { get; set; }
            public string CodComuneDom { get; set; } = "";
        }

        private sealed class OutputRow
        {
            public OutputRow(DomandaRow d, string statusSede)
            {
                CodFiscale = d.CodFiscale;
                StatusSede = statusSede;
                RichiedentePA = d.IsRichiedentePA;
            }

            public string CodFiscale { get; set; } = "";
            public string StatusSede { get; set; } = "";

            public bool TitoloOneroso { get; set; }
            public string SerieContratto { get; set; } = "";
            public string DataRegistrazione { get; set; } = "";
            public string DataDecorrenza { get; set; } = "";
            public string DataScadenza { get; set; } = "";
            public int DurataContratto { get; set; }
            public bool Prorogato { get; set; }
            public int DurataProroga { get; set; }
            public string SerieProroga { get; set; } = "";
            public bool ContrattoEnte { get; set; }
            public string DenomEnte { get; set; } = "";
            public double ImportoRataEnte { get; set; }

            public string ComuneDomicilio { get; set; } = "";
            public string ComuneResidenza { get; set; } = "";
            public string ProvinciaRes { get; set; } = "";

            public string ComuneSedeStudi { get; set; } = "";
            public string CodSedeStudi { get; set; } = "";
            public string CodCorso { get; set; } = "";
            public string CodFacolta { get; set; } = "";
            public string CodSedeDistaccata { get; set; } = "";

            public string CodBlocchi { get; set; } = "";

            public bool Detenuto { get; set; }
            public bool RifugiatoPolitico { get; set; }
            public bool ProlungamentoPA { get; set; }
            public bool FamigliaResidenteItalia { get; set; } = true;

            public string ForcedStatus { get; set; } = "";
            public string AnnoAccademico { get; set; } = "";

            public bool RichiedentePA { get; set; }
            public bool PaRichiestaAttiva { get; set; }
            public bool IdoneoPA_Attesa2Assegn { get; set; }
            public bool VincitorePA { get; set; }
            public bool VincitorePANoAssegn { get; set; }
            public bool RinunciaPA { get; set; }
            public string EnteGestione { get; set; } = "";
        }

        private static class DomicilioUtils
        {
            public static bool IsValidSerie(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return false;
                var x = s.Trim();
                return x.Length >= 3;
            }
        }
    }
}
