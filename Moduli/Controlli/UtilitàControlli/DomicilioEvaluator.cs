// =====================
// 1) FUNZIONE STATICA RIUSABILE (domicilio + istanze)
// =====================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal static class DomicilioEvaluator
{
    // stessi codici del ControlloDomicilio
    public static readonly IReadOnlyDictionary<string, string> ErrorDescriptions = new Dictionary<string, string>
    {
        ["001"] = "Codice fiscale mancante per il domicilio.",
        ["002"] = "Data registrazione contratto non valida.",
        ["003"] = "Data decorrenza contratto non valida.",
        ["004"] = "Data scadenza contratto non valida.",
        ["005"] = "Contratto copre meno di 10 mesi nel periodo indicato dal bando.",
        ["006"] = "Contratto non copre alcun periodo utile nell'anno accademico.",
        ["007"] = "Contratto ente indicato ma denominazione ente mancante.",
        ["008"] = "Durata contratto ente inferiore a 10 mesi.",
        ["009"] = "Importo rata ente nullo o negativo.",
        ["010"] = "La serie della proroga è uguale alla serie del contratto.",
        ["011"] = "Numero di serie del contratto non valido.",
        ["012"] = "Numero di serie della proroga non valida."
    };

    private static readonly string[] _dateFormats = new[]
    {
        "yyyy-MM-dd","yyyyMMdd","dd/MM/yyyy","d/M/yyyy","dd-MM-yyyy","d-M-yyyy",
        "yyyy-MM-dd HH:mm:ss","dd/MM/yyyy HH:mm:ss"
    };

    public sealed record ContractInput(
        string CodFiscale,
        string ComuneDomicilio,
        bool TitoloOneroso,
        bool ContrattoEnte,
        string SerieContratto,
        string DataRegistrazioneString,
        string DataDecorrenzaString,
        string DataScadenzaString,
        int DurataContratto,
        bool Prorogato,
        int DurataProroga,
        string SerieProroga,
        string DenominazioneEnte,
        double ImportoRataEnte
    );
    public sealed record LastWorkedIstanza(
        int Esito,                      // 0 = rifiutata
        DateTime DataCreazione,         // idg.Data_validita
        DateTime? ScadenzaContratto,    // icl.Data_scadenza (dell’istanza)
        DateTime? DataEsito             // max(iis.DataFineValidita)
    );
    public sealed record ContractValidationResult(
        bool IsValid,
        bool ValidatoDaContratto,
        bool ValidatoDaEnte,
        string TipoValidazione,
        int MesiCoperti,
        bool ContrattoValido,
        bool ProrogaValida,
        bool ContrattoEnteValido,
        IReadOnlyList<string> ErrorCodes,
        bool WithinGrace30,
        DateTime? DataScadenza,
        bool Within10AfterReject        // NUOVO
    )
    {
        public string ErrorDescriptionsJoined =>
            ErrorCodes.Count == 0 ? "" :
            string.Join(" | ", ErrorCodes.Select(c => ErrorDescriptions.TryGetValue(c, out var d) ? d : c));
    }

    public sealed record DomicilioStatusResult(
        bool DomicilioValidoPerStatus,
        string Source,                // "LRS", "ISTANZA", "LRS+ISTANZA", "NESSUNO"
        string Reason,
        ContractValidationResult? Main,
        ContractValidationResult? Istanza
    );

    // Validazione "contratto" (come ControlloDomicilio) senza UI/DB
    public static ContractValidationResult ValidateContract(
            ContractInput x,
            DateTime aaStart,
            DateTime aaEnd,
            DateTime now,
            int graceDays = 30)
    {
        var err = new List<string>();
        void Add(string c) { if (!err.Contains(c)) err.Add(c); }

        if (string.IsNullOrWhiteSpace(x.CodFiscale)) Add("001");

        bool validatoDaEnte = false;
        bool contrattoEnteValido = false;

        if (x.ContrattoEnte)
        {
            if (string.IsNullOrWhiteSpace(x.DenominazioneEnte)) Add("007");
            else
            {
                if (x.DurataContratto < 10) Add("008");
                if (x.ImportoRataEnte <= 0) Add("009");

                if (x.DurataContratto >= 10 && x.ImportoRataEnte > 0)
                {
                    contrattoEnteValido = true;
                    validatoDaEnte = true;
                }
            }
        }

        var dataRegOk = TryParseDate(x.DataRegistrazioneString, out var dataReg);
        var dataDecOk = TryParseDate(x.DataDecorrenzaString, out var dataDec);
        var dataScaOk = TryParseDate(x.DataScadenzaString, out var dataSca);

        if (!x.ContrattoEnte)
        {
            if (!dataRegOk) Add("002");
            if (!dataDecOk) Add("003");
            if (!dataScaOk) Add("004");
        }

        bool withinGrace30 = false;
        DateTime? parsedScadenza = null;

        if (dataScaOk)
        {
            dataSca = dataSca.Date;
            parsedScadenza = dataSca;
            withinGrace30 = now.Date <= dataSca.AddDays(graceDays);
        }

        bool validatoDaContratto = false;
        int mesiCoperti = 0;

        if (x.TitoloOneroso && dataDecOk && dataScaOk)
        {
            dataDec = dataDec.Date;

            var effectiveStart = (dataDec > aaStart) ? dataDec : aaStart;
            var effectiveEnd = (dataSca < aaEnd) ? dataSca : aaEnd;

            if (effectiveStart <= effectiveEnd)
            {
                mesiCoperti = ComputeCoveredMonths15Days(effectiveStart, effectiveEnd);

                // CONTRATTO valido solo se mesi>=10 e siamo entro scadenza+30
                if (mesiCoperti >= 10 && withinGrace30)
                    validatoDaContratto = true;
                else
                {
                    if (mesiCoperti < 10) Add("005");
                }
            }
            else
            {
                Add("006");
            }
        }

        bool contrattoValido = false;
        bool prorogaValida = false;

        if (!x.ContrattoEnte)
        {
            if (!string.IsNullOrWhiteSpace(x.SerieContratto))
            {
                contrattoValido = IsValidSerie(x.SerieContratto);
                if (!contrattoValido) Add("011");
            }

            if (!string.IsNullOrWhiteSpace(x.SerieProroga))
            {
                prorogaValida = IsValidSerie(x.SerieProroga);

                if (!string.IsNullOrWhiteSpace(x.SerieContratto) &&
                    x.SerieProroga.IndexOf(x.SerieContratto, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    prorogaValida = false;
                    Add("010");
                }

                if (!prorogaValida) Add("012");
            }
        }

        var isValid = validatoDaContratto || validatoDaEnte;

        var tipo =
            (validatoDaContratto && validatoDaEnte) ? "CONTRATTO+ENTE" :
            (validatoDaContratto) ? "CONTRATTO" :
            (validatoDaEnte) ? "ENTE" : "NESSUNA";

        return new ContractValidationResult(
            IsValid: isValid,
            ValidatoDaContratto: validatoDaContratto,
            ValidatoDaEnte: validatoDaEnte,
            TipoValidazione: tipo,
            MesiCoperti: mesiCoperti,
            ContrattoValido: contrattoValido,
            ProrogaValida: prorogaValida,
            ContrattoEnteValido: contrattoEnteValido,
            ErrorCodes: err,
            WithinGrace30: withinGrace30,
            DataScadenza: parsedScadenza,
            Within10AfterReject: false
        );
    }

    // MODIFICA: EvaluateForStatus calcola la finestra “10 giorni post rifiuto” e la usa come time-override
    public static DomicilioStatusResult EvaluateForStatus(
        ContractInput mainLrs,
        ContractInput? openIstanza,
        DateTime aaStart,
        DateTime aaEnd,
        DateTime now,
        DateTime deadlineBase,                 // es. 2025-12-30 23:59:59
        LastWorkedIstanza? lastWorked,         // ultimo lavorato (anche null)
        string comuneResidenza,
        string comuneSedeStudi,
        Func<string?, string?, bool> areComuniCompatible,
        bool requireGeoForStatus = true)
    {
        var main0 = ValidateContract(mainLrs, aaStart, aaEnd, now);
        ContractValidationResult? ist0 = null;

        if (openIstanza != null)
            ist0 = ValidateContract(openIstanza, aaStart, aaEnd, now);

        // geo
        bool mainGeoOk = !requireGeoForStatus || GeoOk(mainLrs.ComuneDomicilio, comuneResidenza, comuneSedeStudi, areComuniCompatible);
        bool istGeoOk = !requireGeoForStatus || (openIstanza != null && GeoOk(openIstanza.ComuneDomicilio, comuneResidenza, comuneSedeStudi, areComuniCompatible));

        // 30gg post-scadenza (su “ultima scadenza” usata per il time gate)
        DateTime? lastScadenza = main0.DataScadenza ?? ist0?.DataScadenza;
        bool within30FromScadenza = lastScadenza.HasValue && now <= EndOfDay(lastScadenza.Value.AddDays(30));

        // 10gg post rifiuto: solo se NON within30FromScadenza
        bool within10AfterReject = false;

        if (!within30FromScadenza && lastWorked != null)
        {
            if (lastWorked.Esito == 0 && lastWorked.DataEsito.HasValue)
            {
                bool inviataInTempo =
                    lastWorked.DataCreazione <= deadlineBase ||
                    (lastWorked.ScadenzaContratto.HasValue &&
                     lastWorked.DataCreazione <= EndOfDay(lastWorked.ScadenzaContratto.Value.Date.AddDays(30)));

                if (inviataInTempo)
                {
                    var fineFinestra = EndOfDay(lastWorked.DataEsito.Value.Date.AddDays(10));
                    within10AfterReject = now <= fineFinestra;
                }
            }
        }

        // Time gate finale per “contratto come valido ai fini status”
        // - normale: scadenza+30 (già in main0/ist0: WithinGrace30)
        // - override: 10gg post rifiuto (soccorso) anche se oltre i 30gg
        bool mainTimeOk = main0.WithinGrace30 || within10AfterReject;
        bool istTimeOk = (ist0?.WithinGrace30 ?? false) || within10AfterReject;

        var main = main0 with { Within10AfterReject = within10AfterReject };
        var ist = ist0 == null ? null : ist0 with { Within10AfterReject = within10AfterReject };

        // VALIDITÀ PER STATUS:
        // - se ho un contratto/ente valido -> ok (ma rispettando time gate per il contratto)
        // - se sono nel 10gg post rifiuto -> ok anche se il contratto non è “validato” (soccorso istruttorio),
        //   ma sempre con geoOk e prorogaOk
        bool mainValidForStatus =
            (within30FromScadenza || within10AfterReject || (main.IsValid && (main.ValidatoDaEnte || mainTimeOk))) &&
            mainGeoOk &&
            (!mainLrs.Prorogato || main.ProrogaValida);

        bool istValidForStatus =
            (ist != null) &&
            (within10AfterReject || (ist.IsValid && (ist.ValidatoDaEnte || istTimeOk))) &&
            istGeoOk &&
            (!openIstanza!.Prorogato || ist.ProrogaValida);

        string source;
        bool ok;
        if (mainValidForStatus && istValidForStatus) { ok = true; source = "LRS+ISTANZA"; }
        else if (mainValidForStatus) { ok = true; source = "LRS"; }
        else if (istValidForStatus) { ok = true; source = "ISTANZA"; }
        else { ok = false; source = "NESSUNO"; }

        string reason =
            $"src={source} | " +
            $"win30={(within30FromScadenza ? "Y" : "N")}, win10={(within10AfterReject ? "Y" : "N")} | " +
            $"LRS[{main.TipoValidazione}, mesi={main.MesiCoperti}, grace30={(main.WithinGrace30 ? "Y" : "N")}, geo={(mainGeoOk ? "Y" : "N")}, prorOk={(!mainLrs.Prorogato || main.ProrogaValida ? "Y" : "N")}] | " +
            (ist == null ? "IST[assente]" :
             $"IST[{ist.TipoValidazione}, mesi={ist.MesiCoperti}, grace30={(ist.WithinGrace30 ? "Y" : "N")}, geo={(istGeoOk ? "Y" : "N")}, prorOk={(!openIstanza!.Prorogato || ist.ProrogaValida ? "Y" : "N")}]");

        return new DomicilioStatusResult(ok, source, reason, main, ist);

        static DateTime EndOfDay(DateTime d) => d.Date.AddDays(1).AddTicks(-1);
    }

    private static bool GeoOk(string? comuneDom, string? comuneRes, string? comuneSede, Func<string?, string?, bool> areComuniCompatible)
    {
        var dom = (comuneDom ?? "").Trim();
        var res = (comuneRes ?? "").Trim();
        var sede = (comuneSede ?? "").Trim();

        bool domEqResCompat = areComuniCompatible(dom, res);
        bool domEqSede = dom.Equals(sede, StringComparison.OrdinalIgnoreCase);
        return !domEqResCompat && domEqSede;
    }

    private static bool TryParseDate(string? s, out DateTime dt)
    {
        dt = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        return DateTime.TryParseExact(
            s.Trim(),
            _dateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
            out dt
        );
    }

    private static int ComputeCoveredMonths15Days(DateTime effectiveStart, DateTime effectiveEnd)
    {
        int monthsCovered = 0;
        var current = new DateTime(effectiveStart.Year, effectiveStart.Month, 1);

        while (current <= effectiveEnd)
        {
            var monthStart = current;
            var monthEnd = current.AddMonths(1).AddDays(-1);

            var coverageStart = monthStart < effectiveStart ? effectiveStart : monthStart;
            var coverageEnd = monthEnd > effectiveEnd ? effectiveEnd : monthEnd;

            var days = (coverageEnd - coverageStart).TotalDays + 1;
            if (days >= 15) monthsCovered++;

            current = current.AddMonths(1);
        }
        return monthsCovered;
    }

    // riusa la tua logica (qui minimale; puoi sostituire con regex reale)
    public static bool IsValidSerie(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var x = s.Trim();
        return x.Length >= 3;
    }
}
