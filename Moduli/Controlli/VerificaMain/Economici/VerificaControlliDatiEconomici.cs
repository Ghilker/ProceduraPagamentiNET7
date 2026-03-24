using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaControlliDatiEconomici
    {
        private readonly SqlConnection _conn;

        public VerificaControlliDatiEconomici(SqlConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        private const string TempCfTable = "#CFEstrazione";
        private const string TempTargetsTable = "#TargetsEconomici";

        private string _aa = "";

        // Adapter tecnico: non contiene più lo stato economico vero.
        // Lo stato vero vive in StudenteInfo.InformazioniEconomiche.
        private readonly Dictionary<StudentKey, EconomicRow> _rows = new();

        private readonly Dictionary<StudentKey, StudenteInfo> _studentsByKey = new();
        private readonly Dictionary<StudentKey, int> _statusInpsOrigineByKey = new();
        private readonly Dictionary<StudentKey, bool> _coAttestazioneOkByKey = new();
        private readonly Dictionary<string, int> _statusInpsIntegrazioneByCf = new(StringComparer.OrdinalIgnoreCase);

        public DataTable OutputEconomici { get; private set; } = BuildOutputTable();
        public IReadOnlyList<ValutazioneEconomici> OutputEconomiciList { get; private set; } = Array.Empty<ValutazioneEconomici>();

        private readonly CalcParams _calc = new();

        public CalcParams GetCalcParams()
        {
            return _calc.Clone();
        }
        private readonly List<Target> _targets = new();
        private bool _collectionCompleted;
        private bool _calculationCompleted;

        private void ResetState(string aa)
        {
            _aa = aa;
            _rows.Clear();
            _studentsByKey.Clear();
            _statusInpsOrigineByKey.Clear();
            _statusInpsIntegrazioneByCf.Clear();
            _coAttestazioneOkByKey.Clear();
            _targets.Clear();
            OutputEconomici = BuildOutputTable();
            OutputEconomiciList = Array.Empty<ValutazioneEconomici>();
            _collectionCompleted = false;
            _calculationCompleted = false;
        }

        private void InitializeStudentsFromContext(IReadOnlyDictionary<StudentKey, StudenteInfo> students)
        {
            foreach (var pair in students)
            {
                var sourceKey = pair.Key;
                var info = pair.Value ?? new StudenteInfo();
                string cf = NormalizeCf(sourceKey.CodFiscale);
                string numDomanda = NormalizeDomanda(sourceKey.NumDomanda);
                var key = new StudentKey(cf, numDomanda);

                info.InformazioniPersonali.CodFiscale = cf;
                info.InformazioniPersonali.NumDomanda = numDomanda;
                info.InformazioniEconomiche ??= new InformazioniEconomiche();

                _studentsByKey[key] = info;
                _targets.Add(new Target(cf, numDomanda));

                if (!_rows.ContainsKey(key))
                    _rows[key] = new EconomicRow(info);
            }
        }

        private void InitializeStudentsFromTargets(
            IReadOnlyCollection<Target> targets,
            IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey)
        {
            foreach (var target in targets)
            {
                string cf = NormalizeCf(target.CodFiscale);
                string numDomanda = NormalizeDomanda(target.NumDomanda);
                var key = new StudentKey(cf, numDomanda);

                StudenteInfo info;
                if (infoByKey != null && infoByKey.TryGetValue(key, out var existingInfo) && existingInfo != null)
                {
                    info = existingInfo;
                }
                else
                {
                    info = new StudenteInfo();
                }

                info.InformazioniPersonali.CodFiscale = cf;
                info.InformazioniPersonali.NumDomanda = numDomanda;

                info.InformazioniEconomiche ??= new InformazioniEconomiche();
                _studentsByKey[key] = info;

                if (!_rows.ContainsKey(key))
                    _rows[key] = new EconomicRow(info);
            }
        }

        private static string NormalizeCf(string? value)
            => Utilities.RemoveAllSpaces((value ?? "").Trim().ToUpperInvariant());

        private static string NormalizeDomanda(string? value)
            => Utilities.RemoveAllSpaces((value ?? "").Trim());

        private static StudentKey BuildStudentKey(string? codFiscale, string? numDomanda)
            => new StudentKey(NormalizeCf(codFiscale), NormalizeDomanda(numDomanda));

        private bool TryGetEconomicRow(string? codFiscale, string? numDomanda, out EconomicRow economicRow)
            => _rows.TryGetValue(BuildStudentKey(codFiscale, numDomanda), out economicRow!);
    }
}
