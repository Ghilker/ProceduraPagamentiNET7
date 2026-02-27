using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class ProceduraControlloDatiEconomici : BaseProcedure<ArgsProceduraControlloDatiEconomici>
    {
        private const string TempCfTable = "#CFEstrazione";
        private const string TempTargetsTable = "#TargetsEconomici";

        private string debugCF = "";

        private readonly Dictionary<string, EconomicRow> _rows =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _statusInpsOrigineByCf = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _statusInpsIntegrazioneByCf = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _coAttestazioneOkByCf = new(StringComparer.OrdinalIgnoreCase);

        public DataTable OutputEconomici { get; private set; } = BuildOutputTable();

        public bool ExportToExcel { get; set; } = true;
        public string ExportFolderPath { get; set; } = "D://";

        private string _aa = "";

        private sealed class CalcParams
        {
            public decimal Franchigia { get; set; }
            public decimal RendPatr { get; set; }              // tasso_rendimento_pat_mobiliare
            public decimal FranchigiaPatMob { get; set; }      // franchigia_pat_mobiliare
        }

        private readonly CalcParams _calc = new();

        private void LoadInpsAndAttestazioni_StoredLike(string aa, List<Target> targets)
        {
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            EnsureTempTargetsTableAndFill(targets);

            _statusInpsOrigineByCf.Clear();
            _statusInpsIntegrazioneByCf.Clear();
            _coAttestazioneOkByCf.Clear();

            // ORIGINE: come stored (NOT IN ('CI','DI') + filtro num_domanda)
            const string sqlOrig = @"
SELECT
    t.Cod_fiscale,
    si.status_inps
FROM #TargetsEconomici t
LEFT JOIN vStatus_INPS si
    ON si.anno_accademico = @AA
   AND si.cod_fiscale     = t.Cod_fiscale
   AND si.num_domanda     = t.Num_domanda
   AND si.data_fine_validita IS NULL
   AND si.tipo_certificaz NOT IN ('CI','DI');";

            using (var command = new SqlCommand(sqlOrig, CONNECTION))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                    int statusInps = reader.SafeGetInt("status_inps");
                    _statusInpsOrigineByCf[codFiscale] = statusInps;
                }
            }

            // INTEGRAZIONE: come stored (IN ('CI','DI') e senza filtro num_domanda)
            const string sqlInt = @"
SELECT
    t.Cod_fiscale,
    si.status_inps
FROM #TargetsEconomici t
LEFT JOIN vStatus_INPS si
    ON si.anno_accademico = @AA
   AND si.cod_fiscale     = t.Cod_fiscale
   AND si.data_fine_validita IS NULL
   AND si.tipo_certificaz IN ('CI','DI');";

            using (var command = new SqlCommand(sqlInt, CONNECTION))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                    int statusInps = reader.SafeGetInt("status_inps");
                    _statusInpsIntegrazioneByCf[codFiscale] = statusInps;
                }
            }

            // CO attestazione: come stored (per 20242025+ basta “non vuoto”)
            const string sqlAtt = @"
SELECT
    t.Cod_fiscale,
    LTRIM(RTRIM(ISNULL(cte.Cod_tipo_attestazione,''))) AS Cod_tipo_attestazione
