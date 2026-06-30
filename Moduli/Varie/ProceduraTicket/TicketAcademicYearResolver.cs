using System;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    /// <summary>
    /// Risolve l'anno accademico di riferimento del ticket secondo una precedenza esplicita:
    /// 1) AA_DICHIARATO_TICKET, 2) testo del messaggio dello studente, 3) nessun anno
    /// identificato (il selettore operativo userà l'ultima domanda disponibile).
    /// </summary>
    internal static class TicketAcademicYearResolver
    {
        public const string SourceDeclaredColumn = "COLONNA_AA_DICHIARATO_TICKET";
        public const string SourceStudentMessage = "MESSAGGIO_STUDENTE";
        public const string SourceLatestFallback = "FALLBACK_ULTIMA_DOMANDA";

        private static readonly Regex MarkerAcademicYearPair = new Regex(
            @"\b(?:a\s*\.?\s*a\s*\.?|anno\s+accademico|academic\s+year)\s*[:\-]?\s*(?<start>20\d{2}|\d{2})\s*(?:/|\-|–|—|_)\s*(?<end>20\d{2}|\d{2})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FullOrMixedAcademicYearPair = new Regex(
            @"(?<!\d)(?<start>20\d{2})\s*(?:/|\-|–|—|_)\s*(?<end>20\d{2}|\d{2})(?!\s*(?:/|\-|–|—|_)\s*\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CompactAcademicYearPair = new Regex(
            @"(?<!\d)(?<start>20\d{2})(?<end>20\d{2})(?!\d)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        internal sealed class Resolution
        {
            public int? AcademicYear { get; init; }
            public string AcademicYearRaw { get; init; } = string.Empty;
            public string Source { get; init; } = SourceLatestFallback;
            public string Confidence { get; init; } = "BASSA";

            public bool HasAcademicYear => AcademicYear.HasValue;

            public string FormattedAcademicYear => AcademicYear.HasValue
                ? Format(AcademicYear.Value)
                : string.Empty;
        }

        public static Resolution Resolve(string declaredAcademicYear, string studentMessage)
        {
            if (TryParseAcademicYear(declaredAcademicYear, out int declaredYear, out string declaredRaw))
            {
                return new Resolution
                {
                    AcademicYear = declaredYear,
                    AcademicYearRaw = declaredRaw,
                    Source = SourceDeclaredColumn,
                    Confidence = "ALTA"
                };
            }

            if (TryDetectFromStudentMessage(studentMessage, out int messageYear, out string messageRaw, out bool explicitMarker))
            {
                return new Resolution
                {
                    AcademicYear = messageYear,
                    AcademicYearRaw = messageRaw,
                    Source = SourceStudentMessage,
                    Confidence = explicitMarker ? "ALTA" : "MEDIA"
                };
            }

            return new Resolution();
        }

        public static bool TryParseAcademicYear(string value, out int academicYear)
        {
            return TryParseAcademicYear(value, out academicYear, out _);
        }

        public static bool TryParseAcademicYear(string value, out int academicYear, out string raw)
        {
            academicYear = default;
            raw = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (TryFindPair(value, MarkerAcademicYearPair, out academicYear, out raw) ||
                TryFindPair(value, FullOrMixedAcademicYearPair, out academicYear, out raw) ||
                TryFindPair(value, CompactAcademicYearPair, out academicYear, out raw))
            {
                return true;
            }

            string digits = Regex.Replace(value, @"\D", string.Empty);
            if (digits.Length == 8 &&
                int.TryParse(digits[..4], out int start) &&
                int.TryParse(digits[4..], out int end) &&
                IsValidPair(start, end))
            {
                academicYear = Compose(start, end);
                raw = value.Trim();
                return true;
            }

            return false;
        }

        private static bool TryDetectFromStudentMessage(
            string message,
            out int academicYear,
            out string raw,
            out bool explicitMarker)
        {
            academicYear = default;
            raw = string.Empty;
            explicitMarker = false;
            if (string.IsNullOrWhiteSpace(message))
                return false;

            // Nel messaggio dello studente possono comparire anni storici e l'anno
            // a cui si riferisce la richiesta. La convenzione operativa stabilita è
            // usare sempre l'ultima occorrenza valida nel testo, indipendentemente
            // dal formato usato (a.a., 2025/2026, 2025/26, 20252026).
            AcademicYearCandidate? lastCandidate = null;
            ConsiderAcademicYearCandidates(message, MarkerAcademicYearPair, true, ref lastCandidate);
            ConsiderAcademicYearCandidates(message, FullOrMixedAcademicYearPair, false, ref lastCandidate);
            ConsiderAcademicYearCandidates(message, CompactAcademicYearPair, false, ref lastCandidate);

            if (lastCandidate == null)
                return false;

            academicYear = lastCandidate.AcademicYear;
            raw = lastCandidate.Raw;
            explicitMarker = lastCandidate.IsExplicitMarker;
            return true;
        }

        private static void ConsiderAcademicYearCandidates(
            string value,
            Regex regex,
            bool isExplicitMarker,
            ref AcademicYearCandidate? lastCandidate)
        {
            foreach (Match match in regex.Matches(value))
            {
                if (!TryNormalizePair(
                        match.Groups["start"].Value,
                        match.Groups["end"].Value,
                        out int start,
                        out int end))
                {
                    continue;
                }

                // Confrontare la posizione della coppia numerica, non l'inizio
                // dell'eventuale prefisso "a.a.". In questo modo le corrispondenze
                // sovrapposte dello stesso anno non alterano l'ordine testuale.
                int occurrenceIndex = match.Groups["start"].Index;
                var candidate = new AcademicYearCandidate(
                    Compose(start, end),
                    match.Value.Trim(),
                    occurrenceIndex,
                    isExplicitMarker);

                if (lastCandidate == null ||
                    candidate.OccurrenceIndex > lastCandidate.OccurrenceIndex ||
                    (candidate.OccurrenceIndex == lastCandidate.OccurrenceIndex &&
                     candidate.IsExplicitMarker &&
                     !lastCandidate.IsExplicitMarker))
                {
                    lastCandidate = candidate;
                }
            }
        }

        private sealed class AcademicYearCandidate
        {
            public AcademicYearCandidate(
                int academicYear,
                string raw,
                int occurrenceIndex,
                bool isExplicitMarker)
            {
                AcademicYear = academicYear;
                Raw = raw;
                OccurrenceIndex = occurrenceIndex;
                IsExplicitMarker = isExplicitMarker;
            }

            public int AcademicYear { get; }
            public string Raw { get; }
            public int OccurrenceIndex { get; }
            public bool IsExplicitMarker { get; }
        }

        private static bool TryFindPair(string value, Regex regex, out int academicYear, out string raw)
        {
            academicYear = default;
            raw = string.Empty;

            Match match = regex.Match(value);
            if (!match.Success ||
                !TryNormalizePair(match.Groups["start"].Value, match.Groups["end"].Value, out int start, out int end))
            {
                return false;
            }

            academicYear = Compose(start, end);
            raw = match.Value.Trim();
            return true;
        }

        private static bool TryNormalizePair(string startRaw, string endRaw, out int start, out int end)
        {
            start = NormalizeYear(startRaw);
            end = NormalizeYear(endRaw);
            return IsValidPair(start, end);
        }

        private static int NormalizeYear(string value)
        {
            if (!int.TryParse(value, out int year))
                return 0;

            return value.Length == 2 ? 2000 + year : year;
        }

        private static bool IsValidPair(int start, int end)
        {
            return start >= 2000 && start <= 2099 && end == start + 1;
        }

        private static int Compose(int start, int end) => (start * 10000) + end;

        public static string Format(int academicYear)
        {
            string value = academicYear.ToString();
            return value.Length == 8 ? $"{value[..4]}/{value[4..]}" : value;
        }
    }
}
