using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class YearInfo
    {
        public HashSet<string> BlocchiParts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> EsitiBS { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> EsitiPA { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SediStudi { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SediDescrizioni { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasPrimaRata { get; set; }
        public bool HasSaldo { get; set; }
        public bool HasRimborso { get; set; }

        public string BlocchiJoined => BlocchiParts.Count == 0 ? "" : string.Join(" | ", BlocchiParts.OrderBy(x => x));
        public string EsitoBSJoined => EsitiBS.Count == 0 ? "" : string.Join(" | ", EsitiBS.OrderBy(x => x));
        public string EsitoPAJoined => EsitiPA.Count == 0 ? "" : string.Join(" | ", EsitiPA.OrderBy(x => x));
        public string SediStudiJoined => SediStudi.Count == 0 ? "" : string.Join(" | ", SediStudi.OrderBy(x => x));
        public string SediDescrizioniJoined => SediDescrizioni.Count == 0 ? "" : string.Join(" | ", SediDescrizioni.OrderBy(x => x));
    }

    /*
       Indici in memoria derivati dall'unica estrazione operativa.
       Evitano cinque query aggiuntive su Domanda, pagamenti, domicilio e allegati.
    */
    internal sealed class TicketOfficeDataIndex
    {
        private TicketOfficeDataIndex(
            Dictionary<string, List<TicketOfficeRecord>> officeRecordsByFiscalCode,
            Dictionary<string, HashSet<int>> academicYearsByFiscalCode,
            Dictionary<string, Dictionary<int, YearInfo>> yearInfoByFiscalCode,
            Dictionary<string, bool> residencePermitDocumentsWorkedByFiscalCode,
            Dictionary<string, (bool HasOpen, bool HasWorked)> domicileInfoByFiscalCode)
        {
            OfficeRecordsByFiscalCode = officeRecordsByFiscalCode;
            AcademicYearsByFiscalCode = academicYearsByFiscalCode;
            YearInfoByFiscalCode = yearInfoByFiscalCode;
            ResidencePermitDocumentsWorkedByFiscalCode = residencePermitDocumentsWorkedByFiscalCode;
            DomicileInfoByFiscalCode = domicileInfoByFiscalCode;
        }

        public Dictionary<string, List<TicketOfficeRecord>> OfficeRecordsByFiscalCode { get; }
        public Dictionary<string, HashSet<int>> AcademicYearsByFiscalCode { get; }
        public Dictionary<string, Dictionary<int, YearInfo>> YearInfoByFiscalCode { get; }
        public Dictionary<string, bool> ResidencePermitDocumentsWorkedByFiscalCode { get; }
        public Dictionary<string, (bool HasOpen, bool HasWorked)> DomicileInfoByFiscalCode { get; }

        public static TicketOfficeDataIndex Create(
            Dictionary<string, List<TicketOfficeRecord>> officeRecordsByFiscalCode,
            IEnumerable<int> targetAcademicYears)
        {
            if (officeRecordsByFiscalCode == null)
                throw new ArgumentNullException(nameof(officeRecordsByFiscalCode));
            if (targetAcademicYears == null)
                throw new ArgumentNullException(nameof(targetAcademicYears));

            var targetYears = new HashSet<int>(targetAcademicYears);
            var academicYears = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            var yearInfo = new Dictionary<string, Dictionary<int, YearInfo>>(StringComparer.OrdinalIgnoreCase);
            var permitDocumentsWorked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var domicileInfo = new Dictionary<string, (bool HasOpen, bool HasWorked)>(StringComparer.OrdinalIgnoreCase);

            foreach ((string fiscalCode, List<TicketOfficeRecord> records) in officeRecordsByFiscalCode)
            {
                if (string.IsNullOrWhiteSpace(fiscalCode) || records == null || records.Count == 0)
                    continue;

                var years = new HashSet<int>();
                var permitStateByDocument = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                bool hasOpenDomicileRequest = false;
                bool hasWorkedDomicileRequest = false;

                foreach (TicketOfficeRecord record in records)
                {
                    if (record.AcademicYear > 0)
                        years.Add(record.AcademicYear);

                    MergePermitDocuments(record.ResidencePermitDocuments, permitStateByDocument);

                    if (targetYears.Contains(record.AcademicYear))
                    {
                        hasOpenDomicileRequest |= record.HasOpenDomicileRequest;
                        hasWorkedDomicileRequest |= record.HasWorkedDomicileRequest;
                        AddYearInfo(yearInfo, fiscalCode, record);
                    }
                }

                academicYears[fiscalCode] = years;
                permitDocumentsWorked[fiscalCode] =
                    permitStateByDocument.Count >= 3 &&
                    permitStateByDocument.TryGetValue("01", out bool document01Worked) && document01Worked &&
                    permitStateByDocument.TryGetValue("02", out bool document02Worked) && document02Worked &&
                    permitStateByDocument.TryGetValue("03", out bool document03Worked) && document03Worked;

                if (targetYears.Count > 0)
                    domicileInfo[fiscalCode] = (hasOpenDomicileRequest, hasWorkedDomicileRequest);
            }

            return new TicketOfficeDataIndex(
                officeRecordsByFiscalCode,
                academicYears,
                yearInfo,
                permitDocumentsWorked,
                domicileInfo);
        }

        private static void AddYearInfo(
            Dictionary<string, Dictionary<int, YearInfo>> result,
            string fiscalCode,
            TicketOfficeRecord record)
        {
            if (!result.TryGetValue(fiscalCode, out Dictionary<int, YearInfo>? perYear))
            {
                perYear = new Dictionary<int, YearInfo>();
                result[fiscalCode] = perYear;
            }

            if (!perYear.TryGetValue(record.AcademicYear, out YearInfo? info))
            {
                info = new YearInfo();
                perYear[record.AcademicYear] = info;
            }

            foreach (string block in SplitValues(record.Blocks, '/'))
                info.BlocchiParts.Add(block);

            AddOutcome(info.EsitiBS, record.BsOutcome);
            AddOutcome(info.EsitiPA, record.PaOutcome);

            if (!string.IsNullOrWhiteSpace(record.StudyLocation))
                info.SediDescrizioni.Add(record.StudyLocation.Trim());

            info.HasPrimaRata |= ContainsPaymentType(record.BsPaymentTypes, "BSP0", "BSP1", "BSP2");
            info.HasSaldo |= ContainsPaymentType(record.BsPaymentTypes, "BSS0", "BSS1", "BSS2");
            info.HasRimborso |= ContainsPaymentType(record.BsPaymentTypes, "BST0", "BST1", "BST2");
        }

        private static void AddOutcome(HashSet<string> outcomes, string outcome)
        {
            if (string.IsNullOrWhiteSpace(outcome))
                return;

            outcomes.Add(outcome.Trim() switch
            {
                "0" => "Escluso",
                "1" => "Idoneo",
                "2" => "Vincitore",
                _ => "Non richiesto"
            });
        }

        private static bool ContainsPaymentType(string paymentTypes, params string[] requestedTypes)
        {
            if (string.IsNullOrWhiteSpace(paymentTypes))
                return false;

            var types = new HashSet<string>(
                SplitValues(paymentTypes, '|'),
                StringComparer.OrdinalIgnoreCase);

            return requestedTypes.Any(types.Contains);
        }

        private static void MergePermitDocuments(
            string permitDocuments,
            IDictionary<string, bool> stateByDocument)
        {
            foreach (string item in SplitValues(permitDocuments, '|'))
            {
                string[] parts = item.Split(new[] { ':' }, 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 ||
                    !string.Equals(parts[0], "01", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(parts[0], "02", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(parts[0], "03", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Per la chiusura automatica il documento è considerato lavorato soltanto
                // nello stato conclusivo 05; stati intermedi non attestano la risoluzione.
                bool worked = string.Equals(parts[1], "05", StringComparison.OrdinalIgnoreCase);

                stateByDocument[parts[0]] =
                    stateByDocument.TryGetValue(parts[0], out bool previous) && previous || worked;
            }
        }

        private static IEnumerable<string> SplitValues(string value, char separator)
        {
            return (value ?? string.Empty)
                .Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item));
        }
    }
}
