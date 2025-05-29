using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProcedureNet7
{
   public static class DomicilioUtils
    {
       public static bool IsValidSerie(string serie)
        {
            if (string.IsNullOrWhiteSpace(serie))
                return false;

            serie = serie.Trim();
            // Remove trailing dots
            serie = serie.TrimEnd('.');

            // Case-insensitive matching
            RegexOptions options = RegexOptions.IgnoreCase;

            // Exclude date-only entries or date ranges
            string dateOnlyPattern1 = @"^\d{1,2}/\d{1,2}/\d{2,4}$";
            string dateOnlyPattern2 = @"^\d{1,2}/\d{1,2}/\d{2,4}\s*[\-–]\s*\d{1,2}/\d{1,2}/\d{2,4}$";
            string dateOnlyPattern3 = @"^dal\s+\d{1,2}/\d{1,2}/\d{2,4}\s+al\s+\d{1,2}/\d{1,2}/\d{2,4}$";
            string dateWordsPattern = @"^dal\s+\d{1,2}\s+\w+\s+\d{4}\s+al\s+\d{1,2}\s+\w+\s+\d{4}$";

            if (Regex.IsMatch(serie, dateOnlyPattern1, options) ||
                Regex.IsMatch(serie, dateOnlyPattern2, options) ||
                Regex.IsMatch(serie, dateOnlyPattern3, options) ||
                Regex.IsMatch(serie, dateWordsPattern, options))
            {
                return false;
            }

            // Exclude '3T' or 'serie 3T' alone
            string serieWithoutSpaces = Regex.Replace(serie, @"\s+", "");
            if (string.Equals(serieWithoutSpaces, "3T", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(serieWithoutSpaces, "serie3T", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Exclude 'Foglio/part/sub/Cat' patterns unless they match a valid code
            if (Regex.IsMatch(serie, @"\b(Foglio|part|sub|Cat)\b", options) &&
                !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
            {
                return false;
            }

            // Exclude 'PRENOTAZIONE' unless there's a valid code
            if (Regex.IsMatch(serie, @"\bPRENOTAZIONE\b", options) &&
                !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
            {
                return false;
            }

            // Exclude 'automatico' unless there's a valid code
            if (Regex.IsMatch(serie, @"automatico", options) &&
                !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
            {
                return false;
            }

            // Patterns for valid codes
            string pattern1 = @"^(T|TRF|TEL)\s?[A-Z0-9]{10,50}\.?$";
            string pattern1b = @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b";
            string pattern2 = @"^[\d/\s\-]{4,}$";
            string pattern2b = @"^\d{1,20}([/\s\-]\d{1,20})+$";
            string pattern3 = @"(?i)^(.*\b(serie\s*3\s*T|serie\s*3T|serie\s*T3|serie\s*T|serie\s*IT|3\s*T|3T|T3|3/T)\b.*)$";
            string pattern4 = @"^QC([\s/]*\w+)+$";
            string pattern5 = @"(?i)^(.*\b(Protocollo|PROT\.?|prot\.?n?\.?|Protocol-?)\b.*\d+.*)$";
            string pattern6 = @"^(RA/|RM|FC/)\s*\S+$";
            // At least one digit, one letter, can include slash/hyphen/spaces, 5-50 in length
            string pattern7 = @"^(?=.*[A-Za-z])(?=.*\d)[A-Za-z0-9/\s\-]{5,50}$";

            // NEW: Pattern for "3Tn.15706" style series.
            string pattern8 = @"^3Tn\.[0-9]+$";

            // Check them in sequence
            if (Regex.IsMatch(serie, pattern1, options)) return true;
            if (Regex.IsMatch(serie, pattern1b, options)) return true;
            if (Regex.IsMatch(serie, pattern2, options)) return true;
            if (Regex.IsMatch(serie, pattern2b, options)) return true;
            if (Regex.IsMatch(serie, pattern3, options)) return true;
            if (Regex.IsMatch(serie, pattern4, options)) return true;
            if (Regex.IsMatch(serie, pattern5, options)) return true;
            if (Regex.IsMatch(serie, pattern6, options)) return true;
            if (Regex.IsMatch(serie, pattern7, options)) return true;
            if (Regex.IsMatch(serie, pattern8, options)) return true;

            // If none match, it's invalid
            return false;
        }
    }
}
