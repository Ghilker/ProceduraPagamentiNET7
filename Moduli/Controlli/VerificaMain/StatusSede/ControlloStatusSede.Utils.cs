using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class ControlloStatusSede
    {
        private static (string ComuneA, string ComuneB) NormalizeComunePair(string? comuneA, string? comuneB)
        {
            string a = (comuneA ?? "").Trim().ToUpperInvariant();
            string b = (comuneB ?? "").Trim().ToUpperInvariant();

            return string.CompareOrdinal(a, b) <= 0
                ? (a, b)
                : (b, a);
        }

        private static void ValidateSelectedAA(string aa)
        {
            if (aa.Length != 8 || !aa.All(char.IsDigit))
                throw new ArgumentException("Anno accademico non valido. Atteso formato YYYYYYYY.");

            int start = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int end = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            if (end != start + 1)
                throw new ArgumentException("Anno accademico incoerente. Fine ≠ inizio+1.");
        }

        private static string FormatDateForExport(DateTime? dt)
        {
            if (!dt.HasValue) return "";
            if (dt.Value == DateTime.MinValue) return "";
            if (dt.Value.Year < 1900) return "";
            return dt.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private static bool Eq(string? a, string? b)
        {
            return string.Equals(
                (a ?? "").Trim(),
                (b ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static (DateTime aaStart, DateTime aaEnd) GetAaDateRange(string aa)
        {
            int startYear = int.Parse(aa.Substring(0, 4), CultureInfo.InvariantCulture);
            int endYear = int.Parse(aa.Substring(4, 4), CultureInfo.InvariantCulture);
            return (new DateTime(startYear, 10, 1), new DateTime(endYear, 9, 30));
        }
    }
}
