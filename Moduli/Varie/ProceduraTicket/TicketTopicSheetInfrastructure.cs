using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7
{
    internal enum StudentApplicationCategory
    {
        SOLO_DOMANDA_ANNO_CORRENTE,
        SOLO_DOMANDE_PRECEDENTI,
        DOMANDA_ANNO_CORRENTE_E_PRECEDENTI,
        NESSUNA_DOMANDA_O_ANNI_NON_CLASSIFICABILI
    }

    internal sealed class TicketRecordSelection
    {
        public TicketOfficeRecord? Record { get; init; }
        public string Criterion { get; init; } = string.Empty;
        public int? RequestedAcademicYear { get; init; }
        public string RequestedAcademicYearSource { get; init; } = TicketAcademicYearResolver.SourceLatestFallback;

        public bool HasRecord => Record != null;
        public bool HasRequestedAcademicYear => RequestedAcademicYear.HasValue;

        // Compatibilità con i punti del codice che usavano il vecchio nome.
        public bool HasDeclaredAcademicYear => HasRequestedAcademicYear;

        // Una corrispondenza è utilizzabile per la chiusura solo se l'anno è stato
        // dichiarato nella colonna oppure rilevato dal messaggio dello studente.
        public bool IsExactAcademicYear =>
            Record != null &&
            RequestedAcademicYear.HasValue &&
            Record.AcademicYear == RequestedAcademicYear.Value &&
            (string.Equals(RequestedAcademicYearSource, TicketAcademicYearResolver.SourceDeclaredColumn, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(RequestedAcademicYearSource, TicketAcademicYearResolver.SourceStudentMessage, StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class TicketTopicSheetContext
    {
        public TicketTopicSheetContext(
            DataTable tickets,
            Dictionary<string, HashSet<int>> academicYearsByFiscalCode,
            Dictionary<string, List<TicketOfficeRecord>> officeRecordsByFiscalCode,
            SqlConnection connection,
            int newStudentAcademicYear)
        {
            Tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
            AcademicYearsByFiscalCode = academicYearsByFiscalCode
                ?? throw new ArgumentNullException(nameof(academicYearsByFiscalCode));
            OfficeRecordsByFiscalCode = officeRecordsByFiscalCode
                ?? throw new ArgumentNullException(nameof(officeRecordsByFiscalCode));
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            NewStudentAcademicYear = newStudentAcademicYear;

            TicketById = BuildTicketIndex(Tickets);
        }

        public DataTable Tickets { get; }
        public Dictionary<string, HashSet<int>> AcademicYearsByFiscalCode { get; }
        public Dictionary<string, List<TicketOfficeRecord>> OfficeRecordsByFiscalCode { get; }
        public SqlConnection Connection { get; }
        public int NewStudentAcademicYear { get; }
        public IReadOnlyDictionary<string, DataRow> TicketById { get; }

        /// <summary>
        /// Classifica lo studente esclusivamente sulle domande effettivamente estratte.
        /// L'anno scritto nel ticket non è usato in alcun passaggio della classificazione.
        /// </summary>
        public StudentApplicationCategory GetStudentApplicationCategory(string fiscalCode)
        {
            IReadOnlyList<int> years = GetValidAcademicYears(fiscalCode);
            if (years.Count == 0)
                return StudentApplicationCategory.NESSUNA_DOMANDA_O_ANNI_NON_CLASSIFICABILI;

            bool hasCurrent = years.Contains(NewStudentAcademicYear);
            bool hasPrevious = years.Any(year => year < NewStudentAcademicYear);
            bool hasFuture = years.Any(year => year > NewStudentAcademicYear);

            if (hasCurrent && hasPrevious)
                return StudentApplicationCategory.DOMANDA_ANNO_CORRENTE_E_PRECEDENTI;

            if (hasCurrent && !hasPrevious && !hasFuture)
                return StudentApplicationCategory.SOLO_DOMANDA_ANNO_CORRENTE;

            if (!hasCurrent && hasPrevious && !hasFuture)
                return StudentApplicationCategory.SOLO_DOMANDE_PRECEDENTI;

            return StudentApplicationCategory.NESSUNA_DOMANDA_O_ANNI_NON_CLASSIFICABILI;
        }

        public string GetStudentApplicationCategoryLabel(string fiscalCode) =>
            GetStudentApplicationCategoryLabel(GetStudentApplicationCategory(fiscalCode));

        public string GetStudentApplicationCategoryLabel(StudentApplicationCategory category)
        {
            string currentYear = FormatAcademicYear(NewStudentAcademicYear);
            return category switch
            {
                StudentApplicationCategory.SOLO_DOMANDA_ANNO_CORRENTE =>
                    $"SOLO DOMANDA {currentYear}",
                StudentApplicationCategory.SOLO_DOMANDE_PRECEDENTI =>
                    "SOLO DOMANDE PRECEDENTI",
                StudentApplicationCategory.DOMANDA_ANNO_CORRENTE_E_PRECEDENTI =>
                    $"DOMANDA {currentYear} E PRECEDENTI",
                _ => "NESSUNA DOMANDA O ANNI NON CLASSIFICABILI"
            };
        }

        public IReadOnlyList<int> GetValidAcademicYears(string fiscalCode)
        {
            if (string.IsNullOrWhiteSpace(fiscalCode) ||
                !AcademicYearsByFiscalCode.TryGetValue(fiscalCode, out HashSet<int>? years))
            {
                return Array.Empty<int>();
            }

            return years
                .Where(IsPlausibleAcademicYear)
                .OrderByDescending(year => year)
                .ToArray();
        }

        public string GetParticipatedAcademicYears(string fiscalCode)
        {
            IReadOnlyList<int> years = GetValidAcademicYears(fiscalCode);
            return years.Count == 0
                ? string.Empty
                : string.Join(" | ", years.Select(FormatAcademicYear));
        }

        private static bool IsPlausibleAcademicYear(int academicYear)
        {
            string value = academicYear.ToString();
            if (value.Length != 8)
                return false;

            int startYear = academicYear / 10000;
            int endYear = academicYear % 10000;
            return startYear >= 2000 && startYear <= 2099 && endYear == startYear + 1;
        }

        private static string FormatAcademicYear(int academicYear)
        {
            string value = academicYear.ToString();
            return value.Length == 8 ? $"{value[..4]}/{value[4..]}" : value;
        }

        public TicketRecordSelection ResolveOperationalRecord(string fiscalCode) =>
            ResolveOperationalRecordByResolvedYear(
                fiscalCode,
                string.Empty,
                TicketAcademicYearResolver.SourceLatestFallback);

        /// <summary>
        /// Risolve l'anno esclusivamente per la selezione interna del record. La fonte non viene
        /// riportata in colonne aggiuntive: resta disponibile nel criterio di selezione e nei controlli.
        /// </summary>
        public TicketRecordSelection ResolveOperationalRecord(
            string fiscalCode,
            string declaredAcademicYear,
            string studentMessage)
        {
            TicketAcademicYearResolver.Resolution resolution = TicketAcademicYearResolver.Resolve(
                declaredAcademicYear,
                studentMessage);

            return ResolveOperationalRecordByResolvedYear(
                fiscalCode,
                resolution.FormattedAcademicYear,
                resolution.Source);
        }

        /// <summary>
        /// Seleziona il record con questo ordine vincolante:
        /// AA_DICHIARATO_TICKET, anno rilevato nel messaggio, ultima domanda disponibile.
        /// Non usa più l'anno corrente di procedura come fallback.
        /// </summary>
        private TicketRecordSelection ResolveOperationalRecordByResolvedYear(
            string fiscalCode,
            string requestedAcademicYear,
            string requestedAcademicYearSource)
        {
            int? requestedYear = TicketAcademicYearResolver.TryParseAcademicYear(
                requestedAcademicYear,
                out int parsedAcademicYear)
                ? parsedAcademicYear
                : null;

            string source = string.IsNullOrWhiteSpace(requestedAcademicYearSource)
                ? TicketAcademicYearResolver.SourceLatestFallback
                : requestedAcademicYearSource.Trim();

            if (string.IsNullOrWhiteSpace(fiscalCode) ||
                !OfficeRecordsByFiscalCode.TryGetValue(fiscalCode, out List<TicketOfficeRecord>? records) ||
                records.Count == 0)
            {
                return new TicketRecordSelection
                {
                    Criterion = "NESSUNA_DOMANDA_OPERATIVA",
                    RequestedAcademicYear = requestedYear,
                    RequestedAcademicYearSource = source
                };
            }

            if (requestedYear.HasValue)
            {
                TicketOfficeRecord? requested = records.FirstOrDefault(record =>
                    record.AcademicYear == requestedYear.Value);
                if (requested != null)
                {
                    return new TicketRecordSelection
                    {
                        Record = requested,
                        Criterion = GetExactAcademicYearCriterion(source),
                        RequestedAcademicYear = requestedYear,
                        RequestedAcademicYearSource = source
                    };
                }
            }

            TicketOfficeRecord latest = records
                .OrderByDescending(record => record.AcademicYear)
                .ThenByDescending(record => record.ApplicationNumber, StringComparer.OrdinalIgnoreCase)
                .First();

            return new TicketRecordSelection
            {
                Record = latest,
                Criterion = requestedYear.HasValue
                    ? GetMissingAcademicYearCriterion(source)
                    : "ULTIMA_DOMANDA_DISPONIBILE",
                RequestedAcademicYear = requestedYear,
                RequestedAcademicYearSource = source
            };
        }

        private static string GetExactAcademicYearCriterion(string source)
        {
            if (string.Equals(source, TicketAcademicYearResolver.SourceDeclaredColumn, StringComparison.OrdinalIgnoreCase))
                return "ANNO_DICHIARATO_TICKET";
            if (string.Equals(source, TicketAcademicYearResolver.SourceStudentMessage, StringComparison.OrdinalIgnoreCase))
                return "ANNO_RILEVATO_MESSAGGIO";
            return "ANNO_RICHIESTO_TICKET";
        }

        private static string GetMissingAcademicYearCriterion(string source)
        {
            if (string.Equals(source, TicketAcademicYearResolver.SourceDeclaredColumn, StringComparison.OrdinalIgnoreCase))
                return "ANNO_DICHIARATO_NON_TROVATO_ULTIMA_DOMANDA";
            if (string.Equals(source, TicketAcademicYearResolver.SourceStudentMessage, StringComparison.OrdinalIgnoreCase))
                return "ANNO_MESSAGGIO_NON_TROVATO_ULTIMA_DOMANDA";
            return "ANNO_RICHIESTO_NON_TROVATO_ULTIMA_DOMANDA";
        }

        private static IReadOnlyDictionary<string, DataRow> BuildTicketIndex(DataTable tickets)
        {
            var result = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
            if (!tickets.Columns.Contains("ID_TICKET"))
                return result;

            foreach (DataRow row in tickets.Rows)
            {
                string id = ReadString(row, "ID_TICKET");
                if (!string.IsNullOrWhiteSpace(id) && !result.ContainsKey(id))
                    result[id] = row;
            }

            return result;
        }

        private static string ReadString(DataRow row, string columnName)
        {
            if (!row.Table.Columns.Contains(columnName))
                return string.Empty;

            object value = row[columnName];
            return value == null || value == DBNull.Value
                ? string.Empty
                : value.ToString()?.Trim() ?? string.Empty;
        }
    }

    internal interface ITicketTopicSheet
    {
        int Order { get; }
        string SheetName { get; }
        DataTable BuildSheet(TicketTopicSheetContext context);
    }

    internal abstract class TicketTopicSheetBase : ITicketTopicSheet
    {
        private static readonly string[] CoreColumns =
        {
            "ID_TICKET",
            "CODSTUD",
            "CODFISC",
            "PRIMO_MSG_STUDENTE",
            "ARGOMENTO_PRIMARIO",
            "ARGOMENTO_SECONDARIO"
        };

        public abstract int Order { get; }
        public abstract string SheetName { get; }

        protected abstract bool Matches(DataRow row);

        protected virtual IEnumerable<string> AdditionalColumns => Array.Empty<string>();

        public virtual DataTable BuildSheet(TicketTopicSheetContext context)
        {
            IEnumerable<DataRow> rows = context.Tickets
                .AsEnumerable()
                .Where(Matches);

            if (context.Tickets.Columns.Contains("ID_TICKET"))
            {
                rows = rows
                    .Select((row, index) => new { Row = row, Index = index })
                    .GroupBy(
                        item =>
                        {
                            string ticketId = SafeString(item.Row, "ID_TICKET");
                            return string.IsNullOrWhiteSpace(ticketId)
                                ? $"__ROW__{item.Index}"
                                : ticketId;
                        },
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First().Row);
            }

            return ProjectRows(context.Tickets, rows, AdditionalColumns);
        }

        protected static DataTable ProjectRows(
            DataTable source,
            IEnumerable<DataRow> rows,
            IEnumerable<string>? additionalColumns = null)
        {
            var requestedColumns = CoreColumns
                .Concat(additionalColumns ?? Array.Empty<string>())
                .Where(source.Columns.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new DataTable();
            foreach (string columnName in requestedColumns)
            {
                DataColumn sourceColumn = source.Columns[columnName]!;
                result.Columns.Add(columnName, sourceColumn.DataType);
            }

            foreach (DataRow sourceRow in rows)
            {
                DataRow targetRow = result.NewRow();
                foreach (string columnName in requestedColumns)
                    targetRow[columnName] = sourceRow[columnName];
                result.Rows.Add(targetRow);
            }

            return result;
        }

        protected static bool PrimaryIs(DataRow row, PrimaryTopic topic) =>
            string.Equals(
                SafeString(row, "ARGOMENTO_PRIMARIO"),
                topic.ToString(),
                StringComparison.OrdinalIgnoreCase);

        protected static string SafeString(DataRow row, string columnName)
        {
            if (!row.Table.Columns.Contains(columnName))
                return string.Empty;

            object value = row[columnName];
            return value == null || value == DBNull.Value
                ? string.Empty
                : value.ToString()?.Trim() ?? string.Empty;
        }

        protected static string FormatAcademicYear(int year)
        {
            string raw = year.ToString();
            return raw.Length == 8
                ? $"{raw[..4]}/{raw[4..]}"
                : raw;
        }

        protected static TicketRecordSelection ResolveOperationalSelection(
            TicketTopicSheetContext context,
            DataRow row)
        {
            return context.ResolveOperationalRecord(
                SafeString(row, "CODFISC"),
                SafeString(row, "AA_DICHIARATO_TICKET"),
                SafeString(row, "PRIMO_MSG_STUDENTE"));
        }

        protected static IReadOnlyList<TicketOfficeRecord> GetOfficeHistory(
            TicketTopicSheetContext context,
            DataRow row)
        {
            string fiscalCode = SafeString(row, "CODFISC");
            return context.OfficeRecordsByFiscalCode.TryGetValue(
                fiscalCode,
                out List<TicketOfficeRecord>? records)
                ? records
                : Array.Empty<TicketOfficeRecord>();
        }

        protected static void AddOfficeColumn(
            DataTable table,
            string name,
            Type? type = null)
        {
            if (!table.Columns.Contains(name))
                table.Columns.Add(name, type ?? typeof(string));
        }

        protected static void SetOfficeValue(
            DataRow row,
            string columnName,
            object? value)
        {
            row[columnName] = value ?? DBNull.Value;
        }
    }
}
