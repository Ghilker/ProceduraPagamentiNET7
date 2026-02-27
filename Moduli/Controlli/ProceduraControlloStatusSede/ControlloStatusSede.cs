using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class ControlloStatusSede : BaseProcedure<ArgsControlloStatusSede>
    {
        public ControlloStatusSede(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        private string _aa = "";
        private string _folderPath = "";

        public DataTable OutputStatusSede { get; private set; } = BuildOutputTable();

        public override void RunProcedure(ArgsControlloStatusSede args)
        {
            var totalSw = Stopwatch.StartNew();

            _aa = (args._selectedAA ?? "").Trim();
            _folderPath = (args._folderPath ?? "").Trim();

            Log(0, $"ControlloStatusSede.RunProcedure - START | AA={_aa} | Folder='{_folderPath}'");

            try
            {
                ValidateSelectedAA(_aa);
                if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

                Log(5, "Validazione parametri completata.");

                // calcolo (standard: non include esclusi / non trasmesse)
                var output = Compute(_aa, includeEsclusi: false, includeNonTrasmesse: false);

                OutputStatusSede = output;

                Log(95, $"Export Excel START | Righe={output.Rows.Count} | Path='{_folderPath}'");
                Utilities.ExportDataTableToExcel(output, _folderPath);
                Log(98, "Export Excel END.");

                Log(100, $"ControlloStatusSede.RunProcedure - END | ms={totalSw.ElapsedMilliseconds} | Righe={output.Rows.Count}");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"ControlloStatusSede.RunProcedure - ERROR: {ex.Message}");
                throw;
            }
        }

        // =========================
        // OUTPUT
        // =========================
        public DataTable Compute(string aa) => Compute(aa, includeEsclusi: false, includeNonTrasmesse: false);

        public DataTable Compute(string aa, bool includeEsclusi, bool includeNonTrasmesse)
        {
            var totalSw = Stopwatch.StartNew();

            Log(0, $"ControlloStatusSede.Compute - START | AA={aa} | IncludeEsclusi={includeEsclusi} | IncludeNonTrasmesse={includeNonTrasmesse}");

            ValidateSelectedAA(aa);
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            var repo = new SqlStatusSedeRepository(CONNECTION);

            // LOAD INPUTS
            var loadSw = Stopwatch.StartNew();
            Log(10, "Esecuzione stored dbo.sp_StatusSede_GetInputs - START.");
            var inputs = repo.LoadInputs(aa, includeEsclusi, includeNonTrasmesse);
            Log(20, $"Esecuzione stored dbo.sp_StatusSede_GetInputs - END | Righe={inputs.Count} | ms={loadSw.ElapsedMilliseconds}");

            // EVAL
            var evalSw = Stopwatch.StartNew();
            Log(25, "Valutazione regole status sede - START.");

            var evaluator = new StatusSedeEvaluator();
            var output = BuildOutputTable();
            var (aaStart, aaEnd) = GetAaDateRange(aa);

            var stats = new DecisionStats();

            int n = inputs.Count;
            int progressStep = Math.Max(1, n / 20); // max 20 update
            for (int idx = 0; idx < n; idx++)
            {
                var row = inputs[idx];
                var decision = evaluator.Evaluate(row, aaStart, aaEnd);
                AppendOutput(output, row, decision);

                stats.Add(decision);

                if ((idx + 1) % progressStep == 0 || (idx + 1) == n)
                {
                    int pct = 25 + (n == 0 ? 60 : (int)Math.Round(((idx + 1) * 60.0) / n));
                    pct = ClampPct(pct);
                    Log(pct, $"Valutazione in corso... {idx + 1}/{n}");
                }
            }

            Log(88, $"Valutazione regole status sede - END | ms={evalSw.ElapsedMilliseconds}");
            Log(100, $"ControlloStatusSede.Compute - END | ms={totalSw.ElapsedMilliseconds} | RigheOutput={output.Rows.Count}");

            return output;
        }

        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("CodFiscale");
            dt.Columns.Add("NumDomanda", typeof(string));
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

        private static void AppendOutput(DataTable dt, StatusSedeStudent row, StatusSedeDecision d)
        {
            var info = row.Info;

            string cf = (info.InformazioniPersonali.CodFiscale ?? "").Trim().ToUpperInvariant();
            string numDomanda = (info.InformazioniPersonali.NumDomanda ?? "").Trim();

            string comuneRes = GetComuneResidenza(info);
            string provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();

            string comuneSede = (info.InformazioniIscrizione.ComuneSedeStudi ?? "").Trim();
            string provSede = (row.ProvinciaSede ?? "").Trim().ToUpperInvariant();

            string codSede = (info.InformazioniIscrizione.CodSedeStudi ?? "").Trim().ToUpperInvariant();
            string codCorso = (info.InformazioniIscrizione.CodCorsoLaurea ?? "").Trim();

            string comuneDom = (info.InformazioniSede.Domicilio?.codComuneDomicilio ?? "").Trim();

            dt.Rows.Add(
                cf,
                numDomanda,
                d.Reason,
                (info.InformazioniSede.StatusSede ?? "").Trim().ToUpperInvariant(),
                d.Suggested,
                comuneRes,
                provRes,
                comuneSede,
                provSede,
                codSede,
                codCorso,
                comuneDom,
                d.DomicilioPresente,
                d.DomicilioValido,
                row.HasAlloggio12
            );
        }

        private static string GetComuneResidenza(StudenteInfo info)
        {
            // I dati possono essere codice o nome comune: mantieni in uscita quello valorizzato.
            var c1 = (info.InformazioniSede.Residenza.codComune ?? "").Trim();
            if (c1.Length > 0) return c1;

            var c2 = (info.InformazioniSede.Residenza.nomeComune ?? "").Trim();
            if (c2.Length > 0) return c2;

            return "";
        }

        // =========================
        // REPOSITORY
        // =========================
        private sealed class SqlStatusSedeRepository
        {
            private readonly SqlConnection _conn;
            public SqlStatusSedeRepository(SqlConnection conn) => _conn = conn;

            public System.Collections.Generic.List<StatusSedeStudent> LoadInputs(
                string aa,
                bool includeEsclusi = false,
                bool includeNonTrasmesse = false)
            {
                var result = new System.Collections.Generic.List<StatusSedeStudent>();

                using var cmd = new SqlCommand("dbo.sp_StatusSede_GetInputs", _conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 9999999,
                };

                cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;
                cmd.Parameters.Add("@IncludeEsclusi", SqlDbType.Bit).Value = includeEsclusi;
                cmd.Parameters.Add("@IncludeNonTrasmesse", SqlDbType.Bit).Value = includeNonTrasmesse;

                Logger.LogInfo(null, $"[Repo] sp_StatusSede_GetInputs | AA={aa} | IncludeEsclusi={includeEsclusi} | IncludeNonTrasmesse={includeNonTrasmesse}");

                using var reader = cmd.ExecuteReader();

                int readCount = 0;
                while (reader.Read())
                {
                    result.Add(StatusSedeStudent.FromRecord(reader));
                    readCount++;

                    if (readCount % 5000 == 0)
                        Logger.LogInfo(null, $"[Repo] Lettura righe... {readCount}");
                }

                Logger.LogInfo(null, $"[Repo] Lettura completata. Righe={readCount}");
                return result;
            }
        }

        private sealed class StatusSedeStudent
        {
            public StudenteInfo Info { get; init; } = new StudenteInfo();

            public bool AlwaysA { get; init; }

            public bool InSedeList { get; init; }
            public bool PendolareList { get; init; }
            public bool FuoriSedeList { get; init; }

            public bool HasAlloggio12 { get; init; }

            public string ProvinciaSede { get; init; } = string.Empty;

            public int MinMesiDomicilioFuoriSede { get; init; }

            public static StatusSedeStudent FromRecord(IDataRecord record)
            {
                var studenteInfo = new StudenteInfo();

                int numDomanda = record.SafeGetInt("NumDomanda");
                string codFiscale = record.SafeGetString("CodFiscale").Trim().ToUpperInvariant();

                studenteInfo.InformazioniPersonali.NumDomanda = numDomanda <= 0
                    ? string.Empty
                    : numDomanda.ToString(CultureInfo.InvariantCulture);

                studenteInfo.InformazioniPersonali.CodFiscale = codFiscale;

                studenteInfo.InformazioniSede.StatusSede = record.SafeGetString("StatusSedeAttuale").Trim().ToUpperInvariant();
                studenteInfo.InformazioniSede.ForzaturaStatusSede = record.SafeGetString("ForcedStatus").Trim().ToUpperInvariant();

                bool alwaysA = record.SafeGetBool("AlwaysA");

                bool rifugiatoPolitico = record.SafeGetBool("RifugiatoPolitico");
                studenteInfo.InformazioniPersonali.Rifugiato = rifugiatoPolitico;

                int numeroComponentiNucleo = record.SafeGetInt("NumComponenti");
                int numeroComponentiNucleoEstero = record.SafeGetInt("NumConvEstero");
                studenteInfo.SetNucleoFamiliare(numeroComponentiNucleo, numeroComponentiNucleoEstero);

                string comuneResidenza = record.SafeGetString("ComuneResidenza").Trim();
                string provinciaResidenza = record.SafeGetString("ProvinciaResidenza").Trim().ToUpperInvariant();
                studenteInfo.SetResidenza(
                    indirizzo: string.Empty,
                    codComune: comuneResidenza,
                    provincia: provinciaResidenza,
                    CAP: string.Empty,
                    nomeComune: comuneResidenza
                );

                studenteInfo.InformazioniIscrizione.CodSedeStudi = record.SafeGetString("CodSedeStudi").Trim().ToUpperInvariant();
                studenteInfo.InformazioniIscrizione.CodCorsoLaurea = record.SafeGetString("CodCorso").Trim();
                studenteInfo.InformazioniIscrizione.CodFacolta = record.SafeGetString("CodFacolta").Trim();
                studenteInfo.InformazioniIscrizione.ComuneSedeStudi = record.SafeGetString("ComuneSedeStudi").Trim();

                string provinciaSede = record.SafeGetString("ProvinciaSede").Trim().ToUpperInvariant();

                bool inSedeList = record.SafeGetBool("InSedeList");
                bool pendolareList = record.SafeGetBool("PendolareList");
                bool fuoriSedeList = record.SafeGetBool("FuoriSedeList");

                bool hasAlloggio12 = record.SafeGetBool("HasAlloggio12");

                string comuneDomicilio = record.SafeGetString("ComuneDomicilio").Trim();
                bool titoloOneroso = record.SafeGetBool("TitoloOneroso");
                bool contrattoEnte = record.SafeGetBool("ContrattoEnte");
                string serieContratto = record.SafeGetString("SerieContratto").Trim();

                DateTime dataRegistrazione = record.SafeGetDateTime("DataRegistrazione");
                DateTime dataDecorrenza = record.SafeGetDateTime("DataDecorrenza");
                DateTime dataScadenza = record.SafeGetDateTime("DataScadenza");

                int durataContratto = record.SafeGetInt("DurataContratto");
                bool prorogato = record.SafeGetBool("Prorogato");
                int durataProroga = record.SafeGetInt("DurataProroga");
                string serieProroga = record.SafeGetString("SerieProroga").Trim();

                string denomEnte = record.SafeGetString("DenomEnte").Trim();
                double importoRataEnte = record.SafeGetDouble("ImportoRataEnte");

                int minMesiDomicilioFuoriSede = record.SafeGetInt("MinMesiDomicilioFuoriSede");

                // Domicilio dentro classi informazione
                studenteInfo.InformazioniSede.Domicilio.codComuneDomicilio = comuneDomicilio;
                studenteInfo.InformazioniSede.Domicilio.titoloOneroso = titoloOneroso;
                studenteInfo.InformazioniSede.Domicilio.codiceSerieLocazione = serieContratto;
                studenteInfo.InformazioniSede.Domicilio.dataRegistrazioneLocazione = dataRegistrazione;
                studenteInfo.InformazioniSede.Domicilio.dataDecorrenzaLocazione = dataDecorrenza;
                studenteInfo.InformazioniSede.Domicilio.dataScadenzaLocazione = dataScadenza;
                studenteInfo.InformazioniSede.Domicilio.durataMesiLocazione = durataContratto;
                studenteInfo.InformazioniSede.Domicilio.prorogatoLocazione = prorogato;
                studenteInfo.InformazioniSede.Domicilio.durataMesiProrogaLocazione = durataProroga;
                studenteInfo.InformazioniSede.Domicilio.codiceSerieProrogaLocazione = serieProroga;

                studenteInfo.InformazioniSede.ContrattoEnte = contrattoEnte;
                studenteInfo.InformazioniSede.Domicilio.contrEnte = contrattoEnte;
                studenteInfo.InformazioniSede.Domicilio.denominazioneIstituto = denomEnte;
                studenteInfo.InformazioniSede.Domicilio.importoMensileRataIstituto = importoRataEnte;

                return new StatusSedeStudent
                {
                    Info = studenteInfo,
                    AlwaysA = alwaysA,
                    InSedeList = inSedeList,
                    PendolareList = pendolareList,
                    FuoriSedeList = fuoriSedeList,
                    HasAlloggio12 = hasAlloggio12,
                    ProvinciaSede = provinciaSede,
                    MinMesiDomicilioFuoriSede = minMesiDomicilioFuoriSede
                };
            }
        }

        // =========================
        // EVALUATOR (usa StudenteInfo)
        // =========================
        private sealed class StatusSedeEvaluator
        {
            public StatusSedeDecision Evaluate(StatusSedeStudent row, DateTime aaStart, DateTime aaEnd)
            {
                var info = row.Info;

                var forced = (info.InformazioniSede.ForzaturaStatusSede ?? "").Trim().ToUpperInvariant();
                if (IsValidStatus(forced))
                    return StatusSedeDecision.Fixed(forced, "Forzatura manuale (primaria)");

                if (row.AlwaysA)
                    return StatusSedeDecision.Fixed("A", "Sempre A (telematico / non in presenza) [DB]");

                if (info.InformazioniPersonali.Rifugiato)
                    return StatusSedeDecision.Fixed("B", "Rifugiato politico");

                if (IsNucleoEsteroOver50(info))
                    return StatusSedeDecision.Fixed("B", "Nucleo familiare con >50% componenti all'estero");

                var comuneRes = GetComuneResidenza(info);
                var comuneSede = (info.InformazioniIscrizione.ComuneSedeStudi ?? "").Trim();

                if (Eq(comuneRes, comuneSede))
                    return StatusSedeDecision.Fixed("A", "Comune residenza = Comune sede studi");

                if (row.HasAlloggio12)
                    return StatusSedeDecision.Fixed("B", "PA: idoneo/vincitore (1/2) => fuori sede");

                bool pendolareDefaultSameProvNoLists = false;

                if (row.InSedeList)
                    return StatusSedeDecision.Fixed("A", "COMUNI_INSEDE (stessa provincia)");

                if (row.PendolareList && !row.FuoriSedeList)
                    return StatusSedeDecision.Fixed("C", "COMUNI_PENDOLARI (stessa provincia, non in COMUNI_FUORISEDE)");

                var provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();
                var provSede = (row.ProvinciaSede ?? "").Trim().ToUpperInvariant();

                if (Eq(provRes, provSede))
                {
                    if (!row.InSedeList && !row.PendolareList && !row.FuoriSedeList)
                        pendolareDefaultSameProvNoLists = true;
                }

                if (pendolareDefaultSameProvNoLists)
                    return StatusSedeDecision.Fixed("C", "Stessa provincia ma assente da COMUNI_INSEDE/COMUNI_PENDOLARI/COMUNI_FUORISEDE => pendolare default");

                var dom = DomicilioValidator.Validate(row, aaStart, aaEnd);
                if (!dom.Presente)
                    return StatusSedeDecision.WithDom("D", "Dati domicilio non presenti => pendolare calcolato (D)", dom);

                var comuneDom = (info.InformazioniSede.Domicilio?.codComuneDomicilio ?? "").Trim();

                if (dom.Valido)
                {
                    if (Eq(comuneDom, comuneSede))
                        return StatusSedeDecision.WithDom("B", $"Domicilio presente, valido e nel comune sede studi => fuori sede (B) | {dom.Reason}", dom);

                    return StatusSedeDecision.WithDom("D", $"Domicilio valido ma comune domicilio diverso da comune sede studi => pendolare calcolato (D) | {dom.Reason}", dom);
                }

                return StatusSedeDecision.WithDom("D", $"Domicilio presente ma non valido => pendolare calcolato (D) | {dom.Reason}", dom);
            }

            private static bool IsNucleoEsteroOver50(StudenteInfo info)
            {
                var provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();
                if (!Eq(provRes, "EE")) return false;

                int comp = info.InformazioniPersonali.NumeroComponentiNucleoFamiliare;
                if (comp <= 0) return false;

                int estero = info.InformazioniPersonali.NumeroComponentiNucleoFamiliareEstero;
                var soglia = (int)Math.Ceiling(comp / 2.0);
                return estero >= soglia;
            }

            private static bool IsValidStatus(string? s) => s is "A" or "B" or "C" or "D";

            private static bool Eq(string? a, string? b)
                => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // =========================
        // DOMICILIO (usa classi informazione)
        // =========================
        private static class DomicilioValidator
        {
            public static DomResult Validate(StatusSedeStudent row, DateTime aaStart, DateTime aaEnd)
            {
                var info = row.Info;
                var dom = info.InformazioniSede.Domicilio ?? new Domicilio();

                var comuneDom = (dom.codComuneDomicilio ?? "").Trim();

                bool titoloOneroso = dom.titoloOneroso;
                bool contrattoEnte = dom.contrEnte || info.InformazioniSede.ContrattoEnte;

                var presente = comuneDom.Length > 0 && (titoloOneroso || contrattoEnte);
                if (!presente)
                    return new DomResult(false, false, "Dati domicilio non presenti");

                var dec = dom.dataDecorrenzaLocazione;
                var scad = dom.dataScadenzaLocazione;

                if (!HasValidDate(dec))
                    return new DomResult(true, false, "Data decorrenza non valida");

                if (!HasValidDate(scad))
                    return new DomResult(true, false, "Data scadenza non valida");

                if (scad < dec)
                    return new DomResult(true, false, "Scadenza < decorrenza");

                var effStart = dec > aaStart ? dec : aaStart;
                var effEnd = scad < aaEnd ? scad : aaEnd;
                if (effStart > effEnd)
                    return new DomResult(true, false, "Contratto fuori dall'intervallo AA");

                var mesi = CoveredMonths(effStart, effEnd);
                var min = row.MinMesiDomicilioFuoriSede;

                if (min > 0 && mesi < min)
                    return new DomResult(true, false, $"Mesi coperti {mesi} < minimo {min} (DB)");

                var serie = (dom.codiceSerieLocazione ?? "").Trim();
                if (serie.Length < 3)
                    return new DomResult(true, false, "Serie contratto assente/corta");

                return new DomResult(true, true, $"OK (mesi coperti={mesi}, minimo={min})");
            }

            private static bool HasValidDate(DateTime dt)
            {
                if (dt == DateTime.MinValue) return false;
                if (dt.Year < 1900) return false;
                return true;
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

        private static int ClampPct(int pct) => pct < 0 ? 0 : (pct > 100 ? 100 : pct);

        private static void Log(int pct, string msg) => Logger.LogInfo(ClampPct(pct), msg);

        private sealed class DecisionStats
        {
            public int A, B, C, D;
            public int DomPres, DomVal;

            public void Add(StatusSedeDecision d)
            {
                switch (d.Suggested)
                {
                    case "A": A++; break;
                    case "B": B++; break;
                    case "C": C++; break;
                    case "D": D++; break;
                }

                if (d.DomicilioPresente) DomPres++;
                if (d.DomicilioValido) DomVal++;
            }
        }
    }
}