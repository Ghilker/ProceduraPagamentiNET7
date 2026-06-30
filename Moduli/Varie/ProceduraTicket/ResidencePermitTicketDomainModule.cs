using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class ResidencePermitOperationalData
    {
        public string RawDocuments { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string> Statuses { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool AreRequiredDocumentsWorked { get; init; }
        public string StatusSummary { get; init; } = string.Empty;
    }

    /// <summary>
    /// Dominio Permesso di soggiorno: riconoscimento della richiesta, estrazione degli
    /// stati 01/02/03, validazione status 05 e destinazione del ticket.
    /// </summary>
    internal sealed class ResidencePermitTicketDomainModule : ITicketDomainModule
    {
        public const string DomainCode = "PERMESSO_SOGGIORNO";

        private static readonly string[] ResidencePermitTokens =
        {
            "permesso di soggiorno", "permesso soggiorno", "permesso", "documenti del permesso",
            "documenti permesso", "allegati permesso", "ricevuta della questura",
            "ricevuta questura", "kit postale", "questura", "titolo di soggiorno", "residence permit"
        };

        private static readonly string[] StateRequestTokens =
        {
            "ho caricato", "ho inserito", "ho inviato", "ho allegato", "gia caricato",
            "gia inserito", "risulta gia caricato", "risulta gia inserito", "documento caricato",
            "documenti caricati", "documenti inseriti", "documenti inviati", "caricando",
            "uploaded", "already uploaded", "submitted", "already submitted", "stato 05",
            "verificare il documento", "titolo di soggiorno caricato", "rimozione del blocco",
            "rimuovere il blocco", "rimuovere blocco", "togliere il blocco", "sblocco permesso",
            "remove the block", "remove the bloc", "remove block", "please remove the block",
            "please remove the bloc", "residence permit has been verified", "permit has been verified",
            "residence permit verified", "permesso verificato", "permesso gia verificato",
            "permesso già verificato", "blocco sul pagamento"
        };

        private static readonly string[] DocumentTokens =
        {
            "allego", "allegato", "caricato", "caricare", "upload", "inviato", "inviare documento",
            "documentazione", "documento", "documenti"
        };

        private static readonly string[] CertificateOrInformationTokens =
        {
            "attestazione", "certificato", "dichiarazione", "documento ufficiale",
            "prova dei mezzi", "disponibilita economica", "disponibilità economica",
            "documento in formato pdf", "rilasciare un documento", "rilascio documento",
            "puo essere accettato", "può essere accettato", "questura come documentazione",
            "certify", "certificate", "official document", "proof of funds",
            "economic means"
        };

        public string Code => DomainCode;

        public TicketDomainScopeMatch DetectScope(TicketDomainRequest request)
        {
            string text = request.NormalizedText;
            bool secondaryPermit = TicketDomainText.TopicContainsAny(request.SecondaryTopicCode, "PERMESSO");
            bool informationalOnly = request.RecognisedIntent == TicketIntent.PRE_DOMANDA ||
                                     request.RecognisedIntent == TicketIntent.INFORMAZIONE;
            bool mentioned = TicketDomainText.ContainsAny(text, ResidencePermitTokens) || secondaryPermit;
            bool certificateOrInformationRequest =
                TicketDomainText.ContainsAny(text, CertificateOrInformationTokens) &&
                !TicketDomainText.ContainsAny(text, "ho caricato", "ho inserito", "ho allegato", "rimuovere il blocco", "sblocco", "remove the block");
            bool requested = mentioned &&
                             (TicketDomainText.ContainsAny(text, StateRequestTokens) ||
                              (secondaryPermit && TicketDomainText.ContainsAny(text, DocumentTokens))) &&
                             !informationalOnly &&
                             !certificateOrInformationRequest;

            // Un primario Documenti e permessi senza un atto sul permesso resta nel flusso storico.
            return new TicketDomainScopeMatch
            {
                DomainCode = Code,
                Signal = "PERMESSO_SOGGIORNO",
                IsRequested = requested
            };
        }

        public ResidencePermitOperationalData Extract(TicketOfficeRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in record.ResidencePermitDocuments.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] values = part.Trim().Split(':', 2, StringSplitOptions.TrimEntries);
                if (values.Length == 2 && !string.IsNullOrWhiteSpace(values[0]))
                    statuses[values[0]] = values[1];
            }

            bool worked = statuses.TryGetValue("01", out string? document01) && document01 == "05" &&
                          statuses.TryGetValue("02", out string? document02) && document02 == "05" &&
                          statuses.TryGetValue("03", out string? document03) && document03 == "05";

            string summary = statuses.Count == 0
                ? "documenti non rilevati"
                : string.Join(" | ", statuses
                    .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => $"{entry.Key}:{entry.Value}"));

            return new ResidencePermitOperationalData
            {
                RawDocuments = record.ResidencePermitDocuments,
                Statuses = statuses,
                AreRequiredDocumentsWorked = worked,
                StatusSummary = summary
            };
        }

        public bool AreRequiredDocumentsWorked(string rawDocuments)
        {
            return Extract(new TicketOfficeRecord { ResidencePermitDocuments = rawDocuments ?? string.Empty })
                .AreRequiredDocumentsWorked;
        }

        public TicketDomainValidationResult Validate(TicketDomainValidationContext context)
        {
            if (context.Record == null)
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "PERMESSO: DATI OPERATIVI NON DISPONIBILI",
                    UnresolvedReason = "record operativo assente",
                    PendingResponseCode = "GEN_01"
                };

            ResidencePermitOperationalData data = Extract(context.Record);
            string text = context.Request.NormalizedText;
            bool mentionsPermitBlockOrPayment =
                TicketDomainText.ContainsAny(text, "blocco", "sblocco", "pagamento", "saldo", "tranche", "block", "unblock", "payment") &&
                TicketDomainText.ContainsAny(text, "permesso", "titolo di soggiorno", "residence permit");
            bool hasPermitBlock = TicketDomainModuleRegistry.Blocks.HasResidencePermitBlock(context.Record.Blocks);

            if (data.AreRequiredDocumentsWorked && mentionsPermitBlockOrPayment && hasPermitBlock)
            {
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "PERMESSO: NON RISOLTO - documenti status 05 ma blocco BPP/permesso ancora presente",
                    UnresolvedReason = "documenti del permesso lavorati ma blocco BPP/permesso di soggiorno mancante ancora presente",
                    PendingResponseCode = "DOC_BLO_02"
                };
            }

            return new TicketDomainValidationResult
            {
                DomainCode = Code,
                IsResolved = data.AreRequiredDocumentsWorked,
                CheckSummary = data.AreRequiredDocumentsWorked
                    ? mentionsPermitBlockOrPayment
                        ? "PERMESSO: RISOLTO - documenti 01/02/03 in status 05 e nessun blocco BPP/permesso presente"
                        : "PERMESSO: RISOLTO - documenti 01/02/03 in status 05"
                    : $"PERMESSO: NON RISOLTO - {data.StatusSummary}",
                Evidence = data.AreRequiredDocumentsWorked
                    ? mentionsPermitBlockOrPayment
                        ? $"Documenti permesso in status 05: {data.RawDocuments}; nessun blocco BPP/permesso presente"
                        : $"Documenti permesso in status 05: {data.RawDocuments}"
                    : string.Empty,
                UnresolvedReason = "documenti del permesso non tutti lavorati in status 05",
                ResolvedCondition = mentionsPermitBlockOrPayment
                    ? "PERMESSO_LAVORATO_E_BLOCCO_PERMESSO_ASSENTE_STESSO_AA"
                    : "DOCUMENTI_PERMESSO_LAVORATI_STATUS_05_STESSO_AA",
                ResolvedResponseCode = mentionsPermitBlockOrPayment ? "DOC_BLO_01" : "DOC_04",
                PendingResponseCode = "DOC_05"
            };
        }


        public string BuildEvidence(TicketOfficeRecord record)
        {
            ResidencePermitOperationalData data = Extract(record);
            return data.AreRequiredDocumentsWorked
                ? $"documenti permesso 01/02/03 lavorati: {data.StatusSummary}"
                : $"documenti permesso: {data.StatusSummary}";
        }

        public string BuildOperatorIndication(TicketOfficeRecord record)
        {
            ResidencePermitOperationalData data = Extract(record);
            if (string.IsNullOrWhiteSpace(data.RawDocuments))
                return "Documenti del permesso non trovati: verificare allegati e richiedere eventuale integrazione.";
            if (!data.AreRequiredDocumentsWorked)
                return "Documenti del permesso presenti ma non tutti lavorati in status 05: verificare 01, 02 e 03.";
            return "Documenti del permesso lavorati in status 05: confrontare il ticket con gli allegati acquisiti.";
        }
    }
}
