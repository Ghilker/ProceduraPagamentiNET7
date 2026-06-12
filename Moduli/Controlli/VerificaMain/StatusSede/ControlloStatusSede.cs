using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed partial class ControlloStatusSede : IVerificaModule
    {
        public string Name => "StatusSede";

        public void Calculate(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            foreach (var info in context.Students.Values)
            {
                ApplicaStatusSede(
                    info,
                    context.AnnoAccademico,
                    context.ComuniEquiparati,
                    context.ReferenceDate.Date);
            }
        }

        internal static StatusSedeResult ValutaStatusSede(
            StudenteInfo info,
            string annoAccademico,
            HashSet<(string ComuneA, string ComuneB)>? comuniEquiparati = null,
            DateTime? referenceDate = null)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            var evaluator = new StatusSedeEvaluator(
                comuniEquiparati ?? new HashSet<(string ComuneA, string ComuneB)>(),
                (referenceDate ?? DateTime.Today).Date);

            var (aaStart, aaEnd) = GetAaDateRange(annoAccademico);
            var decision = evaluator.Evaluate(info, aaStart, aaEnd);

            return new StatusSedeResult(
                decision.SuggestedStatus,
                decision.Reason,
                decision.DomicilioPresente,
                decision.DomicilioValido,
                decision.FuoriSedeCertoPerSaldo);
        }

        internal static StatusSedeResult ApplicaStatusSede(
            StudenteInfo info,
            string annoAccademico,
            HashSet<(string ComuneA, string ComuneB)>? comuniEquiparati = null,
            DateTime? referenceDate = null)
        {
            var result = ValutaStatusSede(info, annoAccademico, comuniEquiparati, referenceDate);

            info.InformazioniSede.StatusSedeSuggerito = result.SuggestedStatus;
            info.InformazioniSede.MotivoStatusSede = result.Reason;
            info.InformazioniSede.DomicilioPresente = result.DomicilioPresente;
            info.InformazioniSede.DomicilioValido = result.DomicilioValido;

            return result;
        }

        internal static bool DevePagareComePendolare(
            StudenteInfo info,
            string annoAccademico,
            HashSet<(string ComuneA, string ComuneB)>? comuniEquiparati,
            DateTime referenceDate,
            out StatusSedeResult statusSede)
        {
            return DevePagareComePendolarePerPagamento(
                info,
                annoAccademico,
                codTipoPagamento: string.Empty,
                comuniEquiparati,
                referenceDate,
                out statusSede);
        }

        internal static bool DevePagareComePendolarePerPagamento(
            StudenteInfo info,
            string annoAccademico,
            string? codTipoPagamento,
            HashSet<(string ComuneA, string ComuneB)>? comuniEquiparati,
            DateTime referenceDate,
            out StatusSedeResult statusSede)
        {
            statusSede = ApplicaStatusSede(info, annoAccademico, comuniEquiparati, referenceDate);

            return DevePagareComePendolarePerPagamentoDaResult(
                info,
                codTipoPagamento,
                statusSede);
        }

        internal static bool DevePagareComePendolarePerPagamentoDaResult(
            StudenteInfo info,
            string? codTipoPagamento,
            StatusSedeResult statusSede)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            string statusAttuale = (info.InformazioniSede.StatusSede ?? "").Trim().ToUpperInvariant();
            string statusCalcolato = (statusSede.SuggestedStatus ?? "").Trim().ToUpperInvariant();
            string codPag = (codTipoPagamento ?? "").Trim().ToUpperInvariant();

            if (statusAttuale != "B")
                return false;

            // Saldo SA: il fuori sede deve essere certo.
            // Istanze future, domicilio sotto i mesi minimi o validità solo in finestra proroga
            // non bastano per pagare il saldo come fuori sede.
            if (codPag == "SA")
                return statusCalcolato != "B" || !statusSede.FuoriSedeCertoPerSaldo;

            return statusCalcolato == "C" || statusCalcolato == "D";
        }

        internal readonly record struct StatusSedeResult(
            string SuggestedStatus,
            string Reason,
            bool DomicilioPresente,
            bool DomicilioValido,
            bool FuoriSedeCertoPerSaldo)
        {
            public StatusSedeResult(
                string SuggestedStatus,
                string Reason,
                bool DomicilioPresente,
                bool DomicilioValido)
                : this(
                    SuggestedStatus,
                    Reason,
                    DomicilioPresente,
                    DomicilioValido,
                    string.Equals((SuggestedStatus ?? "").Trim(), "B", StringComparison.OrdinalIgnoreCase) && DomicilioValido)
            {
            }
        }

        private static readonly object ComuniEquiparatiCacheLock = new();
        private static HashSet<(string ComuneA, string ComuneB)>? _comuniEquiparatiCache;
        private static DateTime _comuniEquiparatiCacheLoadedAtUtc;
        private static readonly TimeSpan ComuniEquiparatiCacheTtl = TimeSpan.FromMinutes(30);

        internal static HashSet<(string ComuneA, string ComuneB)> LoadComuniEquiparatiFromDb(
            SqlConnection connection,
            SqlTransaction? transaction = null,
            bool forceRefresh = false)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            lock (ComuniEquiparatiCacheLock)
            {
                if (!forceRefresh
                    && _comuniEquiparatiCache != null
                    && (DateTime.UtcNow - _comuniEquiparatiCacheLoadedAtUtc) <= ComuniEquiparatiCacheTtl)
                {
                    return new HashSet<(string ComuneA, string ComuneB)>(_comuniEquiparatiCache);
                }
            }

            const string sql = @"
