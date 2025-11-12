// ControlloStatusSede.cs — versione completa con RunProcedure ripristinato e logiche VB integrate
// Solo logica. Dipendenze attese: BaseProcedure<T>, ArgsControlloStatusSede, MasterForm, Logger, Utilities.

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
            // VB + estensioni telematiche/casi storici
            "29386","29400","129618","129618_1","12961_1","DPTEA_57","ECOLUISS_52","04UUTK42",
            // facoltà-corso storici (replicati via helper IsTelematicoOrForcedA)
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
            var iscr = new Dictionary<int, (string CodTipol, string ComuneSede, string CodSedeStudi, string CodCorso, string CodFacolta)>();
            var resComune = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var resProv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lrs = new Dictionary<string, LrsDomRow>(StringComparer.OrdinalIgnoreCase);
            var codBlocchi = new Dictionary<int, string>();
            var nucleo = new Dictionary<int, (int NumComp, int ConvEstero, string TipoNucleo)>();
            var tipRed = new Dictionary<int, (string? Integr, string? Origine)>();
            var inSedeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var pendolariMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var fuoriSedeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var inSedeDistPairs = new HashSet<(string ComuneSede, string ComuneRes)>(new PairComparer());
            var prolungamentiCF = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var terminePAByEnte = new Dictionary<string, DateTime>();

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
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
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
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) vv[rd.GetInt32(0)] = Utilities.SafeGetString(rd, 1);

                cmd.CommandText = @"
SELECT CAST(e.Num_domanda AS INT) AS Num_domanda
FROM vEsiti_concorsiBS e
JOIN #domande t ON t.Num_domanda = e.Num_domanda
WHERE e.Anno_accademico = @AA AND e.Cod_tipo_esito <> 0;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) esitiBS.Add(rd.GetInt32(0));

                cmd.CommandText = @"
SELECT CAST(e.Num_domanda AS INT) AS Num_domanda
FROM vEsiti_concorsiPA e
JOIN #domande t ON t.Num_domanda = e.Num_domanda
WHERE e.Anno_accademico = @AA AND e.Cod_tipo_esito <> 0;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) esitiPA.Add(rd.GetInt32(0));

                cmd.CommandText = @"
SELECT CAST(d.Num_domanda AS INT) AS Num_domanda,
       COALESCE(v.Rifug_politico,0) AS Rifugiato,
       0
FROM #domande d
JOIN vDATIGENERALI_dom v ON v.Anno_accademico = @AA AND v.Num_domanda = d.Num_domanda;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        dg[rd.GetInt32(0)] = (Utilities.SafeGetInt(rd, 1) == 1, Utilities.SafeGetInt(rd, 2) == 1);

                cmd.CommandText = @"
SELECT CAST(d.Num_domanda AS INT) AS Num_domanda,
       i.Cod_tipologia_studi,
       cl.Comune_Sede_studi,
       i.Cod_sede_studi,
       i.Cod_corso_laurea,
       i.Cod_facolta
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
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        iscr[rd.GetInt32(0)] = (Utilities.SafeGetString(rd, 1),
                                                Utilities.SafeGetString(rd, 2),
                                                Utilities.SafeGetString(rd, 3),
                                                Utilities.SafeGetString(rd, 4),
                                                Utilities.SafeGetString(rd, 5));

                cmd.CommandText = @"
SELECT DISTINCT d.Cod_fiscale, r.Cod_comune, r.provincia_residenza
FROM #domande d
JOIN vResidenza r ON r.Cod_fiscale = d.Cod_fiscale AND r.Anno_accademico = @AA;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var cf = Utilities.SafeGetString(rd, 0);
                        resComune[cf] = Utilities.SafeGetString(rd, 1);
                        resProv[cf] = Utilities.SafeGetString(rd, 2);
                    }
                }

                // Forzature: carica status destinazione (logica VB)
                cmd.CommandText = @"
SELECT DISTINCT f.Cod_Fiscale, f.Status_sede
FROM Forzature_StatusSede f
JOIN #domande d ON d.Cod_fiscale = f.Cod_Fiscale
WHERE f.Anno_Accademico = @AA
  AND f.Data_fine_validita IS NULL
  AND f.Status_sede IN ('A','B','C','D');";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
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
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
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
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
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
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
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
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
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

                cmd.CommandText = @"SELECT cod_sede_studi, Cod_comune FROM COMUNI_FUORISEDE;";
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        AddToMap(fuoriSedeMap, Utilities.SafeGetString(rd, 0), Utilities.SafeGetString(rd, 1));

                cmd.CommandText = @"
