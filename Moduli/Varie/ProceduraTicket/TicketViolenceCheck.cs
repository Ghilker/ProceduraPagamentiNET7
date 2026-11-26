using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal static class TicketViolenceCheck
    {
        public const string ColumnName = "SEGNALAZIONE_VIOLENZA";

        // Ricerca volutamente inclusiva: comprende flessioni e parole composte,
        // senza escludere negazioni o altri possibili falsi positivi.
        private static readonly Regex Keywords = new(
            @"violen|abus|sessual|stupr|stalk|vittim|sexual|rap(?:e|ing|ist)|victim|\b(?:codice[\s\p{P}]*rosso|code[\s\p{P}]*red|red[\s\p{P}]*code)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Evaluate(string? message, string? subject = null) =>
            ContainsKeywords(message) || ContainsKeywords(subject)
                ? "SI" : "NO";

        private static bool ContainsKeywords(string? text) =>
            Keywords.IsMatch(text ?? string.Empty) || Keywords.IsMatch(Normalize(text));

        private static string Normalize(string? text) =>
            Regex.Replace(WebUtility.HtmlDecode(text ?? string.Empty), "<[^>]+>", " ");

        public static bool IsFlagged(DataRow row) => Evaluate(
            TicketClosureReview.Read(row, "PRIMO_MSG_STUDENTE"),
            TicketClosureReview.Read(row, "OGGETTO")) == "SI";

        public static DataTable SelectUnflagged(DataTable tickets)
        {
            DataTable result = tickets.Clone();
            foreach (DataRow row in tickets.Rows)
                if (!IsFlagged(row)) result.ImportRow(row);
            return result;
        }

        public static DataTable Build(DataTable tickets)
        {
            if (!tickets.Columns.Contains(ColumnName))
                tickets.Columns.Add(ColumnName, typeof(string));
            foreach (DataRow row in tickets.Rows)
                row[ColumnName] = Evaluate(
                    TicketClosureReview.Read(row, "PRIMO_MSG_STUDENTE"),
                    TicketClosureReview.Read(row, "OGGETTO"));

            string[] columns = new[]
            {
                "ID_TICKET", ColumnName, "CODSTUD", "COGNOME", "NOME", "CODFISC", "STATO",
                "CATEGORIA", "SOTTOCATEGORIA", "OGGETTO", "PRIMO_MSG_STUDENTE", "DATA_CREAZIONE"
            }.Where(tickets.Columns.Contains).ToArray();
            var result = new DataTable();
            foreach (string column in columns)
                result.Columns.Add(column, tickets.Columns[column]!.DataType);

            // Il file separato contiene solo i SI, nell'ordine del file importato.
            foreach (DataRow row in tickets.AsEnumerable().Where(row => (string)row[ColumnName] == "SI"))
                result.Rows.Add(columns.Select(column => row[column]).ToArray());
            return result;
        }
    }
}
