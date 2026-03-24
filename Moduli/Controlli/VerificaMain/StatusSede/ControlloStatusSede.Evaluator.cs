using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class ControlloStatusSede
    {
        private sealed class StatusSedeEvaluator
        {
            private readonly HashSet<(string ComuneA, string ComuneB)> _comuniEquiparati;

            public StatusSedeEvaluator(HashSet<(string ComuneA, string ComuneB)> comuniEquiparati)
            {
                _comuniEquiparati = comuniEquiparati ?? new HashSet<(string ComuneA, string ComuneB)>();
            }

            public StatusSedeDecision Evaluate(StatusSedeStudent row, DateTime aaStart, DateTime aaEnd, DateTime referenceDate)
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

                var eco = info.InformazioniEconomiche;
                if (eco != null
                    && string.Equals((eco.TipoRedditoOrigine ?? "").Trim(), "EE", StringComparison.OrdinalIgnoreCase)
                    && IsSeqOne(eco.SEQ)
                    && eco.ISRDSU >= 9000m)
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

                if (row.HasAlloggio12)
                    return StatusSedeDecision.Fixed("B", "PA: idoneo/vincitore (1/2) => fuori sede");

                bool pendolareDefaultSameProvNoLists = false;

                if (row.InSedeList)
                    return StatusSedeDecision.Fixed("A", "COMUNI_INSEDE (stessa provincia)");

                if (row.PendolareList && !row.FuoriSedeList)
                    return StatusSedeDecision.Fixed("C", "COMUNI_PENDOLARI (stessa provincia, non in COMUNI_FUORISEDE)");

                var provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();
                var provSede = (info.InformazioniIscrizione.ProvinciaSedeStudi ?? "").Trim().ToUpperInvariant();

                if (Eq(provRes, provSede))
                {
                    if (!row.InSedeList && !row.PendolareList && !row.FuoriSedeList)
                        pendolareDefaultSameProvNoLists = true;
                }

                if (pendolareDefaultSameProvNoLists)
                    return StatusSedeDecision.Fixed("C", "Stessa provincia ma assente da COMUNI_INSEDE/COMUNI_PENDOLARI/COMUNI_FUORISEDE => pendolare default");

                var dom = DomicilioValidator.Validate(row, aaStart, aaEnd, referenceDate);
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

        // =========================
        // DOMICILIO (usa classi informazione)
        // =========================
        private static class DomicilioValidator
        {
            public static DomResult Validate(StatusSedeStudent row, DateTime aaStart, DateTime aaEnd, DateTime referenceDate)
            {
                int minMesiDom = row.MinMesiDomicilioFuoriSede;
                if(row.Info.InformazioniIscrizione.ConfermaSemestreFiltro == 1)
                {
                    minMesiDom = 3;
                }

                var corrente = ValidateSnapshot(
                    BuildCurrentSnapshot(row.Info),
                    minMesiDom,
                    aaStart,
                    aaEnd,
                    referenceDate,
                    "DOMICILIO CORRENTE");

                bool hasIstanza = row.HasIstanzaDomicilio && row.IstanzaDomicilio != null;
                if (!hasIstanza)
                    return corrente;

                var istanza = ValidateSnapshot(
                    row.IstanzaDomicilio!,
                    minMesiDom,
                    aaStart,
                    aaEnd,
                    referenceDate,
                    $"ISTANZA APERTA {row.NumIstanzaDomicilio}");

                if (!corrente.Presente && istanza.Presente)
                    return istanza;

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
                            Source: "ISTANZA FUTURA + DOMICILIO CORRENTE");
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
                        Source: "ISTANZA FUTURA");
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
                        Source: "DOMICILIO CORRENTE + ISTANZA FUTURA");
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
                    Source: "DOMICILIO CORRENTE + ISTANZA");
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
                    if (referenceDate.Date <= dom.DataScadenza.Date.AddDays(30))
                    {
                        return new DomResult(
                            true,
                            true,
                            $"Valido in finestra proroga 30 giorni (mesi coperti={mesi}, minimo={min}, scadenza={dom.DataScadenza:dd/MM/yyyy})",
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

        private readonly record struct DomResult(
            bool Presente,
            bool Valido,
            string Reason,
            string ComuneDomicilio,
            string TipoEnte,
            string Source);

        private sealed class StatusSedeDecision
        {
            public string SuggestedStatus { get; }
            public string Reason { get; }
            public bool DomicilioPresente { get; }
            public bool DomicilioValido { get; }

            private StatusSedeDecision(string suggested, string reason, bool domPres, bool domVal)
            {
                SuggestedStatus = suggested;
                Reason = reason;
                DomicilioPresente = domPres;
                DomicilioValido = domVal;
            }

            public static StatusSedeDecision Fixed(string suggested, string reason)
                => new StatusSedeDecision(suggested, reason, domPres: false, domVal: false);

            public static StatusSedeDecision WithDom(string suggested, string reason, DomResult dom)
                => new StatusSedeDecision(suggested, reason, dom.Presente, dom.Valido);
        }
    }
}
