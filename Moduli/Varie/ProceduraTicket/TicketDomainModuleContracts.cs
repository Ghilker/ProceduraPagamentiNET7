using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ProcedureNet7
{
    /// <summary>
    /// Contesto comune passato ai moduli di dominio. Il modulo non legge direttamente
    /// DataRow o controlli UI: riceve solo i dati necessari alla propria valutazione.
    /// </summary>
    internal sealed class TicketDomainRequest
    {
        public string Subject { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string PrimaryTopicCode { get; init; } = string.Empty;
        public string SecondaryTopicCode { get; init; } = string.Empty;
        public bool ReferredToBlocks { get; init; }
        public TicketIntent? RecognisedIntent { get; init; }

        public string NormalizedText => TicketDomainText.Normalize($"{Subject} {Message}");
    }

    /// <summary>
    /// Esito del riconoscimento di un ambito operativo nel testo del ticket.
    /// </summary>
    internal sealed class TicketDomainScopeMatch
    {
        public string DomainCode { get; init; } = string.Empty;
        public string Signal { get; init; } = string.Empty;
        public bool IsRequested { get; init; }
    }

    /// <summary>
    /// Contesto per estrazioni e validazioni del singolo dominio.
    /// </summary>
    internal sealed class TicketDomainValidationContext
    {
        public TicketDomainRequest Request { get; init; } = new();
        public TicketOfficeRecord? Record { get; init; }
        public PaymentRequestKind PaymentRequest { get; init; }
        public DateTime? TicketCreationDate { get; init; }
    }

    /// <summary>
    /// Risultato della validazione di una condizione richiesta dal ticket.
    /// I codici sono posseduti dal modulo per evitare logiche di dominio nel workbook.
    /// </summary>
    internal sealed class TicketDomainValidationResult
    {
        public string DomainCode { get; init; } = string.Empty;
        public bool IsResolved { get; init; }
        public string CheckSummary { get; init; } = string.Empty;
        public string Evidence { get; init; } = string.Empty;
        public string UnresolvedReason { get; init; } = string.Empty;
        public string ResolvedCondition { get; init; } = string.Empty;
        public string ResolvedResponseCode { get; init; } = string.Empty;
        public string PendingResponseCode { get; init; } = string.Empty;
    }

    /// <summary>
    /// Contratto applicato ai moduli di argomento. Ogni modulo possiede riconoscimento,
    /// estrazione, validazione ed evidenze del proprio dominio. L'instradamento dei fogli
    /// è gestito centralmente dal solo argomento primario.
    /// </summary>
    internal interface ITicketDomainModule
    {
        string Code { get; }
        TicketDomainScopeMatch DetectScope(TicketDomainRequest request);
        TicketDomainValidationResult Validate(TicketDomainValidationContext context);
        string BuildEvidence(TicketOfficeRecord record);
    }

    internal static class TicketDomainText
    {
        // Solo gli elementi elencati qui sotto possono trovare una radice lessicale.
        // Tutti gli altri token richiedono confini di parola: "rata" non riconosce "durata".
        private static readonly HashSet<string> PrefixMatchTokens = new(StringComparer.Ordinal)
        {
            "contest", "sbagliat", "aggiorn", "reced", "disdet", "cessaz",
            "accredit", "erog", "liquid", "pagament", "non ricevut", "non accredit",
            "sospes", "sospension", "blocc", "sblocc", "document", "allegat",
            "iscrizion", "credit", "immatricol", "detraz", "contabil", "alloggi", "tecnic"
        };

        public static bool ContainsAny(string text, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(text) || tokens == null || tokens.Length == 0)
                return false;

            return tokens.Any(token => ContainsTokenOrPhrase(text, token));
        }

        public static bool ContainsTokenOrPhrase(string text, string token)
        {
            string haystack = Normalize(text);
            string needle = Normalize(token).Trim();
            if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
                return false;

            bool allowSuffix = PrefixMatchTokens.Contains(needle);
            int position = 0;
            while (position < haystack.Length)
            {
                int index = haystack.IndexOf(needle, position, StringComparison.Ordinal);
                if (index < 0)
                    return false;

                int end = index + needle.Length;
                bool hasLeftBoundary = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
                bool hasRightBoundary = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);
                if (hasLeftBoundary && (allowSuffix || hasRightBoundary))
                    return true;

                position = index + 1;
            }

            return false;
        }

        public static bool TopicContainsAny(string topicText, params string[] topics)
        {
            if (string.IsNullOrWhiteSpace(topicText) || topics == null || topics.Length == 0)
                return false;

            return topicText
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(value => topics.Any(topic =>
                    string.Equals(value, topic, StringComparison.OrdinalIgnoreCase)));
        }

        public static string Normalize(string value)
        {
            var builder = new StringBuilder();
            foreach (char character in (value ?? string.Empty).Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                    builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }
    }
}
