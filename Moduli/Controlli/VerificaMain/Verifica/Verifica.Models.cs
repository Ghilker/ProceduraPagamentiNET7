using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;

namespace ProcedureNet7.Verifica
{
    internal sealed class VerificaPipelineContext
    {
        public VerificaPipelineContext(SqlConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public SqlConnection Connection { get; }
        public string AnnoAccademico { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public bool IncludeEsclusi { get; set; }
        public bool IncludeNonTrasmesse { get; set; }
        public string TempPipelineTable { get; set; } = "#VerificaPipelineTargets";
        public DateTime ReferenceDate { get; set; } = DateTime.Now;

        public Dictionary<StudentKey, StudenteInfo> Students { get; } = new();
        public HashSet<(string ComuneA, string ComuneB)> ComuniEquiparati { get; } = new();
        public CalcParams CalcParams { get; set; } = new();
        public List<string> CodiciFiscaliFiltro { get; } = new();

        public IReadOnlyList<StudenteInfo> OrderedStudents =>
            Students
                .OrderBy(pair => pair.Key.CodFiscale, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key.NumDomanda, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value)
                .ToList();
    }
}
