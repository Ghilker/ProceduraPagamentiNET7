using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
        TASSE, RIMBORSO_TASSA, DEBITORIA,

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
        public int MarginTop1Top2 { get; set; }            // bestTopicScore - secondBestTopicScore
        public bool IsLowConfidence { get; set; }
        public bool IsGenericInfoRequest { get; set; }
        public bool SecondaryCutoffApplied { get; set; }

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
        private const int PHRASE_WEIGHT = 3;   // frasi più pesanti
        private const int WORD_WEIGHT = 1;
        private const int BOOST_STRONG = 6;
        private const int BOOST_MED = 3;

        private const int MIN_PRIMARY_SCORE = 4;
        private const int MIN_SUB_SCORE = 1;

        private const int PENALTY_ANTI = 3;
        private const int BOOST_REQUIRE = 2;

        // Reliability gating
        private const int MIN_TOTAL_SCORE_FOR_CONF = 3;
        private const double LOW_CONF_PRIMARY = 0.25;

        // Secondary cutoff (2° e 3°)
        private const int MIN_T2_ABS = 3;
        private const double T2_REL_TO_T1 = 0.45;
        private const int MIN_T3_ABS = 2;
        private const double T3_REL_TO_T2 = 0.80;

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
            Topic.TASSE, Topic.DEBITORIA, Topic.RIMBORSO_TASSA,
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

            int bestTopicScore = 0;
            int secondTopicScore = 0;

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
                        if (headSet.Contains(key)) score += w * WORD_WEIGHT;
                        if (tailSet.Contains(key)) score += w * WORD_WEIGHT;
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

                // 4) precedence boost
                if (score > 0 && topic != Topic.BLOCCHI)
                {
                    int rank = Array.IndexOf(TOPIC_PRECEDENCE, topic);
                    if (rank >= 0)
                    {
                        if (rank <= 4) score += 3;
                        else if (rank <= 12) score += 2;
                        else if (rank <= 20) score += 1;
                    }
                }

                counts[ti] = score;

                if (score > bestTopicScore)
                {
                    secondTopicScore = bestTopicScore;
                    bestTopicScore = score;
                }
                else if (score > secondTopicScore)
                {
                    secondTopicScore = score;
                }
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

            // Tasse / rimborso / importi
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

            // ──────────────────────────────────────────────────────────────────
            // Risoluzione conflitti mirati (riduce falsi positivi)
            // ──────────────────────────────────────────────────────────────────
            Suppress(counts, Topic.IBAN, Topic.TASSE);
            Suppress(counts, Topic.IBAN, Topic.RIMBORSO_TASSA);
            Suppress(counts, Topic.IBAN, Topic.IMPORTI);
            Suppress(counts, Topic.IBAN, Topic.SALDO);

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

            // ──────────────────────────────────────────────────────────────────
            // Total score + margin
            // ──────────────────────────────────────────────────────────────────
            int totalScore = 0;
            int best = 0, second = 0;
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                if ((Topic)ti == Topic.BLOCCHI) continue; // non “inquina” il totale semantico
                int sc = counts[ti];
                totalScore += sc;

                if (sc > best) { second = best; best = sc; }
                else if (sc > second) { second = sc; }
            }
            ex.TotalScore = totalScore;
            ex.MarginTop1Top2 = Math.Max(0, best - second);

            // ──────────────────────────────────────────────────────────────────
            // Aggregazione per categoria primaria
            // ──────────────────────────────────────────────────────────────────
            var catScores = new int[Enum.GetValues<PrimaryTopic>().Length];
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                if ((Topic)ti == Topic.BLOCCHI) continue;
                int cat = Topic2CatArray[ti];
                catScores[cat] += counts[ti];
            }

            // Ordine categorie per greedy
            var catOrder = new List<int>();
            for (int ci = 0; ci < catScores.Length; ci++)
                if (catScores[ci] > 0) catOrder.Add(ci);

            catOrder.Sort((a, b) =>
            {
                int sa = catScores[a];
                int sb = catScores[b];
                if (sa != sb) return sb.CompareTo(sa);
                int pa = CatPrecedenceRank((PrimaryTopic)a);
                int pb = CatPrecedenceRank((PrimaryTopic)b);
                return pa.CompareTo(pb);
            });

            // ──────────────────────────────────────────────────────────────────
            // Selezione greedy topic secondari (max 3)
            // ──────────────────────────────────────────────────────────────────
            var selectedTopics = new List<Topic>(3);
            var used = new bool[TOPIC_LEN];

            // Pass 1: un topic per categoria
            foreach (var catIdx in catOrder)
            {
                if (selectedTopics.Count >= 3)
                    break;

                Topic bestTopic = Topic.BLOCCHI;
                int bestScore = 0;
                int bestPrec = int.MaxValue;

                for (int ti = 0; ti < TOPIC_LEN; ti++)
                {
                    if (used[ti]) continue;
                    var t = (Topic)ti;
                    if (t == Topic.BLOCCHI) continue;
                    if (Topic2CatArray[ti] != catIdx) continue;

                    int sc = counts[ti];
                    int minScore = MinTopicScore.TryGetValue(t, out var ms) ? ms : MIN_SUB_SCORE;
                    if (sc < minScore) continue;

                    int prec = Array.IndexOf(TOPIC_PRECEDENCE, t);
                    if (prec < 0) prec = int.MaxValue;

                    if (sc > bestScore || (sc == bestScore && prec < bestPrec))
                    {
                        bestTopic = t;
                        bestScore = sc;
                        bestPrec = prec;
                    }
                }

                if (bestScore > 0 && bestTopic != Topic.BLOCCHI)
                {
                    selectedTopics.Add(bestTopic);
                    used[(int)bestTopic] = true;
                }
            }

            // Pass 2: riempi con migliori rimasti
            if (selectedTopics.Count < 3)
            {
                var remaining = new List<(Topic topic, int sc, int prec)>();
                for (int ti = 0; ti < TOPIC_LEN; ti++)
                {
                    if (used[ti]) continue;
                    var t = (Topic)ti;
                    if (t == Topic.BLOCCHI) continue;

                    int sc = counts[ti];
                    int minScore = MinTopicScore.TryGetValue(t, out var ms) ? ms : MIN_SUB_SCORE;
                    if (sc < minScore) continue;

                    int prec = Array.IndexOf(TOPIC_PRECEDENCE, t);
                    if (prec < 0) prec = int.MaxValue;

                    remaining.Add((t, sc, prec));
                }

                remaining.Sort((a, b) =>
                {
                    if (a.sc != b.sc) return b.sc.CompareTo(a.sc);
                    return a.prec.CompareTo(b.prec);
                });

                foreach (var item in remaining)
                {
                    if (selectedTopics.Count >= 3) break;
                    selectedTopics.Add(item.topic);
                    used[(int)item.topic] = true;
                }
            }

            // ──────────────────────────────────────────────────────────────────
            // Cutoff secondari (2° e 3°) per eliminare rumore
            // ──────────────────────────────────────────────────────────────────
            ex.SecondaryCutoffApplied = false;

            if (selectedTopics.Count > 1)
            {
                int sc1 = counts[(int)selectedTopics[0]];
                int sc2 = counts[(int)selectedTopics[1]];

                int min2 = Math.Max(MIN_T2_ABS, (int)Math.Ceiling(sc1 * T2_REL_TO_T1));
                int min2Topic = MinTopicScore.TryGetValue(selectedTopics[1], out var ms2) ? ms2 : MIN_SUB_SCORE;
                min2 = Math.Max(min2, min2Topic);

                if (sc2 < min2)
                {
                    selectedTopics.RemoveRange(1, selectedTopics.Count - 1);
                    ex.SecondaryCutoffApplied = true;
                }
            }

            if (selectedTopics.Count > 2)
            {
                int sc2 = counts[(int)selectedTopics[1]];
                int sc3 = counts[(int)selectedTopics[2]];

                int min3 = Math.Max(MIN_T3_ABS, (int)Math.Ceiling(sc2 * T3_REL_TO_T2));
                int min3Topic = MinTopicScore.TryGetValue(selectedTopics[2], out var ms3) ? ms3 : MIN_SUB_SCORE;
                min3 = Math.Max(min3, min3Topic);

                if (sc3 < min3)
                {
                    selectedTopics.RemoveAt(2);
                    ex.SecondaryCutoffApplied = true;
                }
            }

            // TopicSecondary: T1 | T2 | T3
            if (selectedTopics.Count > 0)
            {
                var names = new List<string>(selectedTopics.Count);
                foreach (var t in selectedTopics) names.Add(t.ToString());
                ex.TopicSecondary = string.Join(" | ", names);
            }
            else
            {
                ex.TopicSecondary = "";
            }

            // ──────────────────────────────────────────────────────────────────
            // Selezione TopicPrimary (categoria migliore) + fallback “generic status”
            // ──────────────────────────────────────────────────────────────────
            int bestCat = -1;
            int bestCatScore = 0;

            for (int ci = 0; ci < catScores.Length; ci++)
            {
                int sc = catScores[ci];
                if (sc <= 0) continue;

                if (bestCat < 0 ||
                    sc > bestCatScore ||
                    (sc == bestCatScore &&
                     CatPrecedenceRank((PrimaryTopic)ci) < CatPrecedenceRank((PrimaryTopic)bestCat)))
                {
                    bestCat = ci;
                    bestCatScore = sc;
                }
            }

            // Preferisci la categoria del primo topic selezionato se presente e coerente
            if (selectedTopics.Count > 0)
            {
                int catFromTopTopic = Topic2CatArray[(int)selectedTopics[0]];
                int catScore = catScores[catFromTopTopic];

                if (catScore >= bestCatScore - 1) // tolleranza minima per “coerenza”
                {
                    bestCat = catFromTopTopic;
                    bestCatScore = catScore;
                }
            }

            // Fallback: richieste generiche “stato domanda / in attesa / documenti caricati”
            // Se non si supera MIN_PRIMARY_SCORE e il testo è generico, sposta su PORTALE_E_ACCESSO
            if (bestCatScore < MIN_PRIMARY_SCORE && ex.IsGenericInfoRequest)
            {
                bestCat = (int)PrimaryTopic.PORTALE_E_ACCESSO;
                bestCatScore = catScores[bestCat];
            }

            ex.TopicPrimary = bestCatScore >= MIN_PRIMARY_SCORE
                ? ((PrimaryTopic)bestCat).ToString()
                : "";

            ex.PrimaryScore = bestCatScore;
            ex.PrimaryConfidence = (totalScore > 0) ? (double)bestCatScore / totalScore : 0.0;

            ex.IsLowConfidence =
                totalScore < MIN_TOTAL_SCORE_FOR_CONF ||
                ex.PrimaryConfidence < LOW_CONF_PRIMARY;

            // Tertiary: blocchi
            ex.TopicTertiary = counts[(int)Topic.BLOCCHI] > 0 ? "SI" : "";

            // Copia score in dizionario Counts
            for (int ti = 0; ti < TOPIC_LEN; ti++)
                ex.Counts[(Topic)ti] = counts[ti];

            // ──────────────────────────────────────────────────────────────────
            // Explain: matched keywords (top)
            // ──────────────────────────────────────────────────────────────────
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

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────
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
        // Build patterns: include BOTH raw token and stem forms in singles (fix “iscrizione”/“permesso” gaps)
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

                // Singles: store raw token AND stem key
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

                        AddW(tok, 1);
                        AddW(st, 1);

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
                        singles[key] = w + inc;
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
                        "sblocco della domanda",
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
