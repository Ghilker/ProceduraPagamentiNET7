using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class ControlloStatusSede : BaseProcedure<ArgsControlloStatusSede>
    {
        public ControlloStatusSede(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        private string _aa = "";
        private string _folderPath = "";

        public override void RunProcedure(ArgsControlloStatusSede args)
        {
            _aa = (args._selectedAA ?? "").Trim();
            _folderPath = (args._folderPath ?? "").Trim();

            ValidateSelectedAA(_aa);
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            var repo = new SqlStatusSedeRepository(CONNECTION);
            var inputs = repo.LoadInputs(_aa);

            var evaluator = new StatusSedeEvaluator();
            var output = BuildOutputTable();

            var (aaStart, aaEnd) = GetAaDateRange(_aa);

            foreach (var i in inputs)
            {
                var decision = evaluator.Evaluate(i, aaStart, aaEnd);
                AppendOutput(output, i, decision);
            }

            Utilities.ExportDataTableToExcel(output, _folderPath);
            Logger.LogInfo(100, "Fine lavorazione");
        }

        // =========================
        // OUTPUT
        // =========================

        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("CodFiscale");
            dt.Columns.Add("Motivo");
            dt.Columns.Add("StatoAttuale");
            dt.Columns.Add("StatoSuggerito");
            dt.Columns.Add("ComuneRes");
            dt.Columns.Add("ProvRes");
            dt.Columns.Add("ComuneSede");
            dt.Columns.Add("ProvSede");
            dt.Columns.Add("CodSede");
            dt.Columns.Add("CodCorso");
            dt.Columns.Add("ComuneDom");
            dt.Columns.Add("DomicilioPresente", typeof(bool));
            dt.Columns.Add("DomicilioValido", typeof(bool));
            dt.Columns.Add("HasAlloggio12", typeof(bool));
            return dt;
        }

        private static void AppendOutput(DataTable dt, StatusSedeInput i, StatusSedeDecision d)
        {
            dt.Rows.Add(
                i.CodFiscale,
                d.Reason,
                i.StatusAttuale,
                d.Suggested,
                i.ComuneResidenza,
                i.ProvinciaResidenza,
                i.ComuneSedeStudi,
                i.ProvinciaSede,
                i.CodSedeStudi,
                i.CodCorso,
                i.ComuneDomicilio,
                d.DomicilioPresente,
                d.DomicilioValido,
                i.HasAlloggio12
            );
        }

        // =========================
        // REPOSITORY
        // =========================

        private sealed class SqlStatusSedeRepository
        {
            private readonly SqlConnection _conn;
            public SqlStatusSedeRepository(SqlConnection conn) => _conn = conn;

            public List<StatusSedeInput> LoadInputs(string aa)
            {
                using var cmd = new SqlCommand("dbo.sp_StatusSede_GetInputs", _conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 600
                };
                cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;

                var list = new List<StatusSedeInput>(capacity: 8192);

                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    list.Add(StatusSedeInput.FromReader(rd));
                }

                return list;
            }
        }

        // =========================
        // RULE ENGINE (ordine richiesto)
        // =========================

        private sealed class StatusSedeEvaluator
        {
            public StatusSedeDecision Evaluate(StatusSedeInput i, DateTime aaStart, DateTime aaEnd)
            {
                // 1) Forzatura primaria
                if (IsValidStatus(i.ForcedStatus))
                    return StatusSedeDecision.Fixed(i.ForcedStatus!, "Forzatura manuale (primaria)");

                // 2) Telematiche + corsi non in presenza => sempre A (tabellati DB)
                if (i.AlwaysA)
                    return StatusSedeDecision.Fixed("A", "Sempre A (telematico / non in presenza) [DB]");

                // 3) Rifugiato politico => B
                if (i.RifugiatoPolitico)
                    return StatusSedeDecision.Fixed("B", "Rifugiato politico");

                // 4) Stranieri con >50% nucleo all'estero => B
                if (IsNucleoEsteroOver50(i))
                    return StatusSedeDecision.Fixed("B", "Nucleo familiare con >50% componenti all'estero");

                // 5) Comune residenza == Comune sede studi => A
                if (Eq(i.ComuneResidenza, i.ComuneSedeStudi))
                    return StatusSedeDecision.Fixed("A", "Comune residenza = Comune sede studi");

                if (i.HasAlloggio12)
                    return StatusSedeDecision.Fixed("B", "PA: idoneo/vincitore (1/2) => fuori sede");

                // 6) Provincia residenza == Provincia sede studi => usa liste comuni_* (DB)
                bool pendolareDefaultSameProvNoLists = false;
                
                if (i.InSedeList)
                    return StatusSedeDecision.Fixed("A", "COMUNI_INSEDE (stessa provincia)");

                // FuoriSedeList qui non assegna status, serve a bloccare la regola pendolare.
                if (i.PendolareList && !i.FuoriSedeList)
                    return StatusSedeDecision.Fixed("C", "COMUNI_PENDOLARI (stessa provincia, non in COMUNI_FUORISEDE)");

                if (Eq(i.ProvinciaResidenza, i.ProvinciaSede))
                {
                    // NUOVO: stessa provincia ma non presente in nessuna tabella => pendolare (C)
                    if (!i.InSedeList && !i.PendolareList && !i.FuoriSedeList)
                        pendolareDefaultSameProvNoLists = true;
                }


                // Applica il fallback dopo la regola 7
                if (pendolareDefaultSameProvNoLists)
                    return StatusSedeDecision.Fixed("C", "Stessa provincia ma assente da COMUNI_INSEDE/COMUNI_PENDOLARI/COMUNI_FUORISEDE => pendolare default");

                // 8) Comune residenza diverso da sede => controllo domicilio
                var dom = DomicilioValidator.Validate(i, aaStart, aaEnd);
                if (!dom.Presente)
                    return StatusSedeDecision.WithDom("D", "Domicilio assente => pendolare calcolato (D)", dom);

                if (dom.Valido)
                {
                    if (Eq(i.ComuneDomicilio, i.ComuneSedeStudi))
                        return StatusSedeDecision.WithDom("B", $"Domicilio presente, valido e nel comune sede studi => fuori sede (B) | {dom.Reason}", dom);

                    return StatusSedeDecision.WithDom("D", $"Domicilio valido ma comune domicilio diverso da comune sede studi => pendolare calcolato (D) | {dom.Reason}", dom);
                }


                return StatusSedeDecision.WithDom("D", $"Domicilio presente ma non valido => pendolare calcolato (D) | {dom.Reason}", dom);
            }

            private static bool IsNucleoEsteroOver50(StatusSedeInput i)
            {
                if (!Eq(i.ProvinciaResidenza, "EE")) return false;
                if (i.NumComponenti <= 0) return false;
                var soglia = (int)Math.Ceiling(i.NumComponenti / 2.0);
                return i.NumConvEstero >= soglia;
            }

            private static bool IsValidStatus(string? s)
                => s is "A" or "B" or "C" or "D";

            private static bool Eq(string? a, string? b)
                => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // =========================
        // DOMICILIO (minimo, configurabile DB)
        // =========================

        private static class DomicilioValidator
        {
            private static readonly string[] DateFormats =
            {
                "yyyy-MM-dd","yyyyMMdd","dd/MM/yyyy","d/M/yyyy","dd-MM-yyyy","d-M-yyyy",
                "yyyy-MM-dd HH:mm:ss","dd/MM/yyyy HH:mm:ss"
            };

            public static DomResult Validate(StatusSedeInput i, DateTime aaStart, DateTime aaEnd)
            {
                var comuneDom = (i.ComuneDomicilio ?? "").Trim();
                var presente = comuneDom.Length > 0 && (i.TitoloOneroso || i.ContrattoEnte);

                if (!presente)
                    return new DomResult(false, false, "Dati domicilio non presenti");

                // Date decorrenza/scadenza: se non parse, non valido.
                if (!TryParse(i.DataDecorrenza, out var dec))
                    return new DomResult(true, false, "Data decorrenza non valida");

                if (!TryParse(i.DataScadenza, out var scad))
                    return new DomResult(true, false, "Data scadenza non valida");

                if (scad < dec)
                    return new DomResult(true, false, "Scadenza < decorrenza");

                // overlap con AA
                var effStart = dec > aaStart ? dec : aaStart;
                var effEnd = scad < aaEnd ? scad : aaEnd;
                if (effStart > effEnd)
                    return new DomResult(true, false, "Contratto fuori dall'intervallo AA");

                // mesi coperti (regola parametrica)
                var mesi = CoveredMonths(effStart, effEnd);
                var min = i.MinMesiDomicilioFuoriSede;

                if (min > 0 && mesi < min)
                    return new DomResult(true, false, $"Mesi coperti {mesi} < minimo {min} (DB)");

                // serie contratto minimale
                if (string.IsNullOrWhiteSpace(i.SerieContratto) || i.SerieContratto.Trim().Length < 3)
                    return new DomResult(true, false, "Serie contratto assente/corta");

                return new DomResult(true, true, $"OK (mesi coperti={mesi}, minimo={min})");
            }

            private static bool TryParse(string? s, out DateTime dt)
            {
                if (string.IsNullOrWhiteSpace(s)) { dt = default; return false; }
                return DateTime.TryParseExact(
                    s.Trim(),
                    DateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                    out dt
                );
            }

            private static int CoveredMonths(DateTime start, DateTime end)
            {
                int count = 0;
                var cur = new DateTime(start.Year, start.Month, 1);
                while (cur <= end)
                {
                    var monthStart = cur;
                    var monthEnd = cur.AddMonths(1).AddDays(-1);

                    var covStart = monthStart < start ? start : monthStart;
                    var covEnd = monthEnd > end ? end : monthEnd;

                    var days = (covEnd - covStart).TotalDays + 1;
                    if (days >= 15) count++;

                    cur = cur.AddMonths(1);
                }
                return count;
            }
        }

        private readonly record struct DomResult(bool Presente, bool Valido, string Reason);

        private sealed class StatusSedeDecision
        {
            public string Suggested { get; }
            public string Reason { get; }
            public bool DomicilioPresente { get; }
            public bool DomicilioValido { get; }

            private StatusSedeDecision(string suggested, string reason, bool domPres, bool domVal)
            {
                Suggested = suggested;
                Reason = reason;
                DomicilioPresente = domPres;
                DomicilioValido = domVal;
            }

            public static StatusSedeDecision Fixed(string suggested, string reason)
                => new StatusSedeDecision(suggested, reason, domPres: false, domVal: false);

            public static StatusSedeDecision WithDom(string suggested, string reason, DomResult dom)
                => new StatusSedeDecision(suggested, reason, dom.Presente, dom.Valido);
        }

        private sealed class StatusSedeInput
        {
            public int NumDomanda { get; init; }
            public string CodFiscale { get; init; } = "";

            public string StatusAttuale { get; init; } = "";
            public string? ForcedStatus { get; init; }

            public bool AlwaysA { get; init; }

            public bool RifugiatoPolitico { get; init; }
            public int NumComponenti { get; init; }
            public int NumConvEstero { get; init; }

            public string ComuneResidenza { get; init; } = "";
            public string ProvinciaResidenza { get; init; } = "";

            public string CodSedeStudi { get; init; } = "";
            public string CodCorso { get; init; } = "";
            public string CodFacolta { get; init; } = "";

            public string ComuneSedeStudi { get; init; } = "";
            public string ProvinciaSede { get; init; } = "";

            public bool InSedeList { get; init; }
            public bool PendolareList { get; init; }
            public bool FuoriSedeList { get; init; }

            public bool HasAlloggio12 { get; init; }

            // domicilio
            public string ComuneDomicilio { get; init; } = "";
            public bool TitoloOneroso { get; init; }
            public bool ContrattoEnte { get; init; }
            public string SerieContratto { get; init; } = "";
            public string DataRegistrazione { get; init; } = "";
            public string DataDecorrenza { get; init; } = "";
            public string DataScadenza { get; init; } = "";
            public int DurataContratto { get; init; }
            public bool Prorogato { get; init; }
            public int DurataProroga { get; init; }
            public string SerieProroga { get; init; } = "";
            public string DenomEnte { get; init; } = "";
            public double ImportoRataEnte { get; init; }

            public int MinMesiDomicilioFuoriSede { get; init; }

            public static StatusSedeInput FromReader(SqlDataReader rd)
            {
                string S(string name)
                    => rd[name] == DBNull.Value ? "" : Convert.ToString(rd[name]) ?? "";

                int I(string name)
                {
                    var o = rd[name];
                    if (o == DBNull.Value) return 0;

                    // tipi numerici comuni
                    if (o is int i) return i;
                    if (o is short s) return s;
                    if (o is long l) return unchecked((int)l);
                    if (o is byte b) return b;
                    if (o is bool bo) return bo ? 1 : 0;

                    // fallback string (gestisce '' -> 0)
                    var str = (Convert.ToString(o) ?? "").Trim();
                    if (str.Length == 0) return 0;

                    return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
                }

                bool B(string name) => I(name) == 1;

                double D(string name)
                {
                    var o = rd[name];
                    if (o == DBNull.Value) return 0;

                    if (o is double dd) return dd;
                    if (o is float ff) return ff;
                    if (o is decimal dc) return (double)dc;

                    var str = (Convert.ToString(o) ?? "").Trim();
                    if (str.Length == 0) return 0;

                    return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
                }

                return new StatusSedeInput
                {
                    NumDomanda = I("NumDomanda"),
                    CodFiscale = S("CodFiscale").Trim().ToUpperInvariant(),

                    StatusAttuale = S("StatusSedeAttuale").Trim().ToUpperInvariant(),
                    ForcedStatus = S("ForcedStatus").Trim().ToUpperInvariant(),

                    AlwaysA = B("AlwaysA"),

                    RifugiatoPolitico = B("RifugiatoPolitico"),
                    NumComponenti = I("NumComponenti"),
                    NumConvEstero = I("NumConvEstero"),

                    ComuneResidenza = S("ComuneResidenza").Trim(),
                    ProvinciaResidenza = S("ProvinciaResidenza").Trim().ToUpperInvariant(),

                    CodSedeStudi = S("CodSedeStudi").Trim().ToUpperInvariant(),
                    CodCorso = S("CodCorso").Trim(),
                    CodFacolta = S("CodFacolta").Trim(),

                    ComuneSedeStudi = S("ComuneSedeStudi").Trim(),
                    ProvinciaSede = S("ProvinciaSede").Trim().ToUpperInvariant(),

                    InSedeList = B("InSedeList"),
                    PendolareList = B("PendolareList"),
                    FuoriSedeList = B("FuoriSedeList"),

                    HasAlloggio12 = B("HasAlloggio12"),

                    ComuneDomicilio = S("ComuneDomicilio").Trim(),
                    TitoloOneroso = B("TitoloOneroso"),
                    ContrattoEnte = B("ContrattoEnte"),
                    SerieContratto = S("SerieContratto"),
                    DataRegistrazione = S("DataRegistrazione"),
                    DataDecorrenza = S("DataDecorrenza"),
                    DataScadenza = S("DataScadenza"),
                    DurataContratto = I("DurataContratto"),
                    Prorogato = B("Prorogato"),
                    DurataProroga = I("DurataProroga"),
                    SerieProroga = S("SerieProroga"),
                    DenomEnte = S("DenomEnte"),
                    ImportoRataEnte = D("ImportoRataEnte"),

                    MinMesiDomicilioFuoriSede = I("MinMesiDomicilioFuoriSede")
                };
            }
        }

        // =========================
        // UTIL
        // =========================

        private static void ValidateSelectedAA(string aa)
        {
            if (aa.Length != 8 || !aa.All(char.IsDigit))
                throw new ArgumentException("Anno accademico non valido. Atteso formato YYYYYYYY.");

            int start = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int end = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            if (end != start + 1)
                throw new ArgumentException("Anno accademico incoerente. Fine ≠ inizio+1.");
        }

        private static (DateTime aaStart, DateTime aaEnd) GetAaDateRange(string aa)
        {
            int startYear = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int endYear = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            return (new DateTime(startYear, 10, 1), new DateTime(endYear, 9, 30));
        }
    }
}
