using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
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

        private readonly record struct Target(string CodFiscale, string NumDomanda);

        private sealed class EconomicRow
        {
            public EconomicRow(StudenteInfo info)
            {
                Info = info ?? throw new ArgumentNullException(nameof(info));
            }

            public StudenteInfo Info { get; }
            private InformazioniEconomiche Eco => Info.InformazioniEconomiche;
            private InformazioniPersonali Pers => Info.InformazioniPersonali;

            public string CodFiscale
            {
                get => Pers.CodFiscale ?? "";
                set => Pers.CodFiscale = value ?? "";
            }

            public string? NumDomanda
            {
                get => Pers.NumDomanda;
                set => Pers.NumDomanda = value ?? "";
            }

            public string? TipoRedditoOrigine
            {
                get => Eco.TipoRedditoOrigine;
                set => Eco.TipoRedditoOrigine = value ?? "";
            }

            public string? TipoRedditoIntegrazione
            {
                get => Eco.TipoRedditoIntegrazione;
                set => Eco.TipoRedditoIntegrazione = value ?? "";
            }

            public int? CodTipoEsitoBS
            {
                get => Eco.CodTipoEsitoBS;
                set => Eco.CodTipoEsitoBS = value;
            }

            public int NumeroComponenti
            {
                get => Eco.NumeroComponenti;
                set => Eco.NumeroComponenti = value;
            }

            public int NumeroConviventiEstero
            {
                get => Eco.NumeroConviventiEstero;
                set => Eco.NumeroConviventiEstero = value;
            }

            public int NumeroComponentiIntegrazione
            {
                get => Eco.NumeroComponentiIntegrazione;
                set => Eco.NumeroComponentiIntegrazione = value;
            }

            public string? TipoNucleo
            {
                get => Eco.TipoNucleo;
                set => Eco.TipoNucleo = value ?? "";
            }

            public decimal AltriMezzi
            {
                get => Eco.AltriMezzi;
                set => Eco.AltriMezzi = value;
            }

            public decimal SEQ_Origine
            {
                get => Eco.SEQ_Origine;
                set => Eco.SEQ_Origine = value;
            }

            public decimal SEQ_Integrazione
            {
                get => Eco.SEQ_Integrazione;
                set => Eco.SEQ_Integrazione = value;
            }

            public decimal ISRDSU
            {
                get => Eco.ISRDSU;
                set => Eco.ISRDSU = value;
            }

            public decimal ISPDSU
            {
                get => Eco.ISPDSU;
                set => Eco.ISPDSU = value;
            }

            public decimal SEQ
            {
                get => Eco.SEQ;
                set => Eco.SEQ = value;
            }

            public decimal Detrazioni
            {
                get => Eco.Detrazioni;
                set => Eco.Detrazioni = value;
            }

            public decimal SommaRedditiStud
            {
                get => Eco.SommaRedditiStud;
                set => Eco.SommaRedditiStud = value;
            }

            public decimal ISEDSU
            {
                get => Eco.ISEDSU;
                set => Eco.ISEDSU = value;
            }

            public decimal ISEEDSU
            {
                get => Eco.ISEEDSU;
                set => Eco.ISEEDSU = value;
            }

            public decimal ISPEDSU
            {
                get => Eco.ISPEDSU;
                set => Eco.ISPEDSU = value;
            }

            public double ISEDSU_Attuale
            {
                get => Eco.ISEDSU_Attuale;
                set => Eco.ISEDSU_Attuale = value;
            }

            public double ISEEDSU_Attuale
            {
                get => Eco.ISEEDSU_Attuale;
                set => Eco.ISEEDSU_Attuale = value;
            }

            public double ISPEDSU_Attuale
            {
                get => Eco.ISPEDSU_Attuale;
                set => Eco.ISPEDSU_Attuale = value;
            }

            public double ISPDSU_Attuale
            {
                get => Eco.ISPDSU_Attuale;
                set => Eco.ISPDSU_Attuale = value;
            }

            public double SEQ_Attuale
            {
                get => Eco.SEQ_Attuale;
                set => Eco.SEQ_Attuale = value;
            }
        }
    }
}
