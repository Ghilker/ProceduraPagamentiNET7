using System;
using System.Collections.Generic;
using System.Data;

namespace ProcedureNet7
{
    internal abstract class OfficeTicketTopicSheetBase : TicketTopicSheetBase
    {
        public override DataTable BuildSheet(TicketTopicSheetContext context)
        {
            Logger.LogInfo(null, $"Avvio costruzione foglio {SheetName}");
            DataTable result = base.BuildSheet(context);
            ConfigureColumns(result);

            foreach (DataRow row in result.Rows)
            {
                TicketRecordSelection selection = ResolveOperationalSelection(context, row);
                PopulateSelectionMetadata(row, selection);
                PopulateRow(row, selection.Record);
            }

            Logger.LogInfo(null, $"Foglio {SheetName} completato. Ticket: {result.Rows.Count}");
            return result;
        }

        protected abstract void ConfigureColumns(DataTable table);
        protected abstract void PopulateRow(DataRow row, TicketOfficeRecord? record);

        protected static void AddReferenceColumns(DataTable table)
        {
            AddOfficeColumn(table, "AA_RECORD_OPERATIVO");
            AddOfficeColumn(table, "CRITERIO_SELEZIONE_RECORD");
            AddOfficeColumn(table, "NUM_DOMANDA");
            AddOfficeColumn(table, "STATUS_COMPILAZIONE", typeof(int));
        }

        protected static void PopulateSelectionMetadata(
            DataRow row,
            TicketRecordSelection selection)
        {
            row["CRITERIO_SELEZIONE_RECORD"] = selection.Criterion;
            row["AA_RECORD_OPERATIVO"] = selection.Record == null
                ? string.Empty
                : FormatAcademicYear(selection.Record.AcademicYear);
        }

        protected static bool PopulateReference(
            DataRow row,
            TicketOfficeRecord? record)
        {
            if (record == null)
            {
                row["INDICAZIONE_OPERATORE"] =
                    "Domanda non trovata: verificare codice fiscale e identità dello studente.";
                return false;
            }

            row["AA_RECORD_OPERATIVO"] = FormatAcademicYear(record.AcademicYear);
            row["NUM_DOMANDA"] = record.ApplicationNumber;
            row["STATUS_COMPILAZIONE"] = record.CompilationStatus;
            return true;
        }

        protected static string YesNo(bool value) => value ? "SI" : "";
    }

    internal sealed class BeneficiImportiTicketTopicSheet : OfficeTicketTopicSheetBase
    {
        public override int Order => 40;
        public override string SheetName => "Benefici e importi";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.BENEFICI_E_IMPORTI);

        protected override void ConfigureColumns(DataTable table)
        {
            AddReferenceColumns(table);
            AddOfficeColumn(table, "ESITO_BS");
            AddOfficeColumn(table, "IMPORTO_ASSEGNATO", typeof(decimal));
            AddOfficeColumn(table, "IMPORTO_SPECIFICHE_IMPEGNI", typeof(decimal));
            AddOfficeColumn(table, "IMPORTO_PAGATO_BS", typeof(decimal));
            AddOfficeColumn(table, "RESIDUO_BS_STIMATO", typeof(decimal));
            AddOfficeColumn(table, "ISEE_DSU", typeof(decimal));
            AddOfficeColumn(table, "STATUS_SEDE");
            AddOfficeColumn(table, "BLOCCHI");
            AddOfficeColumn(table, "INDICAZIONE_OPERATORE");
        }

