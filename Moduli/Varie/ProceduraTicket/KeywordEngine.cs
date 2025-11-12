using System;
using System.Buffers;
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

        // Graduatorie
        GRADUATORIA,

        // Mobilità
        MOBILITA_ERASMUS,

        // Servizio Mensa
        MENSA,

        // Solo terziario
        BLOCCHI
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
        public Dictionary<Topic, int> Counts { get; } = new();
        public string TopicPrimary { get; set; } = "";
        public string TopicSecondary { get; set; } = "";
        public string TopicTertiary { get; set; } = "";

        public Lang DetectedLanguage { get; set; } = Lang.UNKNOWN;
        public string OriginalText { get; set; } = "";
        public string NormalizedText { get; set; } = "";

        public bool MentionsBlocks { get; set; }
        public bool MentionsOwnPosition { get; set; }
        public bool BlocksOnOwnPosition => MentionsBlocks && MentionsOwnPosition;

        // Anno Accademico
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
        private const int PHRASE_WEIGHT = 2;
        private const int WORD_WEIGHT = 1;
        private const int BOOST_STRONG = 6;
        private const int BOOST_MED = 3;

        private const int MIN_PRIMARY_SCORE = 4;
        private const int MIN_SUB_SCORE = 2;
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
            Topic.TASSE, Topic.DEBITORIA, Topic.RIMBORSO_TASSA,
            Topic.ISCRIZIONE, Topic.CARRIERA, Topic.CREDITI, Topic.TIROCINIO, Topic.PASSAGGIO_TRASF, Topic.DOV_CIMEA,
            Topic.SALDO, Topic.SALDO_IMPORTO_ERRATO, Topic.IMPORTI, Topic.RINUNCIA_REVOCA,
            Topic.ALLOGGIO, Topic.CONTRATTO,
            Topic.PERMESSO, Topic.ISEE_REDDITI, Topic.PEC_EMAIL, Topic.INDIPENDENTE,
            Topic.CODICE_FISCALE, Topic.IBAN,
            Topic.GRADUATORIA,
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
            [Topic.MOBILITA_ERASMUS] = PrimaryTopic.MOBILITA,

            [Topic.MENSA] = PrimaryTopic.MENSA,

            [Topic.BLOCCHI] = PrimaryTopic.ALTRO
        };

        private static readonly (string bad, string good)[] Canon = new[]
        {
            ("sogiorno", "soggiorno"),
            ("isee universita", "isee università"),
            ("resident permit", "residence permit"),
            ("qr code", "qrcode")
        };

        // Require/Anti keywords (rafforzano/limitano i topic)
        private static readonly Dictionary<Topic, string[]> RequireAny = new()
        {
            [Topic.TASSE] = new[] { "pagopa", "iuv", "tassa", "regionale", "avviso", "quietanza", "imposta", "unpaid", "scadenz" },
            [Topic.RIMBORSO_TASSA] = new[] { "rimbors", "refund", "tassa", "regional" },
            [Topic.IBAN] = new[] { "iban", "conto", "bancari", "bank", "account", "intestat", "sepa", "revolut" },
            [Topic.CONTRATTO] = new[] { "contratt", "locaz", "affitt", "prorog", "registraz", "agenzia", "protocol" },
            [Topic.MENSA] = new[] { "mensa", "monetizz", "pasto", "buon", "600" },
            [Topic.PORTALE] = new[] { "portale", "sito", "errore", "login", "accesso", "spid", "timeout" }
        };

        private static readonly Dictionary<Topic, string[]> Anti = new()
        {
            [Topic.TASSE] = new[] { "codice", "fiscale", "taxcode", "fiscalcode", "iban" },
            [Topic.PORTALE] = new[] { "permesso", "questur", "impront", "residence", "permit" },
            [Topic.IBAN] = new[] { "barcode", "qr", "qrcode", "pagopa", "iuv" },
            [Topic.SALDO_IMPORTO_ERRATO] = new[] { "tassa", "regionale" }, // evita confusione con tasse
            [Topic.IMPORTI] = new[] { "tassa", "regionale" }              // evita confusione con tasse
        };

        // Full dictionary
        private static readonly Dictionary<Topic, (string[] multi, string[] single)> Dict = BuildDict();

        // Hot-path precomputations
        private static readonly Topic[] AllTopics = (Topic[])Enum.GetValues(typeof(Topic));
        private static readonly int TOPIC_LEN = AllTopics.Length;
        private static readonly int[] Topic2CatArray;
        private static readonly (Regex phrasesRx, HashSet<string> singleStems)[] TopicPatterns;
        private static readonly (HashSet<string> req, HashSet<string> anti)[] TopicReqAnti;

        [GeneratedRegex(@"(?i)\b(?:a\.?\s*a\.?\.?)\s*(\d{2,4})\s*[/\-]?\s*(\d{2,4})\b", RegexOptions.CultureInvariant)]
        private static partial Regex RxAA_WithMarker_Gen();

        // AA: "24/25" o "2024/2025" o "24-25" o "2024-2025"
        [GeneratedRegex(@"(?i)\b(\d{2,4})\s*[/\-]\s*(\d{2,4})\b", RegexOptions.CultureInvariant)]
        private static partial Regex RxAA_Sep_Gen();

        // AA: concatenati "2425" o "20242025" o "202425" (4..8 cifre che rappresentano due anni consecutivi)
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

        // Stem cache
        private static readonly Dictionary<string, string> StemCache;

        static KeywordEngineV6()
        {
            StemCache = new Dictionary<string, string>();          // 1
            Topic2CatArray = BuildTopic2CatArray();                // 2
            TopicPatterns = BuildTopicPatterns();                  // 3 (usa StemCache)
            TopicReqAnti = BuildReqAnti();                         // 4 (usa StemCache)
        }

        // ──────────────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────────────
        public static ExtractionV6 Extract(string raw, Lang? preferred = null)
        {
            var ex = new ExtractionV6 { OriginalText = raw ?? "" };
            if (string.IsNullOrWhiteSpace(raw)) return ex;

            // Normalize
            var text = Normalize(raw);
            ex.NormalizedText = text;

            // Language
            ex.DetectedLanguage = preferred is { } p && p != Lang.UNKNOWN
                                  ? (p == Lang.MIXED ? DetectLang(text) : p)
                                  : DetectLang(text);

            // Tokenize once
            var rawTokens = Tokenize(text);
            var tokenSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in rawTokens) tokenSet.Add(t);
            foreach (var st in StemAll(rawTokens)) tokenSet.Add(st);

            // Init counts array
            var counts = ArrayPool<int>.Shared.Rent(TOPIC_LEN);
            Array.Clear(counts, 0, TOPIC_LEN);

            // Score per topic
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                var (phrRx, singles) = TopicPatterns[ti];
                int score = 0;

                if (phrRx is not null)
                {
                    var mc = phrRx.Matches(text);
                    if (mc.Count > 0) score += PHRASE_WEIGHT * mc.Count;
                }

                foreach (var s in singles)
                    if (tokenSet.Contains(s)) score += WORD_WEIGHT;

                var (req, anti) = TopicReqAnti[ti];

                if (req is not null && req.Count > 0)
                {
                    bool ok = false;
                    foreach (var r in req) { if (tokenSet.Contains(r)) { ok = true; break; } }
                    if (!ok) score = 0; else score += BOOST_REQUIRE;
                }

                if (anti is not null && anti.Count > 0)
                {
                    foreach (var a in anti)
                        if (tokenSet.Contains(a)) { score = Math.Max(0, score - PENALTY_ANTI); }
                }

                counts[ti] = score;
            }

            // Heuristics via token checks (invariata per compatibilità)
            ex.MentionsBlocks =
                HasAny(tokenSet, "blocco", "blocchi", "sblocco", "incongruenza", "incongruenze", "unpaid", "block", "debitoria", "indipendente", "tirocinio");

            ex.MentionsOwnPosition =
                HasAny(tokenSet, "io", "mio", "mia", "mie", "miei", "my", "profilo", "posizione", "eligible", "winner", "idoneo", "idonea", "vincitore", "vincitrice");

            if (ex.MentionsBlocks) counts[(int)Topic.BLOCCHI] += BOOST_STRONG;

            if (HasAll(tokenSet, "tassa", "regionale") || HasAll(tokenSet, "regional", "tax"))
                counts[(int)Topic.TASSE] += BOOST_MED;

            if (HasAll(tokenSet, "verifica", "iscrizione") || HasAll(tokenSet, "enrollment", "certificate"))
                counts[(int)Topic.ISCRIZIONE] += BOOST_MED;

            if (HasAny(tokenSet, "cfu", "crediti", "credits", "incongruenza", "incongruenze"))
                counts[(int)Topic.CREDITI] += BOOST_MED;

            if (HasAny(tokenSet, "codice", "fiscale", "taxcode", "fiscalcode"))
                counts[(int)Topic.CODICE_FISCALE] += BOOST_MED;

            if (tokenSet.Contains("iban") && (HasAny(tokenSet, "invalid", "revolut", "sepa", "macedonia", "estero", "non", "accettato", "accettata")))
                counts[(int)Topic.IBAN] += BOOST_MED;

            if (HasAny(tokenSet, "proroga", "protocollo") || HasAll(tokenSet, "agenzia", "entrate"))
                counts[(int)Topic.CONTRATTO] += WORD_WEIGHT;

            // Conflicts
            Suppress(counts, Topic.IBAN, Topic.TASSE);
            Suppress(counts, Topic.IBAN, Topic.RIMBORSO_TASSA);
            Suppress(counts, Topic.CONTRATTO, Topic.ALLOGGIO);
            Suppress(counts, Topic.PERMESSO, Topic.PORTALE);

            // Aggregate categories
            var catScores = new int[Enum.GetValues<PrimaryTopic>().Length];
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                if ((Topic)ti == Topic.BLOCCHI) continue;
                var cat = Topic2CatArray[ti];
                catScores[cat] += counts[ti];
            }

            // Choose primary with precedence tiebreak
            int bestCatIdx = -1, bestScore = 0;
            for (int ci = 0; ci < catScores.Length; ci++)
            {
                int sc = catScores[ci];
                if (sc >= MIN_PRIMARY_SCORE)
                {
                    if (bestCatIdx < 0 ||
                        sc > bestScore ||
                        (sc == bestScore && CatPrecedenceRank((PrimaryTopic)ci) < CatPrecedenceRank((PrimaryTopic)bestCatIdx)))
                    {
                        bestCatIdx = ci; bestScore = sc;
                    }
                }
            }

            if (bestCatIdx >= 0)
            {
                var primName = ((PrimaryTopic)bestCatIdx).ToString();
                var prim = primName;

                Span<(Topic t, int sc, int prec)> buf = stackalloc (Topic, int, int)[TOPIC_LEN];
                int n = 0;
                for (int ti = 0; ti < TOPIC_LEN; ti++)
                {
                    var t = (Topic)ti;
                    if (t == Topic.BLOCCHI) continue;
                    if (Topic2CatArray[ti] != bestCatIdx) continue;
                    int sc = counts[ti];
                    if (sc >= MIN_SUB_SCORE)
                        buf[n++] = (t, sc, Array.IndexOf(TOPIC_PRECEDENCE, t));
                }
                for (int i = 0; i < n; i++)
                {
                    int max = i;
                    for (int j = i + 1; j < n; j++)
                    {
                        if (buf[j].sc > buf[max].sc || (buf[j].sc == buf[max].sc && buf[j].prec < buf[max].prec))
                            max = j;
                    }
                    (buf[i], buf[max]) = (buf[max], buf[i]);
                }

                var names = new List<string>(3);
                for (int i = 0; i < n && i < 3; i++) names.Add(buf[i].t.ToString());

                ex.TopicPrimary = prim;
                ex.TopicSecondary = string.Join(" | ", names);
            }
            else
            {
                ex.TopicPrimary = "";
                ex.TopicSecondary = "";
            }

            ex.TopicTertiary = counts[(int)Topic.BLOCCHI] > 0 ? "SI" : "";

            for (int ti = 0; ti < TOPIC_LEN; ti++)
                ex.Counts[(Topic)ti] = counts[ti];

            ArrayPool<int>.Shared.Return(counts, clearArray: true);
            return ex;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────
        private static void Suppress(int[] c, Topic a, Topic b)
        {
            int ia = (int)a, ib = (int)b;
            if (c[ia] >= c[ib] + 3) c[ib] = 0;
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
                if (i > start) list.Add(s.AsSpan(start, i - start).ToString());
            }
            return list;
        }

        private static List<string> StemAll(List<string> toks)
        {
            var list = new List<string>(toks.Count);
            foreach (var t in toks)
            {
                if (!string.IsNullOrEmpty(t) && t.Length > 1) list.Add(StemTokenCached(t));
            }
            return list;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasAny(HashSet<string> set, params string[] keys)
        {
            foreach (var k in keys) if (set.Contains(k)) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasAll(HashSet<string> set, params string[] keys)
        {
            foreach (var k in keys) if (!set.Contains(k)) return false;
            return true;
        }

        private static (bool has, string raw, int s, int e, double conf) DetectAcademicYearFast(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return default;

            string stext = text;

            var m1 = RxAA_WithMarker.Match(stext);
            if (m1.Success && TryNormalizeYears(m1.Groups[1].Value, m1.Groups[2].Value, out var y1, out var y2))
            {
                if (y2 == y1 + 1) return (true, m1.Value, y1, y2, 0.98);
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
            if (bestSep.conf > 0) return (true, bestSep.raw, bestSep.s, bestSep.e, bestSep.conf);

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
                    if (TryNormalizeYears(m.Groups[3].Value, m.Groups[4].Value, out var a, out var b) && b == a + 1)
                        return (true, m.Value, a, b, 0.91);
                }

                if (m.Groups[5].Success && m.Groups[6].Success)
                {
                    if (TryNormalizeYears(m.Groups[5].Value, m.Groups[6].Value, out var a, out var b) && b == a + 1)
                        return (true, m.Value, a, b, 0.92);
                }
            }

            return default;

            static bool TryNormalizeYears(string y1s, string y2s, out int y1, out int y2)
            {
                y1 = y2 = 0;
                if (!int.TryParse(y1s, out var a) || !int.TryParse(y2s, out var b)) return false;

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

        private static string MakeAASticker(int? start, int? end, double conf, double minConf = 0.90)
        {
            if (start is int s && end is int e && conf >= minConf && e == s + 1)
            {
                int s2 = s % 100, e2 = e % 100;
                return $"AA{(s2 < 10 ? "0" : "")}{s2}{(e2 < 10 ? "0" : "")}{e2}";
            }
            return "";
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = text.ToLowerInvariant();

            text = text.Replace('’', '\'').Replace('‘', '\'')
                       .Replace('“', '"').Replace('”', '"')
                       .Replace('–', '-').Replace('—', '-');

            if (Canon != null && Canon.Length > 0)
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
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            text = sb.ToString().Normalize(NormalizationForm.FormC);

            text = RxSlash.Replace(text, " ");
            text = RxSpace.Replace(text, " ").Trim();
            return text;
        }

        private static Lang DetectLang(string norm)
        {
            if (string.IsNullOrEmpty(norm)) return Lang.UNKNOWN;

            string[] itHints = { " il ", " la ", " lo ", " gli ", " le ", " un ", " una ", " di ", " che ", " non ", " per ", " con ", " alla ", " della ", "tassa", "iscrizione", "credito", "graduatoria", "borsa" };
            string[] enHints = { " the ", " a ", " an ", " of ", " for ", " with ", " and ", " but ", " block", " blocks", " enrollment", " scholarship", " refund", " permit" };

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
                while ((idx = s.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0) { count++; idx += sub.Length; }
                return count;
            }
        }

        private static string StemTokenCached(string t)
        {
            if (string.IsNullOrEmpty(t)) return string.Empty;
            if (t.Length <= 4) return t;

            if (StemCache.TryGetValue(t, out var s)) return s;

            string[] suf = {
                "zioni","menti","mente","sione","sioni","ismi","ismo",
                "zione","zioni","tion","tions","ness","ingly",
                "ing","ed","es","ly","i","e","s"
            };
            foreach (var su in suf)
            {
                if (t.EndsWith(su, StringComparison.Ordinal) && t.Length > su.Length + 2)
                {
                    s = t[..^su.Length];
                    StemCache[t] = s;
                    return s;
                }
            }
            StemCache[t] = t;
            return t;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Precompute topic patterns
        // ──────────────────────────────────────────────────────────────────────
        private static (Regex phrasesRx, HashSet<string> singleStems)[] BuildTopicPatterns()
        {
            var arr = new (Regex, HashSet<string>)[TOPIC_LEN];
            for (int ti = 0; ti < TOPIC_LEN; ti++)
            {
                var t = (Topic)ti;
                if (!Dict.TryGetValue(t, out var tuple))
                {
                    arr[ti] = (null, new(StringComparer.Ordinal));
                    continue;
                }

                Regex phr = null;
                var multi = tuple.multi?.Length > 0
                    ? tuple.multi
                    : Array.Empty<string>();

                if (multi.Length > 0)
                {
                    var sb = new StringBuilder(64);
                    sb.Append(@"(?<![\p{L}\p{N}])(?:");
                    int appended = 0;
                    for (int i = 0; i < multi.Length; i++)
                    {
                        var m = multi[i];
                        if (string.IsNullOrWhiteSpace(m)) continue;
                        if (appended++ > 0) sb.Append('|');
                        sb.Append(Regex.Escape(m.ToLowerInvariant()));
                    }
                    sb.Append(")(?![\\p{L}\\p{N}])");
                    phr = appended > 0 ? new Regex(sb.ToString(), RXOPT) : null;
                }

                var singles = new HashSet<string>(StringComparer.Ordinal);
                foreach (var raw in tuple.single ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var low = raw.ToLowerInvariant();
                    var stem = StemTokenCached(low);
                    if (!string.IsNullOrEmpty(stem))
                        singles.Add(stem);
                }

                arr[ti] = (phr, singles);
            }
            return arr;
        }

        private static (HashSet<string> req, HashSet<string> anti)[] BuildReqAnti()
        {
            var arr = new (HashSet<string>, HashSet<string>)[TOPIC_LEN];
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
                        req.Add(StemTokenCached(r.ToLowerInvariant()));
                    }
                }
                if (Anti.TryGetValue(t, out var al))
                {
                    anti = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var a in al)
                    {
                        if (string.IsNullOrWhiteSpace(a)) continue;
                        anti.Add(StemTokenCached(a.ToLowerInvariant()));
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
                var cat = Topic2Cat.TryGetValue(t, out var c) ? c : PrimaryTopic.ALTRO;
                arr[ti] = (int)c;
            }
            return arr;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Data dictionary (ristrutturato, IT-focused, senza duplicati superflui)
        // ──────────────────────────────────────────────────────────────────────
        private static Dictionary<Topic, (string[] multi, string[] single)> BuildDict()
        {
            return new()
            {
                // ───────── PAGAMENTI/TASSE ─────────
                [Topic.TASSE] = (
                    new[] {
                        "tassa regionale",
                        "imposta regionale per il diritto allo studio",
                        "mancato pagamento della tassa regionale",
                        "scadenza tassa regionale",
                        "avviso pagopa",
                        "quietanza pagopa",
                        "codice iuv",
                        "codice avviso pagopa",
                        "pagamento non andato a buon fine",
                        "unpaid regional tax",
                        "pendenza tassa regionale"
                    },
                    new[] { "regional", "pagopa", "iuv", "tassa", "regionale", "quietanza", "avviso", "imposta", "scadenz" }
                ),

                [Topic.RIMBORSO_TASSA] = (
                    new[] {
                        "richiesta rimborso tassa regionale",
                        "rimborso tassa pagata erroneamente",
                        "rimborso tassa pagata due volte",
                        "rimborso per non iscritto",
                        "regional tax refund"
                    },
                    new[] { "rimbors", "restituz", "refund", "tassa", "regional" }
                ),

                [Topic.DEBITORIA] = (
                    new[] {
                        "posizione debitoria",
                        "ingiunzione di pagamento",
                        "sollecito di pagamento",
                        "rateizzazione del debito",
                        "pendenza economica"
                    },
                    new[] { "debitor", "debito", "pendenz", "ingiunz", "sollecit", "rateizz" }
                ),

                // ───────── ISCRIZIONE/CARRIERA ─────────
                [Topic.ISCRIZIONE] = (
                    new[] {
                        "verifica iscrizione",
                        "certificato di iscrizione",
                        "prima immatricolazione",
                        "iscrizione part time",
                        "iscrizione magistrale",
                        "iscrizione non perfezionata",
                        "enrollment certificate",
                        "verify enrollment"
                    },
                    new[] { "iscrizion", "immatricol", "matricol", "enroll", "fuoricors", "part", "perfezion", "attesa", "certificat" }
                ),

                [Topic.CARRIERA] = (
                    new[] {
                        "carriera pregressa",
                        "rinuncia agli studi",
                        "decadenza dagli studi",
                        "doppia carriera",
                        "riconoscimento carriera",
                        "piano di studi"
                    },
                    new[] { "pregress", "rinunc", "decadenz", "doppi", "riconosc", "piano", "studi" }
                ),

                [Topic.CREDITI] = (
                    new[] {
                        "crediti insufficienti",
                        "incongruenza crediti",
                        "riconoscimento cfu",
                        "idoneità inglese",
                        "abbreviazione di carriera",
                        "crediti bonus"
                    },
                    new[] { "credit", "cfu", "dpcm", "riconosc", "convalid", "incongru", "idone", "abbreviaz", "bonus" }
                ),

                [Topic.TIROCINIO] = (
                    new[] { "attestazione tirocinio", "documentazione tirocinio", "esame di tirocinio", "tirocinio crui" },
                    new[] { "tirocin", "stage", "attestaz", "internship", "certificato tirocinio" }
                ),

                [Topic.PASSAGGIO_TRASF] = (
                    new[] { "trasferimento di ateneo", "cambio corso", "passaggio di corso", "double degree" },
                    new[] { "passagg", "trasfer", "cambi", "double", "dual", "degree" }
                ),

                [Topic.DOV_CIMEA] = (
                    new[] { "dichiarazione di valore", "cimea statement of comparability", "riconoscimento titolo estero", "diploma supplement", "declaration of value" },
                    new[] { "dov", "cimea", "comparabil", "qualific", "titolo", "ester", "legalizz", "apostill" }
                ),

                // ───────── BENEFICI/IMPORTI ─────────
                [Topic.IMPORTI] = (
                    new[] { "importo assegnato", "differenza di importo", "prima rata", "bonifico ricevuto", "non ho ricevuto la prima rata", "non ho ricevuto la borsa di studio" },
                    new[] { "import", "erog", "rata", "bonific", "accredit", "inferior", "previst", "amount", "payment" }
                ),

                [Topic.SALDO] = (
                    new[] { "saldo borsa di studio", "erogazione del saldo", "liquidazione del saldo", "accredito del saldo", "quando riceverò il saldo", "seconda rata", "non ho ricevuto la seconda rata" },
                    new[] { "saldo", "liquid", "accredit", "second", "rata", "arriv" }
                ),

                [Topic.SALDO_IMPORTO_ERRATO] = (
                    new[] {
                        "saldo non corretto", "importo del saldo errato", "saldo più basso del previsto",
                        "mi avete pagato meno", "mancano euro", "non corrisponde", "importo saldo sbagliato"
                    },
                    new[] { "errat", "sbagliat", "mancan", "meno", "differenz", "corrispond", "previst" }
                ),

                [Topic.RINUNCIA_REVOCA] = (
                    new[] { "rinuncia ai benefici", "rinuncio alla borsa di studio", "revoca della borsa", "restituzione della borsa", "rateizzare la restituzione" },
                    new[] { "rinunc", "revoc", "restituz", "rateizz" }
                ),

                // ───────── ALLOGGIO ─────────
                [Topic.ALLOGGIO] = (
                    new[] { "posto alloggio", "assegnazione alloggio", "residenza universitaria", "check in residenza", "check out residenza", "casa dello studente" },
                    new[] { "allogg", "residenz", "dormitor", "housing", "residence", "dorm", "studentato" }
                ),

                [Topic.CONTRATTO] = (
                    new[] { "contratto di locazione", "contratto d'affitto", "proroga contratto", "registrazione contratto", "protocollo registrazione", "agenzia delle entrate", "subentro", "cointestato", "contributo affitto", "contributo alloggio" },
                    new[] { "contratt", "affitt", "locaz", "prorog", "registraz", "protocol", "agenzia", "subentr", "cointestat" }
                ),

                // ───────── DOCUMENTI/PERMESSI ─────────
                [Topic.PERMESSO] = (
                    new[] { "permesso di soggiorno", "ricevuta permesso", "impronte digitali", "appuntamento in questura", "in fase di rinnovo", "kit postale", "residence permit", "permesso scaduto" },
                    new[] { "permess", "soggiorn", "questur", "impront", "ricevut", "rinnov", "residence", "permit", "post", "kit" }
                ),

                [Topic.ISEE_REDDITI] = (
                    new[] { "isee universitario", "isee parificato", "integrazione isee", "redditi esteri", "documentazione economica", "scadenza dsu", "isee scaduto", "isee corrente" },
                    new[] { "isee", "dsu", "caf", "parificat", "ispdsu", "reddit", "ispe", "scadenz", "scadut", "corrente" }
                ),

                [Topic.PEC_EMAIL] = (
                    new[] { "indirizzo pec", "invio tramite pec", "posso inviare via email", "accettate questa pec", "non possiedo una pec" },
                    new[] { "pec", "posta certificata", "mail", "email", "allegat" }
                ),

                [Topic.INDIPENDENTE] = (
                    new[] { "studente indipendente", "condizione studente indipendente irregolare", "condizione indipendente non soddisfatta" },
                    new[] { "indipendent", "independ", "studente indipendente" }
                ),

                [Topic.CODICE_FISCALE] = (
                    new[] { "correzione codice fiscale", "codice fiscale errato", "omocodia", "variazione codice fiscale", "data di nascita errata", "invalid tax code" },
                    new[] { "codic", "fiscal", "cf", "taxcod", "fiscalcod", "omocod", "nascit" }
                ),

                // ───────── PORTALE ─────────
                [Topic.PORTALE] = (
                    new[] {
                        "pagina bianca", "la pagina bufferizza", "problema tecnico sul sito",
                        "sezione non disponibile", "non consente di caricare", "request entity too large",
                        "errore 500", "errore 502", "errore 504", "white page", "server error", "timeout",
                        "impossibile effettuare il login", "non riesco ad accedere al portale"
                    },
                    new[] { "portal", "portale", "sito", "bug", "error", "server", "upload", "buffer", "white", "page", "500", "502", "504", "timeout", "accesso", "login", "spid", "credenzial" }
                ),

                // ───────── GRADUATORIE ─────────
                [Topic.GRADUATORIA] = (
                    new[] { "graduatoria definitiva", "graduatoria provvisoria", "scorrimento graduatoria", "posizione in graduatoria", "premio di laurea", "esito della graduatoria", "pubblicazione del bando", "scadenza bando" },
                    new[] { "graduator", "idone", "vincitor", "scorriment", "posizion", "premio", "laure", "bando" }
                ),

                // ───────── MOBILITÀ ─────────
                [Topic.MOBILITA_ERASMUS] = (
                    new[] { "contributo mobilità internazionale", "studente erasmus", "borsa erasmus", "learning agreement", "arrival certificate", "acceptance letter", "erasmus grant" },
                    new[] { "erasm", "mobilit", "contribut", "learning", "arrival", "acceptance", "ester" }
                ),

                // ───────── MENSA ─────────
                [Topic.MENSA] = (
                    new[] {
                        "monetizzazione del servizio mensa",
                        "rimborso servizio mensa non usufruito",
                        "richiedere monetizzazione mensa",
                        "non ho usufruito del servizio mensa",
                        "preferisco ricevere denaro invece dei pasti gratuiti",
                        "deduzione mensa 600€",
                        "buoni pasto mensa"
                    },
                    new[] { "mensa", "monetizz", "pasto", "pasti", "buon", "meal", "service", "600" }
                ),

                // ───────── BLOCCO: flag terziario ─────────
                [Topic.BLOCCHI] = (
                    new[] {
                        "blocco pagamenti", "sono presenti dei blocchi", "domanda bloccata", "rimuovere il blocco",
                        "incongruenza tra documenti", "incongruenze iscrizione",
                        "attestazione tirocinio mancante", "indipendente irregolare", "posizione debitoria in sospeso"
                    },
                    new[] { "blocc", "sblocc", "incongru", "unpaid", "block", "irregolar" }
                )
            };
        }
    }
}
