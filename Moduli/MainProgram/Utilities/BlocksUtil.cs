using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    public sealed class BlockAddResult
    {
        public List<string> AlreadyHasBlock { get; } = new();
        public List<string> PreviouslyRemovedButSkipped { get; } = new();
        public List<string> ActuallyAdded { get; } = new();
    }

    public sealed class BlockRemoveResult
    {
        public List<string> ActuallyRemoved { get; } = new();
        public List<string> NothingToRemove { get; } = new();
    }

    public static class BlocksUtil
    {
        private const string TipoBandoFilter = "('lz','l2')";
        private const string CfTempTable = "#CodFiscaleTempTable";
        private const string NumTempTable = "#NumDomandaTempTable";

        private sealed record DatiGeneraliSpec(string InsertColumnsList, string SelectColumnsTemplate);

        // Key = DataSource|Database
        private static readonly ConcurrentDictionary<string, DatiGeneraliSpec> _datiGenCache = new();

        public static BlockAddResult AddBlock(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleList,
            string blockCode,
            string annoAccademico,
            string utente,
            bool inserisciGiaRimossi = false)
        {
            EnsureOpen(conn);

            var result = new BlockAddResult();

            var cfList = NormalizeCfList(codFiscaleList);
            if (cfList.Count == 0)
                return result;

            CreateAndPopulateCfTempTable(conn, transaction, cfList);

            try
            {
                var datiSpec = GetDatiGeneraliSpec(conn, transaction);
                string insertColumnsList = datiSpec.InsertColumnsList;
                string selectColumnsList = datiSpec.SelectColumnsTemplate.Replace("{BLOCCO}", "1", StringComparison.Ordinal);

                string sql = $@"
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#Base') IS NOT NULL DROP TABLE #Base;

SELECT d.Anno_accademico, d.Num_domanda, d.Cod_fiscale, d.Id_domanda
INTO #Base
FROM dbo.Domanda d
INNER JOIN {CfTempTable} cf ON cf.CodFiscale = d.Cod_fiscale
WHERE d.Anno_accademico = @aa
  AND d.tipo_bando IN {TipoBandoFilter};

CREATE UNIQUE CLUSTERED INDEX IX_Base ON #Base(Anno_accademico, Num_domanda);

-- RS1: already active
SELECT DISTINCT b.Cod_fiscale
FROM #Base b
INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp
    ON mbp.Anno_accademico = b.Anno_accademico
   AND mbp.Num_domanda     = b.Num_domanda
WHERE mbp.Cod_tipologia_blocco = @block
  AND mbp.Blocco_pagamento_attivo = 1
  AND mbp.Data_fine_validita IS NULL;

-- RS2: previously removed (or empty)
IF (@insGiaRimossi = 0)
BEGIN
    SELECT DISTINCT b.Cod_fiscale
    FROM #Base b
    INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp
        ON mbp.Anno_accademico = b.Anno_accademico
       AND mbp.Num_domanda     = b.Num_domanda
    WHERE mbp.Cod_tipologia_blocco = @block
      AND mbp.Data_fine_validita IS NOT NULL;
END
ELSE
BEGIN
    SELECT TOP (0) CAST(NULL AS NVARCHAR(16)) AS Cod_fiscale;
END

DECLARE @Added TABLE (Num_domanda DECIMAL(18,0) NOT NULL PRIMARY KEY);

-- Insert block (OUTPUT domande inserite)
INSERT INTO dbo.Motivazioni_blocco_pagamenti
    (Anno_accademico, Num_domanda, Cod_tipologia_blocco, Blocco_pagamento_attivo,
     Data_validita, Utente, Data_fine_validita, Utente_sblocco)
OUTPUT inserted.Num_domanda
INTO @Added(Num_domanda)
SELECT b.Anno_accademico, b.Num_domanda, @block, 1,
       CURRENT_TIMESTAMP, @utente, NULL, NULL
FROM #Base b
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.Motivazioni_blocco_pagamenti mbp
    WHERE mbp.Anno_accademico = b.Anno_accademico
      AND mbp.Num_domanda     = b.Num_domanda
      AND mbp.Cod_tipologia_blocco = @block
      AND mbp.Data_fine_validita IS NULL
)
AND (
    @insGiaRimossi = 1
    OR NOT EXISTS (
        SELECT 1
        FROM dbo.Motivazioni_blocco_pagamenti mbp
        WHERE mbp.Anno_accademico = b.Anno_accademico
          AND mbp.Num_domanda     = b.Num_domanda
          AND mbp.Cod_tipologia_blocco = @block
          AND mbp.Data_fine_validita IS NOT NULL
    )
);

-- Snapshot DatiGenerali_dom SOLO per domande effettivamente aggiunte
INSERT INTO dbo.DatiGenerali_dom ({insertColumnsList})
SELECT DISTINCT {selectColumnsList}
FROM dbo.Domanda d
INNER JOIN dbo.vDATIGENERALI_dom v
    ON d.Anno_accademico = v.Anno_accademico
   AND d.Num_domanda     = v.Num_domanda
INNER JOIN @Added a
    ON a.Num_domanda     = d.Num_domanda
WHERE d.Anno_accademico = @aa
  AND d.tipo_bando IN {TipoBandoFilter};

-- RS3: actually added (CF esatti)
SELECT DISTINCT b.Cod_fiscale
FROM #Base b
INNER JOIN @Added a ON a.Num_domanda = b.Num_domanda;

DROP TABLE #Base;
";

                using var cmd = new SqlCommand(sql, conn, transaction)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 300
                };

                cmd.Parameters.Add("@aa", SqlDbType.Char, 8).Value = annoAccademico;
                cmd.Parameters.Add("@block", SqlDbType.VarChar, 10).Value = blockCode;
                cmd.Parameters.Add("@utente", SqlDbType.NVarChar, 50).Value = utente ?? string.Empty;
                cmd.Parameters.Add("@insGiaRimossi", SqlDbType.Bit).Value = inserisciGiaRimossi ? 1 : 0;

                using var rdr = cmd.ExecuteReader();

                // RS1
                while (rdr.Read())
                    result.AlreadyHasBlock.Add(rdr.GetString(0));

                // RS2
                rdr.NextResult();
                while (rdr.Read())
                {
                    if (!rdr.IsDBNull(0))
                        result.PreviouslyRemovedButSkipped.Add(rdr.GetString(0));
                }

                // RS3
                rdr.NextResult();
                while (rdr.Read())
                    result.ActuallyAdded.Add(rdr.GetString(0));
            }
            finally
            {
                DropCfTempTable(conn, transaction);
            }

            return result;
        }

        public static BlockRemoveResult RemoveBlock(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> codFiscaleList,
            string blockCode,
            string annoAccademico,
            string utente)
        {
            EnsureOpen(conn);

            var result = new BlockRemoveResult();

            var cfList = NormalizeCfList(codFiscaleList);
            if (cfList.Count == 0)
                return result;

            CreateAndPopulateCfTempTable(conn, transaction, cfList);

            try
            {
                var datiSpec = GetDatiGeneraliSpec(conn, transaction);
                string insertColumnsList = datiSpec.InsertColumnsList;
                string selectColumnsList = datiSpec.SelectColumnsTemplate.Replace("{BLOCCO}", "0", StringComparison.Ordinal);

                string sql = $@"
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#Base') IS NOT NULL DROP TABLE #Base;

SELECT d.Anno_accademico, d.Num_domanda, d.Cod_fiscale, d.Id_domanda
INTO #Base
FROM dbo.Domanda d
INNER JOIN {CfTempTable} cf ON cf.CodFiscale = d.Cod_fiscale
WHERE d.Anno_accademico = @aa
  AND d.tipo_bando IN {TipoBandoFilter};

CREATE UNIQUE CLUSTERED INDEX IX_Base ON #Base(Anno_accademico, Num_domanda);

DECLARE @Removed TABLE (Num_domanda DECIMAL(18,0) NOT NULL PRIMARY KEY);

UPDATE mbp
SET Blocco_pagamento_attivo = 0,
    Data_fine_validita = CURRENT_TIMESTAMP,
    Utente_sblocco = @utente
OUTPUT inserted.Num_domanda
INTO @Removed(Num_domanda)
FROM dbo.Motivazioni_blocco_pagamenti mbp
INNER JOIN #Base b
    ON mbp.Anno_accademico = b.Anno_accademico
   AND mbp.Num_domanda     = b.Num_domanda
WHERE mbp.Cod_tipologia_blocco = @block
  AND mbp.Blocco_pagamento_attivo = 1
  AND mbp.Data_fine_validita IS NULL;

-- Snapshot DatiGenerali_dom SOLO per domande rimosse che ora NON hanno più nessun blocco attivo
INSERT INTO dbo.DatiGenerali_dom ({insertColumnsList})
SELECT DISTINCT {selectColumnsList}
FROM dbo.Domanda d
INNER JOIN dbo.vDATIGENERALI_dom v
    ON d.Anno_accademico = v.Anno_accademico
   AND d.Num_domanda     = v.Num_domanda
INNER JOIN @Removed r
    ON r.Num_domanda     = d.Num_domanda
WHERE d.Anno_accademico = @aa
  AND d.tipo_bando IN {TipoBandoFilter}
  AND NOT EXISTS (
        SELECT 1
        FROM dbo.Motivazioni_blocco_pagamenti mbp2
        WHERE mbp2.Anno_accademico = d.Anno_accademico
          AND mbp2.Num_domanda     = d.Num_domanda
          AND mbp2.Data_fine_validita IS NULL
          AND mbp2.Blocco_pagamento_attivo = 1
  );

-- RS1: actually removed (CF)
SELECT DISTINCT b.Cod_fiscale
FROM #Base b
INNER JOIN @Removed r ON r.Num_domanda = b.Num_domanda;

-- RS2: nothing to remove (CF input - removed CF)
SELECT cf.CodFiscale
FROM {CfTempTable} cf
LEFT JOIN (
    SELECT DISTINCT b.Cod_fiscale
    FROM #Base b
    INNER JOIN @Removed r ON r.Num_domanda = b.Num_domanda
) x ON x.Cod_fiscale = cf.CodFiscale
WHERE x.Cod_fiscale IS NULL;

DROP TABLE #Base;
";

                using var cmd = new SqlCommand(sql, conn, transaction)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 300
                };

                cmd.Parameters.Add("@aa", SqlDbType.Char, 8).Value = annoAccademico;
                cmd.Parameters.Add("@block", SqlDbType.VarChar, 10).Value = blockCode;
                cmd.Parameters.Add("@utente", SqlDbType.NVarChar, 50).Value = utente ?? string.Empty;

                using var rdr = cmd.ExecuteReader();

                // RS1
                while (rdr.Read())
                    result.ActuallyRemoved.Add(rdr.GetString(0));

                // RS2
                rdr.NextResult();
                while (rdr.Read())
                    result.NothingToRemove.Add(rdr.GetString(0));
            }
            finally
            {
                DropCfTempTable(conn, transaction);
            }

            return result;
        }

        public static BlockRemoveResult RemoveBlockNumDomanda(
            SqlConnection conn,
            SqlTransaction transaction,
            List<string> numDomandaList,
            string blockCode,
            string annoAccademico,
            string utente)
        {
            EnsureOpen(conn);

            var result = new BlockRemoveResult();

            var nums = NormalizeNumDomandaList(numDomandaList);
            if (nums.Count == 0)
                return result;

            CreateAndPopulateNumDomandaTempTable(conn, transaction, nums);

            try
            {
                var datiSpec = GetDatiGeneraliSpec(conn, transaction);
                string insertColumnsList = datiSpec.InsertColumnsList;
                string selectColumnsList = datiSpec.SelectColumnsTemplate.Replace("{BLOCCO}", "0", StringComparison.Ordinal);

                string sql = $@"
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#Base') IS NOT NULL DROP TABLE #Base;

SELECT d.Anno_accademico, d.Num_domanda, d.Id_domanda
INTO #Base
FROM dbo.Domanda d
INNER JOIN {NumTempTable} nd ON nd.NumDomanda = d.Num_domanda
WHERE d.Anno_accademico = @aa
  AND d.tipo_bando IN {TipoBandoFilter};

CREATE UNIQUE CLUSTERED INDEX IX_Base ON #Base(Anno_accademico, Num_domanda);

DECLARE @Removed TABLE (Num_domanda DECIMAL(18,0) NOT NULL PRIMARY KEY);

UPDATE mbp
SET Blocco_pagamento_attivo = 0,
    Data_fine_validita = CURRENT_TIMESTAMP,
    Utente_sblocco = @utente
OUTPUT inserted.Num_domanda
INTO @Removed(Num_domanda)
FROM dbo.Motivazioni_blocco_pagamenti mbp
INNER JOIN #Base b
    ON mbp.Anno_accademico = b.Anno_accademico
   AND mbp.Num_domanda     = b.Num_domanda
WHERE mbp.Cod_tipologia_blocco = @block
  AND mbp.Blocco_pagamento_attivo = 1
  AND mbp.Data_fine_validita IS NULL;

INSERT INTO dbo.DatiGenerali_dom ({insertColumnsList})
SELECT DISTINCT {selectColumnsList}
FROM dbo.Domanda d
INNER JOIN dbo.vDATIGENERALI_dom v
    ON d.Anno_accademico = v.Anno_accademico
   AND d.Num_domanda     = v.Num_domanda
INNER JOIN @Removed r
    ON r.Num_domanda     = d.Num_domanda
WHERE d.Anno_accademico = @aa
  AND d.tipo_bando IN {TipoBandoFilter}
  AND NOT EXISTS (
        SELECT 1
        FROM dbo.Motivazioni_blocco_pagamenti mbp2
        WHERE mbp2.Anno_accademico = d.Anno_accademico
          AND mbp2.Num_domanda     = d.Num_domanda
          AND mbp2.Data_fine_validita IS NULL
          AND mbp2.Blocco_pagamento_attivo = 1
  );

-- RS1: actually removed (Num_domanda come string)
SELECT DISTINCT CONVERT(VARCHAR(50), Num_domanda) AS Num_domanda
FROM @Removed;

-- RS2: nothing to remove
SELECT CONVERT(VARCHAR(50), nd.NumDomanda) AS Num_domanda
FROM {NumTempTable} nd
LEFT JOIN @Removed r ON r.Num_domanda = nd.NumDomanda
WHERE r.Num_domanda IS NULL;

DROP TABLE #Base;
";

                using var cmd = new SqlCommand(sql, conn, transaction)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 300
                };

                cmd.Parameters.Add("@aa", SqlDbType.Char, 8).Value = annoAccademico;
                cmd.Parameters.Add("@block", SqlDbType.VarChar, 10).Value = blockCode;
                cmd.Parameters.Add("@utente", SqlDbType.NVarChar, 50).Value = utente ?? string.Empty;

                using var rdr = cmd.ExecuteReader();

                while (rdr.Read())
                    result.ActuallyRemoved.Add(rdr.GetString(0));

                rdr.NextResult();
                while (rdr.Read())
                    result.NothingToRemove.Add(rdr.GetString(0));
            }
            finally
            {
                DropNumDomandaTempTable(conn, transaction);
            }

            return result;
        }

        public static Dictionary<string, bool> HasBlock(
            SqlConnection conn,
            SqlTransaction? transaction,
            List<string> codFiscaleList,
            string blockCode,
            string annoAccademico)
        {
            EnsureOpen(conn);

            var cfList = NormalizeCfList(codFiscaleList);

            var result = cfList
                .ToDictionary(cf => cf, _ => false, StringComparer.OrdinalIgnoreCase);

            if (cfList.Count == 0)
                return result;

            CreateAndPopulateCfTempTable(conn, transaction, cfList);

            try
            {
                string sql = $@"
SET NOCOUNT ON;

SELECT DISTINCT d.Cod_fiscale
FROM dbo.Domanda d
INNER JOIN dbo.Motivazioni_blocco_pagamenti mbp
    ON d.Num_domanda = mbp.Num_domanda
   AND d.Anno_accademico = mbp.Anno_accademico
INNER JOIN {CfTempTable} cf
    ON cf.CodFiscale = d.Cod_fiscale
WHERE mbp.Cod_tipologia_blocco = @block
  AND mbp.Blocco_pagamento_attivo = 1
  AND mbp.Data_fine_validita IS NULL
  AND d.Anno_accademico = @aa
  AND d.tipo_bando IN {TipoBandoFilter};
";

                using var cmd = new SqlCommand(sql, conn, transaction)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 120
                };

                cmd.Parameters.Add("@aa", SqlDbType.Char, 8).Value = annoAccademico;
                cmd.Parameters.Add("@block", SqlDbType.VarChar, 10).Value = blockCode;

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var cf = rdr.GetString(0);
                    if (result.ContainsKey(cf))
                        result[cf] = true;
                }
            }
            finally
            {
                DropCfTempTable(conn, transaction);
            }

            return result;
        }

        public static bool HasBlock(
            SqlConnection conn,
            SqlTransaction? transaction,
            string codFiscale,
            string blockCode,
            string annoAccademico)
        {
            var dict = HasBlock(conn, transaction, new List<string> { codFiscale }, blockCode, annoAccademico);
            return dict.TryGetValue(codFiscale, out bool v) && v;
        }

        private static void EnsureOpen(SqlConnection conn)
        {
            if (conn == null) throw new ArgumentNullException(nameof(conn));
            if (conn.State != ConnectionState.Open) throw new InvalidOperationException("SqlConnection must be open.");
        }

        private static List<string> NormalizeCfList(IEnumerable<string> cfs)
        {
            if (cfs == null) return new List<string>(0);

            return cfs
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length <= 16)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<decimal> NormalizeNumDomandaList(IEnumerable<string> nums)
        {
            if (nums == null) return new List<decimal>(0);

            var set = new HashSet<decimal>();
            foreach (var s in nums)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    set.Add(d);
                else if (decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out d))
                    set.Add(d);
            }

            return set.ToList();
        }

        private static void CreateAndPopulateCfTempTable(SqlConnection conn, SqlTransaction? tx, List<string> cfList)
        {
            string createSql = $@"
IF OBJECT_ID('tempdb..{CfTempTable}') IS NOT NULL
    DROP TABLE {CfTempTable};

CREATE TABLE {CfTempTable} (
    CodFiscale NVARCHAR(16) COLLATE Latin1_General_CI_AS NOT NULL PRIMARY KEY
);";

            using (var cmd = new SqlCommand(createSql, conn, tx) { CommandTimeout = 120 })
                cmd.ExecuteNonQuery();

            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, tx)
            {
                DestinationTableName = CfTempTable,
                BatchSize = 5000,
                BulkCopyTimeout = 120
            };

            var table = new DataTable();
            table.Columns.Add("CodFiscale", typeof(string));
            table.BeginLoadData();
            foreach (var cf in cfList)
                table.Rows.Add(cf);
            table.EndLoadData();

            bulk.WriteToServer(table);
        }

        private static void DropCfTempTable(SqlConnection conn, SqlTransaction? tx)
        {
            string dropSql = $@"
IF OBJECT_ID('tempdb..{CfTempTable}') IS NOT NULL
    DROP TABLE {CfTempTable};";

            using var cmd = new SqlCommand(dropSql, conn, tx) { CommandTimeout = 120 };
            cmd.ExecuteNonQuery();
        }

        private static void CreateAndPopulateNumDomandaTempTable(SqlConnection conn, SqlTransaction? tx, List<decimal> nums)
        {
            string createSql = $@"
IF OBJECT_ID('tempdb..{NumTempTable}') IS NOT NULL
    DROP TABLE {NumTempTable};

CREATE TABLE {NumTempTable} (
    NumDomanda DECIMAL(18,0) NOT NULL PRIMARY KEY
);";

            using (var cmd = new SqlCommand(createSql, conn, tx) { CommandTimeout = 120 })
                cmd.ExecuteNonQuery();

            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, tx)
            {
                DestinationTableName = NumTempTable,
                BatchSize = 5000,
                BulkCopyTimeout = 120
            };

            var table = new DataTable();
            table.Columns.Add("NumDomanda", typeof(decimal));
            table.BeginLoadData();
            foreach (var n in nums)
                table.Rows.Add(n);
            table.EndLoadData();

            bulk.WriteToServer(table);
        }

        private static void DropNumDomandaTempTable(SqlConnection conn, SqlTransaction? tx)
        {
            string dropSql = $@"
IF OBJECT_ID('tempdb..{NumTempTable}') IS NOT NULL
    DROP TABLE {NumTempTable};";

            using var cmd = new SqlCommand(dropSql, conn, tx) { CommandTimeout = 120 };
            cmd.ExecuteNonQuery();
        }

        private static DatiGeneraliSpec GetDatiGeneraliSpec(SqlConnection conn, SqlTransaction? tx)
        {
            var key = $"{conn.DataSource}|{conn.Database}";
            return _datiGenCache.GetOrAdd(key, _ => BuildDatiGeneraliSpec(conn, tx));
        }

        private static DatiGeneraliSpec BuildDatiGeneraliSpec(SqlConnection conn, SqlTransaction? tx)
        {
            var tableCols = GetColumnNames(conn, tx, "dbo.DatiGenerali_dom");
            var viewCols = new HashSet<string>(GetColumnNames(conn, tx, "dbo.vDATIGENERALI_dom"), StringComparer.OrdinalIgnoreCase);

            // Template: {BLOCCO} placeholder
            var explicitValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Data_validita"] = "CURRENT_TIMESTAMP",
                ["Utente"] = "@utente",
                ["Blocco_pagamento"] = "{BLOCCO}",
                ["Id_domanda"] = "d.Id_domanda"
            };

            var insertColumns = new List<string>(tableCols.Count);
            var selectColumns = new List<string>(tableCols.Count);

            foreach (var col in tableCols)
            {
                insertColumns.Add($"[{col}]");

                if (explicitValues.TryGetValue(col, out var expr))
                {
                    selectColumns.Add(expr);
                }
                else if (viewCols.Contains(col))
                {
                    selectColumns.Add($"v.[{col}]");
                }
                else
                {
                    selectColumns.Add("NULL");
                }
            }

            return new DatiGeneraliSpec(
                InsertColumnsList: string.Join(", ", insertColumns),
                SelectColumnsTemplate: string.Join(", ", selectColumns));
        }

        public static List<string> GetColumnNames(SqlConnection conn, SqlTransaction? tx, string objectName)
        {
            // objectName: "dbo.TableOrView"
            // sys.columns più veloce di INFORMATION_SCHEMA
            const string sql = @"
SELECT c.name
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(@obj)
ORDER BY c.column_id;";

            var cols = new List<string>(64);

            using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 120 };
            cmd.Parameters.Add("@obj", SqlDbType.NVarChar, 256).Value = objectName;

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var name = rdr.GetString(0);
                if (name.Equals("Id_DatiGenerali_dom", StringComparison.OrdinalIgnoreCase))
                    continue;

                cols.Add(name);
            }

            return cols;
        }
    }
}