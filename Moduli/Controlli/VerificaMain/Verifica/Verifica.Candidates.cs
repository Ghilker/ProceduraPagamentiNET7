using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    internal sealed class VerificaCandidate
    {
        public int NumDomanda { get; init; }
        public string CodFiscale { get; init; } = "";
        public string TipoBando { get; init; } = "";
        public int CodTipoEsitoBS { get; init; }
        public int StatusCompilazione { get; init; }
    }

    internal sealed partial class Verifica
    {
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

        private static List<VerificaCandidate> LoadStatusSedeCandidates(
            SqlConnection conn,
            string aa,
            bool includeEsclusi,
            bool includeNonTrasmesse)
        {
            var list = new List<VerificaCandidate>(capacity: 50_000);

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
                list.Add(new VerificaCandidate
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

        private static void BulkCopyCandidates(SqlConnection conn, string tempTableName, IReadOnlyCollection<VerificaCandidate> candidates)
        {
            var dt = new DataTable();
            dt.Columns.Add("NumDomanda", typeof(int));
            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("TipoBando", typeof(string));
            dt.Columns.Add("CodTipoEsitoBS", typeof(int));
            dt.Columns.Add("StatusCompilazione", typeof(int));

            foreach (var candidate in candidates)
                dt.Rows.Add(candidate.NumDomanda, candidate.CodFiscale, candidate.TipoBando ?? "", candidate.CodTipoEsitoBS, candidate.StatusCompilazione);

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
    }
}
