using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class TopicOperationalDecision
    {
        public string Decision { get; init; } = string.Empty;
        public string RequiredAction { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string ResponseCode { get; init; } = string.Empty;
    }

    internal interface ITicketTopicModule
    {
        string Code { get; }
        string Label { get; }
        string DetailSheet { get; }
        ITicketTopicSheet CreateSheet();
        TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record);
        string BuildEvidence(TicketOfficeRecord record);
    }

    internal abstract class TicketTopicModuleBase : ITicketTopicModule
    {
        protected const string ClosePersonalized = "FUORI_PERIMETRO_RISCONTRO_ASSISTITO";
        protected const string ActionRequired = "NON_CHIUDIBILE_AZIONE_OPERATIVA_NECESSARIA";
        protected const string DataMissing = "NON_CHIUDIBILE_DOCUMENTO_O_DATO_MANCANTE";
        protected const string Verify = "DA_VERIFICARE_DATI_INSUFFICIENTI";

        private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

        public abstract string Code { get; }
        public abstract string Label { get; }
        public abstract string DetailSheet { get; }
        public abstract ITicketTopicSheet CreateSheet();
        public abstract TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record);
        public abstract string BuildEvidence(TicketOfficeRecord record);

        protected static TopicOperationalDecision Decision(
            string decision,
            string action,
            string reason,
            string responseCode) => new()
        {
            Decision = decision,
            RequiredAction = action,
            Reason = reason,
            ResponseCode = responseCode
        };

        protected static void AddIfNotEmpty(List<string> values, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values.Add($"{label}: {value}");
        }

        protected static string FormatAmount(decimal amount) =>
            amount.ToString("N2", ItalianCulture);

        protected static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
                return value ?? string.Empty;
            return value[..Math.Max(0, maxLength - 3)] + "...";
        }

        protected static string JoinEvidence(IEnumerable<string> values) =>
            string.Join("; ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    internal sealed class PaymentsTopicModule : TicketTopicModuleBase
    {
        public override string Code => "PAGAMENTI_E_TASSE";
        public override string Label => "Pagamenti e tasse";
        public override string DetailSheet => "10_Pagamenti";
        public override ITicketTopicSheet CreateSheet() => new PagamentiOperatoreTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record)
        {
            PaymentOperationalData payment = TicketDomainModuleRegistry.Payments.Extract(record);
            decimal assignedAmount = payment.AssignedAmount;
            decimal residual = payment.ResidualAmount;

            if (record.BsOutcome == "0")
                return Decision(ClosePersonalized, "Predisporre riscontro con l'esito disponibile.", "La domanda risulta esclusa dalla borsa.", "PAG_07");

            if (record.HasUnclassifiedBsPaymentCodes)
                return Decision(Verify, "Verificare i codici BS non classificati prima di calcolare importi o residui.", "Sono presenti movimenti BS con codici non inclusi nella mappa BSP/BSS/BSI; tali importi non entrano nelle somme automatiche.", "PAG_11");

            if (record.HasBsStornedPayments && record.BsPaidAmount <= 0m)
                return Decision(ActionRequired, "Verificare la causa dello storno e l'eventuale riemissione del pagamento.", "Sono presenti pagamenti BS ritirati dall'azienda: risultano stornati e non devono essere considerati accreditati allo studente.", "PAG_10");

            if (record.BenefitAmount > 0m && record.SpecificheAmount > 0m &&
                Math.Abs(record.BenefitAmount - record.SpecificheAmount) >= 0.01m)
            {
                return Decision(ActionRequired, "Verificare coerenza tra esito e impegni contabili.", "L'importo dell'esito differisce dalle specifiche degli impegni.", "BEN_02");
            }

            if (string.IsNullOrWhiteSpace(record.Iban) &&
                (record.BsOutcome == "2" || assignedAmount > record.BsPaidAmount + 0.01m))
            {
                return Decision(DataMissing, "Richiedere o verificare l'IBAN e la modalità di pagamento.", "Non risulta un IBAN per una pratica con pagamento potenzialmente dovuto.", "PAG_04");
            }

            if (record.BsOutcome == "2" && record.BsPaidAmount <= 0m)
                return Decision(ActionRequired, "Verificare iter contabile, impegni e mandati.", "La domanda risulta vincitrice ma non emergono pagamenti BS.", "PAG_05");

            if (assignedAmount > 0m &&
                residual <= 0.01m &&
                !string.IsNullOrWhiteSpace(record.BsMandates))
            {
                return Decision("CHIUDIBILE_SITUAZIONE_GIA_RISOLTA", "Confermare mandato e importo e chiudere il ticket.", "L'importo assegnato risulta interamente pagato, al netto di reversali e detrazioni.", "PAG_01");
            }

            if (record.BsPaidAmount > 0m && residual > 0.01m)
                return Decision(ActionRequired, "Verificare rate, residuo, reversali e detrazioni.", "Risulta un pagamento parziale con residuo stimato.", "PAG_06");

            return Decision(Verify, "Confrontare la richiesta con esito, importi e pagamenti BS disponibili.", "I dati contabili non consentono una proposta di chiusura automatica.", "PAG_08");
        }

        public override string BuildEvidence(TicketOfficeRecord record) =>
            TicketDomainModuleRegistry.Payments.BuildEvidence(record);
    }

    internal sealed class BenefitsTopicModule : TicketTopicModuleBase
    {
        public override string Code => "BENEFICI_E_IMPORTI";
        public override string Label => "Benefici e importi";
        public override string DetailSheet => "11_Benefici e importi";
        public override ITicketTopicSheet CreateSheet() => new BeneficiImportiTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record)
        {
            if (record.BsOutcome == "0")
                return Decision(ClosePersonalized, "Predisporre riscontro con l'esito disponibile.", "La domanda risulta esclusa dalla borsa.", "PAG_07");

            if (record.BenefitAmount > 0m && record.SpecificheAmount > 0m &&
                Math.Abs(record.BenefitAmount - record.SpecificheAmount) >= 0.01m)
            {
                return Decision(ActionRequired, "Verificare coerenza tra esito e impegni contabili.", "L'importo dell'esito differisce dalle specifiche degli impegni.", "BEN_02");
            }

            if (record.BsOutcome == "2" && record.BsPaidAmount <= 0m && record.BenefitAmount > 0m)
                return Decision(ActionRequired, "Verificare iter contabile, impegni e mandati.", "La domanda risulta vincitrice senza pagamenti BS registrati.", "PAG_05");

            return Decision(ClosePersonalized, "Confrontare testo del ticket con esito e importi estratti.", "Sono disponibili dati su esito o importi della domanda.", "BEN_01");
        }

        public override string BuildEvidence(TicketOfficeRecord record) =>
            TicketDomainModuleRegistry.Payments.BuildEvidence(record);
    }

    internal sealed class HousingTopicModule : TicketTopicModuleBase
    {
        public override string Code => "ALLOGGIO";
        public override string Label => "Alloggio";
        public override string DetailSheet => "12_Alloggio";
        public override ITicketTopicSheet CreateSheet() => new AlloggioTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record)
        {
            if (record.HasOpenDomicileRequest)
                return Decision(ActionRequired, "Verificare lavorazione dell'istanza di domicilio.", "Risulta un'istanza di domicilio ancora aperta.", "ALL_01");
            if (string.IsNullOrWhiteSpace(record.ContractSeries))
                return Decision(DataMissing, "Verificare presenza, validità o integrazione del contratto.", "Non risulta un contratto di domicilio associato alla domanda.", "ALL_02");
            if (record.PaOutcome == "0")
                return Decision(ClosePersonalized, "Predisporre riscontro con esito alloggio disponibile.", "La domanda risulta esclusa dal posto alloggio.", "ALL_03");
            return Decision(ClosePersonalized, "Confrontare richiesta, esito alloggio e dati di domicilio.", "Sono disponibili dati su alloggio, domicilio o contratto.", "ALL_04");
        }

        public override string BuildEvidence(TicketOfficeRecord record)
        {
            var values = new List<string>();
            AddIfNotEmpty(values, "esito PA", PaymentsTicketDomainModule.FormatOutcome(record.PaOutcome));
            AddIfNotEmpty(values, "domicilio", record.DomicileMunicipality);
            if (record.HasOpenDomicileRequest)
                values.Add("istanza domicilio aperta");
            return JoinEvidence(values);
        }
    }

    internal sealed class CanteenTopicModule : TicketTopicModuleBase
    {
        public override string Code => "MENSA";
        public override string Label => "Mensa";
        public override string DetailSheet => "13_Mensa";
        public override ITicketTopicSheet CreateSheet() => new MensaTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record) =>
            record.CanteenMonetizationGranted
                ? Decision(ClosePersonalized, "Confrontare richiesta con monetizzazione e pagamenti BS disponibili.", "La monetizzazione mensa risulta concessa.", "MEN_01")
                : Decision(ClosePersonalized, "Predisporre riscontro sulla monetizzazione non concessa.", "La monetizzazione mensa non risulta concessa per la domanda selezionata.", "MEN_02");

        public override string BuildEvidence(TicketOfficeRecord record) =>
            record.CanteenMonetizationGranted ? "monetizzazione concessa" : "monetizzazione non concessa";
    }

    internal sealed class DocumentsTopicModule : TicketTopicModuleBase
    {
        public override string Code => "DOCUMENTI_E_PERMESSI";
        public override string Label => "Documenti e permessi";
        public override string DetailSheet => "14_Documenti e permessi";
        public override ITicketTopicSheet CreateSheet() => new DocumentiPermessiTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record)
        {
            ResidencePermitOperationalData permit = TicketDomainModuleRegistry.ResidencePermit.Extract(record);
            if (string.IsNullOrWhiteSpace(permit.RawDocuments))
                return Decision(DataMissing, "Verificare allegati e richiedere eventuale integrazione.", "Non risultano documenti del permesso di soggiorno.", "DOC_01");
            if (record.Isee <= 0m)
                return Decision(DataMissing, "Verificare DSU, acquisizione ISEE e possibili integrazioni.", "L'ISEE DSU non risulta valorizzato.", "DOC_02");
            return Decision(ClosePersonalized, "Controllare stato dei documenti e dati economici dichiarati.", "I documenti e i dati economici risultano disponibili per la domanda selezionata.", "DOC_03");
        }

        public override string BuildEvidence(TicketOfficeRecord record)
        {
            var values = new List<string>
            {
                TicketDomainModuleRegistry.ResidencePermit.BuildEvidence(record),
                $"ISEE {FormatAmount(record.Isee)}"
            };
            return JoinEvidence(values);
        }
    }

    internal sealed class CareersTopicModule : TicketTopicModuleBase
    {
        public override string Code => "ISCRIZIONE_E_CARRIERA";
        public override string Label => "Carriere";
        public override string DetailSheet => "15_Carriere";
        public override ITicketTopicSheet CreateSheet() => new CarriereTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record) =>
            Decision(Verify, "Verificare carriera, anno di corso, crediti e precedenti partecipazioni.", "La richiesta riguarda dati di carriera che richiedono una verifica puntuale.", "CAR_01");

        public override string BuildEvidence(TicketOfficeRecord record)
        {
            var values = new List<string>();
            AddIfNotEmpty(values, "sede", record.StudyLocation);
            AddIfNotEmpty(values, "esito BS", PaymentsTicketDomainModule.FormatOutcome(record.BsOutcome));
            AddIfNotEmpty(values, "status compilazione", record.CompilationStatus.ToString(CultureInfo.InvariantCulture));
            values.Add(string.IsNullOrWhiteSpace(record.Blocks) ? "blocchi assenti" : "blocchi presenti");
            return JoinEvidence(values);
        }
    }

    internal sealed class RankingsTopicModule : TicketTopicModuleBase
    {
        public override string Code => "GRADUATORIE";
        public override string Label => "Graduatorie";
        public override string DetailSheet => "16_Graduatorie";
        public override ITicketTopicSheet CreateSheet() => new GraduatorieTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record) =>
            string.IsNullOrWhiteSpace(record.FinalRankings)
                ? Decision(ActionRequired, "Verificare pubblicazione, caricamento o stato del procedimento.", "Non risulta una graduatoria definitiva per la domanda selezionata.", "GRA_02")
                : Decision(ClosePersonalized, "Confrontare beneficio richiesto, esito e graduatoria definitiva.", "È disponibile almeno una graduatoria definitiva.", "GRA_01");

        public override string BuildEvidence(TicketOfficeRecord record)
        {
            var values = new List<string>();
            AddIfNotEmpty(values, "graduatorie definitive", record.FinalRankings);
            return JoinEvidence(values);
        }
    }

    internal sealed class MobilityTopicModule : TicketTopicModuleBase
    {
        public override string Code => "MOBILITA";
        public override string Label => "Mobilità";
        public override string DetailSheet => "17_Mobilita";
        public override ITicketTopicSheet CreateSheet() => new MobilitaTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record)
        {
            if (!record.MobilityBenefitRequested)
                return Decision(ClosePersonalized, "Confrontare richiesta dello studente con i benefici registrati.", "Il contributo di mobilità non risulta tra i benefici richiesti.", "MOB_01");
            if (string.IsNullOrWhiteSpace(record.CiOutcome) && record.CiAmount <= 0m)
                return Decision(Verify, "Verificare richiesta, esito CI e documentazione collegata.", "Il contributo di mobilità risulta richiesto ma non emergono esito o importo CI.", "MOB_02");
            return Decision(ClosePersonalized, "Confrontare esito e importo CI con quanto dichiarato nel ticket.", "Sono disponibili dati sul contributo mobilità richiesto.", "MOB_03");
        }

        public override string BuildEvidence(TicketOfficeRecord record)
        {
            var values = new List<string>
            {
                record.MobilityBenefitRequested ? "contributo mobilità richiesto" : "contributo mobilità non richiesto"
            };
            AddIfNotEmpty(values, "esito CI", PaymentsTicketDomainModule.FormatOutcome(record.CiOutcome));
            return JoinEvidence(values);
        }
    }

    internal sealed class IbanTopicModule : TicketTopicModuleBase
    {
        public override string Code => "IBAN";
        public override string Label => "IBAN";
        public override string DetailSheet => "18_IBAN";
        public override ITicketTopicSheet CreateSheet() => new IbanTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record) =>
            string.IsNullOrWhiteSpace(record.Iban)
                ? Decision(DataMissing, "Richiedere o verificare l'IBAN e la modalità di pagamento.", "Non risulta un IBAN associato alla domanda.", "PAG_04")
                : Decision(ClosePersonalized, "Verificare eventuali blocchi e mandati prima dell'invio.", "L'IBAN risulta presente nei dati operativi.", "IBAN_01");

        public override string BuildEvidence(TicketOfficeRecord record)
        {
            var values = new List<string>();
            AddIfNotEmpty(values, "IBAN", record.Iban);
            AddIfNotEmpty(values, "mandati BS", record.BsMandates);
            return JoinEvidence(values);
        }
    }

    internal sealed class PortalTopicModule : TicketTopicModuleBase
    {
        public override string Code => "PORTALE_E_ACCESSO";
        public override string Label => "Portale e accesso";
        public override string DetailSheet => "19_Portale e accesso";
        public override ITicketTopicSheet CreateSheet() => new PortaleTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record) =>
            Decision(Verify, "Verificare problema tecnico/accesso e stato domanda.", "Il ticket riguarda un accesso o una funzionalità del portale e richiede verifica puntuale.", "POR_01");

        public override string BuildEvidence(TicketOfficeRecord record)
        {
            var values = new List<string>();
            AddIfNotEmpty(values, "sede", record.StudyLocation);
            AddIfNotEmpty(values, "esito BS", PaymentsTicketDomainModule.FormatOutcome(record.BsOutcome));
            return JoinEvidence(values);
        }
    }

    internal sealed class OtherTopicModule : TicketTopicModuleBase
    {
        public override string Code => "ALTRO";
        public override string Label => "Altro da verificare";
        public override string DetailSheet => "20_Altro da verificare";
        public override ITicketTopicSheet CreateSheet() => new AltroTicketTopicSheet();

        public override TopicOperationalDecision BuildFallbackDecision(TicketOfficeRecord record) =>
            Decision(Verify, "Leggere il ticket e assegnare l'ufficio o argomento corretto.", "L'argomento non rientra nelle regole operative automatiche.", "ALT_01");

        public override string BuildEvidence(TicketOfficeRecord record)
        {
            var values = new List<string>();
            AddIfNotEmpty(values, "sede", record.StudyLocation);
            AddIfNotEmpty(values, "esito BS", PaymentsTicketDomainModule.FormatOutcome(record.BsOutcome));
            return JoinEvidence(values);
        }
    }

    internal static class TicketTopicModuleRegistry
    {
        private static readonly IReadOnlyList<ITicketTopicModule> OrderedModules =
            new ITicketTopicModule[]
            {
                new CareersTopicModule(),
                new PaymentsTopicModule(),
                new BenefitsTopicModule(),
                new HousingTopicModule(),
                new CanteenTopicModule(),
                new DocumentsTopicModule(),
                new IbanTopicModule(),
                new RankingsTopicModule(),
                new MobilityTopicModule(),
                new PortalTopicModule(),
                new OtherTopicModule()
            };

        private static readonly IReadOnlyDictionary<string, ITicketTopicModule> Modules =
            OrderedModules.ToDictionary(module => module.Code, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<ITicketTopicModule> All => OrderedModules;

        public static ITicketTopicSheet[] CreateSheets() =>
            OrderedModules
                .Select(module => module.CreateSheet())
                .OrderBy(sheet => sheet.Order)
                .ToArray();

        public static ITicketTopicModule Resolve(string topicCode) =>
            Modules.TryGetValue(topicCode ?? string.Empty, out ITicketTopicModule? module)
                ? module
                : Modules["ALTRO"];

        public static bool TryResolve(string topicCode, out ITicketTopicModule module)
        {
            if (Modules.TryGetValue(topicCode ?? string.Empty, out ITicketTopicModule? resolved))
            {
                module = resolved;
                return true;
            }

            module = Modules["ALTRO"];
            return false;
        }
    }
}