SELECT S.COD_COMUNE as ComuneSede, C.COD_COMUNE as ComuneRes
FROM COMUNI_INSEDE C
JOIN SEDI_DISTACCATE S ON C.COD_SEDE_DISTACCATA = S.COD_SEDE_DISTACCATA
WHERE C.COD_SEDE_DISTACCATA <> '00000';";
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        inSedeDistPairs.Add((Utilities.SafeGetString(rd, 0), Utilities.SafeGetString(rd, 1)));

                cmd.CommandText = @"
SELECT DISTINCT Cod_Fiscale
FROM Prolungamenti_posto_alloggio
WHERE anno_accademico = @AA AND data_fine_validita IS NULL;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) prolungamentiCF.Add(Utilities.SafeGetString(rd, 0));

                cmd.CommandText = @"
SELECT cod_ente, data_termine_ass_pa
FROM termine_assegnazione_pa
WHERE anno_accademico = @AA;";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("@AA", System.Data.SqlDbType.VarChar, 8).Value = selectedAA;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                    {
                        var ente = Utilities.SafeGetString(rd, 0);
                        if (DateTime.TryParse(Utilities.SafeGetString(rd, 1), out var dt))
                            terminePAByEnte[ente] = dt;
                    }

                // Precarica province per tutti i comuni usati — senza parametri, via temp table
                var comuniNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in resComune.Values) if (!string.IsNullOrWhiteSpace(v)) comuniNeeded.Add(v);
                foreach (var v in iscr.Values) if (!string.IsNullOrWhiteSpace(v.ComuneSede)) comuniNeeded.Add(v.ComuneSede);
                PreloadProvinces(comuniNeeded, CONNECTION);


                cmd.CommandText = "DROP TABLE #domande;";
                cmd.Parameters.Clear();
                cmd.ExecuteNonQuery();

                var inSedeListB = inSedeMap.TryGetValue("B", out var setB) ? setB : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var d in domande)
                {
                    if (!vv.TryGetValue(d.NumDomanda, out var statusSede)) statusSede = "0";
                    if (!esitiBS.Contains(d.NumDomanda)) continue;    // solo BS
                    if (esitiPA.Contains(d.NumDomanda)) continue;     // escludi se ha esito PA
                    if (!iscr.TryGetValue(d.NumDomanda, out var iscrRow)) continue;

                    var outRow = new OutputRow(d, statusSede)
                    {
                        ComuneResidenza = (resComune.TryGetValue(d.CodFiscale, out var cr) ? cr : "").Trim(),
                        ProvinciaRes = (resProv.TryGetValue(d.CodFiscale, out var pr) ? pr : "").Trim(),
                        ComuneSedeStudi = iscrRow.ComuneSede?.Trim() ?? "",
                        CodSedeStudi = iscrRow.CodSedeStudi?.Trim() ?? "",
                        CodCorso = iscrRow.CodCorso?.Trim() ?? "",
                        CodFacolta = iscrRow.CodFacolta?.Trim() ?? "",
                        CodBlocchi = codBlocchi.TryGetValue(d.NumDomanda, out var cb) ? cb : "",
                        ProlungamentoPA = prolungamentiCF.Contains(d.CodFiscale),
                        AnnoAccademico = selectedAA,
                        RichiedentePA = d.IsRichiedentePA
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
                    if (tipRed.TryGetValue(d.NumDomanda, out var tr))
                    {
                        var useIntegr = (nucleo.TryGetValue(d.NumDomanda, out var nx) && string.Equals(nx.TipoNucleo, "I", StringComparison.OrdinalIgnoreCase));
                        var flag = (useIntegr ? tr.Integr : tr.Origine) ?? "";
                        if (string.Equals(flag, "EE", StringComparison.OrdinalIgnoreCase)) famigliaItalia = false;
                    }
                    outRow.FamigliaResidenteItalia = famigliaItalia;

                    ApplySedeOverrides(outRow); // distaccate + corsi → comune sede o forced A

                    EvaluateAndAppend(outRow, aaStart, aaEnd, inSedeMap, inSedeListB, pendolariMap, fuoriSedeMap, inSedeDistPairs, terminePAByEnte);
                }
            }
        }

        private void EvaluateAndAppend(
            OutputRow row,
            DateTime aaStart,
            DateTime aaEnd,
            Dictionary<string, HashSet<string>> inSedeMap,
            HashSet<string> inSedeListB,
            Dictionary<string, HashSet<string>> pendolariMap,
            Dictionary<string, HashSet<string>> fuoriSedeMap,
            HashSet<(string ComuneSede, string ComuneRes)> inSedeDistPairs,
            Dictionary<string, DateTime> terminePAByEnte)
        {
            // 0) forzatura tabellare
            if (_forcedStatusByCF.TryGetValue(row.CodFiscale, out var forced))
            {
                AppendData("Forzatura tabellare", row, row.StatusSede, forced);
                return;
            }

            // 1) detenuto → A
            if (row.Detenuto)
            {
                AppendData("Speciale: detenuto → A", row, row.StatusSede, "A");
                return;
            }

            // 2) rifugiato → B
            if (row.RifugiatoPolitico)
            {
                AppendData("Speciale: rifugiato politico → B", row, row.StatusSede, "B");
                return;
            }

            // 3) telematici/corsi forzati → A
            if (row.ForcedStatus == "A" || IsTelematicoOrForcedA(row.CodSedeStudi, row.CodFacolta, row.CodCorso))
            {
                AppendData("Telematico/corso forzato → A", row, row.StatusSede, "A");
                return;
            }

            // 4) nucleo estero prevalente o provincia estera → B
            if (!row.FamigliaResidenteItalia || row.ProvinciaRes == "EE")
            {
                AppendData("Nucleo estero prevalente → B", row, row.StatusSede, "B");
                return;
            }

            // 5) residenza == sede → A
            if (!string.IsNullOrEmpty(row.ComuneResidenza) &&
                row.ComuneResidenza.Equals(row.ComuneSedeStudi, StringComparison.OrdinalIgnoreCase))
            {
                AppendData("OK | [→A] res==sede|prio1", row, row.StatusSede, "A");
                return;
            }

            // 6) inSede tabellare (sedi A/O/P/D con liste proprie, altrimenti uso B + distaccate)
            bool resInSedeList;
            string srcInSede;
            switch ((row.CodSedeStudi ?? "").ToUpperInvariant())
            {
                case "A":
                case "O":
                case "P":
                case "D":
                    resInSedeList = InSet(inSedeMap, row.CodSedeStudi, row.ComuneResidenza);
                    srcInSede = resInSedeList ? $"list:{row.CodSedeStudi}" : "";
                    break;
                default:
                    var dist = inSedeDistPairs.Contains((row.ComuneSedeStudi, row.ComuneResidenza));
                    var listB = inSedeListB.Contains(row.ComuneResidenza);
                    resInSedeList = dist || listB;
                    srcInSede = dist ? "distaccata" : (listB ? "list:B" : "");
                    break;
            }
            if (resInSedeList)
            {
                AppendData($"MODIFICA | [{row.StatusSede}→A] insede:{srcInSede}→A|prio2", row, row.StatusSede, "A");
                return;
            }

            // 7) province speciali: Viterbo e Cassino → C
            if (IsSameProvince(row.ComuneResidenza, row.ComuneSedeStudi, out var _, out var provSede))
            {
                if (row.ComuneSedeStudi == "H501" && provSede == "VT")
                {
                    AppendData("Viterbo: provincia VT → C", row, row.StatusSede, "C");
                    return;
                }
                if (row.ComuneSedeStudi is "C034" or "D810" or "I838")
                {
                    AppendData("Cassino: stessa provincia → C", row, row.StatusSede, "C");
                    return;
                }
            }

            // 8) PA: sede consentita → B
            if (row.RichiedentePA && IsSedePAConsentita(row.AnnoAccademico, row.ComuneSedeStudi))
            {
                var sugPA = "B";
                MaybeDowngradePA(ref sugPA, row, terminePAByEnte);
                AppendData($"PA: sede consentita → {sugPA}", row, row.StatusSede, sugPA);
                return;
            }

            // 9) fuoriSede tabellare → B
            bool resInFuoriList = InSet(fuoriSedeMap, NormalizeFuoriKey(row.CodSedeStudi, row.ComuneSedeStudi), row.ComuneResidenza);
            if (resInFuoriList)
            {
                var sug = "B";
                MaybeDowngradePA(ref sug, row, terminePAByEnte);
                AppendData($"MODIFICA | [{row.StatusSede}→{sug}] fuori:list→{sug}|prio3", row, row.StatusSede, sug);
                return;
            }

            // 10) pendolari tabellare
            bool resInPendList = InSet(pendolariMap, NormalizePendKey(row.CodSedeStudi, row.ComuneSedeStudi), row.ComuneResidenza);

            // normalizzazioni puntuali VB su domicilio
            if ((row.CodCorso is "29868" or "29868_1") && row.ComuneDomicilio == "D708")
                row.ComuneDomicilio = row.ComuneSedeStudi;
            if (row.ComuneSedeStudi == "G954" && row.ComuneDomicilio == "L725")
                row.ComuneDomicilio = row.ComuneSedeStudi;

            // geo e contratto
            bool domEqResCompat = AreComuniCompatible(row.ComuneDomicilio, row.ComuneResidenza);
            bool domEqSede = string.Equals(row.ComuneDomicilio, row.ComuneSedeStudi, StringComparison.OrdinalIgnoreCase);

            ParseDate(row.DataDecorrenza, out DateTime dataDec);
            ParseDate(row.DataScadenza, out DateTime dataScad);
            int mesiCoperti = ComputeCoveredMonths(dataDec, dataScad, aaStart, aaEnd);
            bool durataOk = mesiCoperti >= 10;
            bool titoloOnerosoOk = row.TitoloOneroso && durataOk;

            bool contrattoEnteValido = row.ContrattoEnte &&
                                       !string.IsNullOrWhiteSpace(row.DenomEnte) &&
                                       row.DurataContratto >= 10 &&
                                       row.ImportoRataEnte > 0;

            bool serieContrattoValida = DomicilioUtils.IsValidSerie(row.SerieContratto);
            bool serieProrogaValida = DomicilioUtils.IsValidSerie(row.SerieProroga);
            if (!string.IsNullOrEmpty(row.SerieContratto) &&
                !string.IsNullOrEmpty(row.SerieProroga) &&
                SerieMatch(row.SerieProroga, row.SerieContratto))
            {
                // stessa serie: proroga non valida
                serieProrogaValida = false;
            }

            bool contrattoValido = contrattoEnteValido || serieContrattoValida;
            bool geoOk = !domEqResCompat && domEqSede; // domicilio deve coincidere con sede e non con residenza

            // pendolari
            if (resInPendList)
            {
                bool domicilioValido = titoloOnerosoOk && contrattoValido && geoOk && (!(row.Prorogato && !serieProrogaValida));
                if (domicilioValido)
                {
                    var sug = "B";
                    MaybeDowngradePA(ref sug, row, terminePAByEnte);
                    AppendData($"MODIFICA | [{row.StatusSede}→{sug}] pend:list + contratto10m→{sug}|prio4", row, row.StatusSede, sug);
                }
                else
                {
                    var sug = string.IsNullOrWhiteSpace(row.ComuneDomicilio) || !row.TitoloOneroso ? "D" : "C";
                    AppendData($"MODIFICA | [{row.StatusSede}→{sug}] pendolare (contratto insufficiente)|prio4", row, row.StatusSede, sug);
                }
                return;
            }

            // fallback: contratto + geo
            {
                bool domicilioValido = titoloOnerosoOk && contrattoValido && geoOk && (!(row.Prorogato && !serieProrogaValida));
                if (domicilioValido)
                {
                    var sug = "B";
                    MaybeDowngradePA(ref sug, row, terminePAByEnte);
                    AppendData($"MODIFICA | [{row.StatusSede}→{sug}] geoOK + contratto10m→{sug}|prio5", row, row.StatusSede, sug);
                }
                else
                {
                    AppendData($"MODIFICA | [{row.StatusSede}→D] nessuna lista, contratto insufficiente → D|prio5", row, row.StatusSede, "D");
                }
            }
        }

        private static string NormalizePendKey(string codSede, string comuneSede) => codSede ?? "B";
        private static string NormalizeFuoriKey(string codSede, string comuneSede) => codSede ?? "B";

        private static bool InSet(Dictionary<string, HashSet<string>> map, string sede, string comune)
            => map.TryGetValue(sede ?? "", out var set) && set.Contains(comune ?? "");

        private static void AddToMap(Dictionary<string, HashSet<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[key] = set;
            }
            set.Add(value);
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

            // Bulk copy dei comuni richiesti
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

            // Join su COMUNI per ottenere le province
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

        private bool IsSameProvince(string comune1, string comune2, out string prov1, out string prov2)
        {
            prov1 = _provCache.TryGetValue(comune1 ?? "", out var a) ? a : "";
            prov2 = _provCache.TryGetValue(comune2 ?? "", out var b) ? b : "";
            return !string.IsNullOrEmpty(prov1) && prov1.Equals(prov2, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTelematicoOrForcedA(string codSede, string fac, string corso)
        {
            codSede = (codSede ?? "").Trim().ToUpperInvariant();
            fac = (fac ?? "").Trim().ToUpperInvariant();
            corso = (corso ?? "").Trim().ToUpperInvariant();

            // Sedi/codici telematici
            if (codSede is "TM" or "TGM" or "TUN" or "UTU" or "UTM" or "TSR" or "TUM" or "TU" or "TNC")
                return true;

            // Facoltà-corso specifici
            if (fac == "I" && (corso == "IID" || corso == "IIF" || corso == "IAJ")) return true;
            if (fac == "P" && corso == "ZAM") return true;
            if (fac == "X" && corso == "04301") return true;

            // Combinazioni specifiche per sede
            if (codSede == "B" && (corso == "29386" || corso == "29400")) return true;
            if (codSede == "E" && (corso == "129618" || corso == "129618_1" || corso == "12961_1")) return true;
            if (codSede == "J" && (corso == "DPTEA_57" || corso == "ECOLUISS_52")) return true;

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

        private void ApplySedeOverrides(OutputRow row)
        {
            // Cassino
            if (row.CodSedeStudi.Equals("A", StringComparison.OrdinalIgnoreCase) ||
                row.CodSedeStudi.Equals("O", StringComparison.OrdinalIgnoreCase))
            {
                if (_distCassino.TryGetValue(row.CodCorso, out var com)) row.ComuneSedeStudi = com;
                // telematici/casi forzati eventualmente marcati sotto
            }

            // Tor Vergata
            if (row.CodSedeStudi.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                if (_distTorVergata.TryGetValue(row.CodCorso, out var com)) row.ComuneSedeStudi = com;
                if (row.CodCorso.Equals("04UUTK42", StringComparison.OrdinalIgnoreCase)) row.ForcedStatus = "A";
            }

            // San Pio V
            if (row.CodSedeStudi.Equals("W", StringComparison.OrdinalIgnoreCase))
            {
                if (_distSanPioV.TryGetValue(row.CodCorso, out var com)) row.ComuneSedeStudi = com;
            }

            // LUMSA
            if (row.CodSedeStudi.Equals("K", StringComparison.OrdinalIgnoreCase))
            {
                if (_distLumsa.TryGetValue(row.CodCorso, out var com)) row.ComuneSedeStudi = com;
            }

            // Latina per PA/corsi specifici
            if (row.RichiedentePA && row.ComuneSedeStudi == "G698" &&
                (row.CodCorso.Equals("CFC", StringComparison.OrdinalIgnoreCase) ||
                 row.CodCorso.Equals("29875", StringComparison.OrdinalIgnoreCase) ||
                 row.CodCorso.Equals("29875_1", StringComparison.OrdinalIgnoreCase)))
            {
                row.ComuneSedeStudi = "E472";
            }
            if (row.ComuneSedeStudi == "G698" &&
                (row.CodCorso.Equals("29875", StringComparison.OrdinalIgnoreCase) ||
                 row.CodCorso.Equals("29875_1", StringComparison.OrdinalIgnoreCase)))
            {
                row.ComuneSedeStudi = "E472";
            }
            if (string.Compare(row.AnnoAccademico, "20092010", StringComparison.Ordinal) >= 0 &&
                row.RichiedentePA && row.ComuneSedeStudi == "F499")
            {
                row.ComuneSedeStudi = "M082";
            }

            // Telematici / corsi forzati sempre in sede
            if (IsTelematicoOrForcedA(row.CodSedeStudi, row.CodFacolta, row.CodCorso))
                row.ForcedStatus = "A";
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
            public string CodBlocchi { get; set; } = "";
            public bool Detenuto { get; set; }
            public bool RifugiatoPolitico { get; set; }
            public bool ProlungamentoPA { get; set; }
            public bool FamigliaResidenteItalia { get; set; } = true;
            public string ForcedStatus { get; set; } = "";
            public string AnnoAccademico { get; set; } = "";
            public bool RichiedentePA { get; set; }

            // campi opzionali PA per downgrade
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
                if (x.Length < 3) return false;
                return true;
            }
        }
    }
}
