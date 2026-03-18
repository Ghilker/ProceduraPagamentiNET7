using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    /// <summary>
    /// Verifica aggregata: usa i risultati object-first dei singoli controlli,
    /// mantenendo i DataTable solo per export/debug.
    /// </summary>
    internal sealed class Verifica : BaseProcedure<ArgsVerifica>
    {
        public Verifica(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        private string _aa = "";
        private string _folderPath = "";

        public IReadOnlyList<ValutazioneVerifica> OutputVerificaList { get; private set; } = Array.Empty<ValutazioneVerifica>();

        public DataTable OutputVerifica { get; private set; } = BuildOutputTable();



        public IReadOnlyList<StudenteInfo> StudentiInfoList { get; private set; } = Array.Empty<StudenteInfo>();
        public override void RunProcedure(ArgsVerifica args)
        {
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            // Lettura parametri senza vincoli di compile-time su ArgsVerifica
            _aa = GetStringArg(args, " _selectedAA", "_selectedAA", "AA", "AnnoAccademico", fallback: "20252026").Trim();
            _folderPath = GetStringArg(args, "_folderPath", "FolderPath", "test","Path", fallback: "D://").Trim();

            // Opzionale: lista CF (se presente in ArgsVerifica) => Verifica su subset
            var cfFilter = GetStringListArg(args, "_codiciFiscali", "CodiciFiscali", "CodiciFiscale", "CF");

            // Opzionale: include esclusi / non trasmesse (se presenti in ArgsVerifica)
            bool includeEsclusi = GetBoolArg(args, "_includeEsclusi", "IncludeEsclusi", fallback: true);
            bool includeNonTrasmesse = GetBoolArg(args, "_includeNonTrasmesse", "IncludeNonTrasmesse", fallback: true);

            // 0) Verifica decide quali studenti lavorare (source of truth)
            var candidates = LoadStatusSedeCandidates(CONNECTION, _aa, includeEsclusi, includeNonTrasmesse);

            if (cfFilter != null && cfFilter.Count > 0)
            {
                var set = new HashSet<string>(
                    cfFilter.Select(NormalizeCf),
                    StringComparer.OrdinalIgnoreCase);

                candidates = candidates
                    .Where(c => set.Contains(c.CodFiscale))
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                OutputVerificaList = Array.Empty<ValutazioneVerifica>();
                OutputVerifica = BuildOutputTable();
                Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath);
                return;
            }

            var candidateKeys = new HashSet<StudentKey>(
                candidates.Select(c => new StudentKey(c.CodFiscale, c.NumDomanda.ToString(CultureInfo.InvariantCulture))),
                default);

            var candidateCfs = candidates
                .Select(c => c.CodFiscale)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 0.1) Lista StudenteInfo "source of truth" (Verifica-owned). Verrà arricchita dalle procedure a cascata.
            var studentiInfo = candidates
                .Select(c =>
                {
                    var info = new StudenteInfo();
                    info.InformazioniPersonali.CodFiscale = c.CodFiscale;
                    info.InformazioniPersonali.NumDomanda = c.NumDomanda.ToString(CultureInfo.InvariantCulture);
                    return info;
                })
                .ToList();

            StudentiInfoList = studentiInfo;

            // Dizionario per CF (economici lavora per CF). In caso di duplicati CF, prende la prima occorrenza.
            var infoByCf = studentiInfo
                .GroupBy(i => NormalizeCf(i.InformazioniPersonali.CodFiscale), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 1) Controllo economici: calcolo e valutazione (filtrato su CF candidati)
            var controlloEconomici = new ProcedureNet7.ProceduraControlloDatiEconomici(CONNECTION);
            controlloEconomici.Compute(_aa, candidateCfs, infoByCf);

            var economiciList = controlloEconomici.OutputEconomiciList
                .Where(e => candidateKeys.Contains(e.Key))
                .ToList();

            // Dizionario per chiave (CF + NumDomanda) da passare al controllo status sede.
            var economiciByKey = economiciList
                .GroupBy(e => e.Key)
                .ToDictionary(g => g.Key, g => g.First());

            // 2) Controllo status sede: usa i candidati decisi da Verifica via temp table
            const string tempCandidates = "#SS_Candidates";
            CreateTempCandidatesTable(CONNECTION, tempCandidates);

            try
            {
                BulkCopyCandidates(CONNECTION, tempCandidates, candidates);

                var controlloStatusSede = new ProcedureNet7.ControlloStatusSede(CONNECTION);
                var statusSedeList = controlloStatusSede.ComputeListFromTempCandidates(
                    _aa,
                    tempCandidatesTable: tempCandidates,
                    iseeByKey: economiciByKey);

                // 3) Merge object-first per uso in altre strutture
                OutputVerificaList = MergeLists(economiciList, statusSedeList);

                // 4) DataTable solo per export/debug
                OutputVerifica = ToDataTable(OutputVerificaList);

                Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath);
            }
            finally
            {
                DropTempCandidatesTable(CONNECTION, tempCandidates);
            }
        }

        private sealed class StatusSedeCandidate
        {
            public int NumDomanda { get; init; }
            public string CodFiscale { get; init; } = "";
            public string TipoBando { get; init; } = "";
            public int CodTipoEsitoBS { get; init; }
            public int StatusCompilazione { get; init; }
        }

        private static string NormalizeCf(string? cf)
            => (cf ?? "").Trim().ToUpperInvariant();

        private const string StatusSedeCandidatesSql = @"
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
SC AS
(
    SELECT
        CAST(v.Num_domanda AS INT) AS NumDomanda,
        CAST(ISNULL(v.status_compilazione,0) AS INT) AS StatusCompilazione
    FROM vstatus_compilazione v
    WHERE v.anno_accademico = @AA
),
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
)
SELECT
    NumDomanda,
    CodFiscale,
    TipoBando,
    CodTipoEsitoBS,
    StatusCompilazione