FROM #TargetsEconomici t
LEFT JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'CO';";

            using (var command = new SqlCommand(sqlAtt, CONNECTION))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                    string tipoAttestazione = reader.SafeGetString("Cod_tipo_attestazione");
                    _coAttestazioneOkByCf[codFiscale] = !string.IsNullOrWhiteSpace(tipoAttestazione);
                }
            }
        }

        private static decimal RoundSql(decimal value, int decimals) =>
            Math.Round(value, decimals, MidpointRounding.AwayFromZero);

        private static decimal ScalaMin(int componentCount)
        {
            if (componentCount < 1) componentCount = 1;
            return componentCount switch
            {
                1 => 1.00m,
                2 => 1.57m,
                3 => 2.04m,
                4 => 2.46m,
                5 => 2.85m,
                _ => 2.85m + (componentCount - 5) * 0.35m
            };
        }

        private static int GetEseFinanziario(string aa) => aa switch
        {
            "20252026" => 2023,
            "20242025" => 2022,
            "20232024" => 2021,
            "20222023" => 2018,
            "20212022" => 2019,
            _ => int.TryParse(aa.Substring(0, 4), out var year) ? year - 2 : 0
        };

        private static string GetFiltroCodTipoPagam(string aa) =>
            aa == "20252026"
                ? "(p.Cod_tipo_pagam IN ('01','06','09','34','39','41','R1','R3','R4','R9','RR','S0','S1','S3','S5') OR p.Cod_tipo_pagam LIKE 'BSA%' OR p.Cod_tipo_pagam LIKE 'BSI%' OR p.Cod_tipo_pagam LIKE 'BSS%' OR p.Cod_tipo_pagam LIKE 'PL%')"
                : "p.Cod_tipo_pagam IN ('01','06','09','34','39','41','R1','R3','R4','R9','RR','S0','S1','S3','S5')";

        private void LoadCalcParams(string aa)
        {
            const string sql = @"
SELECT Franchigia, tasso_rendimento_pat_mobiliare, franchigia_pat_mobiliare
FROM DatiGenerali_con
WHERE Anno_accademico = @AA;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                _calc.Franchigia = reader.SafeGetDecimal("Franchigia");
                _calc.RendPatr = reader.SafeGetDecimal("tasso_rendimento_pat_mobiliare");
                _calc.FranchigiaPatMob = reader.SafeGetDecimal("franchigia_pat_mobiliare");
            }
        }

        private void LoadNucleoFamiliare(string aa)
        {
            const string sql = @"
SELECT
    t.Cod_fiscale,
    ISNULL(nf.Num_componenti, 0) AS Num_componenti,
    ISNULL(nf.Cod_tipologia_nucleo, '') AS Cod_tipologia_nucleo,
    ISNULL(nf.Numero_conviventi_estero, 0) AS Numero_conviventi_estero
FROM #TargetsEconomici t
INNER JOIN vNucleo_familiare nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                economicRow.NumeroComponenti = reader.SafeGetInt("Num_componenti");
                economicRow.TipoNucleo = reader.SafeGetString("Cod_tipologia_nucleo");
                economicRow.NumeroConviventiEstero = reader.SafeGetInt("Numero_conviventi_estero");
            }
        }

        private decimal ComputeSeqFinal(EconomicRow economicRow)
        {
            // maggiorazioni studente
            int componentiTotali = economicRow.NumeroComponenti > 0 ? economicRow.NumeroComponenti : 1;
            int conviventiEstero = Math.Max(economicRow.NumeroConviventiEstero, 0);

            decimal maggiorazioneStudente = 0m;
            int componentiStudente = componentiTotali;

            if (string.Equals(economicRow.TipoRedditoOrigine, "it", StringComparison.OrdinalIgnoreCase))
            {
                int baseComponenti = Math.Max(componentiTotali - conviventiEstero, 1);
                maggiorazioneStudente = (economicRow.SEQ_Origine > 0 ? economicRow.SEQ_Origine : 0m) - ScalaMin(baseComponenti);
                componentiStudente = baseComponenti + conviventiEstero; // come stored
            }

            // integrazione
            int componentiIntegrazione = Math.Max(economicRow.NumeroComponentiIntegrazione, 0);
            if (componentiIntegrazione <= 0)
            {
                var seqSoloStudente = ScalaMin(componentiStudente) + maggiorazioneStudente;
                return RoundSql(seqSoloStudente <= 0 ? 1m : seqSoloStudente, 2);
            }

            decimal maggiorazioneIntegrazione = 0m;
            if (string.Equals(economicRow.TipoRedditoIntegrazione, "it", StringComparison.OrdinalIgnoreCase))
                maggiorazioneIntegrazione = (economicRow.SEQ_Integrazione > 0 ? economicRow.SEQ_Integrazione : 0m) - ScalaMin(componentiIntegrazione);

            int componentiTot = componentiStudente + componentiIntegrazione;
            decimal seq = ScalaMin(componentiTot) + maggiorazioneStudente +
                          (string.Equals(economicRow.TipoRedditoIntegrazione, "it", StringComparison.OrdinalIgnoreCase) ? maggiorazioneIntegrazione : 0m);

            return RoundSql(seq <= 0 ? 1m : seq, 2);
        }

        public ProceduraControlloDatiEconomici(MasterForm? masterForm, SqlConnection? connection)
            : base(masterForm, connection) { }

        public override void RunProcedure(ArgsProceduraControlloDatiEconomici args)
        {
            void Log(int pct, string msg) => Logger.LogInfo(Math.Max(0, Math.Min(100, pct)), msg);

            Log(0, "Avvio procedura ProceduraControlloDatiEconomici");

            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            string aa = (args._selectedAA ?? "").Trim();
            if (string.IsNullOrWhiteSpace(aa) || aa.Length != 8)
                throw new ArgumentException("Anno accademico non valido (atteso char(8), es: 20232024).");

            _aa = aa;
            _rows.Clear();
            OutputEconomici = BuildOutputTable();

            Log(5, $"Parametri validati. AA={aa}");

            var targets = (args._codiciFiscali != null && args._codiciFiscali.Count > 0)
                ? LoadTargetsFromCfList(aa, args._codiciFiscali)
                : LoadTargetsAll(aa);

            Log(15, $"Targets caricati: {targets.Count}");

            foreach (var target in targets)
            {
                if (!_rows.ContainsKey(target.CodFiscale))
                {
                    _rows[target.CodFiscale] = new EconomicRow
                    {
                        CodFiscale = target.CodFiscale,
                        NumDomanda = target.NumDomanda
                    };
                }
            }

            Log(18, "Caricamento valori attuali da vValori_calcolati.");
            LoadValoriCalcolatiAttuali(aa, targets);

            LoadCalcParams(aa);
            LoadNucleoFamiliare(aa);

            Log(19, "Caricamento esito concorso BS (cod_tipo_esito) da vEsiti_concorsi.");
            LoadEsitoBorsaStudio(aa, targets);

            Log(20, "Preparazione tabella temporanea CF e bulk insert.");
            EnsureTempCfTableAndFill(targets.Select(t => t.CodFiscale));

            Log(22, "Caricamento INPS + attestazione CO (stored-like, >=20242025).");
            LoadInpsAndAttestazioni_StoredLike(aa, targets);

            Log(30, "Esecuzione della query per tipologie reddito e split stored-like.");
            var split = LoadTipologieRedditiAndSplit(aa);

            Log(40, "Avvio estrazione dati economici (origine).");
            if (split.OrigIT_CO.Count > 0) AddDatiEconomiciItaliani_CO(aa, split.OrigIT_CO);
            if (split.OrigIT_DO.Count > 0) AddDatiEconomiciItaliani_DOFromCert(aa, split.OrigIT_DO);
            if (split.OrigEE.Count > 0) AddDatiEconomiciStranieri_DO(aa, split.OrigEE);

            Log(60, "Avvio estrazione dati economici (integrazione) - solo nucleo 'I'.");
            if (split.IntIT_CI.Count > 0) AddDatiEconomiciItaliani_CI(aa, split.IntIT_CI);
            if (split.IntDI.Count > 0) AddDatiEconomiciStranieri_DI(aa, split.IntDI);

            Log(85, "Calcolo ISEDSU/ISEEDSU/ISPEDSU.");
            CalcoloDatiEconomici();

            Log(95, "Costruzione DataTable di output.");
            foreach (var economicRow in _rows.Values.OrderBy(x => x.CodFiscale))
            {
                var outputRow = OutputEconomici.NewRow();
                outputRow["CodFiscale"] = economicRow.CodFiscale;
                outputRow["NumDomanda"] = economicRow.NumDomanda ?? "";
                outputRow["TipoRedditoOrigine"] = economicRow.TipoRedditoOrigine ?? "";
                outputRow["TipoRedditoIntegrazione"] = economicRow.TipoRedditoIntegrazione ?? "";
                outputRow["CodTipoEsitoBS"] = (object?)economicRow.CodTipoEsitoBS ?? DBNull.Value;

                outputRow["ISR"] = (double)RoundSql(economicRow.ISRDSU, 2);
                outputRow["ISP"] = (double)RoundSql(economicRow.ISPDSU, 2);
                outputRow["Detrazioni"] = (double)RoundSql(economicRow.Detrazioni, 2);

                outputRow["ISEDSU"] = (double)economicRow.ISEDSU;
                outputRow["ISEEDSU"] = (double)economicRow.ISEEDSU;
                outputRow["ISPEDSU"] = (double)economicRow.ISPEDSU;

                outputRow["ISPDSU"] = (double)RoundSql(economicRow.ISPDSU, 2);
                outputRow["SEQ"] = (double)RoundSql(economicRow.SEQ, 2);

                outputRow["ISEDSU_Attuale"] = economicRow.ISEDSU_Attuale;
                outputRow["ISEEDSU_Attuale"] = economicRow.ISEEDSU_Attuale;
                outputRow["ISPEDSU_Attuale"] = economicRow.ISPEDSU_Attuale;
                outputRow["ISPDSU_Attuale"] = economicRow.ISPDSU_Attuale;
                outputRow["SEQ_Attuale"] = economicRow.SEQ_Attuale;

                OutputEconomici.Rows.Add(outputRow);
            }

            if (ExportToExcel)
                Utilities.ExportDataTableToExcel(OutputEconomici, ExportFolderPath);

            Log(100, $"Completato. Record output: {OutputEconomici.Rows.Count}");
        }

        // =========================
        //  TARGETS (CF + NumDomanda)
        // =========================

        private List<Target> LoadTargetsAll(string aa)
        {
            Logger.LogInfo(10, "Esecuzione della query per ottenere i codici fiscali per i blocchi.");

            const string sql = @"
;WITH D AS
(
    SELECT
        UPPER(LTRIM(RTRIM(d.Cod_fiscale))) AS Cod_fiscale,
        d.Num_domanda,
        d.Data_validita,
        ROW_NUMBER() OVER
        (
            PARTITION BY UPPER(LTRIM(RTRIM(d.Cod_fiscale)))
            ORDER BY d.Data_validita DESC, d.Num_domanda DESC
        ) AS rn
    FROM Domanda d
    INNER JOIN vStatus_compilazione vv
        ON d.Anno_accademico = vv.anno_accademico
       AND d.Num_domanda     = vv.num_domanda
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando = 'lz'
      AND vv.status_compilazione >= 90
)
SELECT Cod_fiscale, Num_domanda
FROM D
WHERE rn = 1
ORDER BY Cod_fiscale;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            var list = new List<Target>(capacity: 8192);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                string numDomanda = reader.SafeGetString("Num_domanda");

                if (!string.IsNullOrWhiteSpace(codFiscale))
                    list.Add(new Target(codFiscale, numDomanda));
            }

            Logger.LogInfo(12, $"Query targets completata. Righe: {list.Count}");
            return list;
        }

        private List<Target> LoadTargetsFromCfList(string aa, List<string> codiciFiscali)
        {
            var codiciFiscaliNormalizzati = codiciFiscali
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Utilities.RemoveAllSpaces(value).ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Logger.LogInfo(8, $"Targets da lista CF richiesti: {codiciFiscaliNormalizzati.Count}");

            if (codiciFiscaliNormalizzati.Count == 0) return new List<Target>();

            Logger.LogInfo(18, "Preparazione tabella temporanea CF per filtro targets.");
            EnsureTempCfTableAndFill(codiciFiscaliNormalizzati);

            Logger.LogInfo(20, "Esecuzione della query per ottenere i codici fiscali per i blocchi.");

            const string sql = @"
;WITH D AS
(
    SELECT
        UPPER(LTRIM(RTRIM(d.Cod_fiscale))) AS Cod_fiscale,
        d.Num_domanda,
        d.Data_validita,
        ROW_NUMBER() OVER
        (
            PARTITION BY UPPER(LTRIM(RTRIM(d.Cod_fiscale)))
            ORDER BY d.Data_validita DESC, d.Num_domanda DESC
        ) AS rn
    FROM Domanda d
    INNER JOIN #CFEstrazione cfe
        ON UPPER(LTRIM(RTRIM(d.Cod_fiscale))) = cfe.Cod_fiscale
    INNER JOIN vStatus_compilazione vv
        ON d.Anno_accademico = vv.anno_accademico
       AND d.Num_domanda     = vv.num_domanda
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando = 'lz'
      AND vv.status_compilazione >= 90
)
SELECT Cod_fiscale, Num_domanda
FROM D
WHERE rn = 1
ORDER BY Cod_fiscale;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            var list = new List<Target>(capacity: codiciFiscaliNormalizzati.Count);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                string numDomanda = reader.SafeGetString("Num_domanda");

                if (!string.IsNullOrWhiteSpace(codFiscale))
                    list.Add(new Target(codFiscale, numDomanda));
            }

            Logger.LogInfo(22, $"Query targets (da lista CF) completata. Righe: {list.Count}");
            return list;
        }

        // =========================
        //  VALORI ATTUALI (vValori_calcolati) - COMPARATIVA
        // =========================

        private void EnsureTempTargetsTableAndFill(List<Target> targets)
        {
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            var list = targets
                .Where(target => !string.IsNullOrWhiteSpace(target.CodFiscale) && !string.IsNullOrWhiteSpace(target.NumDomanda))
                .Select(target => new Target(
                    Utilities.RemoveAllSpaces(target.CodFiscale).ToUpperInvariant(),
                    Utilities.RemoveAllSpaces(target.NumDomanda)))
                .GroupBy(target => target.CodFiscale, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            const string ensureSql = @"
IF OBJECT_ID('tempdb..#TargetsEconomici') IS NOT NULL
BEGIN
    TRUNCATE TABLE #TargetsEconomici;
END
ELSE
BEGIN
    CREATE TABLE #TargetsEconomici
    (
        Cod_fiscale VARCHAR(16) NOT NULL,
        Num_domanda VARCHAR(20) NOT NULL
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM tempdb.sys.indexes
    WHERE name = 'ix_TargetsEconomici_CF'
      AND object_id = OBJECT_ID('tempdb..#TargetsEconomici')
)
BEGIN
    CREATE INDEX ix_TargetsEconomici_CF ON #TargetsEconomici (Cod_fiscale);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM tempdb.sys.indexes
    WHERE name = 'ix_TargetsEconomici_ND'
      AND object_id = OBJECT_ID('tempdb..#TargetsEconomici')
)
BEGIN
    CREATE INDEX ix_TargetsEconomici_ND ON #TargetsEconomici (Num_domanda);
END;";

            using (var command = new SqlCommand(ensureSql, CONNECTION))
                command.ExecuteNonQuery();

            if (list.Count == 0)
            {
                using var statsCommand = new SqlCommand("UPDATE STATISTICS #TargetsEconomici;", CONNECTION);
                statsCommand.ExecuteNonQuery();
                return;
            }

            using (var dataTable = new DataTable())
            {
                dataTable.Columns.Add("Cod_fiscale", typeof(string));
                dataTable.Columns.Add("Num_domanda", typeof(string));

                foreach (var target in list)
                    dataTable.Rows.Add(target.CodFiscale, target.NumDomanda);

                using var bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = TempTargetsTable,
                    BatchSize = 10000,
                    BulkCopyTimeout = 600
                };
                bulkCopy.WriteToServer(dataTable);
            }

            using (var statsCommand = new SqlCommand("UPDATE STATISTICS #TargetsEconomici;", CONNECTION))
                statsCommand.ExecuteNonQuery();
        }

        private void LoadValoriCalcolatiAttuali(string aa, List<Target> targets)
        {
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    vv.ISPEDSU,
    vv.ISEDSU,
    vv.SEQ,
    vv.ISPDSU,
    vv.ISEEDSU
FROM #TargetsEconomici t
LEFT JOIN vValori_calcolati vv
    ON vv.Anno_accademico = @AA
   AND vv.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                economicRow.ISPEDSU_Attuale = reader.SafeGetDouble("ISPEDSU");
                economicRow.ISEDSU_Attuale = reader.SafeGetDouble("ISEDSU");
                economicRow.SEQ_Attuale = reader.SafeGetDouble("SEQ");
                economicRow.ISPDSU_Attuale = reader.SafeGetDouble("ISPDSU");
                economicRow.ISEEDSU_Attuale = reader.SafeGetDouble("ISEEDSU");
            }
        }

        // =========================
        //  ESITO CONCORSO BS (vEsiti_concorsi)
        // =========================

        private void LoadEsitoBorsaStudio(string aa, List<Target> targets)
        {
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
WITH EsitoBS AS
(
    SELECT
        ec.Anno_accademico,
        ec.Num_domanda,
        MAX(ec.Cod_tipo_esito) AS Cod_tipo_esito
    FROM vEsiti_concorsi ec
    WHERE ec.Anno_accademico = @AA
      AND ec.Cod_beneficio = 'BS'
    GROUP BY ec.Anno_accademico, ec.Num_domanda
)
SELECT
    t.Cod_fiscale,
    e.Cod_tipo_esito
FROM #TargetsEconomici t
LEFT JOIN EsitoBS e
    ON e.Anno_accademico = @AA
   AND e.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                object rawEsito = reader["Cod_tipo_esito"];
                int? codTipoEsito = rawEsito is DBNull or null ? (int?)null : Convert.ToInt32(rawEsito, CultureInfo.InvariantCulture);

                economicRow.CodTipoEsitoBS = codTipoEsito;
            }
        }

        // =========================
        //  TIPOLOGIE REDDITI + SPLIT
        // =========================

        private sealed class SplitResult
        {
            public List<string> OrigIT_CO { get; } = new();
            public List<string> OrigIT_DO { get; } = new();     // IT dichiarato, ma INPS non ok -> usa vCertificaz_ISEE 'DO'
            public List<string> OrigEE { get; } = new();        // redditi estero -> usa nucleo stranieri DO

            public List<string> IntIT_CI { get; } = new();      // integrazione IT + INPS ok -> vCertificaz_ISEE 'CI'
            public List<string> IntDI { get; } = new();         // integrazione estero o IT non ok -> nucleo stranieri 'DI'
        }

        private SplitResult LoadTipologieRedditiAndSplit(string aa)
        {
            Logger.LogInfo(30, "Esecuzione query tipologie reddito (vTipologie_redditi) + split stored-like.");

            var result = new SplitResult();

            const string sql = @"
SELECT
    d.Cod_fiscale,
    tr.Tipo_redd_nucleo_fam_origine,
    tr.Tipo_redd_nucleo_fam_integr,
    ISNULL(tr.altri_mezzi,0) AS altri_mezzi
FROM Domanda d
INNER JOIN #CFEstrazione cfe
    ON UPPER(LTRIM(RTRIM(d.Cod_fiscale))) = cfe.Cod_fiscale
INNER JOIN vTipologie_redditi tr
    ON d.Anno_accademico = tr.Anno_accademico
   AND d.Num_domanda     = tr.Num_domanda
WHERE d.Anno_accademico = @AA
  AND d.Tipo_bando = 'lz'
ORDER BY d.Cod_fiscale;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            int readCount = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readCount++;

                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                string tipoOrigine = Utilities.RemoveAllSpaces(reader.SafeGetString("Tipo_redd_nucleo_fam_origine"));
                string tipoIntegrazione = Utilities.RemoveAllSpaces(reader.SafeGetString("Tipo_redd_nucleo_fam_integr"));

                economicRow.TipoRedditoOrigine = tipoOrigine;
                economicRow.TipoRedditoIntegrazione = tipoIntegrazione;

                economicRow.AltriMezzi = reader.SafeGetDecimal("altri_mezzi");

                // === ORIGINE ===
                if (tipoOrigine.Equals("it", StringComparison.OrdinalIgnoreCase))
                {
                    int statusInps = _statusInpsOrigineByCf.TryGetValue(codFiscale, out var found) ? found : 0;
                    bool coOk = statusInps == 2 && (_coAttestazioneOkByCf.TryGetValue(codFiscale, out var ok) && ok);

                    if (coOk) result.OrigIT_CO.Add(codFiscale);
                    else result.OrigIT_DO.Add(codFiscale);
                }
                else if (tipoOrigine.Equals("ee", StringComparison.OrdinalIgnoreCase))
                {
                    result.OrigEE.Add(codFiscale);
                }

                // === INTEGRAZIONE === (solo se nucleo = 'I' come stored)
                bool doIntegrazione = string.Equals(economicRow.TipoNucleo, "I", StringComparison.OrdinalIgnoreCase)
                                      && !string.IsNullOrWhiteSpace(tipoIntegrazione);

                if (doIntegrazione)
                {
                    if (tipoIntegrazione.Equals("it", StringComparison.OrdinalIgnoreCase))
                    {
                        int statusInpsI = _statusInpsIntegrazioneByCf.TryGetValue(codFiscale, out var foundI) ? foundI : 0;
                        if (statusInpsI == 2) result.IntIT_CI.Add(codFiscale);
                        else result.IntDI.Add(codFiscale);
                    }
                    else if (tipoIntegrazione.Equals("ee", StringComparison.OrdinalIgnoreCase))
                    {
                        result.IntDI.Add(codFiscale);
                    }
                }
            }

            Logger.LogInfo(33, $"Tipologie reddito lette: {readCount}");
            return result;
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - IT (CO)
        // =========================

        private void AddDatiEconomiciItaliani_CO(string aa, List<string> codiciFiscali)
        {
            EnsureTempCfTableAndFill(codiciFiscali);

            int eseFin = GetEseFinanziario(aa);
            string filtroPagam = GetFiltroCodTipoPagam(aa);

            string sql = $@"
WITH sumPagamenti AS (
    SELECT SUM(p.imp_pagato) AS somma, d.Cod_fiscale
    FROM Pagamenti p
    INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
    WHERE p.Ritirato_azienda = 0
      AND p.Ese_finanziario = @EseFin
      AND {filtroPagam}
    GROUP BY d.Cod_fiscale
),
impAltreBorse AS (
    SELECT vb.num_domanda, vb.anno_accademico, SUM(vb.importo_borsa) AS importo_borsa
    FROM vimporti_borsa_percepiti vb
    INNER JOIN Allegati a ON vb.anno_accademico = a.anno_accademico AND vb.num_domanda = a.num_domanda
    INNER JOIN vstatus_allegati vs ON a.id_allegato = vs.id_allegato
    WHERE vb.data_fine_validita IS NULL
      AND a.data_fine_validita IS NULL
      AND a.cod_tipo_allegato = '07'
      AND vs.cod_status IN ('03','05')
      AND vb.anno_accademico = @AA
    GROUP BY vb.num_domanda, vb.anno_accademico
)
SELECT
    t.Cod_fiscale,
    ISNULL(sp.somma, 0) AS detrazioniADISU,
    ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse,

    ISNULL(cte.Somma_redditi,0) AS Somma_redditi,
    ISNULL(cte.ISR,0) AS ISR,
    ISNULL(cte.ISP,0) AS ISP,
    ISNULL(cte.Scala_equivalenza,0) AS SEQU,

    ISNULL(cte.Redd_fratelli_50,0) AS Redd_fratelli_50,
    ISNULL(cte.Patr_fratelli_50,0) AS Patr_fratelli_50,
    ISNULL(cte.Patr_frat_50_est,0) AS Patr_frat_50_est,
    ISNULL(cte.Redd_frat_50_est,0) AS Redd_frat_50_est,
    ISNULL(cte.Patr_fam_50_est,0) AS Patr_fam_50_est,
    ISNULL(cte.Metri_quadri,0) AS Metri_quadri,
    ISNULL(cte.Redd_fam_50_est,0) AS Redd_fam_50_est,
    ISNULL(cte.patr_imm_50_frat_sor,0) AS patr_imm_50_frat_sor
FROM #TargetsEconomici t
INNER JOIN #CFEstrazione cfe ON t.Cod_fiscale = cfe.Cod_fiscale
INNER JOIN Domanda d ON d.Anno_accademico = @AA AND d.Num_domanda = t.Num_domanda
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'CO'
LEFT JOIN sumPagamenti sp ON t.Cod_fiscale = sp.Cod_fiscale
LEFT JOIN impAltreBorse iab ON t.Num_domanda = iab.num_domanda AND @AA = iab.anno_accademico;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);
            command.Parameters.AddWithValue("@EseFin", eseFin);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                decimal isr = reader.SafeGetDecimal("ISR");
                decimal isp = reader.SafeGetDecimal("ISP");
                decimal seqCert = reader.SafeGetDecimal("SEQU");

                decimal reddFr50 = reader.SafeGetDecimal("Redd_fratelli_50");
                decimal patrFr50 = reader.SafeGetDecimal("Patr_fratelli_50");
                decimal patrFr50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                decimal reddFr50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                decimal patrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                decimal metri = reader.SafeGetDecimal("Metri_quadri");
                decimal reddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");
                decimal patrImm50FratSor = reader.SafeGetDecimal("patr_imm_50_frat_sor");

                decimal detrazioni = reader.SafeGetDecimal("detrazioniADISU") + reader.SafeGetDecimal("detrazioniAltreBorse");

                economicRow.SEQ_Origine = seqCert;
                economicRow.SommaRedditiStud = reader.SafeGetDecimal("Somma_redditi");

                economicRow.ISRDSU = isr - reddFr50 + reddFr50Est + reddFam50Est + economicRow.AltriMezzi
                                    + (patrFr50Est - patrFr50 + patrFam50Est) * _calc.RendPatr;

                economicRow.ISPDSU = isp - patrImm50FratSor + metri * 500m;

                economicRow.Detrazioni = detrazioni;
            }
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - EE (DO)
        // =========================

        private void AddDatiEconomiciStranieri_DO(string aa, List<string> codiciFiscali)
        {
            EnsureTempCfTableAndFill(codiciFiscali);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    nf.Numero_componenti,
    ISNULL(nf.Redd_complessivo,0) AS Redd_complessivo,
    ISNULL(nf.Patr_mobiliare,0) AS Patr_mobiliare,
    ISNULL(nf.Superf_abitaz_MQ,0) AS Superf_abitaz_MQ,
    ISNULL(nf.Sup_compl_altre_MQ,0) AS Sup_compl_altre_MQ,
    ISNULL(nf.Sup_compl_MQ,0) AS Sup_compl_MQ,
    ISNULL(nf.Redd_lordo_fratell,0) AS Redd_lordo_fratell,
    ISNULL(nf.Patr_mob_fratell,0) AS Patr_mob_fratell
FROM #TargetsEconomici t
INNER JOIN #CFEstrazione cfe ON t.Cod_fiscale = cfe.Cod_fiscale
INNER JOIN vNucleo_fam_stranieri_DO nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                int nComp = reader.SafeGetInt("Numero_componenti");
                decimal redd = reader.SafeGetDecimal("Redd_complessivo");
                decimal patrMob = reader.SafeGetDecimal("Patr_mobiliare");
                decimal superfAb = reader.SafeGetDecimal("Superf_abitaz_MQ");
                decimal supAltre = reader.SafeGetDecimal("Sup_compl_altre_MQ");
                decimal supCompl = reader.SafeGetDecimal("Sup_compl_MQ");
                decimal reddFr = reader.SafeGetDecimal("Redd_lordo_fratell");
                decimal patrFr = reader.SafeGetDecimal("Patr_mob_fratell");

                decimal patrAdj = Math.Max(patrMob + patrFr * 0.5m - _calc.FranchigiaPatMob, 0m);

                economicRow.ISRDSU = redd + reddFr * 0.5m + patrAdj * _calc.RendPatr + economicRow.AltriMezzi;

                decimal isp = Math.Max((superfAb + supAltre + supCompl * 0.5m) * 500m - _calc.Franchigia, 0m);
                economicRow.ISPDSU = isp + patrAdj;

                economicRow.SEQ_Origine = ScalaMin(nComp);
            }
        }

        private void AddDatiEconomiciItaliani_DOFromCert(string aa, List<string> codiciFiscali)
        {
            EnsureTempCfTableAndFill(codiciFiscali);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    ISNULL(cte.Somma_redditi,0) AS Somma_redditi,
    ISNULL(cte.ISR,0) AS ISR,
    ISNULL(cte.ISP,0) AS ISP,
    ISNULL(cte.Scala_equivalenza,0) AS SEQU,

    ISNULL(cte.Redd_fratelli_50,0) AS Redd_fratelli_50,
    ISNULL(cte.Patr_fratelli_50,0) AS Patr_fratelli_50,
    ISNULL(cte.Patr_frat_50_est,0) AS Patr_frat_50_est,
    ISNULL(cte.Redd_frat_50_est,0) AS Redd_frat_50_est,
    ISNULL(cte.Patr_fam_50_est,0) AS Patr_fam_50_est,
    ISNULL(cte.Metri_quadri,0) AS Metri_quadri,
    ISNULL(cte.Redd_fam_50_est,0) AS Redd_fam_50_est,
    ISNULL(cte.patr_imm_50_frat_sor,0) AS patr_imm_50_frat_sor
FROM #TargetsEconomici t
INNER JOIN #CFEstrazione cfe ON t.Cod_fiscale = cfe.Cod_fiscale
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'DO';";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                decimal isr = reader.SafeGetDecimal("ISR");
                decimal isp = reader.SafeGetDecimal("ISP");
                decimal seqCert = reader.SafeGetDecimal("SEQU");

                decimal reddFr50 = reader.SafeGetDecimal("Redd_fratelli_50");
                decimal patrFr50 = reader.SafeGetDecimal("Patr_fratelli_50");
                decimal patrFr50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                decimal reddFr50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                decimal patrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                decimal metri = reader.SafeGetDecimal("Metri_quadri");
                decimal reddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");
                decimal patrImm50FratSor = reader.SafeGetDecimal("patr_imm_50_frat_sor");

                economicRow.SEQ_Origine = seqCert;
                economicRow.SommaRedditiStud = reader.SafeGetDecimal("Somma_redditi");

                economicRow.ISRDSU = isr - reddFr50 + reddFr50Est + reddFam50Est + economicRow.AltriMezzi
                                    + (patrFr50Est - patrFr50 + patrFam50Est) * _calc.RendPatr;

                economicRow.ISPDSU = isp - patrImm50FratSor + metri * 500m;
            }
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - IT integrazione (CI)
        // =========================

        private void AddDatiEconomiciItaliani_CI(string aa, List<string> codiciFiscali)
        {
            EnsureTempCfTableAndFill(codiciFiscali);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    ISNULL(cte.ISR,0) AS ISR,
    ISNULL(cte.ISP,0) AS ISP,
    ISNULL(cte.Scala_equivalenza,0) AS SEQU,
    ISNULL(cte.numero_componenti_attestazione,0) AS NumCompAtt,

    ISNULL(cte.Redd_fratelli_50,0) AS Redd_fratelli_50,
    ISNULL(cte.Patr_fratelli_50,0) AS Patr_fratelli_50,
    ISNULL(cte.Patr_frat_50_est,0) AS Patr_frat_50_est,
    ISNULL(cte.Redd_frat_50_est,0) AS Redd_frat_50_est,
    ISNULL(cte.Patr_fam_50_est,0) AS Patr_fam_50_est,
    ISNULL(cte.Metri_quadri,0) AS Metri_quadri,
    ISNULL(cte.Redd_fam_50_est,0) AS Redd_fam_50_est
FROM #TargetsEconomici t
INNER JOIN #CFEstrazione cfe ON t.Cod_fiscale = cfe.Cod_fiscale
INNER JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'CI';";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                decimal isr = reader.SafeGetDecimal("ISR");
                decimal isp = reader.SafeGetDecimal("ISP");
                decimal seqCert = reader.SafeGetDecimal("SEQU");
                int numComponentiAtt = reader.SafeGetInt("NumCompAtt");

                decimal reddFr50 = reader.SafeGetDecimal("Redd_fratelli_50");
                decimal patrFr50 = reader.SafeGetDecimal("Patr_fratelli_50");
                decimal patrFr50Est = reader.SafeGetDecimal("Patr_frat_50_est");
                decimal reddFr50Est = reader.SafeGetDecimal("Redd_frat_50_est");
                decimal patrFam50Est = reader.SafeGetDecimal("Patr_fam_50_est");
                decimal metri = reader.SafeGetDecimal("Metri_quadri");
                decimal reddFam50Est = reader.SafeGetDecimal("Redd_fam_50_est");

                economicRow.SEQ_Integrazione = seqCert;
                economicRow.NumeroComponentiIntegrazione = numComponentiAtt;

                // Formule stored (integrazione IT): termine patrimoniale con “-” nella stored
                economicRow.ISRDSU += isr - reddFr50 + reddFr50Est + reddFam50Est
                                    - (patrFr50Est - patrFr50 + patrFam50Est) * _calc.RendPatr;

                economicRow.ISPDSU += isp + metri * 500m;
            }
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - EE integrazione (DI)
        // =========================

        private void AddDatiEconomiciStranieri_DI(string aa, List<string> codiciFiscali)
        {
            EnsureTempCfTableAndFill(codiciFiscali);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    nf.Numero_componenti,
    ISNULL(nf.Redd_complessivo,0) AS Redd_complessivo,
    ISNULL(nf.Patr_mobiliare,0) AS Patr_mobiliare,
    ISNULL(nf.Superf_abitaz_MQ,0) AS Superf_abitaz_MQ,
    ISNULL(nf.Sup_compl_altre_MQ,0) AS Sup_compl_altre_MQ,
    ISNULL(nf.Sup_compl_MQ,0) AS Sup_compl_MQ,
    ISNULL(nf.Redd_lordo_fratell,0) AS Redd_lordo_fratell,
    ISNULL(nf.Patr_mob_fratell,0) AS Patr_mob_fratell
FROM #TargetsEconomici t
INNER JOIN #CFEstrazione cfe ON t.Cod_fiscale = cfe.Cod_fiscale
INNER JOIN vNucleo_fam_stranieri_DI nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, CONNECTION);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                int nComp = reader.SafeGetInt("Numero_componenti");
                decimal redd = reader.SafeGetDecimal("Redd_complessivo");
                decimal patrMob = reader.SafeGetDecimal("Patr_mobiliare");
                decimal superfAb = reader.SafeGetDecimal("Superf_abitaz_MQ");
                decimal supAltre = reader.SafeGetDecimal("Sup_compl_altre_MQ");
                decimal supCompl = reader.SafeGetDecimal("Sup_compl_MQ");
                decimal reddFr = reader.SafeGetDecimal("Redd_lordo_fratell");
                decimal patrFr = reader.SafeGetDecimal("Patr_mob_fratell");

                economicRow.NumeroComponentiIntegrazione = nComp;
                economicRow.SEQ_Integrazione = ScalaMin(nComp);

                decimal patrAdj = Math.Max(patrMob + patrFr * 0.5m - _calc.FranchigiaPatMob, 0m);

                economicRow.ISRDSU += redd + reddFr * 0.5m + patrAdj * _calc.RendPatr;

                decimal ispAdd = Math.Max((superfAb + supAltre + supCompl * 0.5m) * 500m - _calc.Franchigia, 0m) + patrAdj;
                economicRow.ISPDSU += ispAdd;
            }
        }

        // =========================
        //  CALCOLO ISE*
        // =========================

        private void CalcoloDatiEconomici()
        {
            foreach (var economicRow in _rows.Values)
            {
                economicRow.SEQ = ComputeSeqFinal(economicRow);

                economicRow.ISRDSU = Math.Max(economicRow.ISRDSU - economicRow.Detrazioni, 0m);

                decimal isedsu = economicRow.ISRDSU + 0.2m * economicRow.ISPDSU;
                decimal iseed = economicRow.SEQ > 0 ? isedsu / economicRow.SEQ : isedsu;
                decimal ispe = (economicRow.ISPDSU > 0 && economicRow.SEQ > 0) ? economicRow.ISPDSU / economicRow.SEQ : 0m;

                economicRow.ISEDSU = RoundSql(isedsu, 2);
                economicRow.ISEEDSU = RoundSql(iseed, 2);
                economicRow.ISPEDSU = RoundSql(ispe, 2);
            }
        }

        private static double CalculateSEQ(int numComponenti)
        {
            if (numComponenti < 1) return 1;

            double seq = numComponenti switch
            {
                1 => 1.00,
                2 => 1.57,
                3 => 2.04,
                4 => 2.46,
                5 => 2.85,
                _ => 2.85 + (numComponenti - 5) * 0.35
            };

            return Math.Round(seq, 2);
        }

        // =========================
        //  TEMP TABLE CF + BULK
        // =========================

        private void EnsureTempCfTableAndFill(IEnumerable<string> codiciFiscali)
        {
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            var codiciFiscaliDistinct = codiciFiscali
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Utilities.RemoveAllSpaces(value).ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Logger.LogInfo(20, $"Preparazione {TempCfTable}. CF distinti: {codiciFiscaliDistinct.Count}");

            const string ensureSql = @"
IF OBJECT_ID('tempdb..#CFEstrazione') IS NOT NULL
BEGIN
    TRUNCATE TABLE #CFEstrazione;
END
ELSE
BEGIN
    CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16) NOT NULL);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM tempdb.sys.indexes
    WHERE name = 'idx_Cod_fiscale'
      AND object_id = OBJECT_ID('tempdb..#CFEstrazione')
)
BEGIN
    CREATE INDEX idx_Cod_fiscale ON #CFEstrazione (Cod_fiscale);
