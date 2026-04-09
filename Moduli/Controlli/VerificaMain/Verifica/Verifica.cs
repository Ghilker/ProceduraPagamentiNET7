using ProcedureNet7.Storni;
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

        public override void RunProcedure(ArgsVerifica args)
        {
            if (CONNECTION == null)
                throw new InvalidOperationException("CONNECTION null");

            _aa = args._selectedAA;
            _folderPath = args._folderPath;

            var context = BuildPipelineContext(args);
            var raccoltaDati = new global::ProcedureNet7.VerificaRaccoltaDati(context.Connection);
            var modules = BuildModules();

            VerificaExecutionSupport.ExecuteTimed("Verifica.RaccoltaDati", () => raccoltaDati.PopolaContesto(context), () => $"AA={context.AnnoAccademico}");

            if (context.Students.Count == 0)
            {
                OutputVerificaList = Array.Empty<StudenteInfo>();
                OutputVerifica = BuildOutputTable();
                Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath);
                return;
            }

            foreach (var module in modules)
            {
                VerificaExecutionSupport.ExecuteTimed($"Verifica.Module.{module.Name}", () => module.Calculate(context), () => $"students={context.Students.Count}");
            }

            VerificaExecutionSupport.ExecuteTimed("Verifica.Output", () =>
            {
                (OutputVerificaList, OutputVerifica) = BuildOrderedOutputs(context.Students);
            }, () => $"students={context.Students.Count}");

            VerificaExecutionSupport.ExecuteTimed("Verifica.Export", () => Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath), () => $"rows={OutputVerifica.Rows.Count}");
        }

        private static IReadOnlyList<IVerificaModule> BuildModules()
            => new IVerificaModule[]
            {
                new VerificaControlliDatiEconomici(),
                new ControlloStatusSede(),
                new CalcoloImportoBorsa(),
                new CalcoloEsitoBorsa()
            };

        private VerificaPipelineContext BuildPipelineContext(ArgsVerifica args)
        {
            var context = new VerificaPipelineContext(CONNECTION!)
            {
                AnnoAccademico = _aa,
                FolderPath = _folderPath,
                IncludeEsclusi = true,
                IncludeNonTrasmesse = true,
                TempPipelineTable = "#VerificaPipelineTargets"
            };

            var cfFilter = GetStringListArg(args, "_codiciFiscali", "CodiciFiscali", "CodiciFiscale", "CF");
            if (cfFilter != null && cfFilter.Count > 0)
            {
                foreach (var cf in cfFilter.Select(NormalizeCf).Where(cf => !string.IsNullOrWhiteSpace(cf)).Distinct(StringComparer.OrdinalIgnoreCase))
                    context.CodiciFiscaliFiltro.Add(cf);
            }

            return context;
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

    internal sealed partial class Verifica
    {
        private static string NormalizeCf(string? cf)
            => (cf ?? "").Trim().ToUpperInvariant();
    }
}
