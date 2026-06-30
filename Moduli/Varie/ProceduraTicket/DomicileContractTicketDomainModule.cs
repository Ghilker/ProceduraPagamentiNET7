using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProcedureNet7
{
    /// <summary>
    /// Dominio Domicilio/Contratto: separato dal beneficio "posto alloggio".
    /// Valuta solo richieste su contratto di locazione, status fuori sede e istanze domicilio.
    /// </summary>
    internal sealed class DomicileContractTicketDomainModule : ITicketDomainModule
    {
        public const string DomainCode = "DOMICILIO_CONTRATTO";
        private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

        private static readonly string[] ContractTokens =
        {
            "contratto di locazione", "contratto locazione", "contratto di affitto",
            "contratto affitto", "contratto", "locazione", "affitto", "proroga",
            "rinnovo automatico", "rinnovato automaticamente", "registrazione agenzia",
            "agenzia delle entrate", "data di fine contratto", "scadenza contratto",
            "decorrenza contratto", "durata contratto", "rental contract", "lease contract",
            "tenancy agreement"
        };

        private static readonly string[] DomicileTokens =
        {
            "domicilio", "fuori sede", "fuorisede", "status sede", "status_sede",
            "istanza domicilio", "istanza di domicilio", "apertura istanza",
            "istanza aperta", "lavorazione istanza", "lavorare l istanza",
            "lavorare l'istanza", "conferma domicilio", "conferma semestre",
            "semestre filtro", "domicile", "off site", "off-site"
        };

        private static readonly string[] DomicileRequestTokens =
        {
            "ho aperto istanza", "ho aperto un istanza", "ho aperto un'istanza",
            "istanza aperta", "richiedo la lavorazione", "chiedo la lavorazione",
            "potete lavorare", "potete verificare", "ho caricato", "ho inserito",
            "ho allegato", "allego", "caricato contratto", "inserito contratto",
            "inserimento proroga", "proroga contratto", "modificato la sezione",
            "documenti relativi al contratto", "please check", "please verify",
            "uploaded", "submitted"
        };

        private static readonly string[] HousingBenefitTokens =
        {
            "posto alloggio", "posti alloggio", "residenza universitaria",
            "residenze universitarie", "posto in residenza", "alloggio presso la residenza",
            "borsa alloggio", "beneficio alloggio", "check out", "check-out",
            "residence", "valle aurelia", "student residence", "university residence"
        };

        private static readonly string[] AddressChangeTokens =
        {
            "cambio residenza", "cambio di residenza", "cambiare residenza",
            "nuova residenza", "ho cambiato residenza", "cambio domicilio",
            "cambio di domicilio", "ho cambiato domicilio", "nuovo domicilio",
            "trasferiro", "trasferirò", "mi trasferiro", "mi trasferirò"
        };

        private static readonly string[] SemesterFilterTokens =
        {
            "conferma semestre", "semestre filtro"
        };

        public string Code => DomainCode;

        public TicketDomainScopeMatch DetectScope(TicketDomainRequest request)
        {
            string text = request.NormalizedText;
            bool topicAlloggio =
                string.Equals(request.PrimaryTopicCode, "ALLOGGIO", StringComparison.OrdinalIgnoreCase) ||
                TicketDomainText.TopicContainsAny(request.SecondaryTopicCode, "CONTRATTO", "ALLOGGIO");
            bool mentionsContract = TicketDomainText.ContainsAny(text, ContractTokens);
            bool mentionsDomicile = TicketDomainText.ContainsAny(text, DomicileTokens);
            bool housingBenefit = TicketDomainText.ContainsAny(text, HousingBenefitTokens);
            bool onlyAddressChange =
                TicketDomainText.ContainsAny(text, AddressChangeTokens) &&
                !mentionsContract &&
                !TicketDomainText.ContainsAny(text, "istanza", "fuori sede", "fuorisede", "status sede");

            bool requested =
                topicAlloggio &&
                !housingBenefit &&
                !onlyAddressChange &&
                (mentionsContract || mentionsDomicile);

            return new TicketDomainScopeMatch
            {
                DomainCode = Code,
                Signal = "DOMICILIO_CONTRATTO",
                IsRequested = requested
            };
        }

        public TicketDomainValidationResult Validate(TicketDomainValidationContext context)
        {
            if (context.Record == null)
            {
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "DOMICILIO/CONTRATTO: DATI OPERATIVI NON DISPONIBILI",
                    UnresolvedReason = "record operativo assente",
                    PendingResponseCode = "GEN_01"
                };
            }

            TicketOfficeRecord record = context.Record;
            string text = context.Request.NormalizedText;
            bool asksOrInformsDomicileRequest = TicketDomainText.ContainsAny(text, DomicileRequestTokens);
            bool semesterFilter = TicketDomainText.ContainsAny(text, SemesterFilterTokens);
            int requiredMonths = semesterFilter ? 3 : 10;
            bool outsideStatus = string.Equals(record.CampusStatus, "B", StringComparison.OrdinalIgnoreCase);
            bool contractCoversPeriod = ContractCoversRequiredPeriod(record, requiredMonths);
            bool workedDomicile = record.HasWorkedDomicileRequest;

            if (outsideStatus && record.HasOpenDomicileRequest && asksOrInformsDomicileRequest)
            {
                return Resolved(
                    "DOMICILIO/CONTRATTO: RISOLTO - status sede B e istanza domicilio aperta presente",
                    BuildEvidence(record, requiredMonths, contractCoversPeriod),
                    "DOMICILIO_ISTANZA_APERTA_PRESENTE",
                    "ALL_06");
            }

            if (outsideStatus && (workedDomicile || record.HasOpenDomicileRequest || contractCoversPeriod))
            {
                return Resolved(
                    workedDomicile
                        ? "DOMICILIO/CONTRATTO: RISOLTO - istanza lavorata e status sede B"
                        : record.HasOpenDomicileRequest
                            ? "DOMICILIO/CONTRATTO: RISOLTO - istanza aperta e status sede B"
                            : $"DOMICILIO/CONTRATTO: RISOLTO - status sede B e contratto copre almeno {requiredMonths} mesi",
                    BuildEvidence(record, requiredMonths, contractCoversPeriod),
                    "DOMICILIO_CONTRATTO_STATUS_B_VALIDO",
                    "ALL_06");
            }

            var unresolved = new List<string>();
            if (!outsideStatus)
                unresolved.Add("status sede attuale diverso da B");
            if (!workedDomicile && !contractCoversPeriod)
            {
                unresolved.Add(string.IsNullOrWhiteSpace(record.ContractSeries)
                    ? "contratto/istanza domicilio non presenti o non lavorati"
                    : $"contratto non copre almeno {requiredMonths} mesi nel periodo richiesto");
            }

            return new TicketDomainValidationResult
            {
                DomainCode = Code,
                CheckSummary = $"DOMICILIO/CONTRATTO: NON RISOLTO - {string.Join("; ", unresolved)}",
                UnresolvedReason = string.Join("; ", unresolved),
                PendingResponseCode = string.IsNullOrWhiteSpace(record.ContractSeries) && !workedDomicile
                    ? "ALL_02"
                    : "ALL_07"
            };
        }

        public string BuildEvidence(TicketOfficeRecord record) =>
            BuildEvidence(record, requiredMonths: 10, contractCoversPeriod: ContractCoversRequiredPeriod(record, 10));

        private static TicketDomainValidationResult Resolved(
            string checkSummary,
            string evidence,
            string condition,
            string responseCode) => new()
        {
            DomainCode = DomainCode,
            IsResolved = true,
            CheckSummary = checkSummary,
            Evidence = evidence,
            ResolvedCondition = condition,
            ResolvedResponseCode = responseCode,
            PendingResponseCode = "ALL_07"
        };

        private static bool ContractCoversRequiredPeriod(TicketOfficeRecord record, int requiredMonths)
        {
            if (!TryGetAcademicYearStart(record.AcademicYear, out int startYear) ||
                !TryParseDate(record.ContractStart, out DateTime start) ||
                !TryParseDate(record.ContractEnd, out DateTime end))
            {
                return false;
            }

            DateTime windowStart = new(startYear, 10, 1);
            DateTime windowEnd = new(startYear + 1, 9, 30);
            DateTime overlapStart = start > windowStart ? start.Date : windowStart;
            DateTime overlapEnd = end < windowEnd ? end.Date : windowEnd;
            if (overlapEnd < overlapStart)
                return false;

            return overlapStart.AddMonths(requiredMonths) <= overlapEnd.AddDays(1);
        }

        private static bool TryGetAcademicYearStart(int academicYear, out int startYear)
        {
            startYear = 0;
            if (academicYear <= 0)
                return false;

            if (academicYear >= 2000 && academicYear <= 2099)
            {
                startYear = academicYear;
                return true;
            }

            string value = academicYear.ToString(CultureInfo.InvariantCulture);
            if (value.Length == 8 &&
                int.TryParse(value[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) &&
                int.TryParse(value[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int end) &&
                start >= 2000 &&
                start <= 2099 &&
                end == start + 1)
            {
                startYear = start;
                return true;
            }

            return false;
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            if (DateTime.TryParse(value, ItalianCulture, DateTimeStyles.AllowWhiteSpaces, out date))
                return true;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);
        }

        private static string BuildEvidence(
            TicketOfficeRecord record,
            int requiredMonths,
            bool contractCoversPeriod)
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(record.CampusStatus))
                values.Add($"status sede: {record.CampusStatus}");
            if (record.HasOpenDomicileRequest)
                values.Add("istanza domicilio aperta");
            if (record.HasWorkedDomicileRequest)
                values.Add("istanza domicilio lavorata");
            if (!string.IsNullOrWhiteSpace(record.LastClosedDomicileOutcome))
                values.Add($"esito ultima istanza: {record.LastClosedDomicileOutcome}");
            if (!string.IsNullOrWhiteSpace(record.ContractSeries))
                values.Add($"contratto: {record.ContractSeries}");
            if (!string.IsNullOrWhiteSpace(record.ContractStart))
                values.Add($"decorrenza: {record.ContractStart}");
            if (!string.IsNullOrWhiteSpace(record.ContractEnd))
                values.Add($"scadenza: {record.ContractEnd}");
            values.Add(contractCoversPeriod
                ? $"copertura contratto: almeno {requiredMonths} mesi"
                : $"copertura contratto: inferiore a {requiredMonths} mesi o non calcolabile");
            return string.Join("; ", values);
        }
    }
}
