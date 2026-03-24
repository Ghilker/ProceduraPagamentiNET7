using ProcedureNet7.Modules.Contracts;
using ProcedureNet7.Storni;
using ProcedureNet7.Verifica.Modules;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Globalization;

namespace ProcedureNet7.Verifica
{
    internal sealed partial class Verifica : BaseProcedure<ArgsVerifica>
    {
        public Verifica(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        private string _aa = "";
        private string _folderPath = "";

        public IReadOnlyList<StudenteInfo> OutputVerificaList { get; private set; } = Array.Empty<StudenteInfo>();
        public DataTable OutputVerifica { get; private set; } = BuildOutputTable();

        public override void RunProcedure(ArgsVerifica args)
        {
            if (CONNECTION == null)
                throw new InvalidOperationException("CONNECTION null");

            _aa = args._selectedAA;
            _folderPath = args._folderPath;

            var context = BuildPipelineContext(args);

            if (context.Candidates.Count == 0)
            {
                OutputVerificaList = Array.Empty<StudenteInfo>();
                OutputVerifica = BuildOutputTable();
                Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath, true, $"Verifica_{_aa}_{DateTime.Now:yyyyMMdd_HHmmss}");
                return;
            }

            PrepareSharedCollectionState(context);

            var economiciService = new VerificaControlliDatiEconomici(context.Connection);
            var statusSedeService = new ControlloStatusSede(context.Connection);
            var importoBorsaService = new CalcoloImportoBorsa();

            IVerificaDataCollector<VerificaPipelineContext> dataCollector =
                new VerificaDataImportModule(economiciService, statusSedeService, importoBorsaService);

            var modules = CreateModules(economiciService, statusSedeService, importoBorsaService).ToList();

            try
            {
                Logger.LogInfo(null, "[Verifica] Import -> SharedData");
                dataCollector.Collect(context);

                foreach (var module in modules)
                {
                    Logger.LogInfo(null, $"[Verifica] Calculate -> {module.Name}");
                    module.Calculate(context);

                    Logger.LogInfo(null, $"[Verifica] Validate -> {module.Name}");
                    module.Validate(context);
                }

                OutputVerificaList = context.OrderedStudents;
                OutputVerifica = ToDataTable(OutputVerificaList);
                Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath, true, $"Verifica_{_aa}");
            }
            finally
            {
                CleanupSharedCollectionState(context);
            }
        }

        private VerificaPipelineContext BuildPipelineContext(ArgsVerifica args)
        {
            var context = new VerificaPipelineContext(CONNECTION!)
            {
                AnnoAccademico = _aa,
                FolderPath = _folderPath,
                IncludeEsclusi = true,
                IncludeNonTrasmesse = true,
                TempCandidatesTable = "#SS_Candidates",
                ReferenceDate = DateTime.Now
            };

            var candidates = LoadStatusSedeCandidates(
                CONNECTION!,
                context.AnnoAccademico,
                context.IncludeEsclusi,
                context.IncludeNonTrasmesse);

            var cfFilter = GetStringListArg(args, "_codiciFiscali", "CodiciFiscali", "CodiciFiscale", "CF");
            if (cfFilter != null && cfFilter.Count > 0)
            {
                var set = new HashSet<string>(cfFilter.Select(NormalizeCf), StringComparer.OrdinalIgnoreCase);
                candidates = candidates.Where(candidate => set.Contains(candidate.CodFiscale)).ToList();
            }

            context.InitializeStudents(candidates);
            return context;
        }

        private static IReadOnlyList<IVerificaModule<VerificaPipelineContext>> CreateModules(
            VerificaControlliDatiEconomici economiciService,
            ControlloStatusSede statusSedeService,
            CalcoloImportoBorsa importoBorsaService)
        {
            return new IVerificaModule<VerificaPipelineContext>[]
            {
                new EconomiciVerificaModule(economiciService),
                new StatusSedeVerificaModule(statusSedeService),
                new ImportoBorsaVerificaModule(importoBorsaService)
            };
        }

        private static void PrepareSharedCollectionState(VerificaPipelineContext context)
        {
            CreateTempCandidatesTable(context.Connection, context.TempCandidatesTable);
            BulkCopyCandidates(context.Connection, context.TempCandidatesTable, context.Candidates);
        }

        private static void CleanupSharedCollectionState(VerificaPipelineContext context)
        {
            DropTempCandidatesTable(context.Connection, context.TempCandidatesTable);
        }
    }

    internal sealed partial class Verifica
    {
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
    }

    internal sealed class VerificaCandidate
    {
        public int NumDomanda { get; init; }
        public string CodFiscale { get; init; } = "";
        public string TipoBando { get; init; } = "";
        public int CodTipoEsitoBS { get; init; }
        public double ImportoAssegnato { get; init; }
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
        ec.Imp_beneficio AS ImportoAssegnato,
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
    SELECT NumDomanda, CodTipoEsitoBS, ImportoAssegnato
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
        ISNULL(bs.ImportoAssegnato,0) AS ImportoAssegnato,
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
    ImportoAssegnato,
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
                    ImportoAssegnato = reader.SafeGetInt("ImportoAssegnato"),
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
    ImportoAssegnato Decimal(10,0) NOT NULL,
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
            dt.Columns.Add("ImportoAssegnato", typeof(double));
            dt.Columns.Add("StatusCompilazione", typeof(int));

            foreach (var candidate in candidates)
                dt.Rows.Add(candidate.NumDomanda, candidate.CodFiscale, candidate.TipoBando ?? "", candidate.CodTipoEsitoBS, candidate.ImportoAssegnato, candidate.StatusCompilazione);

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
            bulk.ColumnMappings.Add("ImportoAssegnato", "ImportoAssegnato");
            bulk.ColumnMappings.Add("StatusCompilazione", "StatusCompilazione");

            Logger.LogInfo(null, $"[Verifica] BulkCopy candidati -> {tempTableName} | Righe={dt.Rows.Count}");
            bulk.WriteToServer(dt);
        }
    }
}
