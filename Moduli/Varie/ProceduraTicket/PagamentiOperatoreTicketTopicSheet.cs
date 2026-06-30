using System;
using System.Collections.Generic;
using System.Data;

namespace ProcedureNet7
{
    internal sealed class PagamentiOperatoreTicketTopicSheet : TicketTopicSheetBase
    {
        public override int Order => 30;
        public override string SheetName => "Pagamenti";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.PAGAMENTI_E_TASSE);

        public override DataTable BuildSheet(TicketTopicSheetContext context)
        {
            Logger.LogInfo(null, "Avvio costruzione foglio Pagamenti a riga unica per ticket");
            DataTable result = base.BuildSheet(context);

            AddOfficeColumn(result, "AA_RECORD_OPERATIVO");
            AddOfficeColumn(result, "CRITERIO_SELEZIONE_RECORD");
            AddOfficeColumn(result, "NUM_DOMANDA");
            AddOfficeColumn(result, "STATUS_COMPILAZIONE", typeof(int));
            AddOfficeColumn(result, "ESITO_BS");
            AddOfficeColumn(result, "IMPORTO_ASSEGNATO", typeof(decimal));
            AddOfficeColumn(result, "IMPORTO_SPECIFICHE_IMPEGNI", typeof(decimal));
            AddOfficeColumn(result, "IMPORTO_PAGATO_BS_VALIDO", typeof(decimal));
            AddOfficeColumn(result, "IMPORTO_PRIMA_RATA_BS", typeof(decimal));
            AddOfficeColumn(result, "IMPORTO_SALDO_BS", typeof(decimal));
            AddOfficeColumn(result, "IMPORTO_INTEGRAZIONI_BS", typeof(decimal));
            AddOfficeColumn(result, "IMPORTO_PAGAMENTI_BS_STORNATI", typeof(decimal));
            AddOfficeColumn(result, "CODICI_PAGAMENTI_BS_STORNATI");
            AddOfficeColumn(result, "MANDATI_BS_STORNATI");
            AddOfficeColumn(result, "IMPORTO_BS_CODICI_NON_CLASSIFICATI", typeof(decimal));
            AddOfficeColumn(result, "CODICI_BS_NON_CLASSIFICATI");
            AddOfficeColumn(result, "IMPORTO_REVERSALI_BS", typeof(decimal));
            AddOfficeColumn(result, "IMPORTO_DETRAZIONI", typeof(decimal));
            AddOfficeColumn(result, "RESIDUO_BS_STIMATO", typeof(decimal));
            AddOfficeColumn(result, "TIPI_PAGAMENTO_BS");
            AddOfficeColumn(result, "MANDATI_BS");
            AddOfficeColumn(result, "MANDATI_PRIMA_RATA_BS");
            AddOfficeColumn(result, "MANDATI_SALDO_BS");
            AddOfficeColumn(result, "MANDATI_INTEGRAZIONI_BS");
            AddOfficeColumn(result, "MODALITA_PAGAMENTO");
            AddOfficeColumn(result, "IBAN");
            AddOfficeColumn(result, "SWIFT");
            AddOfficeColumn(result, "BONIFICO_ESTERO");
            AddOfficeColumn(result, "BLOCCHI");
            AddOfficeColumn(result, "INDICAZIONE_OPERATORE");

            foreach (DataRow row in result.Rows)
            {
                TicketRecordSelection selection = ResolveOperationalSelection(context, row);
                TicketOfficeRecord? record = selection.Record;
                row["CRITERIO_SELEZIONE_RECORD"] = selection.Criterion;
                row["AA_RECORD_OPERATIVO"] = record == null
                    ? string.Empty
                    : FormatAcademicYear(record.AcademicYear);

                if (record == null)
                {
                    row["INDICAZIONE_OPERATORE"] =
                        "Nessuna domanda trovata: verificare codice fiscale e identità dello studente.";
                    continue;
                }

                // Le estrazioni e le regole contabili appartengono al modulo Pagamenti.
                PaymentOperationalData payment = TicketDomainModuleRegistry.Payments.Extract(record);

                row["AA_RECORD_OPERATIVO"] = FormatAcademicYear(record.AcademicYear);
                row["NUM_DOMANDA"] = record.ApplicationNumber;
                row["STATUS_COMPILAZIONE"] = record.CompilationStatus;
                row["ESITO_BS"] = PaymentsTicketDomainModule.FormatOutcome(record.BsOutcome);
                row["IMPORTO_ASSEGNATO"] = record.BenefitAmount;
                row["IMPORTO_SPECIFICHE_IMPEGNI"] = record.SpecificheAmount;
                row["IMPORTO_PAGATO_BS_VALIDO"] = payment.PaidAmount;
                row["IMPORTO_PRIMA_RATA_BS"] = payment.FirstInstallmentAmount;
                row["IMPORTO_SALDO_BS"] = payment.BalanceAmount;
                row["IMPORTO_INTEGRAZIONI_BS"] = payment.IntegrationAmount;
                row["IMPORTO_PAGAMENTI_BS_STORNATI"] = record.BsStornedAmount;
                row["CODICI_PAGAMENTI_BS_STORNATI"] = record.BsStornedPaymentTypes;
                row["MANDATI_BS_STORNATI"] = record.BsStornedMandates;
                row["IMPORTO_BS_CODICI_NON_CLASSIFICATI"] = record.BsUnclassifiedAmount;
                row["CODICI_BS_NON_CLASSIFICATI"] = record.BsUnclassifiedPaymentTypes;
                row["IMPORTO_REVERSALI_BS"] = record.BsReversalAmount;
                row["IMPORTO_DETRAZIONI"] = record.DeductionAmount;
                row["RESIDUO_BS_STIMATO"] = payment.ResidualAmount;
                row["TIPI_PAGAMENTO_BS"] = record.BsPaymentTypes;
                row["MANDATI_BS"] = payment.Mandates;
                row["MANDATI_PRIMA_RATA_BS"] = payment.FirstInstallmentMandates;
                row["MANDATI_SALDO_BS"] = payment.BalanceMandates;
                row["MANDATI_INTEGRAZIONI_BS"] = payment.IntegrationMandates;
                row["MODALITA_PAGAMENTO"] = record.PaymentMethod;
                row["IBAN"] = record.Iban;
                row["SWIFT"] = record.Swift;
                row["BONIFICO_ESTERO"] = record.ForeignTransfer ? "SI" : "";
                row["BLOCCHI"] = record.Blocks;
                row["INDICAZIONE_OPERATORE"] = TicketDomainModuleRegistry.Payments.BuildOperatorIndication(record);
            }

            Logger.LogInfo(
                null,
                $"Foglio Pagamenti completato. Ticket: {result.Rows.Count}; righe: {result.Rows.Count}");
            return result;
        }

        // Compatibilità con gli altri fogli: la conversione dell'esito è di proprietà
        // del modulo Pagamenti, non della costruzione del foglio.
        internal static string FormatOutcome(string outcome) =>
            PaymentsTicketDomainModule.FormatOutcome(outcome);
    }
}
