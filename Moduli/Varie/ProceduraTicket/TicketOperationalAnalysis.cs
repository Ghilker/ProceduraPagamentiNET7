using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcedureNet7
{
    internal enum TicketIntent
    {
        INFORMAZIONE,
        STATO_PRATICA,
        SOLLECITO,
        CONTESTAZIONE,
        ERRORE_TECNICO,
        INVIO_DOCUMENTO,
        AGGIORNAMENTO_DATI,
        RINUNCIA_O_RECESSO,
        PRE_DOMANDA,
        RIMOZIONE_BLOCCO,
        ALTRO_DA_VERIFICARE
    }

    internal enum ApplicationState
    {
        SCONOSCIUTO,
        INCOMPLETA_O_NON_TRASMESSA,
        TRASMESSA_O_COMPLETA
    }

    internal enum PaymentRequestKind
    {
        GENERICO,
        PRIMA_RATA,
        SALDO,
        INTEGRAZIONE
    }

    internal enum BlockCategory
    {
        DOCUMENTI_E_PERMESSI,
        REDDITI_E_ISEE,
        ISCRIZIONE_E_CARRIERA,
        PAGAMENTI_E_TASSE,
        ALLOGGIO_E_DOMICILIO,
        PORTALE_E_TECNICO,
        ALTRO
    }

    internal sealed class TicketIntentAssessment
    {
        public TicketIntent Intent { get; init; }
        public string Confidence { get; init; } = "BASSA";
        public string Signals { get; init; } = string.Empty;
    }

    internal sealed class TicketResolutionScope
    {
        public bool ScholarshipPayments { get; init; }
        public bool ResidencePermitDocuments { get; init; }
        public bool ApplicationBlocks { get; init; }
        public bool IbanUpdates { get; init; }
        public bool ApplicationWindowInformation { get; init; }
        public bool DomicileContract { get; init; }
        public string Signals { get; init; } = string.Empty;

        // Il workbook conserva il contesto e gli esiti dei moduli che hanno riconosciuto
        // un ambito operativo. In questo modo i controlli non restano nel coordinatore.
        public TicketDomainRequest Request { get; init; } = new();
        public IReadOnlyList<TicketDomainScopeMatch> DomainMatches { get; init; } =
            Array.Empty<TicketDomainScopeMatch>();

        public bool HasConditions =>
            ScholarshipPayments ||
            ResidencePermitDocuments ||
            ApplicationBlocks ||
            IbanUpdates ||
            ApplicationWindowInformation ||
            DomicileContract;

        public bool RequiresOperationalRecord =>
            ScholarshipPayments ||
            ResidencePermitDocuments ||
            ApplicationBlocks ||
            IbanUpdates ||
            DomicileContract;
    }

    internal sealed class BlockAssessment
    {
        public string RelevantBlocks { get; init; } = string.Empty;
        public string NonRelevantBlocks { get; init; } = string.Empty;
        public IReadOnlyCollection<BlockCategory> RelevantCategories { get; init; } = Array.Empty<BlockCategory>();
        public bool HasRelevantBlocks => RelevantCategories.Count > 0;
    }

    internal static class TicketOperationalAnalysis
    {
        private static readonly string[] ContestationTokens =
        {
            "contest", "ricorso", "errato", "sbagliat", "non corretto", "ingiusto", "reclamo",
            "differenza", "importo inferiore", "meno del previsto", "pagamento in eccesso",
            "trattenuta", "rimborso", "restituzione", "riaccredito", "stornato"
        };

        private static readonly string[] TechnicalTokens =
        {
            "errore", "accesso", "login", "password", "portale", "schermata", "non riesco", "bloccato il sito"
        };

        private static readonly string[] DocumentTokens =
        {
            "allego", "allegato", "caricato", "caricare", "upload", "inviato", "inviare documento", "documentazione", "documento", "documenti"
        };

        private static readonly string[] UpdateTokens =
        {
            "aggiorn", "modific", "cambiare", "cambio", "variazione", "nuovo iban"
        };

        private static readonly string[] WithdrawalTokens =
        {
            "rinunci", "rinuncia", "recesso", "reced", "check out", "check-out", "disdet", "cessaz", "restituzione chiavi"
        };

        private static readonly string[] SolicitationTokens =
        {
            "sollecito", "non ricevuto", "non accreditato", "ritardo nel pagamento",
            "ritardo dell accredito", "quando verrà pagata", "quando sara pagata", "quando sarà pagata"
        };

        // "domanda" e "pratica" da sole sono parole descrittive: non identificano una
        // richiesta di stato. La classificazione richiede quindi una formulazione esplicita.
        private static readonly string[] StatusTokens =
        {
            "stato domanda", "stato della domanda", "stato pratica", "stato della pratica",
            "esito domanda", "esito della domanda", "esito pratica", "esito della pratica",
            "graduatoria", "risultato della domanda"
        };

        private static readonly string[] PreApplicationTokens =
        {
            "requisiti", "come fare", "come si fa", "posso presentare", "posso fare domanda",
            "posso fare richiesta", "posso fare comunque richiesta", "posso richiedere",
            "desidero sapere se posso", "presentero domanda", "presentera domanda", "presentare domanda",
            "far domanda", "bando", "informazioni generali"
        };

        private static readonly string[] InformationTokens =
        {
            "informazione", "informazioni", "chiarimento", "vorrei sapere", "potrei sapere",
            "desidero sapere", "dubbio"
        };

        // I token di Pagamenti, Blocchi e Permesso di soggiorno sono posseduti
        // dai moduli di dominio dedicati. Qui restano solo regole trasversali di intento.

        public static TicketIntentAssessment AnalyzeIntent(string subject, string message, string topicCode)
        {
            string text = Normalize(subject + " " + message);
            if (string.IsNullOrWhiteSpace(text))
                return NewAssessment(TicketIntent.ALTRO_DA_VERIFICARE, "BASSA", "testo assente");

            if (ContainsAny(text, ContestationTokens))
                return NewAssessment(TicketIntent.CONTESTAZIONE, "ALTA", "contestazione/importo o esito contestato");
            if (ContainsAny(text, WithdrawalTokens))
                return NewAssessment(TicketIntent.RINUNCIA_O_RECESSO, "ALTA", "rinuncia/recesso/check-out");

            // Le richieste di ammissibilità o di presentazione futura prevalgono su parole
            // descrittive come "domanda", "borsa" o "in attesa di un riscontro".
            if (ContainsAny(text, PreApplicationTokens))
                return NewAssessment(TicketIntent.PRE_DOMANDA, "ALTA", "richiesta di ammissibilità o presentazione della domanda");

            // La richiesta esplicita di rimozione/sblocco prevale su parole come
            // "documento", "uploaded" o "portale": descrive l'azione richiesta,
            // non un semplice invio di allegati né un errore tecnico.
            if (TicketDomainModuleRegistry.Blocks.MentionsBlockRemoval(subject, message))
                return NewAssessment(TicketIntent.RIMOZIONE_BLOCCO, "ALTA", "richiesta esplicita di rimozione o sblocco del blocco amministrativo");

            // Il solo topic PORTALE_E_ACCESSO non dimostra un errore tecnico. Un ticket può
            // citare il portale mentre richiede la rimozione di un blocco amministrativo.
            if (ContainsAny(text, TechnicalTokens) && !TicketDomainModuleRegistry.Blocks.MentionsBlockRemoval(subject, message))
                return NewAssessment(TicketIntent.ERRORE_TECNICO, "ALTA", "errore tecnico/accesso al portale");
            if (ContainsAny(text, DocumentTokens))
                return NewAssessment(TicketIntent.INVIO_DOCUMENTO, "MEDIA", "invio o caricamento documenti");
            if (ContainsAny(text, UpdateTokens) ||
                string.Equals(topicCode, "IBAN", StringComparison.OrdinalIgnoreCase))
                return NewAssessment(TicketIntent.AGGIORNAMENTO_DATI, "MEDIA", "aggiornamento dati o coordinate di pagamento");
            if (ContainsAny(text, SolicitationTokens))
                return NewAssessment(TicketIntent.SOLLECITO, "MEDIA", "sollecito o mancata ricezione");
            if (ContainsAny(text, StatusTokens))
                return NewAssessment(TicketIntent.STATO_PRATICA, "MEDIA", "richiesta esplicita di stato o esito");
            if (ContainsAny(text, InformationTokens))
                return NewAssessment(TicketIntent.INFORMAZIONE, "MEDIA", "richiesta informativa");

            return NewAssessment(TicketIntent.ALTRO_DA_VERIFICARE, "BASSA", "nessun segnale di intento univoco");
        }

        public static bool TicketMentionsBlocks(string subject, string message) =>
            TicketDomainModuleRegistry.Blocks.MentionsBlockReference(subject, message);

        /// <summary>
        /// La selezione di un solo AA estratto non è sufficiente quando il testo cita più anni:
        /// in quel caso non è possibile attribuire automaticamente la condizione risolta al
        /// corretto anno senza una lettura operatore.
        /// </summary>
        public static bool HasMultipleAcademicYears(string subject, string message)
        {
            string text = Normalize(subject + " " + message);
            var years = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index <= text.Length - 9; index++)
            {
                if (!char.IsDigit(text[index]) || !char.IsDigit(text[index + 1]) ||
                    !char.IsDigit(text[index + 2]) || !char.IsDigit(text[index + 3]) ||
                    (text[index + 4] != '/' && text[index + 4] != '-') ||
                    !char.IsDigit(text[index + 5]) || !char.IsDigit(text[index + 6]) ||
                    !char.IsDigit(text[index + 7]) || !char.IsDigit(text[index + 8]))
                    continue;

                string start = text.Substring(index, 4);
                string end = text.Substring(index + 5, 4);
                if (int.TryParse(start, out int startYear) &&
                    int.TryParse(end, out int endYear) &&
                    endYear == startYear + 1)
                    years.Add($"{startYear:D4}/{endYear:D4}");
            }
            return years.Count > 1;
        }

        public static bool TicketRequestsBlockRemoval(string subject, string message)
        {
            return AnalyzeResolutionScope(subject, message).ApplicationBlocks;
        }

        public static TicketResolutionScope AnalyzeResolutionScope(
            string subject,
            string message,
            string primaryTopicCode = "",
            string secondaryTopicCode = "",
            bool referredToBlocks = false,
            TicketIntent? recognisedIntent = null)
        {
            TicketDomainRequest request = TicketDomainModuleRegistry.CreateRequest(
                subject,
                message,
                primaryTopicCode,
                secondaryTopicCode,
                referredToBlocks,
                recognisedIntent);
            return TicketDomainModuleRegistry.AnalyzeResolutionScope(request);
        }

        public static PaymentRequestKind ClassifyPaymentRequest(
            string subject,
            string message,
            string secondaryTopicCode = "") =>
            TicketDomainModuleRegistry.Payments.ClassifyPaymentRequest(
                subject,
                message,
                secondaryTopicCode);

        public static string FormatPaymentRequestKind(PaymentRequestKind kind) => kind switch
        {
            PaymentRequestKind.PRIMA_RATA => "PRIMA_RATA",
            PaymentRequestKind.SALDO => "SALDO",
            PaymentRequestKind.INTEGRAZIONE => "INTEGRAZIONE",
            _ => "PAGAMENTO_GENERICO"
        };

        public static bool AreResidencePermitDocumentsWorked(string rawDocuments) =>
            TicketDomainModuleRegistry.ResidencePermit.AreRequiredDocumentsWorked(rawDocuments);

        public static ApplicationState ClassifyApplicationState(int status)
        {
            if (status <= 0)
                return ApplicationState.SCONOSCIUTO;
            if (status < 90)
                return ApplicationState.INCOMPLETA_O_NON_TRASMESSA;
            return ApplicationState.TRASMESSA_O_COMPLETA;
        }

        public static string GetApplicationStateLabel(ApplicationState state) => state switch
        {
            ApplicationState.SCONOSCIUTO => "SCONOSCIUTO",
            ApplicationState.INCOMPLETA_O_NON_TRASMESSA => "INCOMPLETA_O_NON_TRASMESSA",
            _ => "TRASMESSA_O_COMPLETA"
        };

        public static BlockAssessment AnalyzeBlocks(string rawBlocks, string topicCode) =>
            TicketDomainModuleRegistry.Blocks.AnalyzeBlocks(rawBlocks, topicCode);

        public static IReadOnlyList<string> FindContradictions(
            TicketIntentAssessment intent,
            TicketOfficeRecord? record,
            string topicCode)
        {
            var findings = new List<string>();
            if (record == null)
                return findings;

            if (intent.Intent == TicketIntent.INVIO_DOCUMENTO &&
                string.Equals(topicCode, "DOCUMENTI_E_PERMESSI", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(record.ResidencePermitDocuments))
            {
                findings.Add("Il ticket dichiara l'invio di documenti ma non risultano documenti del permesso nei dati estratti.");
            }

            if (intent.Intent == TicketIntent.AGGIORNAMENTO_DATI &&
                string.Equals(topicCode, "IBAN", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(record.Iban))
            {
                findings.Add("L'IBAN è presente: la richiesta può riguardare una variazione, non l'assenza del dato.");
            }

            if (intent.Intent == TicketIntent.CONTESTAZIONE)
                findings.Add("Il ticket contiene una contestazione: l'esito operativo non è sufficiente per una chiusura automatica.");

            return findings;
        }

        public static bool IsClosureCompatible(TicketIntent intent) =>
            intent == TicketIntent.SOLLECITO ||
            intent == TicketIntent.STATO_PRATICA ||
            intent == TicketIntent.INVIO_DOCUMENTO ||
            intent == TicketIntent.RIMOZIONE_BLOCCO;

        public static string FormatIntent(TicketIntent intent) => intent switch
        {
            TicketIntent.INFORMAZIONE => "INFORMAZIONE",
            TicketIntent.STATO_PRATICA => "STATO_PRATICA",
            TicketIntent.SOLLECITO => "SOLLECITO",
            TicketIntent.CONTESTAZIONE => "CONTESTAZIONE",
            TicketIntent.ERRORE_TECNICO => "ERRORE_TECNICO",
            TicketIntent.INVIO_DOCUMENTO => "INVIO_DOCUMENTO",
            TicketIntent.AGGIORNAMENTO_DATI => "AGGIORNAMENTO_DATI",
            TicketIntent.RINUNCIA_O_RECESSO => "RINUNCIA_O_RECESSO",
            TicketIntent.PRE_DOMANDA => "PRE_DOMANDA",
            TicketIntent.RIMOZIONE_BLOCCO => "RIMOZIONE_BLOCCO",
            _ => "ALTRO_DA_VERIFICARE"
        };

        private static TicketIntentAssessment NewAssessment(TicketIntent intent, string confidence, string signals) => new()
        {
            Intent = intent,
            Confidence = confidence,
            Signals = signals
        };

        // I criteri di matching lessicale sono condivisi con i moduli di dominio.
        private static bool ContainsAny(string text, params string[] tokens) =>
            TicketDomainText.ContainsAny(text, tokens);

        private static string Normalize(string value) =>
            TicketDomainText.Normalize(value);
    }
}
