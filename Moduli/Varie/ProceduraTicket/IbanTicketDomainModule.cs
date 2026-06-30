using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    /// <summary>
    /// Dominio IBAN: valuta se una richiesta di inserimento/aggiornamento coordinate
    /// risulta già risolta rispetto all'ultimo IBAN attivo registrato.
    /// </summary>
    internal sealed class IbanTicketDomainModule : ITicketDomainModule
    {
        public const string DomainCode = "IBAN";
        private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

        private static readonly Regex IbanCandidateRegex = new(
            @"\b[A-Z]{2}\s*\d{2}(?:[\s\-]?[A-Z0-9]){11,30}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] IbanTokens =
        {
            "iban", "coordinate bancarie", "conto corrente", "bank account",
            "bank details", "dati bancari", "modalita pagamento", "modalita di pagamento"
        };

        private static readonly string[] UpdateTokens =
        {
            "aggiorn", "modific", "cambiare", "cambio", "variazione", "nuovo iban",
            "inserito iban", "ho inserito", "ho caricato", "caricato iban", "comunico iban",
            "rettifica iban", "iban corretto", "iban valido", "update iban", "updated iban",
            "update another payment method", "change iban", "change bank account", "new iban"
        };

        private static readonly string[] PaymentComplaintTokens =
        {
            "non ho ricevuto", "non ricevut", "mancato accredito", "non accredit",
            "quando arriva", "stato pagamento", "pagamento non ricevuto",
            "payment not received", "not received", "missing payment"
        };

        public string Code => DomainCode;

        public TicketDomainScopeMatch DetectScope(TicketDomainRequest request)
        {
            string text = request.NormalizedText;
            bool topicIban =
                string.Equals(request.PrimaryTopicCode, "IBAN", StringComparison.OrdinalIgnoreCase) ||
                TicketDomainText.TopicContainsAny(request.SecondaryTopicCode, "IBAN");
            bool mentionsIban = topicIban || TicketDomainText.ContainsAny(text, IbanTokens);
            bool asksUpdate =
                TicketDomainText.ContainsAny(text, UpdateTokens);
            bool onlyProvidesIbanForPaymentComplaint =
                HasIbanInText($"{request.Subject} {request.Message}") &&
                TicketDomainText.ContainsAny(text, PaymentComplaintTokens) &&
                !asksUpdate;

            return new TicketDomainScopeMatch
            {
                DomainCode = Code,
                Signal = "AGGIORNAMENTO_IBAN",
                IsRequested = mentionsIban && asksUpdate && !onlyProvidesIbanForPaymentComplaint
            };
        }

        public TicketDomainValidationResult Validate(TicketDomainValidationContext context)
        {
            if (context.Record == null)
            {
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "IBAN: DATI OPERATIVI NON DISPONIBILI",
                    UnresolvedReason = "record operativo assente per verificare l'ultimo IBAN attivo",
                    PendingResponseCode = "GEN_01"
                };
            }

            string currentIban = NormalizeIban(context.Record.Iban);
            string ticketIban = ExtractLastIban($"{context.Request.Subject} {context.Request.Message}");
            bool currentIbanValid = !string.IsNullOrWhiteSpace(currentIban) &&
                                    IbanValidatorUtil.ValidateIban(currentIban);
            bool sameAsTicket = currentIbanValid &&
                                !string.IsNullOrWhiteSpace(ticketIban) &&
                                string.Equals(currentIban, ticketIban, StringComparison.OrdinalIgnoreCase);
            bool updatedAfterTicket =
                currentIbanValid &&
                context.Record.IbanDataValidita.HasValue &&
                context.TicketCreationDate.HasValue &&
                context.Record.IbanDataValidita.Value.Date >= context.TicketCreationDate.Value.Date;

            if (sameAsTicket || updatedAfterTicket)
            {
                string condition = sameAsTicket
                    ? "IBAN_PRESENTE_NEL_TICKET_COINCIDE"
                    : "IBAN_AGGIORNATO_DOPO_TICKET";
                string responseCode = sameAsTicket ? "IBAN_04" : "IBAN_03";
                string evidence = BuildIbanEvidence(
                    context.Record,
                    ticketIban,
                    sameAsTicket ? "IBAN corrente coincidente con quello indicato nel ticket" : "IBAN corrente validato dopo apertura ticket");

                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    IsResolved = true,
                    CheckSummary = sameAsTicket
                        ? "IBAN: RISOLTO - ultimo IBAN attivo coincidente con quello indicato nel ticket"
                        : "IBAN: RISOLTO - ultimo IBAN attivo valido con data validità successiva/allineata al ticket",
                    Evidence = evidence,
                    ResolvedCondition = condition,
                    ResolvedResponseCode = responseCode,
                    PendingResponseCode = "IBAN_05"
                };
            }

            return new TicketDomainValidationResult
            {
                DomainCode = Code,
                CheckSummary = BuildPendingSummary(context.Record, ticketIban, currentIbanValid, context.TicketCreationDate),
                UnresolvedReason = "aggiornamento IBAN non dimostrato: ultimo IBAN assente/non valido, non successivo al ticket o non coincidente con quello indicato",
                PendingResponseCode = "IBAN_05"
            };
        }

        public string BuildEvidence(TicketOfficeRecord record) =>
            BuildIbanEvidence(record, string.Empty, "ultimo IBAN attivo");

        public string BuildOperatorIndication(TicketOfficeRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Iban))
                return "IBAN assente: verificare la modalità di pagamento dichiarata.";

            var values = new List<string>
            {
                IbanValidatorUtil.ValidateIban(record.Iban)
                    ? "IBAN formalmente valido"
                    : "IBAN presente ma non valido secondo controllo formale"
            };
            if (record.IbanDataValidita.HasValue)
                values.Add($"data validità {record.IbanDataValidita.Value.ToString("dd/MM/yyyy", ItalianCulture)}");
            return string.Join("; ", values);
        }

        private static bool HasIbanInText(string value) =>
            !string.IsNullOrWhiteSpace(ExtractLastIban(value));

        private static string ExtractLastIban(string value)
        {
            MatchCollection matches = IbanCandidateRegex.Matches(value ?? string.Empty);
            if (matches.Count == 0)
                return string.Empty;

            return NormalizeIban(matches[matches.Count - 1].Value);
        }

        private static string NormalizeIban(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static string BuildIbanEvidence(
            TicketOfficeRecord record,
            string ticketIban,
            string label)
        {
            var values = new List<string> { label };
            if (!string.IsNullOrWhiteSpace(record.Iban))
                values.Add($"IBAN attivo: {record.Iban}");
            if (record.IbanDataValidita.HasValue)
                values.Add($"data validità IBAN: {record.IbanDataValidita.Value.ToString("dd/MM/yyyy", ItalianCulture)}");
            if (!string.IsNullOrWhiteSpace(ticketIban))
                values.Add($"IBAN nel ticket: {ticketIban}");
            return string.Join("; ", values);
        }

        private static string BuildPendingSummary(
            TicketOfficeRecord record,
            string ticketIban,
            bool currentIbanValid,
            DateTime? ticketCreationDate)
        {
            var values = new List<string>
            {
                currentIbanValid ? "IBAN corrente valido" : "IBAN corrente assente o non valido"
            };
            if (record.IbanDataValidita.HasValue)
                values.Add($"data validità {record.IbanDataValidita.Value.ToString("dd/MM/yyyy", ItalianCulture)}");
            if (ticketCreationDate.HasValue)
                values.Add($"ticket aperto il {ticketCreationDate.Value.ToString("dd/MM/yyyy", ItalianCulture)}");
            if (!string.IsNullOrWhiteSpace(ticketIban))
                values.Add($"IBAN nel ticket {ticketIban}");
            return "IBAN: NON RISOLTO - " + string.Join(", ", values);
        }
    }
}
