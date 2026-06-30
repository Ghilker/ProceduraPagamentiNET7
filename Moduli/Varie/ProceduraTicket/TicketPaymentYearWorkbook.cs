using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    /// <summary>
    /// Espone, per i ticket di pagamenti, una riga per domanda e anno accademico dal 2024/2025.
    /// Gli importi ricevuti escludono sempre Ritirato_azienda = 1; gli storni sono mostrati a parte.
    /// </summary>
    internal static class TicketPaymentYearWorkbook
    {
        public const int FirstAcademicYear = 20242025;
        private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

        public static DataTable Build(TicketTopicSheetContext context, DataTable operationalQueue)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (operationalQueue == null)
                throw new ArgumentNullException(nameof(operationalQueue));

            DataTable result = CreateTable();
            IEnumerable<DataRow> paymentTickets = operationalQueue.AsEnumerable()
                .Where(row => string.Equals(
                    ReadString(row, "ARGOMENTO"),
                    "Pagamenti e tasse",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => ReadString(row, "CODFISC"), StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => ReadString(row, "ID_TICKET"), StringComparer.OrdinalIgnoreCase);

            foreach (DataRow ticket in paymentTickets)
            {
                string fiscalCode = ReadString(ticket, "CODFISC");
                if (!context.OfficeRecordsByFiscalCode.TryGetValue(fiscalCode, out List<TicketOfficeRecord>? records))
                    continue;

                foreach (TicketOfficeRecord record in records
                    .Where(record => record.AcademicYear >= FirstAcademicYear)
                    .OrderByDescending(record => record.AcademicYear)
                    .ThenByDescending(record => record.ApplicationNumber, StringComparer.OrdinalIgnoreCase))
                {
                    AddRow(result, ticket, record);
                }
            }

            return result;
        }

        private static void AddRow(DataTable table, DataRow ticket, TicketOfficeRecord record)
        {
            PaymentOperationalData payment = TicketDomainModuleRegistry.Payments.Extract(record);

            DataRow row = table.NewRow();
            row["ID_TICKET"] = ReadString(ticket, "ID_TICKET");
            row["CODSTUD"] = ReadString(ticket, "CODSTUD");
            row["CODFISC"] = record.FiscalCode;
            row["OGGETTO"] = ReadString(ticket, "OGGETTO");
            row["PRIORITA"] = ReadString(ticket, "PRIORITA");
            row["DECISIONE_PROPOSTA"] = ReadString(ticket, "DECISIONE_PROPOSTA");
            row["TICKET_RIGUARDA_BLOCCHI"] = ReadString(ticket, "TICKET_RIGUARDA_BLOCCHI");
            row["STUDENTE_HA_BLOCCHI"] = string.IsNullOrWhiteSpace(record.Blocks) ? "NO" : "SI";
            row["ANNO_ACCADEMICO_PAGAMENTO"] = FormatAcademicYear(record.AcademicYear);
            row["NUM_DOMANDA"] = record.ApplicationNumber;
            row["ESITO_BS"] = PaymentsTicketDomainModule.FormatOutcome(record.BsOutcome);
            row["IMPORTO_ASSEGNATO"] = record.BenefitAmount;
            row["IMPORTO_SPECIFICHE_IMPEGNI"] = record.SpecificheAmount;
            row["IMPORTO_BSP0_PRIMA_RATA"] = record.BsFirstInstallmentOriginalAmount;
            row["IMPORTO_BSP_RIEMISSIONI_PRIMA_RATA"] = record.BsFirstInstallmentReissueAmount;
            row["IMPORTO_BSS0_SALDO"] = record.BsBalanceOriginalAmount;
            row["IMPORTO_BSS_RIEMISSIONI_SALDO"] = record.BsBalanceReissueAmount;
            row["IMPORTO_BSI0_INTEGRAZIONE_PRIMA_RATA"] = record.BsIntegrationFirstInstallmentOriginalAmount;
            row["IMPORTO_BSI_RIEMISSIONI_INTEGRAZIONE_PRIMA_RATA"] = record.BsIntegrationFirstInstallmentReissueAmount;
            row["IMPORTO_BSI9_INTEGRAZIONE_SALDO"] = record.BsIntegrationBalanceOriginalAmount;
            row["IMPORTO_BSI_RIEMISSIONI_INTEGRAZIONE_SALDO"] = record.BsIntegrationBalanceReissueAmount;
            row["IMPORTO_PAGATO_BS_VALIDO"] = payment.PaidAmount;
            row["IMPORTO_PAGAMENTI_BS_STORNATI"] = record.BsStornedAmount;
            row["CODICI_PAGAMENTI_BS_STORNATI"] = record.BsStornedPaymentTypes;
            row["MANDATI_BS_STORNATI"] = record.BsStornedMandates;
            row["IMPORTO_BS_CODICI_NON_CLASSIFICATI"] = record.BsUnclassifiedAmount;
            row["CODICI_BS_NON_CLASSIFICATI"] = record.BsUnclassifiedPaymentTypes;
            row["IMPORTO_REVERSALI_BS"] = record.BsReversalAmount;
            row["IMPORTO_DETRAZIONI"] = record.DeductionAmount;
            row["RESIDUO_BS_STIMATO"] = payment.ResidualAmount;
            row["CODICI_PAGAMENTO_BS_VALIDI"] = record.BsPaymentTypes;
            row["MANDATI_BS_VALIDI"] = payment.Mandates;
            row["ESERCIZI_FINANZIARI_BS"] = record.BsFinancialYears;
            row["BLOCCHI"] = record.Blocks;
            table.Rows.Add(row);
        }

        private static DataTable CreateTable()
        {
            var result = new DataTable();
            result.Columns.Add("ID_TICKET", typeof(string));
            result.Columns.Add("CODSTUD", typeof(string));
            result.Columns.Add("CODFISC", typeof(string));
            result.Columns.Add("OGGETTO", typeof(string));
            result.Columns.Add("PRIORITA", typeof(string));
            result.Columns.Add("DECISIONE_PROPOSTA", typeof(string));
            result.Columns.Add("TICKET_RIGUARDA_BLOCCHI", typeof(string));
            result.Columns.Add("STUDENTE_HA_BLOCCHI", typeof(string));
            result.Columns.Add("ANNO_ACCADEMICO_PAGAMENTO", typeof(string));
            result.Columns.Add("NUM_DOMANDA", typeof(string));
            result.Columns.Add("ESITO_BS", typeof(string));
            result.Columns.Add("IMPORTO_ASSEGNATO", typeof(decimal));
            result.Columns.Add("IMPORTO_SPECIFICHE_IMPEGNI", typeof(decimal));
            result.Columns.Add("IMPORTO_BSP0_PRIMA_RATA", typeof(decimal));
            result.Columns.Add("IMPORTO_BSP_RIEMISSIONI_PRIMA_RATA", typeof(decimal));
            result.Columns.Add("IMPORTO_BSS0_SALDO", typeof(decimal));
            result.Columns.Add("IMPORTO_BSS_RIEMISSIONI_SALDO", typeof(decimal));
            result.Columns.Add("IMPORTO_BSI0_INTEGRAZIONE_PRIMA_RATA", typeof(decimal));
            result.Columns.Add("IMPORTO_BSI_RIEMISSIONI_INTEGRAZIONE_PRIMA_RATA", typeof(decimal));
            result.Columns.Add("IMPORTO_BSI9_INTEGRAZIONE_SALDO", typeof(decimal));
            result.Columns.Add("IMPORTO_BSI_RIEMISSIONI_INTEGRAZIONE_SALDO", typeof(decimal));
            result.Columns.Add("IMPORTO_PAGATO_BS_VALIDO", typeof(decimal));
            result.Columns.Add("IMPORTO_PAGAMENTI_BS_STORNATI", typeof(decimal));
            result.Columns.Add("CODICI_PAGAMENTI_BS_STORNATI", typeof(string));
            result.Columns.Add("MANDATI_BS_STORNATI", typeof(string));
            result.Columns.Add("IMPORTO_BS_CODICI_NON_CLASSIFICATI", typeof(decimal));
            result.Columns.Add("CODICI_BS_NON_CLASSIFICATI", typeof(string));
            result.Columns.Add("IMPORTO_REVERSALI_BS", typeof(decimal));
            result.Columns.Add("IMPORTO_DETRAZIONI", typeof(decimal));
            result.Columns.Add("RESIDUO_BS_STIMATO", typeof(decimal));
            result.Columns.Add("CODICI_PAGAMENTO_BS_VALIDI", typeof(string));
            result.Columns.Add("MANDATI_BS_VALIDI", typeof(string));
            result.Columns.Add("ESERCIZI_FINANZIARI_BS", typeof(string));
            result.Columns.Add("BLOCCHI", typeof(string));
            return result;
        }

        private static string FormatAcademicYear(int value)
        {
            string raw = value.ToString(CultureInfo.InvariantCulture);
            return raw.Length == 8 ? $"{raw[..4]}/{raw[4..]}" : raw;
        }

        private static string ReadString(DataRow row, string columnName)
        {
            if (!row.Table.Columns.Contains(columnName))
                return string.Empty;
            object value = row[columnName];
            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value, ItalianCulture)?.Trim() ?? string.Empty;
        }
    }
}