END;";

            using (var command = new SqlCommand(ensureSql, CONNECTION))
                command.ExecuteNonQuery();

            if (codiciFiscaliDistinct.Count == 0)
            {
                using var statsCommand = new SqlCommand("UPDATE STATISTICS #CFEstrazione;", CONNECTION);
                statsCommand.ExecuteNonQuery();
                Logger.LogInfo(21, "CF table aggiornata (vuota) + statistiche.");
                return;
            }

            Logger.LogInfo(22, "Bulk copy su tabella temporanea CF.");

            using (var dataTable = new DataTable())
            {
                dataTable.Columns.Add("Cod_fiscale", typeof(string));
                foreach (var codFiscale in codiciFiscaliDistinct) dataTable.Rows.Add(codFiscale);

                using var bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = TempCfTable,
                    BatchSize = 10000,
                    BulkCopyTimeout = 600
                };
                bulkCopy.WriteToServer(dataTable);
            }

            using (var statsCommand = new SqlCommand("UPDATE STATISTICS #CFEstrazione;", CONNECTION))
                statsCommand.ExecuteNonQuery();

            Logger.LogInfo(25, "Bulk copy completato + statistiche aggiornate.");
        }

        // =========================
        //  OUTPUT TABLE
        // =========================

        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable("DatiEconomici");
            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("NumDomanda", typeof(string));
            dt.Columns.Add("TipoRedditoOrigine", typeof(string));
            dt.Columns.Add("TipoRedditoIntegrazione", typeof(string));

            dt.Columns.Add("CodTipoEsitoBS", typeof(int));

            dt.Columns.Add("ISR", typeof(double));
            dt.Columns.Add("ISP", typeof(double));
            dt.Columns.Add("Detrazioni", typeof(double));
            dt.Columns.Add("ISEDSU", typeof(double));
            dt.Columns.Add("ISEEDSU", typeof(double));
            dt.Columns.Add("ISPEDSU", typeof(double));
            dt.Columns.Add("ISPDSU", typeof(double));
            dt.Columns.Add("SEQ", typeof(double));

            dt.Columns.Add("ISEDSU_Attuale", typeof(double));
            dt.Columns.Add("ISEEDSU_Attuale", typeof(double));
            dt.Columns.Add("ISPEDSU_Attuale", typeof(double));
            dt.Columns.Add("ISPDSU_Attuale", typeof(double));
            dt.Columns.Add("SEQ_Attuale", typeof(double));

            return dt;
        }

        // =========================
        //  DTO
        // =========================

        private readonly record struct Target(string CodFiscale, string NumDomanda);

        private sealed class EconomicRow
        {
            public string CodFiscale { get; set; } = "";
            public string? NumDomanda { get; set; }

            public string? TipoRedditoOrigine { get; set; }
            public string? TipoRedditoIntegrazione { get; set; }

            public int? CodTipoEsitoBS { get; set; }

            public int NumeroComponenti { get; set; }
            public int NumeroConviventiEstero { get; set; }
            public int NumeroComponentiIntegrazione { get; set; }
            public string? TipoNucleo { get; set; }

            public decimal AltriMezzi { get; set; }

            public decimal SEQ_Origine { get; set; }
            public decimal SEQ_Integrazione { get; set; }

            public decimal ISRDSU { get; set; }
            public decimal ISPDSU { get; set; }
            public decimal SEQ { get; set; }
            public decimal Detrazioni { get; set; }
            public decimal SommaRedditiStud { get; set; }

            public decimal ISEDSU { get; set; }
            public decimal ISEEDSU { get; set; }
            public decimal ISPEDSU { get; set; }

            public double ISEDSU_Attuale { get; set; }
            public double ISEEDSU_Attuale { get; set; }
            public double ISPEDSU_Attuale { get; set; }
            public double ISPDSU_Attuale { get; set; }
            public double SEQ_Attuale { get; set; }
        }
    }
}