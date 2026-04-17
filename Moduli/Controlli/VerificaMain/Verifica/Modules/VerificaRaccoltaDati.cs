using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private readonly SqlConnection _conn;
        private VerificaPipelineContext? _currentContext;

        private readonly CalcParams _calc = new();
        private readonly HashSet<(string ComuneA, string ComuneB)> _comuniEquiparati = new();

        private const string VerificaStudentiBaseSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH
DomandeRaw AS
(
    SELECT
        CAST(d.Num_domanda AS INT) AS NumDomanda,
        d.Cod_fiscale AS CodFiscale,
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
      AND ec.Cod_beneficio = 'BS'
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
        (@IncludeNonTrasmesse = 1 OR
         (
            (@AA >= '20202021' AND ISNULL(sc.StatusCompilazione,0) >= 80)
            OR (@AA = '20192020' AND ISNULL(sc.StatusCompilazione,0) > 70)
            OR (@AA < '20192020' AND ISNULL(sc.StatusCompilazione,0) > 70)
         ))
)
SELECT
    NumDomanda,
    CodFiscale,
    TipoBando
FROM D;";

        public VerificaRaccoltaDati(SqlConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        public void PopolaContesto(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _currentContext = context;

            var pipelineTable = VerificaExecutionSupport.ExecuteTimed(
                "VerificaRaccoltaDati.StudentiBase",
                () => CaricaCandidatiEInizializzaStudenti(context),
                () => $"AA={context.AnnoAccademico}");

            if (context.Students.Count == 0)
            {
                context.CalcParams = _calc.Clone();
                return;
            }

            CreateTempPipelineTable(context.Connection, context.TempPipelineTable);
            BulkCopyPipelineTargets(context.Connection, context.TempPipelineTable, pipelineTable);

            try
            {
                VerificaExecutionSupport.ExecuteTimed(
                    "VerificaRaccoltaDati.BeneficioBS",
                    () => LoadEsitoBs(context),
                    () => $"students={context.Students.Count}");

                VerificaExecutionSupport.ExecuteTimed(
                    "VerificaRaccoltaDati.StatusCompilazione",
                    () => LoadStatusCompilazione(context),
                    () => $"students={context.Students.Count}");

                VerificaExecutionSupport.ExecuteTimed(
                    "VerificaRaccoltaDati.Economici",
                    () => RaccogliEconomiciDaContesto(context),
                    () => $"students={context.Students.Count}");
                context.CalcParams = _calc.Clone();

                VerificaExecutionSupport.ExecuteTimed(
                    "VerificaRaccoltaDati.IscrizioneBase",
                    () =>
                    {
                        ResetIscrizioneState(context);
                        LoadBaseIscrizione(context);
                        LoadCarrieraPregressa(context);
                        BuildCarrieraPregressaAggregate(context);
                        LoadEsamiCatalog(context);
                    },
                    () => $"students={context.Students.Count}");

                VerificaExecutionSupport.ExecuteTimed(
                    "VerificaRaccoltaDati.EsitoBorsaFacts",
                    () => LoadEsitoBorsaSupportFacts(context),
                    () => $"students={context.Students.Count}");

                VerificaExecutionSupport.ExecuteTimed(
                    "VerificaRaccoltaDati.StatusSedeInput",
                    () => RaccogliStatusSede(context),
                    () => $"students={context.Students.Count}");
            }
            finally
            {
                DropTempPipelineTable(context.Connection, context.TempPipelineTable);
                _currentContext = null;
            }
        }

        private void ResetComuniEquiparatiState()
        {
            _comuniEquiparati.Clear();
        }

        private void RaccogliStatusSede(VerificaPipelineContext context)
        {
            ResetComuniEquiparatiState();

            LoadStatusSedeAttuale(context);
            LoadForzatureStatusSede(context);
            LoadSessoStudente(context);
            LoadDatiGeneraliDomanda(context);
            LoadMonetizzazioneMensa(context);
            LoadNucleoFamiliarePerStatusSede(context);
            LoadResidenza(context);
            LoadStatusSedeClassificationFlags(context);
            LoadEsitoPaPerAlloggio(context);
            LoadDomicilioCorrente(context);
            LoadIstanzaDomicilioAperta(context);
            LoadUltimaIstanzaChiusaDomicilio(context);

            foreach (var pair in LoadComuniEquiparatiFromDb())
                _comuniEquiparati.Add(pair);

            context.ComuniEquiparati.Clear();
            foreach (var pair in _comuniEquiparati)
                context.ComuniEquiparati.Add(pair);
        }

        private DataTable CaricaCandidatiEInizializzaStudenti(VerificaPipelineContext context)
        {
            context.Students.Clear();
            context.ComuniEquiparati.Clear();

            var table = BuildPipelineTargetsDataTable();

            using var cmd = new SqlCommand(VerificaStudentiBaseSql, _conn)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };

            cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = context.AnnoAccademico;
            cmd.Parameters.Add("@IncludeEsclusi", SqlDbType.Bit).Value = context.IncludeEsclusi;
            cmd.Parameters.Add("@IncludeNonTrasmesse", SqlDbType.Bit).Value = context.IncludeNonTrasmesse;

            var filtroCf = context.CodiciFiscaliFiltro.Count == 0
                ? null
                : new HashSet<string>(context.CodiciFiscaliFiltro.Select(NormalizeCf), StringComparer.OrdinalIgnoreCase);

            Logger.LogInfo(null, $"[VerificaRaccoltaDati] Estrazione studenti base | AA={context.AnnoAccademico} | IncludeEsclusi={context.IncludeEsclusi} | IncludeNonTrasmesse={context.IncludeNonTrasmesse}");

            using var reader = cmd.ExecuteReader();
            int read = 0;
            while (reader.Read())
            {
                int numDomanda = reader.SafeGetInt("NumDomanda");
                string codFiscale = NormalizeCf(reader.SafeGetString("CodFiscale"));
                if (string.IsNullOrWhiteSpace(codFiscale))
                {
                    read++;
                    continue;
                }

                if (filtroCf != null && !filtroCf.Contains(codFiscale))
                {
                    read++;
                    continue;
                }

                string tipoBando = (reader.SafeGetString("TipoBando") ?? string.Empty).Trim();
                string numDomandaText = numDomanda.ToString(CultureInfo.InvariantCulture);

                var info = new StudenteInfo
                {
                    TipoBando = tipoBando
                };
                info.InformazioniPersonali.CodFiscale = codFiscale;
                info.InformazioniPersonali.NumDomanda = numDomandaText;

                var key = new StudentKey(codFiscale, numDomandaText);
                context.Students[key] = info;

                table.Rows.Add(numDomanda, codFiscale, tipoBando, false, false, false, false, false);

                read++;
                if (read % 5000 == 0)
                    Logger.LogInfo(null, $"[VerificaRaccoltaDati] Studenti base letti... {read}");
            }

            Logger.LogInfo(null, $"[VerificaRaccoltaDati] Studenti base utilizzati: {context.Students.Count}");
            return table;
        }

        private static DataTable BuildPipelineTargetsDataTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("NumDomanda", typeof(int));
            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("TipoBando", typeof(string));
            dt.Columns.Add("IsOrigIT_CO", typeof(bool));
            dt.Columns.Add("IsOrigIT_DO", typeof(bool));
            dt.Columns.Add("IsOrigEE", typeof(bool));
            dt.Columns.Add("IsIntIT_CI", typeof(bool));
            dt.Columns.Add("IsIntDI", typeof(bool));
            return dt;
        }

        private static void CreateTempPipelineTable(SqlConnection conn, string tempTableName)
        {
            string sql = $@"
IF OBJECT_ID('tempdb..{tempTableName}') IS NOT NULL
    DROP TABLE {tempTableName};

CREATE TABLE {tempTableName}
(
    NumDomanda INT NOT NULL,
    CodFiscale NVARCHAR(32) NOT NULL,
    TipoBando NVARCHAR(16) NOT NULL,
    IsOrigIT_CO BIT NOT NULL,
    IsOrigIT_DO BIT NOT NULL,
    IsOrigEE BIT NOT NULL,
    IsIntIT_CI BIT NOT NULL,
    IsIntDI BIT NOT NULL
);

CREATE INDEX IX_{tempTableName.TrimStart('#')}_CF_ND ON {tempTableName}(CodFiscale, NumDomanda);
CREATE INDEX IX_{tempTableName.TrimStart('#')}_ND ON {tempTableName}(NumDomanda);";

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 9999999 };
            cmd.ExecuteNonQuery();
        }

        private static void DropTempPipelineTable(SqlConnection conn, string tempTableName)
        {
            string sql = $"IF OBJECT_ID('tempdb..{tempTableName}') IS NOT NULL DROP TABLE {tempTableName};";
            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 9999999 };
            cmd.ExecuteNonQuery();
        }

        private static void BulkCopyPipelineTargets(SqlConnection conn, string tempTableName, DataTable candidatesTable)
        {
            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, externalTransaction: null)
            {
                DestinationTableName = tempTableName,
                BulkCopyTimeout = 9999999,
                BatchSize = 5000
            };

            bulk.ColumnMappings.Add("NumDomanda", "NumDomanda");
            bulk.ColumnMappings.Add("CodFiscale", "CodFiscale");
            bulk.ColumnMappings.Add("TipoBando", "TipoBando");
            bulk.ColumnMappings.Add("IsOrigIT_CO", "IsOrigIT_CO");
            bulk.ColumnMappings.Add("IsOrigIT_DO", "IsOrigIT_DO");
            bulk.ColumnMappings.Add("IsOrigEE", "IsOrigEE");
            bulk.ColumnMappings.Add("IsIntIT_CI", "IsIntIT_CI");
            bulk.ColumnMappings.Add("IsIntDI", "IsIntDI");
            bulk.WriteToServer(candidatesTable);
        }

        private static (string ComuneA, string ComuneB) NormalizeComunePair(string? comuneA, string? comuneB)
        {
            string a = (comuneA ?? string.Empty).Trim().ToUpperInvariant();
            string b = (comuneB ?? string.Empty).Trim().ToUpperInvariant();
            return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        }
    }
}
