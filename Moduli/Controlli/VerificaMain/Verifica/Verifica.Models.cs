using System;
using System.Collections.Generic;
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
        public Dictionary<StudentKey, EsitoBorsaFacts> EsitoBorsaFactsByStudent { get; } = new();
        public HashSet<(string ComuneA, string ComuneB)> ComuniEquiparati { get; } = new();
        public CalcParams CalcParams { get; set; } = new();
        public List<string> CodiciFiscaliFiltro { get; } = new();
    }

    internal sealed class EsitoBorsaFacts
    {
        public HashSet<string> ForzatureGenerali { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ForzaturaRinunciaNoEsclusione { get; set; }
        public string CodTipoOrdinamento { get; set; } = string.Empty;
    }
}
