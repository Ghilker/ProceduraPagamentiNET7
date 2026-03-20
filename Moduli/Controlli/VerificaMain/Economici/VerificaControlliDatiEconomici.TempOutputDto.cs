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
        private void EnsureTempCfTableAndFill(IEnumerable<string> codiciFiscali)
        {
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

            using (var command = new SqlCommand(ensureSql, _conn))
                command.ExecuteNonQuery();

            if (codiciFiscaliDistinct.Count == 0)
            {
                using var statsCommand = new SqlCommand("UPDATE STATISTICS #CFEstrazione;", _conn);
                statsCommand.ExecuteNonQuery();
                Logger.LogInfo(21, "CF table aggiornata (vuota) + statistiche.");
                return;
            }

            Logger.LogInfo(22, "Bulk copy su tabella temporanea CF.");

            using (var dataTable = new DataTable())
            {
                dataTable.Columns.Add("Cod_fiscale", typeof(string));
                foreach (var codFiscale in codiciFiscaliDistinct) dataTable.Rows.Add(codFiscale);

                using var bulkCopy = new SqlBulkCopy(_conn, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = TempCfTable,
                    BatchSize = 10000,
                    BulkCopyTimeout = 600
                };
                bulkCopy.WriteToServer(dataTable);
            }

            using (var statsCommand = new SqlCommand("UPDATE STATISTICS #CFEstrazione;", _conn))
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
            public StudenteInfo Info { get; set; } = new StudenteInfo();

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
