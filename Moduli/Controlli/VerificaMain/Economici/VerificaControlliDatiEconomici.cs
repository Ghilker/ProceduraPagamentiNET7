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

        private string debugCF = "";
        private string _aa = "";

        // Adapter tecnico: non contiene più lo stato economico vero.
        // Lo stato vero vive in StudenteInfo.InformazioniEconomiche.
        private readonly Dictionary<string, EconomicRow> _rows =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<StudentKey, StudenteInfo> _studentsByKey = new();
        private readonly Dictionary<string, InformazioniEconomiche> _sharedEconomiciByCf =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _statusInpsOrigineByCf = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _statusInpsIntegrazioneByCf = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _coAttestazioneOkByCf = new(StringComparer.OrdinalIgnoreCase);

        public DataTable OutputEconomici { get; private set; } = BuildOutputTable();
        public IReadOnlyList<ValutazioneEconomici> OutputEconomiciList { get; private set; } = Array.Empty<ValutazioneEconomici>();

        private sealed class CalcParams
        {
            public decimal Franchigia { get; set; }
            public decimal RendPatr { get; set; }
            public decimal FranchigiaPatMob { get; set; }
        }

        private readonly CalcParams _calc = new();
        private readonly List<Target> _targets = new();
        private bool _collectionCompleted;
        private bool _calculationCompleted;

        private void ResetState(string aa)
        {
            _aa = aa;
            _rows.Clear();
            _studentsByKey.Clear();
            _sharedEconomiciByCf.Clear();
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
                var key = pair.Key;
                var info = pair.Value ?? new StudenteInfo();
                string cf = NormalizeCf(key.CodFiscale);
                string numDomanda = NormalizeDomanda(key.NumDomanda);

                info.InformazioniPersonali.CodFiscale = cf;
                info.InformazioniPersonali.NumDomanda = numDomanda;

                if (!_sharedEconomiciByCf.TryGetValue(cf, out var sharedEco))
                {
                    sharedEco = info.InformazioniEconomiche ?? new InformazioniEconomiche();
                    _sharedEconomiciByCf[cf] = sharedEco;
                }

                info.InformazioniEconomiche = sharedEco;
                _studentsByKey[new StudentKey(cf, numDomanda)] = info;
                _targets.Add(new Target(cf, numDomanda));

                if (!_rows.ContainsKey(cf))
                    _rows[cf] = new EconomicRow(info);
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

                if (!_sharedEconomiciByCf.TryGetValue(cf, out var sharedEco))
                {
                    sharedEco = info.InformazioniEconomiche ?? new InformazioniEconomiche();
                    _sharedEconomiciByCf[cf] = sharedEco;
                }

                info.InformazioniEconomiche = sharedEco;
                _studentsByKey[key] = info;

                if (!_rows.ContainsKey(cf))
                    _rows[cf] = new EconomicRow(info);
            }
        }

        private static string NormalizeCf(string? value)
            => Utilities.RemoveAllSpaces((value ?? "").Trim().ToUpperInvariant());

        private static string NormalizeDomanda(string? value)
            => Utilities.RemoveAllSpaces((value ?? "").Trim());
    }
}
