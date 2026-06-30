using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    // ──────────────────────────────────────────────────────────────────────────
    // Enums
    // ──────────────────────────────────────────────────────────────────────────
    public enum Topic
    {
        // Iscrizione/Carriera
        ISCRIZIONE, CARRIERA, CREDITI, TIROCINIO, PASSAGGIO_TRASF, DOV_CIMEA,

        // Benefici/Importi
        IMPORTI, RINUNCIA_REVOCA, SALDO, SALDO_IMPORTO_ERRATO,

        // Alloggio
        ALLOGGIO, CONTRATTO,

        // Documenti/Permessi
        PERMESSO, ISEE_REDDITI, PEC_EMAIL, INDIPENDENTE,

        // Identità/IBAN
        IBAN, CODICE_FISCALE,

        // Pagamenti/Tasse
        PAGAMENTI, TASSE, RIMBORSO_TASSA, DEBITORIA,

        // Portale/Accesso
        PORTALE,

        // Graduatorie / Premi
        GRADUATORIA,
        PREMIO_LAUREA,

        // Mobilità
        MOBILITA_ERASMUS,

        // Servizio Mensa
        MENSA,

        // Solo terziario
        BLOCCHI,

        CAF
    }

    public enum PrimaryTopic
    {
        PAGAMENTI_E_TASSE,
        ISCRIZIONE_E_CARRIERA,
        BENEFICI_E_IMPORTI,
        ALLOGGIO,
        MENSA,
        DOCUMENTI_E_PERMESSI,
        IBAN,
        GRADUATORIE,
        MOBILITA,
        PORTALE_E_ACCESSO,
        ALTRO
    }

    public enum Lang { UNKNOWN, IT, EN, MIXED }

    // ──────────────────────────────────────────────────────────────────────────
    // Result DTO
    // ──────────────────────────────────────────────────────────────────────────
    public sealed class ExtractionV6
    {
        // Raw per-topic scores
        public Dictionary<Topic, int> Counts { get; } = new();

        // Output strings
        public string TopicPrimary { get; set; } = "";
        public string TopicSecondary { get; set; } = "";
        public string TopicTertiary { get; set; } = "";

        // Diagnostics / confidence
        public int TotalScore { get; set; }
        public int PrimaryScore { get; set; }
        public double PrimaryConfidence { get; set; }      // PrimaryScore / TotalScore
        public int SecondaryScore { get; set; }
        public double SecondaryConfidence { get; set; }
        public int MarginTop1Top2 { get; set; }            // bestTopicScore - secondBestTopicScore
        public bool IsLowConfidence { get; set; }
        public bool IsGenericInfoRequest { get; set; }
        public bool SecondaryCutoffApplied { get; set; }
        public int ConfidenceScore { get; set; }            // 0-100, per uso operativo nel workbook
        public int PlausibleCategoryCount { get; set; }
        public bool PrimaryHasSpecificEvidence { get; set; }
        public string VerificationReason { get; set; } = "";

        // Optional quick explain
        public string MatchedPrimaryKeywords { get; set; } = "";  // top matched tokens for primary category
        public string MatchedTop1Keywords { get; set; } = "";     // top matched tokens for first selected topic

        // Language / text
        public Lang DetectedLanguage { get; set; } = Lang.UNKNOWN;
        public string OriginalText { get; set; } = "";
        public string NormalizedText { get; set; } = "";

        // Flags: blocks
        public bool MentionsBlocks { get; set; }
        public bool MentionsOwnPosition { get; set; }
        public bool BlocksOnOwnPosition => MentionsBlocks && MentionsOwnPosition;

        // Academic year
        public string AcademicYearRaw { get; set; } = "";
        public int? AcademicYearStart { get; set; }
        public int? AcademicYearEnd { get; set; }
        public double AcademicYearConfidence { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Engine (fast)
    // ──────────────────────────────────────────────────────────────────────────
    public static partial class KeywordEngineV6
    {
        // Weights and thresholds
        // Le frasi e gli indicatori specifici pesano più delle parole generiche.
        // La precedenza non altera il punteggio: viene usata solo a parità di punteggio.
        private const int PHRASE_WEIGHT = 6;
        private const int SINGLE_SPECIFIC_WEIGHT = 3;
        private const int SINGLE_GENERIC_WEIGHT = 1;
        private const int WORD_WEIGHT = 1;
        private const int BOOST_STRONG = 6;
        private const int BOOST_MED = 3;

        private const int MIN_PRIMARY_SCORE = 4;
        private const int MIN_SUB_SCORE = 1;
        private const int MIN_SECONDARY_ABS = 3;
        private const double SECONDARY_REL_TO_PRIMARY = 0.55;
        private const double PLAUSIBLE_CATEGORY_REL_TO_PRIMARY = 0.65;

        private const int PENALTY_ANTI = 3;
        private const int BOOST_REQUIRE = 2;

        private static readonly RegexOptions RXOPT =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly PrimaryTopic[] CAT_PRECEDENCE =
        {
            PrimaryTopic.PAGAMENTI_E_TASSE,
            PrimaryTopic.ISCRIZIONE_E_CARRIERA,
            PrimaryTopic.BENEFICI_E_IMPORTI,
            PrimaryTopic.ALLOGGIO,
            PrimaryTopic.MENSA,
            PrimaryTopic.DOCUMENTI_E_PERMESSI,
            PrimaryTopic.IBAN,
            PrimaryTopic.GRADUATORIE,
            PrimaryTopic.MOBILITA,
            PrimaryTopic.PORTALE_E_ACCESSO,
            PrimaryTopic.ALTRO
        };

        private static int CatPrecedenceRank(PrimaryTopic pt)
        {
            for (int i = 0; i < CAT_PRECEDENCE.Length; i++)
                if (CAT_PRECEDENCE[i] == pt) return i;
            return int.MaxValue;
        }

        private static readonly Topic[] TOPIC_PRECEDENCE =
        {
            Topic.CAF,
            Topic.PAGAMENTI, Topic.TASSE, Topic.DEBITORIA, Topic.RIMBORSO_TASSA,
            Topic.ISCRIZIONE, Topic.CARRIERA, Topic.CREDITI, Topic.TIROCINIO, Topic.PASSAGGIO_TRASF, Topic.DOV_CIMEA,
            Topic.SALDO, Topic.SALDO_IMPORTO_ERRATO, Topic.IMPORTI, Topic.RINUNCIA_REVOCA,
            Topic.CONTRATTO, Topic.ALLOGGIO,
            Topic.PERMESSO, Topic.ISEE_REDDITI, Topic.PEC_EMAIL, Topic.INDIPENDENTE,
            Topic.CODICE_FISCALE, Topic.IBAN,
            Topic.GRADUATORIA, Topic.PREMIO_LAUREA,
            Topic.MOBILITA_ERASMUS,
            Topic.PORTALE,
            Topic.MENSA,
            Topic.BLOCCHI
        };

        // Topic→Category map
        private static readonly Dictionary<Topic, PrimaryTopic> Topic2Cat = new()
        {
            [Topic.ISCRIZIONE] = PrimaryTopic.ISCRIZIONE_E_CARRIERA,
            [Topic.CARRIERA] = PrimaryTopic.ISCRIZIONE_E_CARRIERA,
            [Topic.CREDITI] = PrimaryTopic.ISCRIZIONE_E_CARRIERA,
            [Topic.TIROCINIO] = PrimaryTopic.ISCRIZIONE_E_CARRIERA,
            [Topic.PASSAGGIO_TRASF] = PrimaryTopic.ISCRIZIONE_E_CARRIERA,
            [Topic.DOV_CIMEA] = PrimaryTopic.ISCRIZIONE_E_CARRIERA,

            [Topic.IMPORTI] = PrimaryTopic.BENEFICI_E_IMPORTI,
            [Topic.RINUNCIA_REVOCA] = PrimaryTopic.BENEFICI_E_IMPORTI,
            [Topic.SALDO] = PrimaryTopic.BENEFICI_E_IMPORTI,
            [Topic.SALDO_IMPORTO_ERRATO] = PrimaryTopic.BENEFICI_E_IMPORTI,

            [Topic.ALLOGGIO] = PrimaryTopic.ALLOGGIO,
            [Topic.CONTRATTO] = PrimaryTopic.ALLOGGIO,

            [Topic.PERMESSO] = PrimaryTopic.DOCUMENTI_E_PERMESSI,
            [Topic.ISEE_REDDITI] = PrimaryTopic.DOCUMENTI_E_PERMESSI,
            [Topic.PEC_EMAIL] = PrimaryTopic.DOCUMENTI_E_PERMESSI,
            [Topic.INDIPENDENTE] = PrimaryTopic.DOCUMENTI_E_PERMESSI,
            [Topic.CODICE_FISCALE] = PrimaryTopic.DOCUMENTI_E_PERMESSI,

            [Topic.IBAN] = PrimaryTopic.IBAN,

            [Topic.PAGAMENTI] = PrimaryTopic.PAGAMENTI_E_TASSE,
            [Topic.TASSE] = PrimaryTopic.PAGAMENTI_E_TASSE,
            [Topic.RIMBORSO_TASSA] = PrimaryTopic.PAGAMENTI_E_TASSE,
            [Topic.DEBITORIA] = PrimaryTopic.PAGAMENTI_E_TASSE,

            [Topic.PORTALE] = PrimaryTopic.PORTALE_E_ACCESSO,
            [Topic.GRADUATORIA] = PrimaryTopic.GRADUATORIE,
            [Topic.PREMIO_LAUREA] = PrimaryTopic.GRADUATORIE,

            [Topic.MOBILITA_ERASMUS] = PrimaryTopic.MOBILITA,
            [Topic.MENSA] = PrimaryTopic.MENSA,

            [Topic.BLOCCHI] = PrimaryTopic.ALTRO,
            [Topic.CAF] = PrimaryTopic.ALTRO
        };

        private static readonly (string bad, string good)[] Canon = new[]
        {
            ("sogiorno", "soggiorno"),
            ("isee universita", "isee università"),
            ("resident permit", "residence permit"),
            ("qr code", "qrcode"),
            ("rimorso", "rimborso")
        };

        // Require/Anti keywords
        private static readonly Dictionary<Topic, string[]> RequireAny = new()
        {
            [Topic.PAGAMENTI] = new[]
            {
                "pagamento", "pagamenti",
                "accredito", "accreditato", "erogazione", "liquidazione",
                "mandato", "bonifico",
                "payment", "credited", "transfer", "disbursement"
            },

            [Topic.TASSE] = new[]
            {
                "pagopa", "iuv", "mav", "bollettino",
                "tassa", "tasse", "regionale", "universitaria",
                "avviso", "quietanza", "ricevuta", "scadenz",
                "imposta", "unpaid", "tuition", "fee", "tax"
            },

            [Topic.RIMBORSO_TASSA] = new[]
            {
                "rimbors", "rimborso", "refund",
                "tassa", "tasse", "regional", "universitaria", "tuition"
            },

            [Topic.IBAN] = new[]
            {
                "iban", "conto", "corrente", "banca", "intestat",
                "bancari", "bank", "account",
                "sepa", "revolut", "wise", "prepagata", "carta"
            },

            [Topic.IMPORTI] = new[]
            {
                "importo", "importi",
                "borsa", "borse", "scholarship",
                "beneficio", "benefici", "grant",
                "contributo", "contributi",
                "accredito", "accrediti",
                "rata", "rate", "installment"
            },

            [Topic.CONTRATTO] = new[]
            {
                "contratto", "contratti",
                "locazione", "affitto", "lease", "rental",
                "proroga", "registrazione",
                "agenzia", "entrate", "domicilio"
            },

            [Topic.MENSA] = new[]
            {
                "mensa", "monetizz", "monetizzazione",
                "pasto", "pasti", "buono", "voucher",
                "tessera", "card", "ricarica", "600"
            },

            [Topic.PORTALE] = new[]
            {
                "portale", "portal", "sito", "website",
                "login", "accesso", "spid",
                "timeout", "errore", "error",
                "upload", "caric", "schermata", "pagina",
                "area", "riservata", "personale"
            }
        };

        private static readonly Dictionary<Topic, string[]> Anti = new()
        {
            [Topic.PAGAMENTI] = new[]
            {
                "tassa", "tasse", "regionale", "universitaria",
                "pagopa", "iuv", "mav", "quietanza", "ricevuta",
                "attestazione", "certificato", "prova", "mezzi",
                "tax", "tuition", "fee", "receipt", "invoice"
            },

            [Topic.TASSE] = new[]
            {
                "codice", "fiscale", "taxcode", "fiscalcode", "iban",
                "rimborso", "rimbors", "refund", "rimorso"
            },

            [Topic.PORTALE] = new[]
            {
                "permesso", "questur", "impront",
                "residence", "permit"
            },

            [Topic.IBAN] = new[]
            {
                "barcode", "qr", "qrcode",
                "pagopa", "iuv"
            },

            [Topic.SALDO_IMPORTO_ERRATO] = new[]
            {
                "tassa", "tasse", "regionale", "pagopa"
            },

            [Topic.IMPORTI] = new[]
            {
                "tassa", "tasse", "regionale", "universitaria",
                "pagopa", "iuv"
            },

            [Topic.CAF] = new[]
            {
                "isee", "iseeup", "ispeup",
                "dsu", "parificato", "parificata",
                "reddit", "ispe"
            }
        };

        // Dizionario completo
        private static readonly Dictionary<Topic, (string[] multi, string[] single)> Dict = BuildDict();

        // Hot-path precomputations
        private static readonly Topic[] AllTopics = (Topic[])Enum.GetValues(typeof(Topic));
        private static readonly int TOPIC_LEN = AllTopics.Length;
        private static readonly int[] Topic2CatArray;

        // phrasesRx + weighted single stems/tokens
        private static readonly (Regex phrasesRx, Dictionary<string, int> singleWeights, HashSet<string> tokenUniverse)[] TopicPatterns;

        private static readonly (HashSet<string> req, HashSet<string> anti)[] TopicReqAnti;

        // Min score per topic
        private static readonly Dictionary<Topic, int> MinTopicScore = new()
        {
            [Topic.PAGAMENTI] = 2,
            [Topic.IBAN] = 2,
            [Topic.PERMESSO] = 2,
            [Topic.MENSA] = 2,
            [Topic.PREMIO_LAUREA] = 2,
            [Topic.MOBILITA_ERASMUS] = 2,
            [Topic.INDIPENDENTE] = 2,
            [Topic.ALLOGGIO] = 2,
            [Topic.RIMBORSO_TASSA] = 2,
            [Topic.TASSE] = 2,
            [Topic.ISCRIZIONE] = 2,

            [Topic.IMPORTI] = 1,
            [Topic.CARRIERA] = 1,
            [Topic.CAF] = 1,
            [Topic.DOV_CIMEA] = 1,
            [Topic.PORTALE] = 1,
            [Topic.DEBITORIA] = 1,
            [Topic.SALDO_IMPORTO_ERRATO] = 1,
            [Topic.RINUNCIA_REVOCA] = 1
        };

        // Generic info / status patterns (copre “stato domanda”, “in attesa”, “risposta ticket”, “documenti caricati”)
        private static readonly string[] GenericInfoHints =
        {
            "stato", "status", "esito", "result",
            "domanda", "istanza", "application",
            "ticket", "risposta", "answer",
            "attesa", "pending", "waiting",
            "verifica", "check",
            "documenti", "documentazione", "allegato", "allegati", "upload", "caric"
        };

        // Termini che possono contribuire al punteggio, ma non costituiscono da soli
        // evidenza specifica. Sono memorizzati sia nella forma intera sia in forme stemmate.
        private static readonly HashSet<string> GenericKeywordTokens = new(StringComparer.Ordinal)
        {
            "borsa", "borse", "beneficio", "benefici", "benefic", "contributo", "contributi", "contribut",
            "problema", "problemi", "problem", "domanda", "domande", "domand", "richiesta", "richieste", "richiest",
            "pagamento", "pagamenti", "pagament", "payment", "rimborso", "rimborsi", "rimbors", "refund",
            "accredito", "accrediti", "accredit", "stato", "status", "informazione", "informazioni", "info",
            "documento", "documenti", "documentazione", "allegato", "allegati", "servizio", "servizi",
            "errore", "error", "pagina", "area", "personale", "conto", "card", "carta", "posto", "camera", "stanza"
        };

        [GeneratedRegex(@"(?i)\b(?:a\.?\s*a\.?\.?)\s*(\d{2,4})\s*[/\-]?\s*(\d{2,4})\b", RegexOptions.CultureInvariant)]
        private static partial Regex RxAA_WithMarker_Gen();

        [GeneratedRegex(@"(?i)\b(\d{2,4})\s*[/\-]\s*(\d{2,4})\b", RegexOptions.CultureInvariant)]
        private static partial Regex RxAA_Sep_Gen();

        [GeneratedRegex(@"\b(\d{4})(\d{4})\b|\b(\d{2})(\d{2})\b|\b(20\d{2})(\d{2})\b", RegexOptions.CultureInvariant)]
        private static partial Regex RxAA_Concat_Gen();

        private static readonly Regex RxAA_WithMarker = RxAA_WithMarker_Gen();
        private static readonly Regex RxAA_Sep = RxAA_Sep_Gen();
        private static readonly Regex RxAA_Concat = RxAA_Concat_Gen();

        [GeneratedRegex(@"[|/\\]+", RegexOptions.CultureInvariant)]
        private static partial Regex RxSlash_Gen();

        [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
        private static partial Regex RxSpace_Gen();

        private static readonly Regex RxSlash = RxSlash_Gen();
        private static readonly Regex RxSpace = RxSpace_Gen();

        // Stem cache (thread-safe)
        private static readonly ConcurrentDictionary<string, string> StemCache;

        static KeywordEngineV6()
        {
            StemCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            Topic2CatArray = BuildTopic2CatArray();
            TopicPatterns = BuildTopicPatterns(); // includes tokenUniverse
            TopicReqAnti = BuildReqAnti();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────────────
        public static ExtractionV6 Extract(string raw, Lang? preferred = null)
        {
            var ex = new ExtractionV6 { OriginalText = raw ?? "" };
            if (string.IsNullOrWhiteSpace(raw))
                return ex;

            // Normalize + language
            var text = Normalize(raw);
            ex.NormalizedText = text;

            ex.DetectedLanguage = preferred is { } p && p != Lang.UNKNOWN
                ? (p == Lang.MIXED ? DetectLang(text) : p)
                : DetectLang(text);

            // Segmentazione: head/tail
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            string headSegment = sentences.Length > 0 ? sentences[0] : text;

            string tailSegment;
            if (sentences.Length >= 2)
            {
                var sbTail = new StringBuilder();
                int startIdx = Math.Max(0, sentences.Length - 2);
                for (int i = startIdx; i < sentences.Length; i++)
                {
                    if (sbTail.Length > 0) sbTail.Append(' ');
                    sbTail.Append(sentences[i]);
                }
                tailSegment = sbTail.ToString();
            }
            else
            {
                tailSegment = text;
            }

            // Tokenize (raw tokens + stems in same set)
            var rawTokens = Tokenize(text);
            var tokenSet = new HashSet<string>(StringComparer.Ordinal);

            foreach (var t in rawTokens) tokenSet.Add(t);
            foreach (var st in StemAll(rawTokens)) tokenSet.Add(st);

            var headTokens = Tokenize(headSegment);
            var headSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in headTokens) headSet.Add(t);
            foreach (var st in StemAll(headTokens)) headSet.Add(st);

            var tailTokens = Tokenize(tailSegment);
            var tailSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in tailTokens) tailSet.Add(t);
            foreach (var st in StemAll(tailTokens)) tailSet.Add(st);

            // Generic info request heuristic
            ex.IsGenericInfoRequest = HasAny(tokenSet, GenericInfoHints);

            // Scoring per-topic
            var counts = ArrayPool<int>.Shared.Rent(TOPIC_LEN);
            Array.Clear(counts, 0, TOPIC_LEN);

            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                var topic = (Topic)ti;
                var (phrRx, singles, _) = TopicPatterns[ti];
                int score = 0;

                // 1) multi-parola
                if (phrRx is not null)
                {
                    var mc = phrRx.Matches(text);
                    if (mc.Count > 0)
                        score += PHRASE_WEIGHT * mc.Count;
                }

                // 2) singole (token+stem) + head/tail boosting
                if (singles is not null && singles.Count > 0)
                {
                    foreach (var kvp in singles)
                    {
                        string key = kvp.Key;
                        int w = kvp.Value;

                        if (!tokenSet.Contains(key))
                            continue;

                        score += w * WORD_WEIGHT;
                        if (!IsGenericKeyword(key))
                        {
                            if (headSet.Contains(key)) score += w * WORD_WEIGHT;
                            if (tailSet.Contains(key)) score += w * WORD_WEIGHT;
                        }
                    }
                }

                // 3) require / anti
                var (req, anti) = TopicReqAnti[ti];

                if (req is not null && req.Count > 0)
                {
                    bool ok = false;
                    foreach (var r in req)
                    {
                        if (tokenSet.Contains(r)) { ok = true; break; }
                    }

                    if (!ok)
                    {
                        score = 0;
                    }
                    else
                    {
                        score += BOOST_REQUIRE;
                    }
                }

                if (anti is not null && anti.Count > 0)
                {
                    foreach (var a in anti)
                    {
                        if (tokenSet.Contains(a))
                            score = Math.Max(0, score - PENALTY_ANTI);
                    }
                }

                // Topic sensibili: una parola generica non basta. La combinazione
                // obbligatoria evita falsi positivi quali "rimborso" non fiscale,
                // "borsa" senza importo e generici errori tecnici non legati al portale.
                if (score > 0 && !PassesMandatoryCombination(topic, tokenSet))
                    score = 0;

                counts[ti] = score;
            }

            // Heuristics trasversali
            ex.MentionsBlocks =
                HasAny(tokenSet,
                    "blocco", "blocchi", "sblocco",
                    "incongruenza", "incongruenze",
                    "unpaid", "block", "blocked", "blocks",
                    "debitoria", "indipendente", "tirocinio",
                    "escluso", "esclusa", "excluded");

            ex.MentionsOwnPosition =
                HasAny(tokenSet,
                    "io", "mio", "mia", "mie", "miei",
                    "my", "profilo", "posizione", "position", "status",
                    "eligible", "winner",
                    "idoneo", "idonea",
                    "vincitore", "vincitrice",
                    "escluso", "esclusa", "excluded");

            if (ex.MentionsBlocks)
                counts[(int)Topic.BLOCCHI] += BOOST_STRONG;

            // Pagamenti / tasse / rimborso / importi
            if (HasAny(tokenSet,
                    "pagamento", "pagamenti", "pagato", "pagata", "pagat",
                    "accredito", "accreditato", "accredit", "erogazione", "erog",
                    "liquidazione", "liquid", "mandato", "bonifico",
                    "payment", "paid", "credited", "transfer", "disbursement") &&
                HasAny(tokenSet,
                    "borsa", "scholarship", "beneficio", "benefit",
                    "rata", "installment", "saldo", "balance",
                    "contributo", "grant", "importo", "amount"))
            {
                counts[(int)Topic.PAGAMENTI] += BOOST_STRONG;
            }

            if (HasAny(tokenSet, "quando", "tempistiche", "data", "when") &&
                HasAny(tokenSet, "pagamento", "pagamenti", "accredito", "erogazione", "payment", "paid"))
            {
                counts[(int)Topic.PAGAMENTI] += BOOST_MED;
            }

            if (HasAll(tokenSet, "tassa", "regionale") || HasAll(tokenSet, "regional", "tax"))
                counts[(int)Topic.TASSE] += BOOST_MED;

            if (HasAll(tokenSet, "tassa", "universitaria") ||
                HasAll(tokenSet, "university", "tax") ||
                HasAll(tokenSet, "tuition", "fee"))
                counts[(int)Topic.TASSE] += BOOST_MED;

            if (HasAll(tokenSet, "rimborso", "tassa") ||
                HasAll(tokenSet, "refund", "tax") ||
                HasAll(tokenSet, "tuition", "refund"))
                counts[(int)Topic.RIMBORSO_TASSA] += BOOST_MED;

            // Iscrizione / crediti / carriera
            if (HasAny(tokenSet, "iscrizione", "iscrizioni", "enrollment", "enrolment") &&
                HasAny(tokenSet, "certificato", "certificate", "verifica", "status", "regolarizz"))
                counts[(int)Topic.ISCRIZIONE] += BOOST_MED;

            if (HasAny(tokenSet, "cfu", "crediti", "credits", "ects", "incongruenza", "mismatch"))
                counts[(int)Topic.CREDITI] += BOOST_MED;

            if (HasAny(tokenSet, "fuori", "fuoricorso", "fuori corso") &&
                HasAny(tokenSet, "anno", "anni", "corso"))
                counts[(int)Topic.CARRIERA] += BOOST_MED;

            // Codice fiscale
            if (HasAny(tokenSet, "codice", "fiscale", "taxcode", "fiscalcode"))
                counts[(int)Topic.CODICE_FISCALE] += BOOST_MED;

            // IBAN
            if (tokenSet.Contains("iban") &&
                HasAny(tokenSet, "invalid", "revolut", "wise", "sepa", "estero", "stranier", "accettato", "rifiutato", "unsupported"))
                counts[(int)Topic.IBAN] += BOOST_MED;

            // Contratto / alloggio
            if (HasAny(tokenSet, "contratto", "locazione", "affitto", "lease", "rental"))
                counts[(int)Topic.CONTRATTO] += BOOST_MED;

            if (HasAny(tokenSet, "proroga", "protocollo") || HasAll(tokenSet, "agenzia", "entrate"))
                counts[(int)Topic.CONTRATTO] += WORD_WEIGHT;

            if (HasAny(tokenSet, "alloggio", "residenza", "residence", "studentato", "dormitory", "housing", "accommodation"))
                counts[(int)Topic.ALLOGGIO] += BOOST_MED;

            // Premio di laurea
            if (HasAny(tokenSet, "premio", "prize", "award") && HasAny(tokenSet, "laurea", "graduation", "degree"))
                counts[(int)Topic.PREMIO_LAUREA] += BOOST_MED;

            // Mensa
            if (HasAny(tokenSet, "mensa", "canteen", "dining", "monetizz", "monetizzazione") ||
                (HasAny(tokenSet, "buono", "voucher", "tessera", "card") && HasAny(tokenSet, "mensa", "canteen")))
                counts[(int)Topic.MENSA] += BOOST_MED;

            // Portale / upload / area riservata
            if (HasAny(tokenSet, "portale", "portal", "login", "accesso", "spid", "timeout", "errore", "error", "upload", "caric") ||
                (HasAny(tokenSet, "area", "riservata", "personale") && HasAny(tokenSet, "acced", "accesso", "login")))
                counts[(int)Topic.PORTALE] += BOOST_MED;

            // CAF (Iran / ambasciata)
            if (HasAll(tokenSet, "iran") &&
                (HasAny(tokenSet, "postilla", "legalizzare", "legalize", "embassy", "ambasciata", "apostilla") ||
                 HasAll(tokenSet, "ambasciata", "italiana") ||
                 HasAll(tokenSet, "italian", "embassy")) &&
                !HasAll(tokenSet, "codice", "fiscale") &&
                !HasAny(tokenSet, "isee", "iseeup") &&
                !HasAny(tokenSet, "contratto"))
            {
                counts[(int)Topic.CAF] += BOOST_STRONG;
            }

            // Le euristiche trasversali possono incrementare i punteggi dopo lo
            // scoring iniziale. Riapplica quindi i vincoli obbligatori ai topic sensibili.
            ApplyMandatoryCombinationGates(counts, tokenSet);

            // ──────────────────────────────────────────────────────────────────
            // Risoluzione conflitti mirati (riduce falsi positivi)
            // ──────────────────────────────────────────────────────────────────
            Suppress(counts, Topic.IBAN, Topic.TASSE);
            Suppress(counts, Topic.IBAN, Topic.RIMBORSO_TASSA);
            Suppress(counts, Topic.IBAN, Topic.IMPORTI);
            Suppress(counts, Topic.IBAN, Topic.SALDO);

            Suppress(counts, Topic.PAGAMENTI, Topic.IMPORTI);
            Suppress(counts, Topic.CONTRATTO, Topic.ALLOGGIO);
            Suppress(counts, Topic.PERMESSO, Topic.PORTALE);
            Suppress(counts, Topic.PREMIO_LAUREA, Topic.GRADUATORIA);

            Suppress(counts, Topic.RIMBORSO_TASSA, Topic.TASSE);
            Suppress(counts, Topic.RIMBORSO_TASSA, Topic.IMPORTI);

            Suppress(counts, Topic.SALDO, Topic.IMPORTI);
            Suppress(counts, Topic.SALDO_IMPORTO_ERRATO, Topic.SALDO);
            Suppress(counts, Topic.SALDO_IMPORTO_ERRATO, Topic.IMPORTI);

            // Co-occorrenza forte TASSE + PORTALE → riduce PORTALE
            if (counts[(int)Topic.TASSE] >= 3 && counts[(int)Topic.PORTALE] >= 2)
                counts[(int)Topic.PORTALE] = Math.Min(counts[(int)Topic.PORTALE], counts[(int)Topic.TASSE] - 1);

            if (counts[(int)Topic.TASSE] >= 4 &&
                counts[(int)Topic.PAGAMENTI] > 0 &&
                !HasAny(tokenSet, "borsa", "scholarship", "beneficio", "grant"))
            {
                counts[(int)Topic.PAGAMENTI] = Math.Min(
                    counts[(int)Topic.PAGAMENTI],
                    Math.Max(0, counts[(int)Topic.TASSE] - 2));
            }

            // ──────────────────────────────────────────────────────────────────
            // Aggregazione per categoria primaria.
            // Si usa il miglior topic della categoria, non la somma dei topic: topic
            // correlati (es. TASSE e RIMBORSO_TASSA) non possono più gonfiare la categoria.
            // ──────────────────────────────────────────────────────────────────
            var catScores = new int[Enum.GetValues<PrimaryTopic>().Length];
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                if ((Topic)ti == Topic.BLOCCHI)
                    continue;

                int cat = Topic2CatArray[ti];
                catScores[cat] = Math.Max(catScores[cat], counts[ti]);
            }

            int bestCat = -1;
            int bestCatScore = 0;
            for (int ci = 0; ci < catScores.Length; ci++)
            {
                int score = catScores[ci];
                if (score <= 0)
                    continue;

                if (bestCat < 0 ||
                    score > bestCatScore ||
                    (score == bestCatScore &&
                     CatPrecedenceRank((PrimaryTopic)ci) < CatPrecedenceRank((PrimaryTopic)bestCat)))
                {
                    bestCat = ci;
                    bestCatScore = score;
                }
            }

            int secondCatScore = 0;
            for (int ci = 0; ci < catScores.Length; ci++)
            {
                if (ci == bestCat)
                    continue;
                secondCatScore = Math.Max(secondCatScore, catScores[ci]);
            }

            Topic primarySecondaryTopic = bestCat >= 0
                ? GetBestTopicForCategory(bestCat, counts)
                : Topic.BLOCCHI;
            int primaryTopicScore = primarySecondaryTopic == Topic.BLOCCHI
                ? 0
                : counts[(int)primarySecondaryTopic];

            int totalScore = catScores.Sum();
            ex.TotalScore = totalScore;
            ex.PrimaryScore = bestCatScore;
            ex.MarginTop1Top2 = Math.Max(0, bestCatScore - secondCatScore);
            ex.PrimaryHasSpecificEvidence = primarySecondaryTopic != Topic.BLOCCHI &&
                                            HasSpecificEvidence(primarySecondaryTopic, text, tokenSet);
            ex.PlausibleCategoryCount = CountPlausibleCategories(
                catScores,
                bestCatScore,
                text,
                tokenSet);

            // Un solo argomento primario: la precedenza interviene esclusivamente
            // per ex-aequo. In assenza di evidenza viene assegnato ALTRO e il ticket
            // viene portato alla revisione manuale.
            ex.TopicPrimary = bestCat >= 0 && bestCatScore > 0
                ? ((PrimaryTopic)bestCat).ToString()
                : PrimaryTopic.ALTRO.ToString();

            // ──────────────────────────────────────────────────────────────────
            // Argomenti secondari: massimo due.
            // 1) miglior topic interno alla categoria primaria;
            // 2) miglior topic di una categoria esterna, solo se vicino al primo,
            //    sopra soglia assoluta e sostenuto da evidenza specifica.
            // ──────────────────────────────────────────────────────────────────
            var selectedTopics = new List<Topic>(2);
            if (primarySecondaryTopic != Topic.BLOCCHI &&
                primaryTopicScore > 0 &&
                ex.PrimaryHasSpecificEvidence)
            {
                selectedTopics.Add(primarySecondaryTopic);
            }

            if (selectedTopics.Count > 0)
            {
                Topic externalTopic = Topic.BLOCCHI;
                int externalScore = 0;
                int externalPrecedence = int.MaxValue;

                for (int ti = 0; ti < TOPIC_LEN; ti++)
                {
                    Topic candidate = (Topic)ti;
                    if (candidate == Topic.BLOCCHI || Topic2CatArray[ti] == bestCat)
                        continue;

                    int score = counts[ti];
                    int minScore = MinTopicScore.TryGetValue(candidate, out var min) ? min : MIN_SUB_SCORE;
                    if (score < minScore || !HasSpecificEvidence(candidate, text, tokenSet))
                        continue;

                    int precedence = Array.IndexOf(TOPIC_PRECEDENCE, candidate);
                    if (precedence < 0)
                        precedence = int.MaxValue;

                    if (externalTopic == Topic.BLOCCHI ||
                        score > externalScore ||
                        (score == externalScore && precedence < externalPrecedence))
                    {
                        externalTopic = candidate;
                        externalScore = score;
                        externalPrecedence = precedence;
                    }
                }

                if (externalTopic != Topic.BLOCCHI)
                {
                    int requiredScore = Math.Max(
                        MIN_SECONDARY_ABS,
                        (int)Math.Ceiling(primaryTopicScore * SECONDARY_REL_TO_PRIMARY));

                    if (externalScore >= requiredScore)
                    {
                        selectedTopics.Add(externalTopic);
                    }
                    else
                    {
                        ex.SecondaryCutoffApplied = true;
                    }
                }
            }

            ex.TopicSecondary = selectedTopics.Count == 0
                ? string.Empty
                : string.Join(" | ", selectedTopics.Select(topic => topic.ToString()));
            ex.SecondaryScore = selectedTopics.Count > 0
                ? counts[(int)selectedTopics[0]]
                : 0;
            ex.SecondaryConfidence = totalScore > 0
                ? (double)ex.SecondaryScore / totalScore
                : 0.0;

            // ──────────────────────────────────────────────────────────────────
            // Confidenza e motivi di verifica.
            // Il flag viene sollevato per assenza di evidenza, più di due categorie
            // plausibili, oppure combinazioni di segnali deboli/conflittuali.
            // ──────────────────────────────────────────────────────────────────
            bool noEvidence = bestCat < 0 || bestCatScore <= 0 || primarySecondaryTopic == Topic.BLOCCHI;
            bool genericOnly = !noEvidence && !ex.PrimaryHasSpecificEvidence;
            bool categoriesClose = !noEvidence && secondCatScore > 0 &&
                                   secondCatScore >= (int)Math.Ceiling(bestCatScore * 0.80);
            bool conflictingCategories = categoriesClose &&
                                         bestCatScore >= MIN_PRIMARY_SCORE &&
                                         secondCatScore >= MIN_PRIMARY_SCORE &&
                                         HasCategorySpecificEvidence(bestCat, text, tokenSet) &&
                                         HasAnyCategorySpecificEvidenceAtScore(
                                             catScores,
                                             bestCat,
                                             secondCatScore,
                                             text,
                                             tokenSet);
            bool tooManyPlausibleCategories = ex.PlausibleCategoryCount > 2;
            bool combinedWeakSignals = (genericOnly && categoriesClose) ||
                                       (categoriesClose && conflictingCategories);

            ex.IsLowConfidence = noEvidence || tooManyPlausibleCategories || combinedWeakSignals;

            var verificationReasons = new List<string>(4);
            if (noEvidence)
                verificationReasons.Add("EVIDENZA_INSUFFICIENTE");
            if (genericOnly)
                verificationReasons.Add("KEYWORD_GENERICA");
            if (categoriesClose)
                verificationReasons.Add("CATEGORIE_VICINE");
            if (conflictingCategories)
                verificationReasons.Add("CONFLITTO_CATEGORIE");
            if (tooManyPlausibleCategories)
                verificationReasons.Add("TROPPE_CATEGORIE_PLAUSIBILI");
            ex.VerificationReason = ex.IsLowConfidence
                ? string.Join(" | ", verificationReasons.Distinct(StringComparer.Ordinal))
                : string.Empty;

            double strength = Math.Min(1.0, primaryTopicScore / 9.0);
            double separation = bestCatScore > 0
                ? Math.Clamp((double)Math.Max(0, bestCatScore - secondCatScore) / bestCatScore, 0.0, 1.0)
                : 0.0;
            int confidenceScore = (int)Math.Round((strength * 55.0) +
                                                   (separation * 30.0) +
                                                   (ex.PrimaryHasSpecificEvidence ? 15.0 : 0.0));
            if (genericOnly)
                confidenceScore = Math.Min(confidenceScore, 45);
            if (categoriesClose)
                confidenceScore -= 15;
            if (conflictingCategories)
                confidenceScore -= 10;
            if (tooManyPlausibleCategories)
                confidenceScore -= 20;
            if (noEvidence)
                confidenceScore = 0;

            ex.ConfidenceScore = Math.Clamp(confidenceScore, 0, 100);
            ex.PrimaryConfidence = ex.ConfidenceScore / 100.0;

            // Tertiary: blocchi
            ex.TopicTertiary = counts[(int)Topic.BLOCCHI] > 0 ? "SI" : "";

            // Copia score in dizionario Counts
            for (int ti = 0; ti < TOPIC_LEN; ti++)
                ex.Counts[(Topic)ti] = counts[ti];

            // Explain: matched keywords (top)
            ex.MatchedPrimaryKeywords = BuildMatchedKeywordsForPrimary(bestCat, tokenSet, max: 12);
            ex.MatchedTop1Keywords = selectedTopics.Count > 0
                ? BuildMatchedKeywordsForTopic(selectedTopics[0], tokenSet, max: 12)
                : "";

            // ──────────────────────────────────────────────────────────────────
            // Academic year
            // ──────────────────────────────────────────────────────────────────
            var aa = DetectAcademicYearFast(text);
            if (aa.has)
            {
                ex.AcademicYearRaw = aa.raw;
                ex.AcademicYearStart = aa.s;
                ex.AcademicYearEnd = aa.e;
                ex.AcademicYearConfidence = aa.conf;
            }

            ArrayPool<int>.Shared.Return(counts, clearArray: true);
            return ex;
        }

        public static ExtractionV6 ExtractTicket(
            string message,
            string subject,
            string category,
            string subcategory,
            Lang? preferred = null)
        {
            var parts = new[] { subject, category, subcategory, message };
            string contextualText = string.Join(
                ". ",
                parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

            var extraction = Extract(contextualText, preferred);
            extraction.OriginalText = message ?? "";
            return extraction;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────
        private static bool PassesMandatoryCombination(Topic topic, HashSet<string> tokenSet)
        {
            return topic switch
            {
                Topic.RIMBORSO_TASSA =>
                    HasAny(tokenSet, "rimborso", "rimbors", "refund") &&
                    HasAny(tokenSet,
                        "tassa", "tasse", "pagopa", "iuv", "mav", "bollettino",
                        "regionale", "universitaria", "tuition", "fee", "tax", "versamento"),

                Topic.IMPORTI =>
                    HasAny(tokenSet, "borsa", "borse", "scholarship", "beneficio", "benefici", "benefic", "contributo", "contributi", "contribut", "grant") &&
                    HasAny(tokenSet,
                        "importo", "importi", "ammontare", "quanto", "assegnato", "assegnata",
                        "spettante", "rata", "rate", "accredito", "accreditato", "saldo", "amount", "installment"),

                Topic.PORTALE =>
                    HasAny(tokenSet,
                        "portale", "portal", "sito", "website", "login", "accesso", "spid",
                        "area", "riservata", "personale") &&
                    HasAny(tokenSet,
                        "errore", "error", "login", "accesso", "acced", "timeout", "upload",
                        "caricare", "caricamento", "caric", "schermata", "pagina", "500", "502", "504",
                        "impossibile", "bloccato", "bloccata", "sessione", "scaduta", "expired"),

                _ => true
            };
        }

        private static void ApplyMandatoryCombinationGates(int[] counts, HashSet<string> tokenSet)
        {
            foreach (Topic topic in new[] { Topic.RIMBORSO_TASSA, Topic.IMPORTI, Topic.PORTALE })
            {
                if (counts[(int)topic] > 0 && !PassesMandatoryCombination(topic, tokenSet))
                    counts[(int)topic] = 0;
            }
        }

        private static Topic GetBestTopicForCategory(int category, int[] counts)
        {
            Topic bestTopic = Topic.BLOCCHI;
            int bestScore = 0;
            int bestPrecedence = int.MaxValue;

            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                Topic topic = (Topic)ti;
                if (topic == Topic.BLOCCHI || Topic2CatArray[ti] != category)
                    continue;

                int score = counts[ti];
                if (score <= 0)
                    continue;

                int precedence = Array.IndexOf(TOPIC_PRECEDENCE, topic);
                if (precedence < 0)
                    precedence = int.MaxValue;

                if (bestTopic == Topic.BLOCCHI ||
                    score > bestScore ||
                    (score == bestScore && precedence < bestPrecedence))
                {
                    bestTopic = topic;
                    bestScore = score;
                    bestPrecedence = precedence;
                }
            }

            return bestTopic;
        }

        private static int CountPlausibleCategories(
            int[] categoryScores,
            int bestCategoryScore,
            string text,
            HashSet<string> tokenSet)
        {
            if (bestCategoryScore <= 0)
                return 0;

            int requiredScore = Math.Max(
                MIN_PRIMARY_SCORE,
                (int)Math.Ceiling(bestCategoryScore * PLAUSIBLE_CATEGORY_REL_TO_PRIMARY));
            int count = 0;

            for (int ci = 0; ci < categoryScores.Length; ci++)
            {
                if (categoryScores[ci] < requiredScore)
                    continue;
                if (HasCategorySpecificEvidence(ci, text, tokenSet))
                    count++;
            }

            return count;
        }

        private static bool HasAnyCategorySpecificEvidenceAtScore(
            int[] categoryScores,
            int excludedCategory,
            int targetScore,
            string text,
            HashSet<string> tokenSet)
        {
            for (int ci = 0; ci < categoryScores.Length; ci++)
            {
                if (ci == excludedCategory || categoryScores[ci] != targetScore)
                    continue;
                if (HasCategorySpecificEvidence(ci, text, tokenSet))
                    return true;
            }

            return false;
        }

        private static bool HasCategorySpecificEvidence(int category, string text, HashSet<string> tokenSet)
        {
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                if ((Topic)ti == Topic.BLOCCHI || Topic2CatArray[ti] != category)
                    continue;
                if (HasSpecificEvidence((Topic)ti, text, tokenSet))
                    return true;
            }

            return false;
        }

        private static bool HasSpecificEvidence(Topic topic, string text, HashSet<string> tokenSet)
        {
            if (!PassesMandatoryCombination(topic, tokenSet))
                return false;

            if (Dict.TryGetValue(topic, out var definition) && definition.multi != null)
            {
                foreach (string phrase in definition.multi)
                {
                    string normalizedPhrase = Normalize(phrase);
                    if (!string.IsNullOrWhiteSpace(normalizedPhrase) &&
                        text.Contains(normalizedPhrase, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            if (!Dict.TryGetValue(topic, out definition) || definition.single == null)
                return false;

            foreach (string rawKeyword in definition.single)
            {
                string normalizedKeyword = Normalize(rawKeyword);
                foreach (string token in normalizedKeyword.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string stem = StemTokenCached(token);
                    if (!IsGenericKeyword(token) && (tokenSet.Contains(token) || tokenSet.Contains(stem)))
                        return true;
                }
            }

            return false;
        }

        private static bool IsGenericKeyword(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            string normalized = Normalize(token);
            string stem = StemTokenCached(normalized);
            return GenericKeywordTokens.Contains(normalized) || GenericKeywordTokens.Contains(stem);
        }

        private static void Suppress(int[] c, Topic a, Topic b)
        {
            int ia = (int)a, ib = (int)b;
            if (c[ia] >= c[ib] + 3)
                c[ib] = 0;
        }

        private static List<string> Tokenize(string s)
        {
            var list = new List<string>(64);
            int i = 0, n = s.Length;
            while (i < n)
            {
                while (i < n && !char.IsLetterOrDigit(s[i])) i++;
                int start = i;
                while (i < n && char.IsLetterOrDigit(s[i])) i++;
                if (i > start)
                    list.Add(s.AsSpan(start, i - start).ToString());
            }
            return list;
        }

        private static List<string> StemAll(List<string> toks)
        {
            var list = new List<string>(toks.Count);
            foreach (var t in toks)
            {
                if (!string.IsNullOrEmpty(t) && t.Length > 1)
                    list.Add(StemTokenCached(t));
            }
            return list;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasAny(HashSet<string> set, params string[] keys)
        {
            foreach (var k in keys)
                if (set.Contains(k))
                    return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasAll(HashSet<string> set, params string[] keys)
        {
            foreach (var k in keys)
                if (!set.Contains(k))
                    return false;
            return true;
        }

        private static string BuildMatchedKeywordsForPrimary(int bestCat, HashSet<string> tokenSet, int max)
        {
            if (bestCat < 0) return "";

            var hs = new HashSet<string>(StringComparer.Ordinal);
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                if ((Topic)ti == Topic.BLOCCHI) continue;
                if (Topic2CatArray[ti] != bestCat) continue;

                var (_, _, uni) = TopicPatterns[ti];
                if (uni is null || uni.Count == 0) continue;

                foreach (var t in uni)
                {
                    if (tokenSet.Contains(t))
                        hs.Add(t);
                    if (hs.Count >= max) break;
                }
                if (hs.Count >= max) break;
            }

            if (hs.Count == 0) return "";
            var arr = new List<string>(hs);
            arr.Sort(StringComparer.Ordinal);
            return string.Join(", ", arr);
        }

        private static string BuildMatchedKeywordsForTopic(Topic topic, HashSet<string> tokenSet, int max)
        {
            var (_, _, uni) = TopicPatterns[(int)topic];
            if (uni is null || uni.Count == 0) return "";

            var hs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in uni)
            {
                if (tokenSet.Contains(t))
                    hs.Add(t);
                if (hs.Count >= max) break;
            }

            if (hs.Count == 0) return "";
            var arr = new List<string>(hs);
            arr.Sort(StringComparer.Ordinal);
            return string.Join(", ", arr);
        }

        private static (bool has, string raw, int s, int e, double conf) DetectAcademicYearFast(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return default;

            string stext = text;

            var m1 = RxAA_WithMarker.Match(stext);
            if (m1.Success &&
                TryNormalizeYears(m1.Groups[1].Value, m1.Groups[2].Value, out var y1, out var y2))
            {
                if (y2 == y1 + 1)
                    return (true, m1.Value, y1, y2, 0.98);
            }

            (int s, int e, string raw, double conf) bestSep = default;
            foreach (Match m in RxAA_Sep.Matches(stext))
            {
                if (!m.Success) continue;
                if (!TryNormalizeYears(m.Groups[1].Value, m.Groups[2].Value, out var a, out var b)) continue;
                if (b != a + 1) continue;

                double c = ScorePair(m.Groups[1].Value, m.Groups[2].Value);
                if (c > bestSep.conf) bestSep = (a, b, m.Value, c);
            }
            if (bestSep.conf > 0)
                return (true, bestSep.raw, bestSep.s, bestSep.e, bestSep.conf);

            foreach (Match m in RxAA_Concat.Matches(stext))
            {
                if (!m.Success) continue;

                if (m.Groups[1].Success && m.Groups[2].Success)
                {
                    if (int.TryParse(m.Groups[1].Value, out var a) &&
                        int.TryParse(m.Groups[2].Value, out var b) &&
                        ValidRange(a) && ValidRange(b) && b == a + 1)
                        return (true, m.Value, a, b, 0.94);
                }

                if (m.Groups[3].Success && m.Groups[4].Success)
                {
                    if (TryNormalizeYears(m.Groups[3].Value, m.Groups[4].Value, out var a, out var b) &&
                        b == a + 1)
                        return (true, m.Value, a, b, 0.91);
                }

                if (m.Groups[5].Success && m.Groups[6].Success)
                {
                    if (TryNormalizeYears(m.Groups[5].Value, m.Groups[6].Value, out var a, out var b) &&
                        b == a + 1)
                        return (true, m.Value, a, b, 0.92);
                }
            }

            return default;

            static bool TryNormalizeYears(string y1s, string y2s, out int y1, out int y2)
            {
                y1 = y2 = 0;
                if (!int.TryParse(y1s, out var a) || !int.TryParse(y2s, out var b))
                    return false;

                a = To4(a);
                b = To4(b);

                if (!ValidRange(a) || !ValidRange(b)) return false;
                y1 = a; y2 = b;
                return true;

                static int To4(int y)
                {
                    if (y >= 1000) return y;
                    if (y < 0 || y > 99) return 0;
                    return (y <= 39) ? (2000 + y) : (1900 + y);
                }
            }

            static bool ValidRange(int y) => y >= 1990 && y <= 2099;

            static double ScorePair(string a, string b)
            {
                bool a4 = a.Length >= 4;
                bool b4 = b.Length >= 4;
                if (a4 && b4) return 0.96;
                if (a4 || b4) return 0.93;
                return 0.90;
            }
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = text.ToLowerInvariant();

            text = text.Replace('’', '\'').Replace('‘', '\'')
                       .Replace('“', '"').Replace('”', '"')
                       .Replace('–', '-').Replace('—', '-');

            if (Canon is not null && Canon.Length > 0)
            {
                for (int i = 0; i < Canon.Length; i++)
                {
                    var bad = Canon[i].bad;
                    var good = Canon[i].good;
                    if (!string.IsNullOrEmpty(bad) &&
                        !string.Equals(bad, good, StringComparison.Ordinal))
                    {
#if NET7_0_OR_GREATER
                        text = text.Replace(bad, good, StringComparison.Ordinal);
#else
                        text = text.Replace(bad, good);
#endif
                    }
                }
            }

            var norm = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(norm.Length);
            foreach (var c in norm)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            text = sb.ToString().Normalize(NormalizationForm.FormC);

            text = RxSlash.Replace(text, " ");
            text = RxSpace.Replace(text, " ").Trim();
            return text;
        }

        private static Lang DetectLang(string norm)
        {
            if (string.IsNullOrEmpty(norm)) return Lang.UNKNOWN;

            string[] itHints =
            {
                " il ", " la ", " lo ", " gli ", " le ",
                " un ", " una ", " di ", " che ", " non ",
                " per ", " con ", " alla ", " della ",
                "tassa", "iscrizione", "crediti", "graduatoria", "borsa"
            };

            string[] enHints =
            {
                " the ", " a ", " an ", " of ", " for ",
                " with ", " and ", " but ",
                " block", " blocks",
                " enrollment", " scholarship",
                " refund", " permit", "accommodation"
            };

            int itScore = 0, enScore = 0;
            var padded = " " + norm + " ";
            foreach (var h in itHints) itScore += CountOccurrences(padded, h);
            foreach (var h in enHints) enScore += CountOccurrences(padded, h);

            if (itScore == 0 && enScore == 0) return Lang.UNKNOWN;
            if (itScore > 0 && enScore > 0) return Lang.MIXED;
            return itScore > enScore ? Lang.IT : Lang.EN;

            static int CountOccurrences(string s, string sub)
            {
                int count = 0, idx = 0;
                while ((idx = s.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    idx += sub.Length;
                }
                return count;
            }
        }

        private static string StemTokenCached(string t)
        {
            if (string.IsNullOrEmpty(t)) return string.Empty;
            if (t.Length <= 4) return t;

            if (StemCache.TryGetValue(t, out var s)) return s;

            string[] suf =
            {
                "zioni","menti","mente","sione","sioni","ismi","ismo",
                "zione","zioni","tion","tions","ness","ingly",
                "ing","ed","es","ly","i","e","s"
            };

            foreach (var su in suf)
            {
                if (t.EndsWith(su, StringComparison.Ordinal) &&
                    t.Length > su.Length + 2)
                {
                    s = t[..^su.Length];
                    StemCache.TryAdd(t, s);
                    return s;
                }
            }

            StemCache.TryAdd(t, t);
            return t;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Build patterns: token stem unico per evitare il doppio conteggio raw/stem
        // ──────────────────────────────────────────────────────────────────────
        private static (Regex phrasesRx, Dictionary<string, int> singleWeights, HashSet<string> tokenUniverse)[] BuildTopicPatterns()
        {
            var arr = new (Regex, Dictionary<string, int>, HashSet<string>)[TOPIC_LEN];

            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                var t = (Topic)ti;

                if (!Dict.TryGetValue(t, out var tuple))
                {
                    arr[ti] = (null, new Dictionary<string, int>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
                    continue;
                }

                // Multi (phrase regex)
                Regex phr = null;
                var multi = tuple.multi?.Length > 0 ? tuple.multi : Array.Empty<string>();

                if (multi.Length > 0)
                {
                    var sb = new StringBuilder(128);
                    sb.Append(@"(?<![\p{L}\p{N}])(?:");
                    int appended = 0;

                    for (int i = 0; i < multi.Length; i++)
                    {
                        var m = multi[i];
                        if (string.IsNullOrWhiteSpace(m)) continue;

                        if (appended++ > 0) sb.Append('|');

                        // normalize to lowercase + strip accents similarly to Normalize()
                        var nm = Normalize(m);
                        sb.Append(Regex.Escape(nm));
                    }

                    sb.Append(")(?![\\p{L}\\p{N}])");
                    phr = appended > 0 ? new Regex(sb.ToString(), RXOPT) : null;
                }

                // Singles: un solo stem pesato; raw e stem restano disponibili solo per diagnostica
                var singles = new Dictionary<string, int>(StringComparer.Ordinal);
                var universe = new HashSet<string>(StringComparer.Ordinal);

                foreach (var raw in tuple.single ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    var nm = Normalize(raw);
                    if (string.IsNullOrEmpty(nm)) continue;

                    // nm could be multiword, split by space into tokens
                    var parts = nm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int p = 0; p < parts.Length; p++)
                    {
                        var tok = parts[p];
                        if (tok.Length == 0) continue;

                        var st = StemTokenCached(tok);
                        int weight = IsGenericKeyword(tok)
                            ? SINGLE_GENERIC_WEIGHT
                            : SINGLE_SPECIFIC_WEIGHT;

                        // Il token viene valutato una sola volta, tramite stem. Il raw token
                        // resta nel vocabolario diagnostico ma non produce un doppio punteggio.
                        AddW(st, weight);

                        universe.Add(tok);
                        universe.Add(st);
                    }
                }

                // Also add RequireAny and Anti tokens into universe for explain
                if (RequireAny.TryGetValue(t, out var reqL))
                {
                    foreach (var r in reqL)
                    {
                        var nm = Normalize(r);
                        if (string.IsNullOrEmpty(nm)) continue;
                        foreach (var tok in nm.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            universe.Add(tok);
                            universe.Add(StemTokenCached(tok));
                        }
                    }
                }

                if (Anti.TryGetValue(t, out var antiL))
                {
                    foreach (var a in antiL)
                    {
                        var nm = Normalize(a);
                        if (string.IsNullOrEmpty(nm)) continue;
                        foreach (var tok in nm.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            universe.Add(tok);
                            universe.Add(StemTokenCached(tok));
                        }
                    }
                }

                arr[ti] = (phr, singles, universe);
                continue;

                void AddW(string key, int inc)
                {
                    if (string.IsNullOrEmpty(key)) return;
                    if (singles.TryGetValue(key, out var w))
                        singles[key] = Math.Max(w, inc);
                    else
                        singles[key] = inc;
                }
            }

            return arr;
        }

        private static (HashSet<string> req, HashSet<string> anti)[] BuildReqAnti()
        {
            var arr = new (HashSet<string> req, HashSet<string> anti)[TOPIC_LEN];

            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                var t = (Topic)ti;
                HashSet<string> req = null, anti = null;

                if (RequireAny.TryGetValue(t, out var rl))
                {
                    req = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var r in rl)
                    {
                        if (string.IsNullOrWhiteSpace(r)) continue;
                        var nm = Normalize(r);
                        foreach (var tok in nm.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            req.Add(tok);
                            req.Add(StemTokenCached(tok));
                        }
                    }
                }

                if (Anti.TryGetValue(t, out var al))
                {
                    anti = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var a in al)
                    {
                        if (string.IsNullOrWhiteSpace(a)) continue;
                        var nm = Normalize(a);
                        foreach (var tok in nm.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            anti.Add(tok);
                            anti.Add(StemTokenCached(tok));
                        }
                    }
                }

                arr[ti] = (req, anti);
            }

            return arr;
        }

        private static int[] BuildTopic2CatArray()
        {
            var arr = new int[TOPIC_LEN];
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                var t = (Topic)ti;
                if (!Topic2Cat.TryGetValue(t, out var cat))
                    cat = PrimaryTopic.ALTRO;
                arr[ti] = (int)cat;
            }
            return arr;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Data dictionary (espanso, IT+EN, ridotta sovrapposizione)
        // Nota: aggiunte varianti “piene” per coprire ticket low-conf (iscrizione/permesso/mensa/iban)
        // ──────────────────────────────────────────────────────────────────────
        private static Dictionary<Topic, (string[] multi, string[] single)> BuildDict()
        {
            return new()
            {
                // ───────── PAGAMENTI/TASSE ─────────
                [Topic.PAGAMENTI] = (
                    new[]
                    {
                        "pagamento borsa di studio",
                        "accredito borsa di studio",
                        "erogazione borsa di studio",
                        "liquidazione borsa di studio",
                        "mancato pagamento della borsa",
                        "pagamento non ricevuto",
                        "accredito non ricevuto",
                        "quando arriva il pagamento",
                        "quando viene pagata la borsa",
                        "mandato di pagamento",
                        "bonifico della borsa",
                        "pagamento prima rata",
                        "pagamento seconda rata",
                        "prima rata non ricevuta",
                        "mancato accredito prima rata",
                        "saldo non ricevuto",
                        "mancato accredito del saldo",
                        "pagamento borsa già effettuato",
                        "pagamento della borsa già effettuato",

                        "scholarship payment",
                        "scholarship not paid",
                        "payment not received",
                        "when will the scholarship be paid",
                        "scholarship bank transfer",
                        "first installment payment",
                        "second installment payment"
                    },
                    new[]
                    {
                        "pagamento","pagamenti","pagato","pagata","pagat",
                        "accredito","accreditato","accreditata","accredit",
                        "erogazione","erogato","erogata","erog",
                        "liquidazione","liquidato","liquidata","liquid",
                        "mandato","bonifico","trasferimento",

                        "payment","paid","credited","disbursement",
                        "transfer"
                    }
                ),

                [Topic.TASSE] = (
                    new[]
                    {
                        "tassa regionale",
                        "imposta regionale per il diritto allo studio",
                        "mancato pagamento della tassa regionale",
                        "scadenza tassa regionale",
                        "avviso pagopa",
                        "quietanza pagopa",
                        "codice iuv",
                        "codice avviso pagopa",
                        "pagamento tassa regionale non registrato",
                        "tassa universitaria",
                        "tassa di iscrizione",
                        "tassa di immatricolazione",
                        "ricevuta tassa regionale",
                        "bollettino tassa regionale",
                        "mav tassa regionale",

                        "regional tax",
                        "payment of regional tax",
                        "unpaid regional tax",
                        "unpaid tuition fee",
                        "tuition fee payment",
                        "university tax payment"
                    },
                    new[]
                    {
                        "tassa","tasse",
                        "regionale","regionali",
                        "universitaria","universitarie",
                        "imposta","pagopa","iuv",
                        "quietanza","avviso","ricevuta",
                        "scadenza","scadenz","mav","bollettino",
                        "versamento","pagamento","pagamenti",

                        "tax","regional",
                        "tuition","fee",
                        "unpaid","payment","paid",
                        "receipt","invoice"
                    }
                ),

                [Topic.RIMBORSO_TASSA] = (
                    new[]
                    {
                        "richiesta rimborso tassa regionale",
                        "rimborso tassa regionale",
                        "rimborso tassa universitaria",
                        "rimborso tassa pagata erroneamente",
                        "rimborso tassa pagata due volte",
                        "rimborso per versamento errato",
                        "rimborso per versamento in eccesso",
                        "rimborso tassa non dovuta",

                        "regional tax refund",
                        "tuition fee refund",
                        "refund for wrong payment",
                        "refund for double payment",
                        "refund for overpayment"
                    },
                    new[]
                    {
                        "rimborso","rimbors",
                        "restituzione","restituire",
                        "tassa","tasse",
                        "errore","errato","erroneamente",
                        "doppio","duplicato","overpayment",
                        "refund","repayment","repay"
                    }
                ),

                [Topic.DEBITORIA] = (
                    new[]
                    {
                        "posizione debitoria",
                        "posizione debitoria aperta",
                        "ingiunzione di pagamento",
                        "sollecito di pagamento",
                        "rateizzazione del debito",
                        "cartella esattoriale",
                        "piano di rientro del debito",

                        "debt position",
                        "outstanding debt",
                        "debt payment plan",
                        "payment reminder",
                        "collection notice"
                    },
                    new[]
                    {
                        "debito","debitoria",
                        "pendenza","ingiunzione",
                        "sollecito","rateizzazione",
                        "cartella","rientro",

                        "debt","outstanding",
                        "arrears","collection",
                        "installment","reminder","overdue"
                    }
                ),

                // ───────── ISCRIZIONE/CARRIERA ─────────
                [Topic.ISCRIZIONE] = (
                    new[]
                    {
                        "verifica iscrizione",
                        "certificato di iscrizione",
                        "prima immatricolazione",
                        "iscrizione non perfezionata",
                        "conferma iscrizione",
                        "regolarizzazione iscrizione",

                        "enrollment certificate",
                        "verify enrollment",
                        "enrollment status",
                        "not enrolled"
                    },
                    new[]
                    {
                        // forme piene + radici
                        "iscrizione","iscrizioni","iscritto","iscritta","iscritti","iscritte",
                        "iscrizion","iscritt",
                        "immatricolazione","immatricolato","immatricolata",
                        "immatricol","matricola","matricol",
                        "certificato","certificati","certificat",
                        "regolarizzazione","regolarizzare","regolarizz",
                        "perfezionata","perfezionare","perfezion",
                        "fuori corso","fuoricorso",

                        "enrollment","enrolment","enrolled",
                        "registration","register","certificate","status"
                    }
                ),

                [Topic.CARRIERA] = (
                    new[]
                    {
                        "carriera pregressa",
                        "ricostruzione carriera",
                        "estratto carriera",
                        "piano di studi",
                        "rinuncia agli studi",
                        "decadenza dalla carriera",
                        "fuori corso",

                        "academic record",
                        "student career",
                        "previous career",
                        "withdrawal from studies",
                        "study plan"
                    },
                    new[]
                    {
                        "carriera","carrier","pregressa","pregress",
                        "estratto","transcript",
                        "piano","studi","ordinamento","ordinament",
                        "rinuncia","rinunc","decadenza","decadenz",
                        "fuori corso","fuoricorso",
                        "sospensione","ripresa","chiusura",

                        "career","record","withdrawal","dropout","plan","curriculum"
                    }
                ),

                [Topic.CREDITI] = (
                    new[]
                    {
                        "crediti insufficienti",
                        "incongruenza crediti",
                        "cfu mancanti",
                        "crediti mancanti",
                        "riconoscimento cfu",

                        "missing credits",
                        "credit recognition",
                        "credit mismatch",
                        "insufficient credits"
                    },
                    new[]
                    {
                        "crediti","crediti mancanti","cfu",
                        "credit","credits","ects",
                        "riconoscimento","riconosc","convalida","convalid",
                        "incongruenza","incongru","mismatch","discrepancy",
                        "insufficienti","insufficient","requirement"
                    }
                ),

                [Topic.TIROCINIO] = (
                    new[]
                    {
                        "attestazione tirocinio",
                        "documentazione tirocinio",
                        "tirocinio curriculare",
                        "tirocinio extracurriculare",

                        "internship certificate",
                        "internship documentation",
                        "mandatory internship"
                    },
                    new[]
                    {
                        "tirocinio","tirocin","stage",
                        "internship","placement","traineeship",
                        "attestazione","attestat","certificate","training"
                    }
                ),

                [Topic.PASSAGGIO_TRASF] = (
                    new[]
                    {
                        "trasferimento di ateneo",
                        "cambio corso",
                        "passaggio di corso",

                        "transfer to another university",
                        "change of degree course"
                    },
                    new[]
                    {
                        "passaggio","passagg",
                        "trasferimento","trasfer","transfer",
                        "cambio","change","switch",
                        "universita","university",
                        "corso","facolta","institution","program"
                    }
                ),

                [Topic.DOV_CIMEA] = (
                    new[]
                    {
                        "dichiarazione di valore",
                        "dov cimea",
                        "statement of comparability",
                        "riconoscimento titolo estero",
                        "diploma supplement",
                        "certificato di comparabilità",
                        "apostilla sul titolo estero",

                        "declaration of value",
                        "certificate of comparability",
                        "recognition of foreign degree"
                    },
                    new[]
                    {
                        "dov","cimea",
                        "comparabilita","comparabil","equivalenza","equivalen",
                        "titolo estero","ester","foreign","degree","qualification",
                        "diploma","supplement",
                        "apostilla","apostill","legalizzazione","legalizz"
                    }
                ),

                // ───────── BENEFICI/IMPORTI ─────────
                [Topic.IMPORTI] = (
                    new[]
                    {
                        "importo assegnato",
                        "importo della borsa di studio",
                        "importo massimo spettante",
                        "prima rata",
                        "seconda rata",
                        "accredito della borsa",
                        "non ho ricevuto l'importo della borsa",

                        "scholarship amount",
                        "amount of the scholarship",
                        "first installment",
                        "second installment"
                    },
                    new[]
                    {
                        "importo","importi","amount",
                        "borsa","borse","scholarship",
                        "beneficio","benefici","grant",
                        "contributo","contributi",
                        "accredito","accrediti","disbursed","awarded",
                        "rata","rate","installment",
                        "pagamento","pagamenti","payment",
                        "spettante","ammontare","differenza","differenz"
                    }
                ),

                [Topic.SALDO] = (
                    new[]
                    {
                        "saldo borsa di studio",
                        "erogazione del saldo",
                        "liquidazione del saldo",
                        "accredito del saldo",
                        "mancato accredito del saldo",

                        "payment of the balance",
                        "scholarship balance"
                    },
                    new[]
                    {
                        "saldo","balance",
                        "liquidazione","liquid",
                        "accredito","accredit",
                        "seconda rata","rata","final","remaining","residuo"
                    }
                ),

                [Topic.SALDO_IMPORTO_ERRATO] = (
                    new[]
                    {
                        "saldo non corretto",
                        "importo del saldo errato",
                        "saldo inferiore al previsto",
                        "errore nel calcolo del saldo",

                        "wrong balance amount",
                        "incorrect balance amount"
                    },
                    new[]
                    {
                        "errore","errato","errat","sbagliato","sbagliat",
                        "manca","mancan","meno","difference","differenz",
                        "previsto","previst","mismatch","wrong","incorrect"
                    }
                ),

                [Topic.RINUNCIA_REVOCA] = (
                    new[]
                    {
                        "rinuncia ai benefici",
                        "rinuncio alla borsa di studio",
                        "revoca della borsa",
                        "restituzione della borsa",
                        "rateizzare la restituzione",

                        "withdrawal from the scholarship",
                        "revocation of scholarship",
                        "repayment of scholarship"
                    },
                    new[]
                    {
                        "rinuncia","rinunc",
                        "revoca","revoc","revocation","revoke",
                        "restituzione","restituz","repayment","repay","return",
                        "rateizzare","rateizz","cancel"
                    }
                ),

                // ───────── ALLOGGIO ─────────
                [Topic.ALLOGGIO] = (
                    new[]
                    {
                        "posto alloggio",
                        "posto letto",
                        "residenza universitaria",
                        "casa dello studente",
                        "assegnazione alloggio",
                        "check in residenza",
                        "check out residenza",

                        "student residence",
                        "student housing",
                        "housing assignment",
                        "dormitory place"
                    },
                    new[]
                    {
                        "alloggio","allogg",
                        "residenza","residenz","residence",
                        "posto","letto","bed","room","camera","stanza",
                        "studentato","dorm","dormitory",
                        "housing","accommodation","assignment"
                    }
                ),

                [Topic.CONTRATTO] = (
                    new[]
                    {
                        "contratto di locazione",
                        "contratto di affitto",
                        "caricare il contratto di locazione",
                        "contratto di locazione scaduto",
                        "contratto di locazione non registrato",
                        "proroga del contratto di locazione",
                        "agenzia delle entrate",
                        "risoluzione anticipata del contratto",

                        "rental contract",
                        "lease agreement",
                        "extension of the lease",
                        "registration of the contract"
                    },
                    new[]
                    {
                        "contratto","contratti","contract",
                        "locazione","affitto","rent","lease","tenancy","agreement",
                        "proroga","extension",
                        "registrazione","register","registration",
                        "agenzia","entrate","domicilio",
                        "scaduto","scaduta","rifiutato","rifiutata",
                        "risoluzione","termination","chiusura"
                    }
                ),

                // ───────── DOCUMENTI/PERMESSI ─────────
                [Topic.PERMESSO] = (
                    new[]
                    {
                        "permesso di soggiorno",
                        "permesso di soggiorno scaduto",
                        "permesso di soggiorno in rinnovo",
                        "documenti permesso di soggiorno",
                        "allegati permesso di soggiorno",
                        "caricamento permesso di soggiorno",
                        "documenti del permesso caricati",
                        "documenti del permesso lavorati",
                        "documenti permesso inseriti",
                        "documenti permesso lavorati",
                        "permesso di soggiorno inserito",
                        "permesso di soggiorno lavorato",
                        "ricevuta della questura",
                        "impronte digitali",
                        "appuntamento in questura",
                        "kit postale",

                        "residence permit",
                        "residence permit renewal",
                        "expired residence permit",
                        "fingerprints appointment"
                    },
                    new[]
                    {
                        "permesso","permessi","permess",
                        "soggiorno","soggiorn",
                        "questura","questur",
                        "impronte","impront","fingerprints",
                        "rinnovo","renewal","expired","scaduto","scaduta",
                        "ricevuta","receipt",
                        "kit","postale","postal"
                    }
                ),

                [Topic.ISEE_REDDITI] = (
                    new[]
                    {
                        "isee universitario",
                        "isee parificato",
                        "integrazione isee",
                        "integrazione dsu",
                        "isee scaduto",
                        "isee corrente",
                        "redditi esteri",
                        "attestazione isee",
                        "iseeup",
                        "ispeup",

                        "expired isee",
                        "current isee",
                        "economic documentation for scholarship"
                    },
                    new[]
                    {
                        "isee","dsu",
                        "iseeup","ispeup",
                        "parificato","parificata","parificat",
                        "redditi","reddit","income",
                        "attestazione","attestaz",
                        "scaduto","scaduta","expired","current",
                        "documentazione","economic","indicator"
                    }
                ),

                [Topic.PEC_EMAIL] = (
                    new[]
                    {
                        "indirizzo pec",
                        "posta elettronica certificata",
                        "invio documenti tramite pec",
                        "non possiedo una pec",
                        "email non valida",

                        "certified email",
                        "pec address",
                        "invalid email address"
                    },
                    new[]
                    {
                        "pec","posta","certificata","certified",
                        "email","e-mail","mail","indirizzo","address",
                        "casella","mailbox",
                        "allegato","allegati","invalid","update","istituzionale"
                    }
                ),

                [Topic.INDIPENDENTE] = (
                    new[]
                    {
                        "studente indipendente",
                        "indipendente irregolare",
                        "condizione indipendente non soddisfatta",

                        "independent student",
                        "independent status not satisfied"
                    },
                    new[]
                    {
                        "indipendente","indipendent",
                        "independent","independ",
                        "irregolare","irregular"
                    }
                ),

                [Topic.CODICE_FISCALE] = (
                    new[]
                    {
                        "correzione codice fiscale",
                        "codice fiscale errato",
                        "codice fiscale non valido",
                        "omocodia",
                        "errore codice fiscale",

                        "invalid tax code",
                        "wrong tax code",
                        "tax code correction"
                    },
                    new[]
                    {
                        "codice","fiscale","cf",
                        "taxcode","fiscalcode",
                        "omocodia","omocod",
                        "errore","errato","wrong","invalid","correction"
                    }
                ),

                [Topic.CAF] = (
                    new[]
                    {
                        "ambasciata italiana",
                        "ambasciata in iran",
                        "legalizzazione dei documenti in ambasciata",
                        "apostilla all'ambasciata",

                        "italian embassy",
                        "embassy in iran",
                        "legalization of documents at the embassy",
                        "apostille at the embassy"
                    },
                    new[]
                    {
                        "iran","ambasciata","embassy",
                        "legalizzazione","legalizz","legalization",
                        "apostilla","apostill",
                        "prefettura","prefettur",
                        "timbro","stamp"
                    }
                ),

                // ───────── IBAN ─────────
                [Topic.IBAN] = (
                    new[]
                    {
                        "iban estero",
                        "iban non italiano",
                        "iban rifiutato",
                        "iban non accettato",
                        "iban non valido",
                        "iban revolut",
                        "iban wise",
                        "iban non sepa",

                        "foreign iban",
                        "international iban",
                        "iban rejected",
                        "iban not accepted",
                        "iban not valid"
                    },
                    new[]
                    {
                        "iban","swift","bic",
                        "conto","corrente","banca","bank","account",
                        "intestatario","intestata","intestat",
                        "sepa","revolut","wise",
                        "estero","straniero","foreign","international",
                        "rifiutato","rejected","invalid","unsupported","accettato"
                    }
                ),

                // ───────── PORTALE ─────────
                [Topic.PORTALE] = (
                    new[]
                    {
                        "pagina bianca",
                        "schermata bianca",
                        "problema tecnico sul portale",
                        "sezione non disponibile",
                        "non consente di caricare i documenti",
                        "request entity too large",
                        "errore 500",
                        "errore 502",
                        "errore 504",
                        "sessione scaduta",
                        "impossibile effettuare il login",
                        "non riesco ad accedere all'area riservata",
                        "pagina che si carica all'infinito",

                        "blank page",
                        "white screen",
                        "technical issue on the portal",
                        "section not available",
                        "cannot upload documents",
                        "session expired",
                        "unable to login"
                    },
                    new[]
                    {
                        "portale","portal","sito","website",
                        "login","accesso","spid",
                        "timeout","errore","error","server",
                        "upload","caricare","caricamento","caric",
                        "pagina","schermata","white","blank",
                        "500","502","504","gateway",
                        "sessione","session","scaduta","expired",
                        "area","riservata","personale","profilo",
                        "documenti","documentazione","allegato","allegati"
                    }
                ),

                // ───────── GRADUATORIE / PREMI ─────────
                [Topic.GRADUATORIA] = (
                    new[]
                    {
                        "graduatoria definitiva",
                        "graduatoria provvisoria",
                        "scorrimento graduatoria",
                        "posizione in graduatoria",
                        "esito della graduatoria",
                        "esclusione dalla graduatoria",
                        "pubblicazione del bando",
                        "scadenza bando",

                        "final ranking list",
                        "provisional ranking list",
                        "ranking position",
                        "exclusion from ranking",
                        "deadline of the call"
                    },
                    new[]
                    {
                        "graduatoria","graduator",
                        "idoneo","idonea","vincitore","vincitrice",
                        "scorrimento","scorriment",
                        "posizione","position",
                        "bando","call","deadline",
                        "escluso","excluded","esito","result"
                    }
                ),

                [Topic.PREMIO_LAUREA] = (
                    new[]
                    {
                        "premio di laurea",
                        "premio laurea",
                        "premio di laurea per merito",

                        "graduation prize",
                        "graduation award",
                        "degree award"
                    },
                    new[]
                    {
                        "premio","premi","prize","award",
                        "laurea","graduation","degree",
                        "merito","merit"
                    }
                ),

                // ───────── MOBILITÀ ─────────
                [Topic.MOBILITA_ERASMUS] = (
                    new[]
                    {
                        "contributo mobilità erasmus",
                        "mobilità internazionale",
                        "learning agreement",
                        "arrival certificate",
                        "acceptance letter",

                        "erasmus mobility grant",
                        "study abroad grant"
                    },
                    new[]
                    {
                        "erasmus","erasm",
                        "mobilita","mobilit","abroad",
                        "grant","contributo",
                        "learning","agreement",
                        "arrival","acceptance","certificate","letter",
                        "plus"
                    }
                ),

                // ───────── MENSA ─────────
                [Topic.MENSA] = (
                    new[]
                    {
                        "servizio mensa",
                        "monetizzazione del servizio mensa",
                        "rimborso servizio mensa non usufruito",
                        "deduzione mensa 600",
                        "buoni pasto mensa",
                        "tessera mensa",
                        "ricarica mensa",

                        "canteen service",
                        "meal vouchers",
                        "meal card",
                        "top up canteen card"
                    },
                    new[]
                    {
                        "mensa","canteen","dining",
                        "monetizzazione","monetizz",
                        "pasto","pasti","meal",
                        "buono","buoni","voucher",
                        "tessera","card",
                        "ricarica","topup","600",
                        "servizio"
                    }
                ),

                // ───────── BLOCCO ─────────
                [Topic.BLOCCHI] = (
                    new[]
                    {
                        "blocco pagamenti",
                        "domanda bloccata",
                        "rimuovere il blocco",
                        "richiesta rimozione blocco",
                        "blocco ancora presente",
                        "blocco non rimosso",
                        "sblocco della domanda",
                        "sblocco domanda",
                        "sbloccare la domanda",
                        "sblocco pratica",
                        "sbloccare la pratica",
                        "pratica sbloccata",
                        "incongruenza tra documenti",
                        "indipendente irregolare",
                        "posizione debitoria in sospeso",

                        "payment block",
                        "blocked application",
                        "remove the block"
                    },
                    new[]
                    {
                        "blocco","blocchi","blocc","sblocco","sblocc",
                        "incongruenza","incongru",
                        "irregolare","irregular",
                        "unpaid","block","blocked","blocks"
                    }
                )
            };
        }
    }
}
