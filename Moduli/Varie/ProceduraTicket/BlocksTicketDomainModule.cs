using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class BlockOperationalData
    {
        public string RawBlocks { get; init; } = string.Empty;
        public BlockAssessment Assessment { get; init; } = new();
        public bool HasAnyBlocks => !string.IsNullOrWhiteSpace(RawBlocks);
    }

    /// <summary>
    /// Dominio Blocchi: riconosce solo blocchi amministrativi della domanda,
    /// separa blocchi rilevanti/non pertinenti e valida l'effettiva rimozione.
    /// </summary>
    internal sealed class BlocksTicketDomainModule : ITicketDomainModule
    {
        public const string DomainCode = "BLOCCHI";

        private static readonly string[] BlockReferenceTokens =
        {
            "blocco", "blocchi", "blocc", "sblocc", "bloc", "rimozione blocco", "rimuovere il blocco",
            "sospes", "sospension", "fermo pratica", "pratica ferma",
            "block", "blocked", "unblock"
        };

        private static readonly string[] BlockRemovalTokens =
        {
            "rimozione blocco", "rimuovere il blocco", "rimuovere blocco", "togliere il blocco",
            "togliere blocco", "sblocco", "sbloccare", "domanda bloccata", "blocco domanda",
            "rimozione del blocco", "blocco del pagamento", "pagamento bloccato", "pagamento sospeso",
            "risolvere questo problema", "come devo procedere per risolvere",
            "remove the block", "remove the bloc", "remove block", "please remove the block",
            "please remove the bloc", "block has not", "remaining block", "blocked application"
        };

        private static readonly string[] AdministrativeBlockRemovalTokens =
        {
            "rimozione blocco", "rimuovere il blocco", "rimuovere blocco", "togliere il blocco",
            "togliere blocco", "domanda bloccata", "blocco domanda", "sblocco domanda",
            "sbloccare domanda", "sblocco pratica", "sbloccare pratica", "blocchi pratica",
            "rimozione del blocco", "blocco del pagamento", "pagamento bloccato", "pagamento sospeso",
            "remove the block", "remove the bloc", "remove block", "please remove the block",
            "please remove the bloc", "blocked application", "application block"
        };

        private static readonly string[] ApplicationBlockContextTokens =
        {
            "domanda", "pratica", "documentazione definitiva", "titolo di soggiorno",
            "permesso", "documenti", "application", "documents", "residence permit"
        };

        private static readonly string[] TechnicalBlockTokens =
        {
            "errore", "error", "accesso", "login", "password", "schermata", "sistema",
            "piattaforma", "portale", "caricamento infinito", "bug", "non riesco"
        };

        private static readonly string[] FinancialInstrumentBlockTokens =
        {
            "carta bloccata", "conto bloccato", "banca ha bloccato", "iban bloccato",
            "pagamento bloccato dalla banca", "sito bloccato", "portale bloccato",
            "bank blocked", "card blocked", "account blocked", "website blocked"
        };

        private static readonly string[] ReopenOrModifyApplicationTokens =
        {
            "riaprire", "riapertura", "sbloccare nuovamente", "sblocco e trasmesso nuovamente",
            "integrare i dati", "integrazione dati", "integrazione relativa", "modificare la domanda",
            "modifica della domanda", "pulsante per modificare", "procedura corretta",
            "come procedere", "posso comunque richiedere", "premio di laurea",
            "non si trasmette la domanda", "riaprire/sbloccare", "reopen", "edit application",
            "modify application", "se dovessi sbloccare", "avanzare richiesta", "avanzare la richiesta",
            "richiesta monetizzazione", "monetizzazione"
        };

        private static readonly string[] ConcreteBlockNameTokens =
        {
            "mancato pagamento tassa regionale", "verifica iscrizione",
            "documentazione definitiva permesso", "titolo di soggiorno",
            "matricole specialistica", "borsa gia percepita", "borsa già percepita",
            "isee", "dsu"
        };

        public string Code => DomainCode;

        public TicketDomainScopeMatch DetectScope(TicketDomainRequest request)
        {
            string text = request.NormalizedText;
            bool informationalOnly = request.RecognisedIntent == TicketIntent.PRE_DOMANDA ||
                                     request.RecognisedIntent == TicketIntent.INFORMAZIONE;
            bool technicalOrFinancialBlock =
                TicketDomainText.ContainsAny(text, FinancialInstrumentBlockTokens) ||
                TicketDomainText.ContainsAny(text, TechnicalBlockTokens);
            bool asksReopenOrModificationInsteadOfRemoval =
                TicketDomainText.ContainsAny(text, ReopenOrModifyApplicationTokens) &&
                !TicketDomainText.ContainsAny(text, ConcreteBlockNameTokens);
            bool secondaryPermit = TicketDomainText.TopicContainsAny(request.SecondaryTopicCode, "PERMESSO");
            bool topicSuggestsApplicationBlock =
                request.ReferredToBlocks ||
                TicketDomainText.TopicContainsAny(request.SecondaryTopicCode, "BLOCCHI") ||
                secondaryPermit ||
                string.Equals(request.PrimaryTopicCode, "DOCUMENTI_E_PERMESSI", StringComparison.OrdinalIgnoreCase);

            bool requested =
                !informationalOnly &&
                !technicalOrFinancialBlock &&
                !asksReopenOrModificationInsteadOfRemoval &&
                (
                    (TicketDomainText.ContainsAny(text, AdministrativeBlockRemovalTokens) &&
                     (TicketDomainText.ContainsAny(text, ApplicationBlockContextTokens) || topicSuggestsApplicationBlock)) ||
                    (topicSuggestsApplicationBlock &&
                     TicketDomainText.ContainsAny(text, BlockRemovalTokens) &&
                     TicketDomainText.ContainsAny(text, BlockReferenceTokens))
                );

            return new TicketDomainScopeMatch
            {
                DomainCode = Code,
                Signal = "BLOCCHI_PRATICA",
                IsRequested = requested
            };
        }

        public bool MentionsBlockReference(string subject, string message) =>
            TicketDomainText.ContainsAny(
                TicketDomainText.Normalize($"{subject} {message}"),
                BlockReferenceTokens);

        public bool MentionsBlockRemoval(string subject, string message) =>
            TicketDomainText.ContainsAny(
                TicketDomainText.Normalize($"{subject} {message}"),
                BlockRemovalTokens);

        public BlockOperationalData Extract(TicketOfficeRecord record, string topicCode)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            return new BlockOperationalData
            {
                RawBlocks = record.Blocks,
                Assessment = AnalyzeBlocks(record.Blocks, topicCode)
            };
        }

        public BlockAssessment AnalyzeBlocks(string rawBlocks, string topicCode)
        {
            if (string.IsNullOrWhiteSpace(rawBlocks))
                return new BlockAssessment();

            var relevant = new List<string>();
            var notRelevant = new List<string>();
            var relevantCategories = new HashSet<BlockCategory>();

            foreach (string block in SplitBlocks(rawBlocks))
            {
                BlockCategory category = ClassifyBlock(block);
                if (IsRelevant(category, topicCode))
                {
                    relevant.Add(block);
                    relevantCategories.Add(category);
                }
                else
                {
                    notRelevant.Add(block);
                }
            }

            return new BlockAssessment
            {
                RelevantBlocks = string.Join(" | ", relevant),
                NonRelevantBlocks = string.Join(" | ", notRelevant),
                RelevantCategories = relevantCategories.ToArray()
            };
        }

        public TicketDomainValidationResult Validate(TicketDomainValidationContext context)
        {
            if (context.Record == null)
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "BLOCCHI: DATI OPERATIVI NON DISPONIBILI",
                    UnresolvedReason = "record operativo assente",
                    PendingResponseCode = "GEN_01"
                };

            string text = context.Request.NormalizedText;
            bool permitBlockRequest =
                TicketDomainText.ContainsAny(text, "permesso", "titolo di soggiorno", "residence permit") &&
                TicketDomainText.ContainsAny(text, "blocco", "sblocco", "pagamento", "saldo", "tranche", "block", "unblock", "payment");
            bool removed = permitBlockRequest
                ? !HasResidencePermitBlock(context.Record.Blocks)
                : string.IsNullOrWhiteSpace(context.Record.Blocks);
            return new TicketDomainValidationResult
            {
                DomainCode = Code,
                IsResolved = removed,
                CheckSummary = removed
                    ? permitBlockRequest
                        ? "BLOCCHI: RISOLTO - nessun blocco BPP/permesso presente"
                        : "BLOCCHI: RISOLTO - nessun blocco amministrativo presente"
                    : permitBlockRequest
                        ? $"BLOCCHI: NON RISOLTO - blocco permesso/BPP presente: {context.Record.Blocks}"
                        : $"BLOCCHI: NON RISOLTO - {context.Record.Blocks}",
                Evidence = removed
                    ? permitBlockRequest
                        ? "Nessun blocco BPP o permesso di soggiorno mancante presente sulla domanda dello stesso anno"
                        : "Nessun blocco amministrativo presente sulla domanda dello stesso anno"
                    : string.Empty,
                UnresolvedReason = permitBlockRequest
                    ? "il blocco BPP/permesso di soggiorno mancante risulta ancora presente"
                    : "uno o più blocchi amministrativi risultano ancora presenti",
                ResolvedCondition = permitBlockRequest
                    ? "BLOCCO_PERMESSO_NON_PIU_PRESENTE_STESSO_AA"
                    : "BLOCCO_AMMINISTRATIVO_NON_PIU_PRESENTE_STESSO_AA",
                ResolvedResponseCode = "BLO_02",
                PendingResponseCode = "BLO_01"
            };
        }


        public string BuildEvidence(TicketOfficeRecord record)
        {
            return string.IsNullOrWhiteSpace(record.Blocks)
                ? "nessun blocco amministrativo presente"
                : $"blocchi presenti: {record.Blocks}";
        }

        public bool HasResidencePermitBlock(string rawBlocks)
        {
            if (string.IsNullOrWhiteSpace(rawBlocks))
                return false;

            foreach (string block in SplitBlocks(rawBlocks))
            {
                string normalized = TicketDomainText.Normalize(block);
                if (TicketDomainText.ContainsAny(
                        normalized,
                        "bpp",
                        "permesso di soggiorno mancante",
                        "documentazione definitiva permesso di soggiorno mancante",
                        "titolo di soggiorno mancante"))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasForeignIncomeBlock(string rawBlocks)
        {
            if (string.IsNullOrWhiteSpace(rawBlocks))
                return false;

            foreach (string block in SplitBlocks(rawBlocks))
            {
                string normalized = TicketDomainText.Normalize(block);
                if (TicketDomainText.ContainsAny(
                        normalized,
                        "bdr",
                        "documentazione definitiva redditi esteri mancante",
                        "redditi esteri mancante",
                        "redditi esteri",
                        "isee parificato"))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> SplitBlocks(string rawBlocks)
        {
            return rawBlocks
                .Split(new[] { '|', '/', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value));
        }

        private static BlockCategory ClassifyBlock(string block)
        {
            string normalized = TicketDomainText.Normalize(block);
            if (TicketDomainText.ContainsAny(normalized, "permesso", "document", "allegat"))
                return BlockCategory.DOCUMENTI_E_PERMESSI;
            if (TicketDomainText.ContainsAny(normalized, "isee", "dsu", "redditi", "caf", "ispe"))
                return BlockCategory.REDDITI_E_ISEE;
            if (TicketDomainText.ContainsAny(normalized, "iscrizion", "carriera", "merito", "credit", "immatricol"))
                return BlockCategory.ISCRIZIONE_E_CARRIERA;
            if (TicketDomainText.ContainsAny(normalized, "pagament", "mandato", "iban", "tassa", "detraz", "reversal", "contabil"))
                return BlockCategory.PAGAMENTI_E_TASSE;
            if (TicketDomainText.ContainsAny(normalized, "alloggi", "domicilio", "contratto", "residenza"))
                return BlockCategory.ALLOGGIO_E_DOMICILIO;
            if (TicketDomainText.ContainsAny(normalized, "portale", "accesso", "tecnic", "login"))
                return BlockCategory.PORTALE_E_TECNICO;
            return BlockCategory.ALTRO;
        }

        private static bool IsRelevant(BlockCategory category, string topicCode)
        {
            string topic = topicCode?.Trim().ToUpperInvariant() ?? string.Empty;
            return topic switch
            {
                "PAGAMENTI_E_TASSE" or "BENEFICI_E_IMPORTI" or "IBAN" =>
                    category == BlockCategory.PAGAMENTI_E_TASSE,
                "DOCUMENTI_E_PERMESSI" =>
                    category == BlockCategory.DOCUMENTI_E_PERMESSI || category == BlockCategory.REDDITI_E_ISEE,
                "ISCRIZIONE_E_CARRIERA" =>
                    category == BlockCategory.ISCRIZIONE_E_CARRIERA,
                "ALLOGGIO" =>
                    category == BlockCategory.ALLOGGIO_E_DOMICILIO,
                "PORTALE_E_ACCESSO" =>
                    category == BlockCategory.PORTALE_E_TECNICO,
                "MOBILITA" =>
                    category == BlockCategory.DOCUMENTI_E_PERMESSI || category == BlockCategory.ISCRIZIONE_E_CARRIERA,
                "GRADUATORIE" =>
                    category == BlockCategory.DOCUMENTI_E_PERMESSI || category == BlockCategory.REDDITI_E_ISEE,
                _ => false
            };
        }
    }
}