FROM D
ORDER BY CodFiscale, NumDomanda;
";

        private static List<StatusSedeCandidate> LoadStatusSedeCandidates(
            SqlConnection conn,
            string aa,
            bool includeEsclusi,
            bool includeNonTrasmesse)
        {
            var list = new List<StatusSedeCandidate>(capacity: 50_000);

            using var cmd = new SqlCommand(StatusSedeCandidatesSql, conn)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };

            cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;
            cmd.Parameters.Add("@IncludeEsclusi", SqlDbType.Bit).Value = includeEsclusi;
            cmd.Parameters.Add("@IncludeNonTrasmesse", SqlDbType.Bit).Value = includeNonTrasmesse;

            Logger.LogInfo(null, $"[Verifica] Estrazione candidati StatusSede | AA={aa} | IncludeEsclusi={includeEsclusi} | IncludeNonTrasmesse={includeNonTrasmesse}");

            using var reader = cmd.ExecuteReader();

            int read = 0;
            while (reader.Read())
            {
                list.Add(new StatusSedeCandidate
                {
                    NumDomanda = reader.SafeGetInt("NumDomanda"),
                    CodFiscale = NormalizeCf(reader.SafeGetString("CodFiscale")),
                    TipoBando = (reader.SafeGetString("TipoBando") ?? "").Trim(),
                    CodTipoEsitoBS = reader.SafeGetInt("CodTipoEsitoBS"),
                    StatusCompilazione = reader.SafeGetInt("StatusCompilazione")
                });

                read++;
                if (read % 5000 == 0)
                    Logger.LogInfo(null, $"[Verifica] Candidati letti... {read}");
            }

            Logger.LogInfo(null, $"[Verifica] Candidati StatusSede letti: {read}");
            return list;
        }

        private static void CreateTempCandidatesTable(SqlConnection conn, string tempTableName)
        {
            string sql = $@"
IF OBJECT_ID('tempdb..{tempTableName}') IS NOT NULL
    DROP TABLE {tempTableName};

CREATE TABLE {tempTableName}
(
    NumDomanda INT NOT NULL,
    CodFiscale NVARCHAR(32) NOT NULL,
    TipoBando NVARCHAR(16) NOT NULL,
    CodTipoEsitoBS INT NOT NULL,
    StatusCompilazione INT NOT NULL
);";

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 9999999 };
            cmd.ExecuteNonQuery();
        }

        private static void DropTempCandidatesTable(SqlConnection conn, string tempTableName)
        {
            string sql = $"IF OBJECT_ID('tempdb..{tempTableName}') IS NOT NULL DROP TABLE {tempTableName};";
            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 9999999 };
            cmd.ExecuteNonQuery();
        }

        private static void BulkCopyCandidates(SqlConnection conn, string tempTableName, List<StatusSedeCandidate> candidates)
        {
            var dt = new DataTable();
            dt.Columns.Add("NumDomanda", typeof(int));
            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("TipoBando", typeof(string));
            dt.Columns.Add("CodTipoEsitoBS", typeof(int));
            dt.Columns.Add("StatusCompilazione", typeof(int));

            foreach (var c in candidates)
                dt.Rows.Add(c.NumDomanda, c.CodFiscale, c.TipoBando ?? "", c.CodTipoEsitoBS, c.StatusCompilazione);

            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, externalTransaction: null)
            {
                DestinationTableName = tempTableName,
                BulkCopyTimeout = 9999999,
                BatchSize = 5000
            };

            bulk.ColumnMappings.Add("NumDomanda", "NumDomanda");
            bulk.ColumnMappings.Add("CodFiscale", "CodFiscale");
            bulk.ColumnMappings.Add("TipoBando", "TipoBando");
            bulk.ColumnMappings.Add("CodTipoEsitoBS", "CodTipoEsitoBS");
            bulk.ColumnMappings.Add("StatusCompilazione", "StatusCompilazione");

            Logger.LogInfo(null, $"[Verifica] BulkCopy candidati -> {tempTableName} | Righe={dt.Rows.Count}");
            bulk.WriteToServer(dt);
        }


        private static string GetStringArg(object args, string n1, string n2, string n3, string n4, string fallback)
        {
            foreach (var name in new[] { n1, n2, n3, n4 })
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = args.GetType().GetProperty(name.Trim(), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop == null) continue;

                var value = prop.GetValue(args);
                if (value is string s && !string.IsNullOrWhiteSpace(s))
                    return s;

                if (value != null)
                    return value.ToString() ?? fallback;
            }
            return fallback;
        }

        private static bool GetBoolArg(object args, string n1, string n2, bool fallback)
        {
            foreach (var name in new[] { n1, n2 })
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = args.GetType().GetProperty(name.Trim(), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop == null) continue;

                var value = prop.GetValue(args);
                if (value is bool b) return b;
                if (value is string s && bool.TryParse(s, out var parsed)) return parsed;
                if (value is int i) return i != 0;
            }
            return fallback;
        }

        private static IReadOnlyCollection<string>? GetStringListArg(object args, params string[] names)
        {
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = args.GetType().GetProperty(name.Trim(), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop == null) continue;

                var value = prop.GetValue(args);
                if (value == null) continue;

                if (value is IReadOnlyCollection<string> roc) return roc;
                if (value is IEnumerable<string> e) return e.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                // singola stringa "CF1;CF2;..."
                if (value is string s)
                {
                    var parts = s.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(x => x.Trim())
                                 .Where(x => x.Length > 0)
                                 .ToList();
                    return parts.Count > 0 ? parts : null;
                }
            }
            return null;
        }

        private static List<ValutazioneVerifica> MergeLists(
            IReadOnlyList<ValutazioneEconomici> economici,
            IReadOnlyList<ValutazioneStatusSede> statusSede)
        {
            var econByKey = economici
                .GroupBy(e => e.Key)
                .ToDictionary(g => g.Key, g => g.First());

            var sedeByKey = statusSede
                .GroupBy(s => s.Key)
                .ToDictionary(g => g.Key, g => g.First());

            var allKeys = new HashSet<StudentKey>(econByKey.Keys);
            allKeys.UnionWith(sedeByKey.Keys);

            var merged = new List<ValutazioneVerifica>(allKeys.Count);

            foreach (var key in allKeys.OrderBy(k => k.CodFiscale).ThenBy(k => k.NumDomanda))
            {
                econByKey.TryGetValue(key, out var eco);
                sedeByKey.TryGetValue(key, out var sede);

                var info = sede?.Info ?? eco?.Info ?? new StudenteInfo();
                info.InformazioniPersonali.CodFiscale = key.CodFiscale;
                info.InformazioniPersonali.NumDomanda = key.NumDomanda;

                if (sede != null)
                    info.InformazioniSede.StatusSedeSuggerito = sede.StatoSuggerito;

                merged.Add(new ValutazioneVerifica
                {
                    Info = info,
                    Economici = eco,
                    StatusSede = sede
                });
            }

            return merged;
        }

        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable("Verifica");

            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("NumDomanda", typeof(string));

            // Economici / ISEE
            dt.Columns.Add("TipoRedditoOrigine", typeof(string));
            dt.Columns.Add("TipoRedditoIntegrazione", typeof(string));
            dt.Columns.Add("CodTipoEsitoBS", typeof(int));

            dt.Columns.Add("ISR", typeof(decimal));
            dt.Columns.Add("ISP", typeof(decimal));
            dt.Columns.Add("Detrazioni", typeof(decimal));

            dt.Columns.Add("ISEDSU", typeof(decimal));
            dt.Columns.Add("ISEEDSU", typeof(decimal));
            dt.Columns.Add("ISPEDSU", typeof(decimal));

            dt.Columns.Add("ISPDSU", typeof(decimal));
            dt.Columns.Add("SEQ", typeof(decimal));

            dt.Columns.Add("ISEDSU_Attuale", typeof(decimal));
            dt.Columns.Add("ISEEDSU_Attuale", typeof(decimal));
            dt.Columns.Add("ISPEDSU_Attuale", typeof(decimal));
            dt.Columns.Add("ISPDSU_Attuale", typeof(decimal));
            dt.Columns.Add("SEQ_Attuale", typeof(decimal));

            // Status sede
            dt.Columns.Add("StatusSedeAttuale", typeof(string));
            dt.Columns.Add("StatusSedeSuggerito", typeof(string));
            dt.Columns.Add("MotivoStatusSede", typeof(string));

            dt.Columns.Add("ComuneResidenza", typeof(string));
            dt.Columns.Add("ProvinciaResidenza", typeof(string));

            dt.Columns.Add("ComuneSedeStudi", typeof(string));
            dt.Columns.Add("ProvinciaSede", typeof(string));

            dt.Columns.Add("ComuneDomicilio", typeof(string));

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

        private static DataTable ToDataTable(IReadOnlyList<ValutazioneVerifica> list)
        {
            var dt = BuildOutputTable();

            foreach (var v in list)
            {
                var info = v.Info;
                var eco = v.Economici;
                var sede = v.StatusSede;
                var dom = info.InformazioniSede.Domicilio;

                var row = dt.NewRow();

                row["CodFiscale"] = info.InformazioniPersonali.CodFiscale;
                row["NumDomanda"] = info.InformazioniPersonali.NumDomanda;

                if (eco != null)
                {
                    row["TipoRedditoOrigine"] = eco.TipoRedditoOrigine;
                    row["TipoRedditoIntegrazione"] = eco.TipoRedditoIntegrazione;
                    row["CodTipoEsitoBS"] = eco.CodTipoEsitoBS ?? 0;

                    row["ISR"] = eco.ISR;
                    row["ISP"] = eco.ISP;
                    row["Detrazioni"] = eco.Detrazioni;

                    row["ISEDSU"] = eco.ISEDSU;
                    row["ISEEDSU"] = eco.ISEEDSU;
                    row["ISPEDSU"] = eco.ISPEDSU;

                    row["ISPDSU"] = eco.ISPDSU;
                    row["SEQ"] = eco.SEQ;

                    row["ISEDSU_Attuale"] = eco.ISEDSU_Attuale ?? 0m;
                    row["ISEEDSU_Attuale"] = eco.ISEEDSU_Attuale ?? 0m;
                    row["ISPEDSU_Attuale"] = eco.ISPEDSU_Attuale ?? 0m;
                    row["ISPDSU_Attuale"] = eco.ISPDSU_Attuale ?? 0m;
                    row["SEQ_Attuale"] = eco.SEQ_Attuale ?? 0m;
                }

                row["StatusSedeAttuale"] = info.InformazioniSede.StatusSede ?? "";
                row["StatusSedeSuggerito"] = sede?.StatoSuggerito ?? "";
                row["MotivoStatusSede"] = sede?.Motivo ?? "";

                row["ComuneResidenza"] = info.InformazioniSede.Residenza.codComune ?? "";
                row["ProvinciaResidenza"] = info.InformazioniSede.Residenza.provincia ?? "";

                row["ComuneSedeStudi"] = info.InformazioniIscrizione.ComuneSedeStudi ?? "";
                row["ProvinciaSede"] = info.InformazioniIscrizione.ProvinciaSedeStudi ?? "";

                row["ComuneDomicilio"] = dom?.codComuneDomicilio ?? "";

                row["SerieContrattoDomicilio"] = dom?.codiceSerieLocazione ?? "";
                row["DataRegistrazioneDomicilio"] = FormatDateForExport(dom?.dataRegistrazioneLocazione);
                row["DataDecorrenzaDomicilio"] = FormatDateForExport(dom?.dataDecorrenzaLocazione);
                row["DataScadenzaDomicilio"] = FormatDateForExport(dom?.dataScadenzaLocazione);
                row["ProrogatoDomicilio"] = dom?.prorogatoLocazione ?? false;
                row["SerieProrogaDomicilio"] = dom?.codiceSerieProrogaLocazione ?? "";

                if (sede != null)
                {
                    row["DomicilioPresente"] = sede.DomicilioPresente;
                    row["DomicilioValido"] = sede.DomicilioValido;
                    row["HasAlloggio12"] = sede.HasAlloggio12;

                    row["HasIstanzaDomicilio"] = sede.HasIstanzaDomicilio;
                    row["CodTipoIstanzaDomicilio"] = sede.CodTipoIstanzaDomicilio ?? "";
                    row["NumIstanzaDomicilio"] = sede.NumIstanzaDomicilio > 0
                        ? sede.NumIstanzaDomicilio.ToString(CultureInfo.InvariantCulture)
                        : "";

                    row["HasUltimaIstanzaChiusaDomicilio"] = sede.HasUltimaIstanzaChiusaDomicilio;
                    row["CodTipoUltimaIstanzaChiusaDomicilio"] = sede.CodTipoUltimaIstanzaChiusaDomicilio ?? "";
                    row["NumUltimaIstanzaChiusaDomicilio"] = sede.NumUltimaIstanzaChiusaDomicilio > 0
                        ? sede.NumUltimaIstanzaChiusaDomicilio.ToString(CultureInfo.InvariantCulture)
                        : "";
                    row["EsitoUltimaIstanzaChiusaDomicilio"] = sede.EsitoUltimaIstanzaChiusaDomicilio ?? "";
                    row["UtentePresaCaricoUltimaIstanzaChiusaDomicilio"] = sede.UtentePresaCaricoUltimaIstanzaChiusaDomicilio ?? "";
                }

                dt.Rows.Add(row);
            }

            return dt;
        }

        private static string FormatDateForExport(DateTime? dt)
        {
            if (!dt.HasValue) return "";
            if (dt.Value == DateTime.MinValue) return "";
            if (dt.Value.Year < 1900) return "";
            return dt.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