SELECT
    UPPER(LTRIM(RTRIM(Cod_Comune_A))) AS Cod_Comune_A,
    UPPER(LTRIM(RTRIM(Cod_Comune_B))) AS Cod_Comune_B
FROM dbo.STATUS_SEDE_COMUNI_EQUIVALENTI
WHERE Data_Fine_Validita IS NULL;";

            var result = new HashSet<(string ComuneA, string ComuneB)>();

            using var cmd = transaction == null
                ? new SqlCommand(sql, connection)
                : new SqlCommand(sql, connection, transaction);

            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 9000000;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string comuneA = Utilities.SafeGetString(reader, "Cod_Comune_A").Trim().ToUpperInvariant();
                string comuneB = Utilities.SafeGetString(reader, "Cod_Comune_B").Trim().ToUpperInvariant();

                if (comuneA.Length == 0 || comuneB.Length == 0)
                    continue;

                result.Add(NormalizeComunePair(comuneA, comuneB));
            }

            lock (ComuniEquiparatiCacheLock)
            {
                _comuniEquiparatiCache = new HashSet<(string ComuneA, string ComuneB)>(result);
                _comuniEquiparatiCacheLoadedAtUtc = DateTime.UtcNow;
            }

            Logger.LogInfo(null, $"[ControlloStatusSede] Comuni equiparati caricati: {result.Count}");
            return result;
        }

        internal static bool DevePagareComePendolareDaStatusCalcolato(
            StudenteInfo info,
            out StatusSedeResult statusSede)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            string statusAttuale = (info.InformazioniSede.StatusSede ?? "").Trim().ToUpperInvariant();
            string statusCalcolato = (info.InformazioniSede.StatusSedeSuggerito ?? "").Trim().ToUpperInvariant();
            string motivo = info.InformazioniSede.MotivoStatusSede ?? "Status sede calcolato non valorizzato";

            statusSede = new StatusSedeResult(
                statusCalcolato,
                motivo,
                info.InformazioniSede.DomicilioPresente,
                info.InformazioniSede.DomicilioValido);

            return DevePagareComePendolarePerPagamentoDaResult(
                info,
                codTipoPagamento: string.Empty,
                statusSede);
        }

        private sealed class StatusSedeEvaluator
        {
            private readonly HashSet<(string ComuneA, string ComuneB)> _comuniEquiparati;
            private readonly DateTime _referenceDate;

            public StatusSedeEvaluator(HashSet<(string ComuneA, string ComuneB)> comuniEquiparati, DateTime referenceDate)
            {
                _comuniEquiparati = comuniEquiparati ?? new HashSet<(string ComuneA, string ComuneB)>();
                _referenceDate = referenceDate.Date;
            }

            public StatusSedeDecision Evaluate(StudenteInfo info, DateTime aaStart, DateTime aaEnd)
            {

                var forced = (info.InformazioniSede.ForzaturaStatusSede ?? "").Trim().ToUpperInvariant();
                if (IsValidStatus(forced))
                    return StatusSedeDecision.Fixed(forced, "Forzatura manuale (primaria)");

                if (info.InformazioniSede.AlwaysA)
                    return StatusSedeDecision.Fixed("A", "Sempre A (telematico / non in presenza) [DB]");

                if (info.InformazioniPersonali.Rifugiato)
                    return StatusSedeDecision.Fixed("B", "Rifugiato politico");

                if (IsNucleoEsteroOver50(info))
                    return StatusSedeDecision.Fixed("B", "Nucleo familiare con >50% componenti all'estero");

                var eco = info.InformazioniEconomiche;
                if (eco != null
                    && string.Equals((eco.Raw.TipoRedditoOrigine ?? "").Trim(), "EE", StringComparison.OrdinalIgnoreCase)
                    && IsSeqOne(eco.Calcolate.SEQ)
                    && eco.Calcolate.ISRDSU >= 9000m)
                {
                    return StatusSedeDecision.Fixed(
                        "B",
                        "Economici: TipoReddito=EE, SEQ=1, ISR>=9000 => fuori sede (B)"
                    );
                }

                var comuneRes = GetComuneResidenza(info);
                var comuneSede = (info.InformazioniIscrizione.ComuneSedeStudi ?? "").Trim();

                if (Eq(comuneRes, comuneSede))
                    return StatusSedeDecision.Fixed("A", "Comune residenza = Comune sede studi");

                if (info.InformazioniSede.HasAlloggio12)
                    return StatusSedeDecision.Fixed("B", "PA: idoneo/vincitore (1/2) => fuori sede");

                bool pendolareDefaultSameProvNoLists = false;

                if (info.InformazioniSede.InSedeList)
                    return StatusSedeDecision.Fixed("A", "COMUNI_INSEDE (stessa provincia)");

                if (info.InformazioniSede.PendolareList && !info.InformazioniSede.FuoriSedeList)
                    return StatusSedeDecision.Fixed("C", "COMUNI_PENDOLARI (stessa provincia, non in COMUNI_FUORISEDE)");

                var provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();
                var provSede = (info.InformazioniIscrizione.ProvinciaSedeStudi ?? "").Trim().ToUpperInvariant();

                if (Eq(provRes, provSede))
                {
                    if (!info.InformazioniSede.InSedeList && !info.InformazioniSede.PendolareList && !info.InformazioniSede.FuoriSedeList)
                        pendolareDefaultSameProvNoLists = true;
                }

                if (pendolareDefaultSameProvNoLists)
                    return StatusSedeDecision.Fixed("C", "Stessa provincia ma assente da COMUNI_INSEDE/COMUNI_PENDOLARI/COMUNI_FUORISEDE => pendolare default");

                var dom = DomicilioValidator.Validate(info, aaStart, aaEnd, _referenceDate);
                if (!dom.Presente)
                    return StatusSedeDecision.WithDom("D", "Dati domicilio non presenti => pendolare calcolato (D)", dom);

                if (dom.Valido)
                {
                    if (IsTipoEnteErasmus(dom.TipoEnte))
                        return StatusSedeDecision.WithDom(
                            "B",
                            $"{dom.Source}: domicilio valido con TipoEnte=SE (contratto Erasmus) => fuori sede (B) | {dom.Reason}",
                            dom);

                    if (IsComuneCompatibile(dom.ComuneDomicilio, comuneSede))
                        return StatusSedeDecision.WithDom(
                            "B",
                            $"{dom.Source}: domicilio valido e nel comune sede studi/equiparato => fuori sede (B) | {dom.Reason}",
                            dom);

                    return StatusSedeDecision.WithDom(
                        "D",
                        $"{dom.Source}: domicilio valido ma comune domicilio diverso da comune sede studi => pendolare calcolato (D) | {dom.Reason}",
                        dom);
                }

                return StatusSedeDecision.WithDom(
                    "D",
                    $"{dom.Source}: domicilio presente ma non valido => pendolare calcolato (D) | {dom.Reason}",
                    dom);
            }

            private bool IsComuneCompatibile(string? comune1, string? comune2)
            {
                if (Eq(comune1, comune2))
                    return true;

                return _comuniEquiparati.Contains(NormalizeComunePair(comune1, comune2));
            }

            private static bool IsTipoEnteErasmus(string? tipoEnte)
                => string.Equals((tipoEnte ?? "").Trim(), "SE", StringComparison.OrdinalIgnoreCase);

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
            private static bool IsSeqOne(decimal seq) => Math.Abs(seq - 1m) < 0.0001m;
            private static bool Eq(string? a, string? b)
                => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        internal static class DomicilioValidator
        {
            public static DomResult Validate(StudenteInfo info, DateTime aaStart, DateTime aaEnd, DateTime referenceDate)
            {
                int minMesiDom = info.InformazioniSede.MinMesiDomicilioFuoriSede;
                string infoSemFiltro = "";
                if (info.InformazioniIscrizione.ConfermaSemestreFiltro == 1)
                {
                    minMesiDom = 3;
                    infoSemFiltro = " SEMESTRE FILTRO";
                }
                var corrente = ValidateSnapshot(
                    BuildCurrentSnapshot(info),
                    minMesiDom,
                    aaStart,
                    aaEnd,
                    referenceDate,
                    "DOMICILIO CORRENTE" + infoSemFiltro);

                bool hasIstanza = info.InformazioniSede.HasIstanzaDomicilio && info.InformazioniSede.IstanzaDomicilio != null;
                if (!hasIstanza)
                    return corrente;

                var istanza = ValidateSnapshot(
                    info.InformazioniSede.IstanzaDomicilio!,
                    minMesiDom,
                    aaStart,
                    aaEnd,
                    referenceDate,
                    $"ISTANZA APERTA {info.InformazioniSede.NumIstanzaDomicilio}");

                if (!corrente.Presente && istanza.Presente)
                {
                    return istanza with
                    {
                        ValidoCertoPerSaldo = false,
                        Reason = "Solo istanza aperta: valida per valutazione ordinaria, non sufficiente per saldo SA" +
                                 $" | Istanza: {istanza.Reason}"
                    };
                }

                if (corrente.Presente && !istanza.Presente)
                    return corrente;

                if (!corrente.Presente && !istanza.Presente)
                    return corrente;

                if (istanza.Valido)
                {
                    string comuneValutato = !string.IsNullOrWhiteSpace(istanza.ComuneDomicilio)
                        ? istanza.ComuneDomicilio
                        : corrente.ComuneDomicilio;

                    string tipoEnteValutato = !string.IsNullOrWhiteSpace(istanza.TipoEnte)
                        ? istanza.TipoEnte
                        : corrente.TipoEnte;

                    if (corrente.Valido)
                    {
                        return new DomResult(
                            Presente: true,
                            Valido: true,
                            Reason:
                                "Domicilio corrente valido e istanza aperta valida: " +
                                "usata l'istanza come probabile validità futura del domicilio" +
                                $" | Corrente: {corrente.Reason}" +
                                $" | Istanza: {istanza.Reason}",
                            ComuneDomicilio: comuneValutato,
                            TipoEnte: tipoEnteValutato,
                            Source: "ISTANZA FUTURA + DOMICILIO CORRENTE",
                            ValidoCertoPerSaldo: corrente.ValidoCertoPerSaldo);
                    }

                    return new DomResult(
                        Presente: true,
                        Valido: true,
                        Reason:
                            "Domicilio corrente non valido, ma istanza aperta valida: " +
                            "usata l'istanza come probabile validità futura del domicilio" +
                            $" | Corrente: {corrente.Reason}" +
                            $" | Istanza: {istanza.Reason}",
                        ComuneDomicilio: comuneValutato,
                        TipoEnte: tipoEnteValutato,
                        Source: "ISTANZA FUTURA",
                        ValidoCertoPerSaldo: false);
                }

                if (corrente.Valido)
                {
                    return new DomResult(
                        Presente: true,
                        Valido: true,
                        Reason:
                            "Domicilio corrente valido, ma istanza aperta non valida: " +
                            "il domicilio attuale resta valido, però l'istanza non conferma la probabile validità futura" +
                            $" | Corrente: {corrente.Reason}" +
                            $" | Istanza: {istanza.Reason}",
                        ComuneDomicilio: corrente.ComuneDomicilio,
                        TipoEnte: corrente.TipoEnte,
                        Source: "DOMICILIO CORRENTE + ISTANZA FUTURA",
                        ValidoCertoPerSaldo: corrente.ValidoCertoPerSaldo);
                }

                return new DomResult(
                    Presente: true,
                    Valido: false,
                    Reason:
                        "Domicilio corrente e istanza aperta presenti ma non validi" +
                        $" | Corrente: {corrente.Reason}" +
                        $" | Istanza: {istanza.Reason}",
                    ComuneDomicilio: !string.IsNullOrWhiteSpace(corrente.ComuneDomicilio)
                        ? corrente.ComuneDomicilio
                        : istanza.ComuneDomicilio,
                    TipoEnte: !string.IsNullOrWhiteSpace(corrente.TipoEnte)
                        ? corrente.TipoEnte
                        : istanza.TipoEnte,
                    Source: "DOMICILIO CORRENTE + ISTANZA",
                    ValidoCertoPerSaldo: false);
            }

            private static DomResult ValidateSnapshot(
                DomicilioSnapshot dom,
                int minMesiDb,
                DateTime aaStart,
                DateTime aaEnd,
                DateTime referenceDate,
                string source)
            {
                string comuneDom = (dom.ComuneDomicilio ?? "").Trim();
                bool titoloOneroso = dom.TitoloOneroso;
                bool contrattoEnte = dom.ContrattoEnte;
                string tipoEnte = (dom.TipoEnte ?? "").Trim().ToUpperInvariant();

                bool presente = comuneDom.Length > 0 && (titoloOneroso || contrattoEnte);
                if (!presente)
                    return new DomResult(false, false, "Dati domicilio non presenti", comuneDom, tipoEnte, source);

                int min = minMesiDb > 0 ? minMesiDb : 10;

                if (contrattoEnte)
                {
                    if (string.IsNullOrWhiteSpace(dom.DenomEnte))
                        return new DomResult(true, false, "Contratto ente senza denominazione ente", comuneDom, tipoEnte, source);

                    if (dom.DurataContratto < min)
                        return new DomResult(true, false, $"Durata contratto ente < minimo richiesto ({dom.DurataContratto} < {min})", comuneDom, tipoEnte, source);

                    if (dom.ImportoRataEnte <= 0)
                        return new DomResult(true, false, "Importo rata ente nullo o negativo", comuneDom, tipoEnte, source);

                    return new DomResult(true, true, $"Contratto ente valido (durata={dom.DurataContratto}, minimo={min})", comuneDom, tipoEnte, source);
                }

                if (!HasValidDate(dom.DataRegistrazione))
                    return new DomResult(true, false, "Data registrazione non valida", comuneDom, tipoEnte, source);

                if (!HasValidDate(dom.DataDecorrenza))
                    return new DomResult(true, false, "Data decorrenza non valida", comuneDom, tipoEnte, source);

                if (!HasValidDate(dom.DataScadenza))
                    return new DomResult(true, false, "Data scadenza non valida", comuneDom, tipoEnte, source);

                if (dom.DataScadenza < dom.DataDecorrenza)
                    return new DomResult(true, false, "Scadenza < decorrenza", comuneDom, tipoEnte, source);

                if (dom.DataRegistrazione.Date > dom.DataScadenza.Date)
                    return new DomResult(true, false, "Data registrazione successiva alla scadenza", comuneDom, tipoEnte, source);

                var effStart = dom.DataDecorrenza > aaStart ? dom.DataDecorrenza : aaStart;
                var effEnd = dom.DataScadenza < aaEnd ? dom.DataScadenza : aaEnd;

                if (effStart > effEnd)
                    return new DomResult(true, false, "Contratto fuori dall'intervallo AA", comuneDom, tipoEnte, source);

                string serieContratto = (dom.SerieContratto ?? "").Trim();
                if (!DomicilioUtils.IsValidSerie(serieContratto))
                    return new DomResult(true, false, "Serie contratto non valida", comuneDom, tipoEnte, source);

                string serieProroga = (dom.SerieProroga ?? "").Trim();
                bool hasProroga = dom.Prorogato;

                if (hasProroga)
                {
                    if (dom.DurataProroga <= 0)
                        return new DomResult(true, false, "Durata proroga non valida", comuneDom, tipoEnte, source);

                    if (!DomicilioUtils.IsValidSerie(serieProroga))
                        return new DomResult(true, false, "Serie proroga non valida", comuneDom, tipoEnte, source);

                    if (serieProroga.IndexOf(serieContratto, StringComparison.OrdinalIgnoreCase) >= 0)
                        return new DomResult(true, false, "Serie proroga uguale o contenente la serie del contratto", comuneDom, tipoEnte, source);
                }

                int mesi = CoveredMonths(effStart, effEnd);
                if (mesi < min)
                {
                    bool haBuchiInizialiRilevanti = false;
                    if (effStart > aaStart)
                    {
                        DateTime periodoScopertoFine = effStart.AddDays(-1);
                        haBuchiInizialiRilevanti = CoveredMonths(aaStart, periodoScopertoFine) > 0;
                    }

                    bool finestraProrogaApplicabile =
                        !haBuchiInizialiRilevanti
                        && dom.DataScadenza.Date < aaEnd.Date
                        && referenceDate.Date <= dom.DataScadenza.Date.AddDays(30);

                    if (finestraProrogaApplicabile)
                    {
                        return new DomResult(
                            true,
                            true,
                            $"Valido in finestra proroga 30 giorni (mesi coperti={mesi}, minimo={min}, scadenza={dom.DataScadenza:dd/MM/yyyy}) - non sufficiente per saldo SA",
                            comuneDom,
                            tipoEnte,
                            source,
                            ValidoCertoPerSaldo: false);
                    }

                    if (haBuchiInizialiRilevanti && dom.DataScadenza.Date >= aaEnd.Date)
                    {
                        return new DomResult(
                            true,
                            false,
                            $"Mesi coperti {mesi} < minimo {min}: copertura concentrata sugli ultimi mesi dell'AA e non prorogabile oltre il termine annuale ({aaEnd:dd/MM/yyyy})",
                            comuneDom,
                            tipoEnte,
                            source);
                    }

                    return new DomResult(true, false, $"Mesi coperti {mesi} < minimo {min}", comuneDom, tipoEnte, source);
                }

                return new DomResult(true, true, $"OK (mesi coperti={mesi}, minimo={min})", comuneDom, tipoEnte, source);
            }

            private static DomicilioSnapshot BuildCurrentSnapshot(StudenteInfo info)
            {
                var dom = info.InformazioniSede.Domicilio ?? new Domicilio();

                return new DomicilioSnapshot
                {
                    ComuneDomicilio = (dom.codComuneDomicilio ?? "").Trim(),
                    TitoloOneroso = dom.titoloOneroso,
                    ContrattoEnte = dom.contrEnte || info.InformazioniSede.ContrattoEnte,
                    TipoEnte = (dom.TipoEnte ?? "").Trim().ToUpperInvariant(),
                    SerieContratto = (dom.codiceSerieLocazione ?? "").Trim(),
                    DataRegistrazione = dom.dataRegistrazioneLocazione,
                    DataDecorrenza = dom.dataDecorrenzaLocazione,
                    DataScadenza = dom.dataScadenzaLocazione,
                    DurataContratto = dom.durataMesiLocazione,
                    Prorogato = dom.prorogatoLocazione,
                    DurataProroga = dom.durataMesiProrogaLocazione,
                    SerieProroga = (dom.codiceSerieProrogaLocazione ?? "").Trim(),
                    DenomEnte = (dom.denominazioneIstituto ?? "").Trim(),
                    ImportoRataEnte = dom.importoMensileRataIstituto
                };
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
                    if (days >= 15)
                        count++;

                    cur = cur.AddMonths(1);
                }

                return count;
            }
        }

        internal readonly record struct DomResult(
            bool Presente,
            bool Valido,
            string Reason,
            string ComuneDomicilio,
            string TipoEnte,
            string Source,
            bool ValidoCertoPerSaldo)
        {
            public DomResult(
                bool Presente,
                bool Valido,
                string Reason,
                string ComuneDomicilio,
                string TipoEnte,
                string Source)
                : this(Presente, Valido, Reason, ComuneDomicilio, TipoEnte, Source, Valido)
            {
            }
        }

        private sealed class StatusSedeDecision
        {
            public string SuggestedStatus { get; }
            public string Reason { get; }
            public bool DomicilioPresente { get; }
            public bool DomicilioValido { get; }
            public bool FuoriSedeCertoPerSaldo { get; }

            private StatusSedeDecision(
                string suggested,
                string reason,
                bool domPres,
                bool domVal,
                bool fuoriSedeCertoPerSaldo)
            {
                SuggestedStatus = suggested;
                Reason = reason;
                DomicilioPresente = domPres;
                DomicilioValido = domVal;
                FuoriSedeCertoPerSaldo = fuoriSedeCertoPerSaldo;
            }

            public static StatusSedeDecision Fixed(string suggested, string reason)
                => new StatusSedeDecision(
                    suggested,
                    reason,
                    domPres: false,
                    domVal: false,
                    fuoriSedeCertoPerSaldo: string.Equals((suggested ?? "").Trim(), "B", StringComparison.OrdinalIgnoreCase));

            public static StatusSedeDecision WithDom(string suggested, string reason, DomResult dom)
                => new StatusSedeDecision(
                    suggested,
                    reason,
                    dom.Presente,
                    dom.Valido,
                    string.Equals((suggested ?? "").Trim(), "B", StringComparison.OrdinalIgnoreCase) && dom.ValidoCertoPerSaldo);
        }

        private static (string ComuneA, string ComuneB) NormalizeComunePair(string? comuneA, string? comuneB)
        {
            string a = (comuneA ?? "").Trim().ToUpperInvariant();
            string b = (comuneB ?? "").Trim().ToUpperInvariant();

            return string.CompareOrdinal(a, b) <= 0
                ? (a, b)
                : (b, a);
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

        private static (DateTime aaStart, DateTime aaEnd) GetAaDateRange(string aa)
        {
            int startYear = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int endYear = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            return (new DateTime(startYear, 10, 1), new DateTime(endYear, 9, 30));
        }
    }
}
