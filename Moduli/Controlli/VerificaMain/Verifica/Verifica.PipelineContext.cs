using System;
using System.Collections.Generic;
using System.Globalization;
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
        public string TempCandidatesTable { get; set; } = "#SS_Candidates";

        public List<VerificaCandidate> Candidates { get; } = new();
        public Dictionary<StudentKey, StudenteInfo> Students { get; } = new();
        public HashSet<StudentKey> CandidateKeys { get; } = new();
        public CalcParams CalcParams { get; set; } = new();

        public IReadOnlyList<StudenteInfo> OrderedStudents =>
            Students
                .OrderBy(pair => pair.Key.CodFiscale, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key.NumDomanda, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value)
                .ToList();

        public void InitializeStudents(IEnumerable<VerificaCandidate> candidates)
        {
            Candidates.Clear();
            Candidates.AddRange(candidates);

            Students.Clear();
            CandidateKeys.Clear();

            foreach (var candidate in Candidates)
            {
                string cf = NormalizeCf(candidate.CodFiscale);
                string numDomanda = candidate.NumDomanda.ToString(CultureInfo.InvariantCulture);
                var key = new StudentKey(cf, numDomanda);

                CandidateKeys.Add(key);

                var info = new StudenteInfo();
                info.InformazioniPersonali.CodFiscale = cf;
                info.InformazioniPersonali.NumDomanda = numDomanda;
                Students[key] = info;
            }
        }

        private static string NormalizeCf(string? value)
            => (value ?? "").Trim().ToUpperInvariant();
    }
}
