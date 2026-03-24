using ProcedureNet7.Modules.Contracts;
using ProcedureNet7.Storni;
using ProcedureNet7.Verifica.Modules;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    internal sealed partial class Verifica : BaseProcedure<ArgsVerifica>
    {
        public Verifica(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        private string _aa = "";
        private string _folderPath = "";

        public IReadOnlyList<StudenteInfo> OutputVerificaList { get; private set; } = Array.Empty<StudenteInfo>();
        public DataTable OutputVerifica { get; private set; } = BuildOutputTable();
        public IReadOnlyList<StudenteInfo> StudentiInfoList { get; private set; } = Array.Empty<StudenteInfo>();
        public DataTable OutputCarrieraPregressa { get; private set; } = BuildCarrieraPregressaOutputTable();

        public override void RunProcedure(ArgsVerifica args)
        {
            if (CONNECTION == null)
                throw new InvalidOperationException("CONNECTION null");

            _aa = args._selectedAA;
            _folderPath = args._folderPath;

            var context = BuildPipelineContext(args);
            StudentiInfoList = context.OrderedStudents;

            if (context.Candidates.Count == 0)
            {
                OutputVerificaList = Array.Empty<StudenteInfo>();
                OutputVerifica = BuildOutputTable();
                OutputCarrieraPregressa = BuildCarrieraPregressaOutputTable();
                Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath);
                return;
            }

            PrepareSharedCollectionState(context);

            var modules = CreateModules(context).ToList();

            try
            {
                foreach (var module in modules)
                {
                    Logger.LogInfo(null, $"[Verifica] Collect -> {module.Name}");
                    module.Collect(context);
                }

                foreach (var module in modules)
                {
                    Logger.LogInfo(null, $"[Verifica] Calculate -> {module.Name}");
                    module.Calculate(context);

                    Logger.LogInfo(null, $"[Verifica] Validate -> {module.Name}");
                    module.Validate(context);
                }

                OutputVerificaList = context.OrderedStudents;
                StudentiInfoList = OutputVerificaList;
                OutputVerifica = ToDataTable(OutputVerificaList);
                OutputCarrieraPregressa = ToCarrieraPregressaDataTable(OutputVerificaList);
                Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath);
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
                IncludeEsclusi = GetBoolArg(args, "_includeEsclusi", "IncludeEsclusi", fallback: true),
                IncludeNonTrasmesse = GetBoolArg(args, "_includeNonTrasmesse", "IncludeNonTrasmesse", fallback: true),
                TempCandidatesTable = "#SS_Candidates"
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

        private static IReadOnlyList<IVerificaModule<VerificaPipelineContext>> CreateModules(VerificaPipelineContext context)
        {
            return new IVerificaModule<VerificaPipelineContext>[]
            {
                new EconomiciVerificaModule(new VerificaControlliDatiEconomici(context.Connection)),
                new StatusSedeVerificaModule(new ControlloStatusSede(context.Connection)),
                new IscrizioneVerificaModule(),
                new ImportoBorsaVerificaModule(new CalcoloImportoBorsa())
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
}
