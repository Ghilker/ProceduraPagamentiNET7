using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class ProceduraControlloDatiEconomici : BaseProcedure<ArgsProceduraControlloDatiEconomici>
    {
        private const string TempCfTable = "#CFEstrazione";
        private const string TempTargetsTable = "#TargetsEconomici";

        private readonly Dictionary<string, EconomicRow> _rows =
            new(StringComparer.OrdinalIgnoreCase);

        public DataTable OutputEconomici { get; private set; } = BuildOutputTable();

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

            _rows.Clear();
            OutputEconomici = BuildOutputTable();

            Log(5, $"Parametri validati. AA={aa}");

            // 1) target studenti (CF + NumDomanda)
            Log(10, "Esecuzione della query per ottenere i codici fiscali per i blocchi.");
            var targets = (args._codiciFiscali != null && args._codiciFiscali.Count > 0)
                ? LoadTargetsFromCfList(aa, args._codiciFiscali)
                : LoadTargetsAll(aa);

            Log(15, $"Targets caricati: {targets.Count}");

            foreach (var t in targets)
            {
                if (!_rows.ContainsKey(t.CodFiscale))
                {
                    _rows[t.CodFiscale] = new EconomicRow
                    {
                        CodFiscale = t.CodFiscale,
                        NumDomanda = t.NumDomanda
                    };
                }
            }

            // 1b) valori attuali da vValori_calcolati (comparativa)
            Log(18, "Caricamento valori attuali da vValori_calcolati.");
            LoadValoriCalcolatiAttuali(aa, targets);

            // 1c) esito concorso BS (cod_tipo_esito) da vEsiti_concorsi
            Log(19, "Caricamento esito concorso BS (cod_tipo_esito) da vEsiti_concorsi.");
            LoadEsitoBorsaStudio(aa, targets);

            // 2) tipologie reddito + split liste
            Log(20, "Preparazione tabella temporanea CF e bulk insert.");
            EnsureTempCfTableAndFill(targets.Select(t => t.CodFiscale));

            Log(30, "Esecuzione della query per tipologie reddito e suddivisione IT/EE (origine e integrazione).");
            var (origIT, origEE, intIT, intEE) = LoadTipologieRedditiAndSplit(aa);

            Log(35, $"Split completato. OrigIT={origIT.Count}, OrigEE={origEE.Count}, IntIT={intIT.Count}, IntEE={intEE.Count}");

            // 3) estrazione/aggregazione economici
            Log(40, "Avvio estrazione dati economici (origine).");

            if (origIT.Count > 0)
            {
                Log(45, "Esecuzione query economici IT (vCertificaz_ISEE_CO).");
                AddDatiEconomiciItaliani_CO(aa, origIT);
            }
            else
            {
                Log(45, "Skip economici IT (origine): nessun CF.");
            }

            if (origEE.Count > 0)
            {
                Log(55, "Esecuzione query economici EE (vNucleo_fam_stranieri_DO).");
                AddDatiEconomiciStranieri_DO(aa, origEE);
            }
            else
            {
                Log(55, "Skip economici EE (origine): nessun CF.");
            }

            Log(60, "Avvio estrazione dati economici (integrazione).");

            if (intIT.Count > 0)
            {
                Log(65, "Esecuzione query economici IT integrazione (vCertificaz_ISEE_CI).");
                AddDatiEconomiciItaliani_CI(aa, intIT);
            }
            else
            {
                Log(65, "Skip economici IT (integrazione): nessun CF.");
            }

            if (intEE.Count > 0)
            {
                Log(75, "Esecuzione query economici EE integrazione (vNucleo_fam_stranieri_DI).");
                AddDatiEconomiciStranieri_DI(aa, intEE);
            }
            else
            {
                Log(75, "Skip economici EE (integrazione): nessun CF.");
            }

            // 4) calcolo ISE*
            Log(85, "Calcolo ISEDSU/ISEEDSU/ISPEDSU.");
            CalcoloDatiEconomici();

            // 5) output tabellare
            Log(95, "Costruzione DataTable di output.");
            foreach (var r in _rows.Values.OrderBy(x => x.CodFiscale))
            {
                var row = OutputEconomici.NewRow();
                row["CodFiscale"] = r.CodFiscale;
                row["NumDomanda"] = r.NumDomanda ?? "";
                row["TipoRedditoOrigine"] = r.TipoRedditoOrigine ?? "";
                row["TipoRedditoIntegrazione"] = r.TipoRedditoIntegrazione ?? "";

                // esito concorso BS
                row["CodTipoEsitoBS"] = (object?)r.CodTipoEsitoBS ?? DBNull.Value;

                // suggeriti (procedura)
                row["ISR"] = r.ISR;
                row["ISP"] = r.ISP;
                row["Detrazioni"] = r.Detrazioni;
                row["ISEDSU"] = r.ISEDSU;
                row["ISEEDSU"] = r.ISEEDSU;
                row["ISPEDSU"] = r.ISPEDSU;
                row["ISPDSU"] = r.ISPDSU;
                row["SEQ"] = r.SEQ;

                // attuali (vValori_calcolati)
                row["ISEDSU_Attuale"] = r.ISEDSU_Attuale;
                row["ISEEDSU_Attuale"] = r.ISEEDSU_Attuale;
                row["ISPEDSU_Attuale"] = r.ISPEDSU_Attuale;
                row["ISPDSU_Attuale"] = r.ISPDSU_Attuale;
                row["SEQ_Attuale"] = r.SEQ_Attuale;

                OutputEconomici.Rows.Add(row);
            }

            Utilities.ExportDataTableToExcel(OutputEconomici, "D://");
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

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            var list = new List<Target>(capacity: 8192);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string cf = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Cod_fiscale").ToUpper());
                string nd = Utilities.SafeGetString(r, "Num_domanda");
                if (!string.IsNullOrWhiteSpace(cf))
                    list.Add(new Target(cf, nd));
            }

            Logger.LogInfo(12, $"Query targets completata. Righe: {list.Count}");
            return list;
        }

        private List<Target> LoadTargetsFromCfList(string aa, List<string> codiciFiscali)
        {
            var cfs = codiciFiscali
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Utilities.RemoveAllSpaces(x).ToUpper())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Logger.LogInfo(8, $"Targets da lista CF richiesti: {cfs.Count}");

            if (cfs.Count == 0) return new List<Target>();

            Logger.LogInfo(18, "Preparazione tabella temporanea CF per filtro targets.");
            EnsureTempCfTableAndFill(cfs);

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

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            var list = new List<Target>(capacity: cfs.Count);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string cf = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Cod_fiscale").ToUpper());
                string nd = Utilities.SafeGetString(r, "Num_domanda");
                if (!string.IsNullOrWhiteSpace(cf))
                    list.Add(new Target(cf, nd));
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
                .Where(t => !string.IsNullOrWhiteSpace(t.CodFiscale) && !string.IsNullOrWhiteSpace(t.NumDomanda))
                .Select(t => new Target(
                    Utilities.RemoveAllSpaces(t.CodFiscale).ToUpper(),
                    Utilities.RemoveAllSpaces(t.NumDomanda)))
                .GroupBy(t => t.CodFiscale, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
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

            using (var cmd = new SqlCommand(ensureSql, CONNECTION))
                cmd.ExecuteNonQuery();

            if (list.Count == 0)
            {
                using var statsCmd = new SqlCommand("UPDATE STATISTICS #TargetsEconomici;", CONNECTION);
                statsCmd.ExecuteNonQuery();
                return;
            }

            using (var dt = new DataTable())
            {
                dt.Columns.Add("Cod_fiscale", typeof(string));
                dt.Columns.Add("Num_domanda", typeof(string));

                foreach (var t in list)
                    dt.Rows.Add(t.CodFiscale, t.NumDomanda);

                using var bulk = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = TempTargetsTable,
                    BatchSize = 10000,
                    BulkCopyTimeout = 600
                };
                bulk.WriteToServer(dt);
            }

            using (var statsCmd = new SqlCommand("UPDATE STATISTICS #TargetsEconomici;", CONNECTION))
                statsCmd.ExecuteNonQuery();
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

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string cf = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Cod_fiscale").ToUpper());
                if (string.IsNullOrWhiteSpace(cf)) continue;

                if (!_rows.TryGetValue(cf, out var er)) continue;

                er.ISPEDSU_Attuale = Utilities.SafeGetDouble(r, "ISPEDSU");
                er.ISEDSU_Attuale = Utilities.SafeGetDouble(r, "ISEDSU");
                er.SEQ_Attuale = Utilities.SafeGetDouble(r, "SEQ");
                er.ISPDSU_Attuale = Utilities.SafeGetDouble(r, "ISPDSU");
                er.ISEEDSU_Attuale = Utilities.SafeGetDouble(r, "ISEEDSU");
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

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            using var r = cmd.ExecuteReader();
            int ordCf = r.GetOrdinal("Cod_fiscale");
            int ordEs = r.GetOrdinal("Cod_tipo_esito");

            while (r.Read())
            {
                string cf = Utilities.RemoveAllSpaces((r.IsDBNull(ordCf) ? "" : r.GetString(ordCf)).ToUpper());
                if (string.IsNullOrWhiteSpace(cf)) continue;

                if (!_rows.TryGetValue(cf, out var er)) continue;

                int? esito = null;
                if (!r.IsDBNull(ordEs))
                {
                    object v = r.GetValue(ordEs);
                    esito = v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
                }

                er.CodTipoEsitoBS = esito;
            }
        }

        // =========================
        //  TIPOLOGIE REDDITI + SPLIT
        // =========================

        private (List<string> origIT, List<string> origEE, List<string> intIT, List<string> intEE)
            LoadTipologieRedditiAndSplit(string aa)
        {
            Logger.LogInfo(30, "Esecuzione query tipologie reddito (vTipologie_redditi).");

            var origIT = new List<string>();
            var origEE = new List<string>();
            var intIT = new List<string>();
            var intEE = new List<string>();

            const string sql = @"
SELECT d.Cod_fiscale, tr.Tipo_redd_nucleo_fam_origine, tr.Tipo_redd_nucleo_fam_integr
FROM Domanda d
INNER JOIN #CFEstrazione cfe
    ON UPPER(LTRIM(RTRIM(d.Cod_fiscale))) = cfe.Cod_fiscale
INNER JOIN vTipologie_redditi tr
    ON d.Anno_accademico = tr.Anno_accademico
   AND d.Num_domanda     = tr.Num_domanda
WHERE d.Anno_accademico = @AA
  AND d.Tipo_bando = 'lz'
ORDER BY d.Cod_fiscale;";

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            int readCount = 0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                readCount++;

                string cf = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Cod_fiscale").ToUpper());
                if (string.IsNullOrWhiteSpace(cf)) continue;

                string tipoOrig = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Tipo_redd_nucleo_fam_origine"));
                string tipoInt = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Tipo_redd_nucleo_fam_integr"));

                if (!_rows.TryGetValue(cf, out var er))
                    continue;

                er.TipoRedditoOrigine = tipoOrig;
                er.TipoRedditoIntegrazione = tipoInt;

                if (tipoOrig.Equals("it", StringComparison.OrdinalIgnoreCase)) origIT.Add(cf);
                else if (tipoOrig.Equals("ee", StringComparison.OrdinalIgnoreCase)) origEE.Add(cf);

                if (tipoInt.Equals("it", StringComparison.OrdinalIgnoreCase)) intIT.Add(cf);
                else if (tipoInt.Equals("ee", StringComparison.OrdinalIgnoreCase)) intEE.Add(cf);
            }

            Logger.LogInfo(33, $"Tipologie reddito lette: {readCount}");
            return (origIT, origEE, intIT, intEE);
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - IT (CO)
        // =========================

        private void AddDatiEconomiciItaliani_CO(string aa, List<string> codiciFiscali)
        {
            Logger.LogInfo(45, "Preparazione CF table per economici IT (CO).");
            EnsureTempCfTableAndFill(codiciFiscali);

            Logger.LogInfo(46, "Esecuzione query economici IT (CO).");

            const string sql = @"
WITH codici_pagam AS (
    SELECT dpn.Cod_tipo_pagam_new AS cod_tipo_pagam
    FROM Decod_pagam_new dpn
    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND tp.visibile = 1
    UNION
    SELECT dpn.Cod_tipo_pagam_old AS cod_tipo_pagam
    FROM Decod_pagam_new dpn
    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND LEFT(tp.Cod_tipo_pagam, 3) <> 'BSA' AND tp.visibile = 1
),
sumPagamenti AS (
    SELECT SUM(p.imp_pagato) AS somma, d.Cod_fiscale
    FROM Pagamenti p
    INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
    WHERE p.Ritirato_azienda = 0
      AND p.Ese_finanziario = 2021
      AND p.cod_tipo_pagam IN (SELECT cod_tipo_pagam FROM codici_pagam)
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
      AND vs.cod_status = '05'
      AND vb.anno_accademico = @AA
    GROUP BY vb.num_domanda, vb.anno_accademico
)
SELECT
    d.Num_domanda,
    d.Cod_fiscale,
    ISNULL(sp.somma, 0) AS detrazioniADISU,
    ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse,
    cte.ISP,
    cte.ISR,
    cte.Scala_equivalenza AS SEQU
FROM Domanda d
INNER JOIN #CFEstrazione cfe ON UPPER(LTRIM(RTRIM(d.Cod_fiscale))) = cfe.Cod_fiscale
INNER JOIN vCertificaz_ISEE_CO cte ON d.Anno_accademico = cte.Anno_accademico AND d.Num_domanda = cte.Num_domanda
LEFT JOIN sumPagamenti sp ON d.Cod_fiscale = sp.Cod_fiscale
LEFT JOIN impAltreBorse iab ON d.Num_domanda = iab.num_domanda AND d.Anno_accademico = iab.anno_accademico
WHERE d.Anno_accademico = @AA
  AND d.Tipo_bando = 'LZ'
ORDER BY d.Num_domanda;";

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            int updated = 0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string cf = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Cod_fiscale").ToUpper());
                if (!_rows.TryGetValue(cf, out var er)) continue;

                double isp = Utilities.SafeGetDouble(r, "ISP");
                double isr = Utilities.SafeGetDouble(r, "ISR");
                double sequ = Utilities.SafeGetDouble(r, "SEQU");
                double det = Utilities.SafeGetDouble(r, "detrazioniADISU") + Utilities.SafeGetDouble(r, "detrazioniAltreBorse");

                er.ISP = isp;
                er.ISPDSU = isp;
                er.ISR = isr;
                er.SEQ = sequ > 0 ? sequ : 1;
                er.Detrazioni = det;
                updated++;
            }

            Logger.LogInfo(50, $"Economici IT (CO) aggiornati: {updated}");
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - EE (DO)
        // =========================

        private void AddDatiEconomiciStranieri_DO(string aa, List<string> codiciFiscali)
        {
            Logger.LogInfo(55, "Preparazione CF table per economici EE (DO).");
            EnsureTempCfTableAndFill(codiciFiscali);

            Logger.LogInfo(56, "Esecuzione query economici EE (DO).");

            const string sql = @"
WITH codici_pagam AS (
    SELECT dpn.Cod_tipo_pagam_new AS cod_tipo_pagam
    FROM Decod_pagam_new dpn
    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND tp.visibile = 1
    UNION
    SELECT dpn.Cod_tipo_pagam_old AS cod_tipo_pagam
    FROM Decod_pagam_new dpn
    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND LEFT(tp.Cod_tipo_pagam, 3) <> 'BSA' AND tp.visibile = 1
),
sumPagamenti AS (
    SELECT SUM(p.imp_pagato) AS somma, d.Cod_fiscale
    FROM Pagamenti p
    INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
    WHERE p.Ritirato_azienda = 0
      AND p.Ese_finanziario = 2021
      AND p.cod_tipo_pagam IN (SELECT cod_tipo_pagam FROM codici_pagam)
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
      AND vs.cod_status = '05'
      AND vb.anno_accademico = @AA
    GROUP BY vb.num_domanda, vb.anno_accademico
)
SELECT
    d.Cod_fiscale,
    nf.Numero_componenti,
    nf.Redd_complessivo,
    nf.Patr_mobiliare,
    nf.Superf_abitaz_MQ,
    nf.Sup_compl_altre_MQ,
    dc.Franchigia,
    dc.franchigia_pat_mobiliare,
    tasso_rendimento_pat_mobiliare,
    ISNULL(sp.somma, 0) AS detrazioniADISU,
    ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse
FROM vNucleo_fam_stranieri_DO nf
INNER JOIN Domanda d ON d.Anno_accademico = nf.Anno_accademico AND d.Num_domanda = nf.Num_domanda
INNER JOIN DatiGenerali_con dc ON d.Anno_accademico = dc.Anno_accademico
INNER JOIN #CFEstrazione cfe ON UPPER(LTRIM(RTRIM(d.Cod_fiscale))) = cfe.Cod_fiscale
LEFT JOIN sumPagamenti sp ON d.Cod_fiscale = sp.Cod_fiscale
LEFT JOIN impAltreBorse iab ON d.Num_domanda = iab.num_domanda AND d.Anno_accademico = iab.anno_accademico
WHERE d.Anno_accademico = @AA
ORDER BY d.Cod_fiscale;";

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            int updated = 0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string cf = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Cod_fiscale").ToUpper());
                if (!_rows.TryGetValue(cf, out var er)) continue;

                int nComp = Utilities.SafeGetInt(r, "Numero_componenti");
                double superfAb = Utilities.SafeGetDouble(r, "Superf_abitaz_MQ");
                double supAltre = Utilities.SafeGetDouble(r, "Sup_compl_altre_MQ");
                double franch = Utilities.SafeGetDouble(r, "Franchigia");

                double patrMob = Utilities.SafeGetDouble(r, "Patr_mobiliare");
                double franchMob = Utilities.SafeGetDouble(r, "franchigia_pat_mobiliare");
                double tasso = Utilities.SafeGetDouble(r, "tasso_rendimento_pat_mobiliare");
                double redd = Utilities.SafeGetDouble(r, "Redd_complessivo");

                double det = Utilities.SafeGetDouble(r, "detrazioniADISU") + Utilities.SafeGetDouble(r, "detrazioniAltreBorse");

                double calcISP = Math.Max((superfAb + supAltre) * 500.0 - franch, 0.0);
                double calcPatrMob = Math.Max(patrMob - franchMob, 0.0);
                double calcISPDSU = calcISP + calcPatrMob;
                double calcISR = Math.Max(redd + calcPatrMob * tasso - det, 0.0);

                er.ISP = calcISP;
                er.ISPDSU = calcISPDSU;
                er.ISR = calcISR;
                er.SEQ = CalculateSEQ(nComp);
                er.Detrazioni = det;
                updated++;
            }

            Logger.LogInfo(60, $"Economici EE (DO) aggiornati: {updated}");
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - IT integrazione (CI)
        // =========================

        private void AddDatiEconomiciItaliani_CI(string aa, List<string> codiciFiscali)
        {
            Logger.LogInfo(65, "Preparazione CF table per economici IT integrazione (CI).");
            EnsureTempCfTableAndFill(codiciFiscali);

            Logger.LogInfo(66, "Esecuzione query economici IT integrazione (CI).");

            const string sql = @"
WITH codici_pagam AS (
    SELECT dpn.Cod_tipo_pagam_new AS cod_tipo_pagam
    FROM Decod_pagam_new dpn
    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND tp.visibile = 1
    UNION
    SELECT dpn.Cod_tipo_pagam_old AS cod_tipo_pagam
    FROM Decod_pagam_new dpn
    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND LEFT(tp.Cod_tipo_pagam, 3) <> 'BSA' AND tp.visibile = 1
),
sumPagamenti AS (
    SELECT SUM(p.imp_pagato) AS somma, d.Cod_fiscale
    FROM Pagamenti p
    INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
    WHERE p.Ritirato_azienda = 0
      AND p.Ese_finanziario = 2021
      AND p.cod_tipo_pagam IN (SELECT cod_tipo_pagam FROM codici_pagam)
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
      AND vs.cod_status = '05'
      AND vb.anno_accademico = @AA
    GROUP BY vb.num_domanda, vb.anno_accademico
)
SELECT
    d.Num_domanda,
    d.Cod_fiscale,
    ISNULL(sp.somma, 0) AS detrazioniADISU,
    ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse,
    cte.ISP,
    cte.ISR,
    cte.Scala_equivalenza AS SEQU
FROM Domanda d
INNER JOIN #CFEstrazione cfe ON UPPER(LTRIM(RTRIM(d.Cod_fiscale))) = cfe.Cod_fiscale
INNER JOIN vCertificaz_ISEE_CI cte ON d.Anno_accademico = cte.Anno_accademico AND d.Num_domanda = cte.Num_domanda
LEFT JOIN sumPagamenti sp ON d.Cod_fiscale = sp.Cod_fiscale
LEFT JOIN impAltreBorse iab ON d.Num_domanda = iab.num_domanda AND d.Anno_accademico = iab.anno_accademico
WHERE d.Anno_accademico = @AA
  AND d.Tipo_bando = 'LZ'
ORDER BY d.Num_domanda;";

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            int updated = 0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string cf = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Cod_fiscale").ToUpper());
                if (!_rows.TryGetValue(cf, out var er)) continue;

                double isp = Utilities.SafeGetDouble(r, "ISP");
                double isr = Utilities.SafeGetDouble(r, "ISR");
                double sequ = Utilities.SafeGetDouble(r, "SEQU");
                double det = Utilities.SafeGetDouble(r, "detrazioniADISU") + Utilities.SafeGetDouble(r, "detrazioniAltreBorse");

                if (er.SEQ <= 0) er.SEQ = sequ > 0 ? sequ : 1;

                er.ISP += isp;
                er.ISR += isr;
                er.ISPDSU = er.ISP;
                er.Detrazioni += det;

                updated++;
            }

            Logger.LogInfo(70, $"Economici IT integrazione (CI) aggiornati: {updated}");
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - EE integrazione (DI)
        // =========================

        private void AddDatiEconomiciStranieri_DI(string aa, List<string> codiciFiscali)
        {
            Logger.LogInfo(75, "Preparazione CF table per economici EE integrazione (DI).");
            EnsureTempCfTableAndFill(codiciFiscali);

            Logger.LogInfo(76, "Esecuzione query economici EE integrazione (DI).");

            const string sql = @"
WITH codici_pagam AS (
    SELECT dpn.Cod_tipo_pagam_new AS cod_tipo_pagam
    FROM Decod_pagam_new dpn
    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND tp.visibile = 1
    UNION
    SELECT dpn.Cod_tipo_pagam_old AS cod_tipo_pagam
    FROM Decod_pagam_new dpn
    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND LEFT(tp.Cod_tipo_pagam, 3) <> 'BSA' AND tp.visibile = 1
),
sumPagamenti AS (
    SELECT SUM(p.imp_pagato) AS somma, d.Cod_fiscale
    FROM Pagamenti p
    INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
    WHERE p.Ritirato_azienda = 0
      AND p.Ese_finanziario = 2021
      AND p.cod_tipo_pagam IN (SELECT cod_tipo_pagam FROM codici_pagam)
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
      AND vs.cod_status = '05'
      AND vb.anno_accademico = @AA
    GROUP BY vb.num_domanda, vb.anno_accademico
)
SELECT
    d.Cod_fiscale,
    nf.Numero_componenti,
    nf.Redd_complessivo,
    nf.Patr_mobiliare,
    nf.Superf_abitaz_MQ,
    nf.Sup_compl_altre_MQ,
    tasso_rendimento_pat_mobiliare,
    ISNULL(sp.somma, 0) AS detrazioniADISU,
    ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse
FROM vNucleo_fam_stranieri_DI nf
INNER JOIN Domanda d ON d.Anno_accademico = nf.Anno_accademico AND d.Num_domanda = nf.Num_domanda
INNER JOIN DatiGenerali_con dc ON d.Anno_accademico = dc.Anno_accademico
INNER JOIN #CFEstrazione cfe ON UPPER(LTRIM(RTRIM(d.Cod_fiscale))) = cfe.Cod_fiscale
LEFT JOIN sumPagamenti sp ON d.Cod_fiscale = sp.Cod_fiscale
LEFT JOIN impAltreBorse iab ON d.Num_domanda = iab.num_domanda AND d.Anno_accademico = iab.anno_accademico
WHERE d.Anno_accademico = @AA
ORDER BY d.Cod_fiscale;";

            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddWithValue("@AA", aa);

            int updated = 0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string cf = Utilities.RemoveAllSpaces(Utilities.SafeGetString(r, "Cod_fiscale").ToUpper());
                if (!_rows.TryGetValue(cf, out var er)) continue;

                int nComp = Utilities.SafeGetInt(r, "Numero_componenti");
                double superfAb = Utilities.SafeGetDouble(r, "Superf_abitaz_MQ");
                double supAltre = Utilities.SafeGetDouble(r, "Sup_compl_altre_MQ");

                double patrMob = Utilities.SafeGetDouble(r, "Patr_mobiliare");
                double tasso = Utilities.SafeGetDouble(r, "tasso_rendimento_pat_mobiliare");
                double redd = Utilities.SafeGetDouble(r, "Redd_complessivo");

                double det = Utilities.SafeGetDouble(r, "detrazioniADISU") + Utilities.SafeGetDouble(r, "detrazioniAltreBorse");

                double calcISP = Math.Max((superfAb + supAltre) * 500.0, 0.0);
                double calcPatrMob = Math.Max(patrMob, 0.0);
                double calcISPDSU = calcISP + calcPatrMob;
                double calcISR = Math.Max(redd + calcPatrMob * tasso, 0.0);

                if (er.SEQ <= 0) er.SEQ = CalculateSEQ(nComp);

                er.ISP += calcISP;
                er.ISPDSU += calcISPDSU;
                er.ISR += calcISR;
                er.Detrazioni += det;

                updated++;
            }

            Logger.LogInfo(80, $"Economici EE integrazione (DI) aggiornati: {updated}");
        }

        // =========================
        //  CALCOLO ISE*
        // =========================

        private void CalcoloDatiEconomici()
        {
            Logger.LogInfo(85, "Calcolo indicatori ISE* (ISEDSU/ISEEDSU/ISPEDSU).");

            int count = 0;
            foreach (var er in _rows.Values)
            {
                if (er.SEQ <= 0) er.SEQ = 1;

                double isedsu = er.ISR + 0.2 * er.ISPDSU;
                double iseed = isedsu / er.SEQ;
                double ispe = er.ISPDSU / er.SEQ;

                er.ISEDSU = Math.Round(isedsu, 2);
                er.ISEEDSU = Math.Round(iseed, 2);
                er.ISPEDSU = Math.Round(ispe, 2);

                count++;
            }

            Logger.LogInfo(90, $"Calcolo ISE* completato. Record: {count}");
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

            var cfs = codiciFiscali
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Utilities.RemoveAllSpaces(x).ToUpper())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Logger.LogInfo(20, $"Preparazione {TempCfTable}. CF distinti: {cfs.Count}");

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

            using (var cmd = new SqlCommand(ensureSql, CONNECTION))
                cmd.ExecuteNonQuery();

            if (cfs.Count == 0)
            {
                using var statsCmd = new SqlCommand("UPDATE STATISTICS #CFEstrazione;", CONNECTION);
                statsCmd.ExecuteNonQuery();
                Logger.LogInfo(21, "CF table aggiornata (vuota) + statistiche.");
                return;
            }

            Logger.LogInfo(22, "Bulk copy su tabella temporanea CF.");

            using (var dt = new DataTable())
            {
                dt.Columns.Add("Cod_fiscale", typeof(string));
                foreach (var cf in cfs) dt.Rows.Add(cf);

                using var bulk = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = TempCfTable,
                    BatchSize = 10000,
                    BulkCopyTimeout = 600
                };
                bulk.WriteToServer(dt);
            }

            using (var statsCmd = new SqlCommand("UPDATE STATISTICS #CFEstrazione;", CONNECTION))
                statsCmd.ExecuteNonQuery();

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

            // esito concorso BS (vEsiti_concorsi)
            dt.Columns.Add("CodTipoEsitoBS", typeof(int));

            // suggeriti (procedura)
            dt.Columns.Add("ISR", typeof(double));
            dt.Columns.Add("ISP", typeof(double));
            dt.Columns.Add("Detrazioni", typeof(double));
            dt.Columns.Add("ISEDSU", typeof(double));
            dt.Columns.Add("ISEEDSU", typeof(double));
            dt.Columns.Add("ISPEDSU", typeof(double));
            dt.Columns.Add("ISPDSU", typeof(double));
            dt.Columns.Add("SEQ", typeof(double));

            // attuali (vValori_calcolati)
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

            // esito concorso BS (vEsiti_concorsi)
            public int? CodTipoEsitoBS { get; set; }

            // suggeriti (procedura)
            public double ISR { get; set; }
            public double ISP { get; set; }
            public double ISPDSU { get; set; }
            public double SEQ { get; set; }
            public double Detrazioni { get; set; }

            public double ISEDSU { get; set; }
            public double ISEEDSU { get; set; }
            public double ISPEDSU { get; set; }

            // attuali (vValori_calcolati)
            public double ISEDSU_Attuale { get; set; }
            public double ISEEDSU_Attuale { get; set; }
            public double ISPEDSU_Attuale { get; set; }
            public double ISPDSU_Attuale { get; set; }
            public double SEQ_Attuale { get; set; }
        }
    }
}