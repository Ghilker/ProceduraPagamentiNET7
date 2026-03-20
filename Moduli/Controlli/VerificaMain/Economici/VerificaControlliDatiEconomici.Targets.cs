using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaControlliDatiEconomici
    {
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

            using var command = new SqlCommand(sql, _conn);
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

            using var command = new SqlCommand(sql, _conn);
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

            using (var command = new SqlCommand(ensureSql, _conn))
                command.ExecuteNonQuery();

            if (list.Count == 0)
            {
                using var statsCommand = new SqlCommand("UPDATE STATISTICS #TargetsEconomici;", _conn);
                statsCommand.ExecuteNonQuery();
                return;
            }

            using (var dataTable = new DataTable())
            {
                dataTable.Columns.Add("Cod_fiscale", typeof(string));
                dataTable.Columns.Add("Num_domanda", typeof(string));

                foreach (var target in list)
                    dataTable.Rows.Add(target.CodFiscale, target.NumDomanda);

                using var bulkCopy = new SqlBulkCopy(_conn, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = TempTargetsTable,
                    BatchSize = 10000,
                    BulkCopyTimeout = 600
                };
                bulkCopy.WriteToServer(dataTable);
            }

            using (var statsCommand = new SqlCommand("UPDATE STATISTICS #TargetsEconomici;", _conn))
                statsCommand.ExecuteNonQuery();
        }

    }
}
