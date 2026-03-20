using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
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

        private readonly Dictionary<string, EconomicRow> _rows =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _statusInpsOrigineByCf = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _statusInpsIntegrazioneByCf = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _coAttestazioneOkByCf = new(StringComparer.OrdinalIgnoreCase);

        public DataTable OutputEconomici { get; private set; } = BuildOutputTable();

        public IReadOnlyList<ValutazioneEconomici> OutputEconomiciList { get; private set; } = Array.Empty<ValutazioneEconomici>(); private string _aa = "";

        private sealed class CalcParams
        {
            public decimal Franchigia { get; set; }
            public decimal RendPatr { get; set; }              // tasso_rendimento_pat_mobiliare
            public decimal FranchigiaPatMob { get; set; }      // franchigia_pat_mobiliare
        }

        private readonly CalcParams _calc = new();
    }
}