        protected override void PopulateRow(DataRow row, TicketOfficeRecord? record)
        {
            if (!PopulateReference(row, record))
                return;

            decimal residual = Math.Max(
                0m,
                record!.BenefitAmount -
                record.BsPaidAmount +
                record.BsReversalAmount -
                record.DeductionAmount);

            row["ESITO_BS"] = PagamentiOperatoreTicketTopicSheet.FormatOutcome(record.BsOutcome);
            row["IMPORTO_ASSEGNATO"] = record.BenefitAmount;
            row["IMPORTO_SPECIFICHE_IMPEGNI"] = record.SpecificheAmount;
            row["IMPORTO_PAGATO_BS"] = record.BsPaidAmount;
            row["RESIDUO_BS_STIMATO"] = residual;
            row["ISEE_DSU"] = record.Isee;
            row["STATUS_SEDE"] = record.CampusStatus;
            row["BLOCCHI"] = record.Blocks;

            var indications = new List<string>();
            if (record.BsOutcome == "0")
                indications.Add("studente escluso dalla borsa");
            else if (record.BsOutcome == "2" && record.BsPaidAmount == 0m)
                indications.Add("vincitore senza pagamenti BS registrati");
            if (record.BenefitAmount != record.SpecificheAmount &&
                record.SpecificheAmount > 0m)
                indications.Add("importo esito diverso dalle specifiche impegni");
            if (!string.IsNullOrWhiteSpace(record.Blocks))
                indications.Add("blocchi presenti");

            row["INDICAZIONE_OPERATORE"] = indications.Count == 0
                ? "Confrontare la richiesta con esito, importo assegnato e pagamenti riportati."
                : string.Join("; ", indications);
        }
    }

    internal sealed class AlloggioTicketTopicSheet : OfficeTicketTopicSheetBase
    {
        public override int Order => 50;
        public override string SheetName => "Alloggio";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.ALLOGGIO);

        protected override void ConfigureColumns(DataTable table)
        {
            AddReferenceColumns(table);
            AddOfficeColumn(table, "ESITO_PA");
            AddOfficeColumn(table, "STATUS_SEDE");
            AddOfficeColumn(table, "SEDE_STUDI");
            AddOfficeColumn(table, "COMUNE_DOMICILIO");
            AddOfficeColumn(table, "SERIE_CONTRATTO");
            AddOfficeColumn(table, "DECORRENZA_CONTRATTO");
            AddOfficeColumn(table, "SCADENZA_CONTRATTO");
            AddOfficeColumn(table, "CONTRATTO_PROROGATO");
            AddOfficeColumn(table, "ISTANZA_DOMICILIO_APERTA");
            AddOfficeColumn(table, "ESITO_ULTIMA_ISTANZA");
            AddOfficeColumn(table, "BLOCCHI");
            AddOfficeColumn(table, "INDICAZIONE_OPERATORE");
        }

        protected override void PopulateRow(DataRow row, TicketOfficeRecord? record)
        {
            if (!PopulateReference(row, record))
                return;

            row["ESITO_PA"] = PagamentiOperatoreTicketTopicSheet.FormatOutcome(record!.PaOutcome);
            row["STATUS_SEDE"] = record.CampusStatus;
            row["SEDE_STUDI"] = record.StudyLocation;
            row["COMUNE_DOMICILIO"] = record.DomicileMunicipality;
            row["SERIE_CONTRATTO"] = record.ContractSeries;
            row["DECORRENZA_CONTRATTO"] = record.ContractStart;
            row["SCADENZA_CONTRATTO"] = record.ContractEnd;
            row["CONTRATTO_PROROGATO"] = YesNo(record.ContractExtended);
            row["ISTANZA_DOMICILIO_APERTA"] = YesNo(record.HasOpenDomicileRequest);
            row["ESITO_ULTIMA_ISTANZA"] = record.LastClosedDomicileOutcome;
            row["BLOCCHI"] = record.Blocks;

            var indications = new List<string>();
            if (record.PaOutcome == "0")
                indications.Add("studente escluso dal posto alloggio");
            if (string.IsNullOrWhiteSpace(record.ContractSeries))
                indications.Add("contratto di domicilio non presente");
            if (record.HasOpenDomicileRequest)
                indications.Add("istanza domicilio ancora aperta");
            if (!string.IsNullOrWhiteSpace(record.Blocks))
                indications.Add("blocchi presenti");

            row["INDICAZIONE_OPERATORE"] = indications.Count == 0
                ? "Verificare esito alloggio, domicilio e stato delle eventuali istanze."
                : string.Join("; ", indications);
        }
    }

    internal sealed class MensaTicketTopicSheet : OfficeTicketTopicSheetBase
    {
        public override int Order => 60;
        public override string SheetName => "Mensa";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.MENSA);

        protected override void ConfigureColumns(DataTable table)
        {
            AddReferenceColumns(table);
            AddOfficeColumn(table, "MONETIZZAZIONE_CONCESSA");
            AddOfficeColumn(table, "ESITO_BS");
            AddOfficeColumn(table, "IMPORTO_ASSEGNATO", typeof(decimal));
            AddOfficeColumn(table, "IMPORTO_PAGATO_BS", typeof(decimal));
            AddOfficeColumn(table, "STATUS_SEDE");
            AddOfficeColumn(table, "INDICAZIONE_OPERATORE");
        }

        protected override void PopulateRow(DataRow row, TicketOfficeRecord? record)
        {
            if (!PopulateReference(row, record))
                return;

            row["MONETIZZAZIONE_CONCESSA"] = YesNo(record!.CanteenMonetizationGranted);
            row["ESITO_BS"] = PagamentiOperatoreTicketTopicSheet.FormatOutcome(record.BsOutcome);
            row["IMPORTO_ASSEGNATO"] = record.BenefitAmount;
            row["IMPORTO_PAGATO_BS"] = record.BsPaidAmount;
            row["STATUS_SEDE"] = record.CampusStatus;
            row["INDICAZIONE_OPERATORE"] = record.CanteenMonetizationGranted
                ? "Monetizzazione mensa concessa: verificare importo ed eventuale pagamento."
                : "Monetizzazione mensa non risultante per la domanda selezionata.";
        }
    }

    internal sealed class DocumentiPermessiTicketTopicSheet : OfficeTicketTopicSheetBase
    {
        public override int Order => 70;
        public override string SheetName => "Documenti e permessi";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.DOCUMENTI_E_PERMESSI);

        protected override void ConfigureColumns(DataTable table)
        {
            AddReferenceColumns(table);
            AddOfficeColumn(table, "DOCUMENTI_PERMESSO_SOGGIORNO");
            AddOfficeColumn(table, "STATO_DOCUMENTI_PERMESSO");
            AddOfficeColumn(table, "DOCUMENTI_PERMESSO_STATUS_05");
            AddOfficeColumn(table, "BLOCCHI_PERMESSO_RILEVANTI");
            AddOfficeColumn(table, "TIPO_REDDITO_ORIGINE");
            AddOfficeColumn(table, "TIPO_REDDITO_INTEGRAZIONE");
            AddOfficeColumn(table, "ISEE_DSU", typeof(decimal));
            AddOfficeColumn(table, "ISPE_DSU", typeof(decimal));
            AddOfficeColumn(table, "BLOCCHI");
            AddOfficeColumn(table, "INDICAZIONE_OPERATORE");
        }

        protected override void PopulateRow(DataRow row, TicketOfficeRecord? record)
        {
            if (!PopulateReference(row, record))
                return;

            // Estrazione e validazione del permesso sono nel modulo dedicato.
            ResidencePermitOperationalData permit = TicketDomainModuleRegistry.ResidencePermit.Extract(record!);
            BlockOperationalData blocks = TicketDomainModuleRegistry.Blocks.Extract(
                record,
                "DOCUMENTI_E_PERMESSI");

            row["DOCUMENTI_PERMESSO_SOGGIORNO"] = permit.RawDocuments;
            row["STATO_DOCUMENTI_PERMESSO"] = permit.StatusSummary;
            row["DOCUMENTI_PERMESSO_STATUS_05"] = YesNo(permit.AreRequiredDocumentsWorked);
            row["BLOCCHI_PERMESSO_RILEVANTI"] = blocks.Assessment.RelevantBlocks;
            row["TIPO_REDDITO_ORIGINE"] = record.IncomeSourceType;
            row["TIPO_REDDITO_INTEGRAZIONE"] = record.IncomeIntegrationType;
            row["ISEE_DSU"] = record.Isee;
            row["ISPE_DSU"] = record.Ispe;
            row["BLOCCHI"] = record.Blocks;

            var indications = new List<string>
            {
                TicketDomainModuleRegistry.ResidencePermit.BuildOperatorIndication(record)
            };
            if (record.Isee <= 0m)
                indications.Add("ISEE DSU assente o non valorizzato");
            if (blocks.Assessment.HasRelevantBlocks)
                indications.Add($"blocchi rilevanti: {blocks.Assessment.RelevantBlocks}");

            row["INDICAZIONE_OPERATORE"] = string.Join("; ", indications);
        }
    }

    internal sealed class IbanTicketTopicSheet : OfficeTicketTopicSheetBase
    {
        public override int Order => 80;
        public override string SheetName => "IBAN";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.IBAN);

        protected override void ConfigureColumns(DataTable table)
        {
            AddReferenceColumns(table);
            AddOfficeColumn(table, "MODALITA_PAGAMENTO");
            AddOfficeColumn(table, "IBAN");
            AddOfficeColumn(table, "DATA_VALIDITA_IBAN", typeof(DateTime));
            AddOfficeColumn(table, "SWIFT");
            AddOfficeColumn(table, "BONIFICO_ESTERO");
            AddOfficeColumn(table, "IMPORTO_PAGATO_BS", typeof(decimal));
            AddOfficeColumn(table, "MANDATI_BS");
            AddOfficeColumn(table, "BLOCCHI");
            AddOfficeColumn(table, "INDICAZIONE_OPERATORE");
        }

        protected override void PopulateRow(DataRow row, TicketOfficeRecord? record)
        {
            if (!PopulateReference(row, record))
                return;

            row["MODALITA_PAGAMENTO"] = record!.PaymentMethod;
            row["IBAN"] = record.Iban;
            row["DATA_VALIDITA_IBAN"] = record.IbanDataValidita.HasValue
                ? (object)record.IbanDataValidita.Value
                : DBNull.Value;
            row["SWIFT"] = record.Swift;
            row["BONIFICO_ESTERO"] = YesNo(record.ForeignTransfer);
            row["IMPORTO_PAGATO_BS"] = record.BsPaidAmount;
            row["MANDATI_BS"] = record.BsMandates;
            row["BLOCCHI"] = record.Blocks;
            row["INDICAZIONE_OPERATORE"] = TicketDomainModuleRegistry.Iban.BuildOperatorIndication(record);
        }
    }

    internal sealed class GraduatorieTicketTopicSheet : OfficeTicketTopicSheetBase
    {
        public override int Order => 90;
        public override string SheetName => "Graduatorie";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.GRADUATORIE);

        protected override void ConfigureColumns(DataTable table)
        {
            AddReferenceColumns(table);
            AddOfficeColumn(table, "ESITO_BS");
            AddOfficeColumn(table, "ESITO_PA");
            AddOfficeColumn(table, "ESITO_CI");
            AddOfficeColumn(table, "GRADUATORIE_PROVVISORIE");
            AddOfficeColumn(table, "GRADUATORIE_DEFINITIVE");
            AddOfficeColumn(table, "BLOCCHI");
            AddOfficeColumn(table, "INDICAZIONE_OPERATORE");
        }

        protected override void PopulateRow(DataRow row, TicketOfficeRecord? record)
        {
            if (!PopulateReference(row, record))
                return;

            row["ESITO_BS"] = PagamentiOperatoreTicketTopicSheet.FormatOutcome(record!.BsOutcome);
            row["ESITO_PA"] = PagamentiOperatoreTicketTopicSheet.FormatOutcome(record.PaOutcome);
            row["ESITO_CI"] = PagamentiOperatoreTicketTopicSheet.FormatOutcome(record.CiOutcome);
            row["GRADUATORIE_PROVVISORIE"] = record.ProvisionalRankings;
            row["GRADUATORIE_DEFINITIVE"] = record.FinalRankings;
            row["BLOCCHI"] = record.Blocks;
            row["INDICAZIONE_OPERATORE"] =
                "Confrontare il beneficio citato nel ticket con graduatoria ed esito corrispondenti.";
        }
    }

    internal sealed class MobilitaTicketTopicSheet : OfficeTicketTopicSheetBase
    {
        public override int Order => 100;
        public override string SheetName => "Mobilita";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.MOBILITA);

        protected override void ConfigureColumns(DataTable table)
        {
            AddReferenceColumns(table);
            AddOfficeColumn(table, "CONTRIBUTO_MOBILITA_RICHIESTO");
            AddOfficeColumn(table, "ESITO_CI");
            AddOfficeColumn(table, "IMPORTO_CI", typeof(decimal));
            AddOfficeColumn(table, "IMPORTO_PAGATO_BS", typeof(decimal));
            AddOfficeColumn(table, "BLOCCHI");
            AddOfficeColumn(table, "INDICAZIONE_OPERATORE");
        }

        protected override void PopulateRow(DataRow row, TicketOfficeRecord? record)
        {
            if (!PopulateReference(row, record))
                return;

            row["CONTRIBUTO_MOBILITA_RICHIESTO"] = YesNo(record!.MobilityBenefitRequested);
            row["ESITO_CI"] = PagamentiOperatoreTicketTopicSheet.FormatOutcome(record.CiOutcome);
            row["IMPORTO_CI"] = record.CiAmount;
            row["IMPORTO_PAGATO_BS"] = record.BsPaidAmount;
            row["BLOCCHI"] = record.Blocks;
            row["INDICAZIONE_OPERATORE"] = !record.MobilityBenefitRequested
                ? "Contributo mobilità non risultante tra i benefici richiesti."
                : "Confrontare esito e importo CI con quanto dichiarato nel ticket.";
        }
    }

    internal sealed class PortaleTicketTopicSheet : OfficeTicketTopicSheetBase
    {
        public override int Order => 110;
        public override string SheetName => "Portale e accesso";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.PORTALE_E_ACCESSO);

        protected override void ConfigureColumns(DataTable table)
        {
            AddReferenceColumns(table);
            AddOfficeColumn(table, "BLOCCHI");
            AddOfficeColumn(table, "INDICAZIONE_OPERATORE");
        }

        protected override void PopulateRow(DataRow row, TicketOfficeRecord? record)
        {
            if (!PopulateReference(row, record))
                return;

            row["BLOCCHI"] = record!.Blocks;
            row["INDICAZIONE_OPERATORE"] =
                record.CompilationStatus >= 90
                    ? "Domanda trasmessa: verificare il problema di accesso descritto nel messaggio."
                    : "Domanda non trasmessa/completa: verificare compilazione e blocchi presenti.";
        }
    }

    internal sealed class AltroTicketTopicSheet : TicketTopicSheetBase
    {
        public override int Order => 120;
        public override string SheetName => "Altro da verificare";

        protected override IEnumerable<string> AdditionalColumns => new[]
        {
            "OGGETTO",
            "CATEGORIA",
            "SOTTOCATEGORIA",
            "CLASSIFICAZIONE_DA_VERIFICARE"
        };

        protected override bool Matches(DataRow row)
        {
            string primary = SafeString(row, "ARGOMENTO_PRIMARIO");
            return string.IsNullOrWhiteSpace(primary) ||
                   string.Equals(
                       primary,
                       PrimaryTopic.ALTRO.ToString(),
                       StringComparison.OrdinalIgnoreCase);
        }

        public override DataTable BuildSheet(TicketTopicSheetContext context)
        {
            Logger.LogInfo(null, "Avvio costruzione foglio Altro da verificare");
            DataTable result = base.BuildSheet(context);
            Logger.LogInfo(
                null,
                $"Foglio Altro da verificare completato. Ticket da riclassificare: {result.Rows.Count}");
            return result;
        }
    }
}
