using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcedureNet7
{
    /// <summary>
    /// Registro centrale dei moduli di dominio. Gli argomenti non ancora migrati
    /// continuano nel percorso storico; i moduli registrati sostituiscono il codice
    /// trasversale per controlli, estrazioni e validazioni del proprio ambito.
    /// </summary>
    internal static class TicketDomainModuleRegistry
    {
        private static readonly PaymentsTicketDomainModule PaymentsModule = new();
        private static readonly ResidencePermitTicketDomainModule ResidencePermitModule = new();
        private static readonly BlocksTicketDomainModule BlocksModule = new();
        private static readonly IbanTicketDomainModule IbanModule = new();
        private static readonly ApplicationWindowTicketDomainModule ApplicationWindowModule = new();
        private static readonly DomicileContractTicketDomainModule DomicileContractModule = new();

        private static readonly IReadOnlyList<ITicketDomainModule> OrderedModules =
            new ITicketDomainModule[]
            {
                PaymentsModule,
                ResidencePermitModule,
                BlocksModule,
                IbanModule,
                ApplicationWindowModule,
                DomicileContractModule
            };

        private static readonly IReadOnlyDictionary<string, ITicketDomainModule> Modules =
            OrderedModules.ToDictionary(module => module.Code, StringComparer.OrdinalIgnoreCase);

        public static PaymentsTicketDomainModule Payments => PaymentsModule;
        public static ResidencePermitTicketDomainModule ResidencePermit => ResidencePermitModule;
        public static BlocksTicketDomainModule Blocks => BlocksModule;
        public static IbanTicketDomainModule Iban => IbanModule;
        public static ApplicationWindowTicketDomainModule ApplicationWindow => ApplicationWindowModule;
        public static DomicileContractTicketDomainModule DomicileContract => DomicileContractModule;

        public static TicketDomainRequest CreateRequest(
            string subject,
            string message,
            string primaryTopicCode = "",
            string secondaryTopicCode = "",
            bool referredToBlocks = false,
            TicketIntent? recognisedIntent = null) => new()
        {
            Subject = subject ?? string.Empty,
            Message = message ?? string.Empty,
            PrimaryTopicCode = primaryTopicCode ?? string.Empty,
            SecondaryTopicCode = secondaryTopicCode ?? string.Empty,
            ReferredToBlocks = referredToBlocks,
            RecognisedIntent = recognisedIntent
        };

        public static TicketResolutionScope AnalyzeResolutionScope(TicketDomainRequest request)
        {
            IReadOnlyList<TicketDomainScopeMatch> matches = OrderedModules
                .Select(module => module.DetectScope(request))
                .Where(match => match.IsRequested)
                .ToArray();

            bool scholarshipPayments = HasMatch(matches, PaymentsModule.Code);
            bool residencePermitDocuments = HasMatch(matches, ResidencePermitModule.Code);
            bool applicationBlocks = HasMatch(matches, BlocksModule.Code);
            bool ibanUpdates = HasMatch(matches, IbanModule.Code);
            bool applicationWindowInformation = HasMatch(matches, ApplicationWindowModule.Code);
            bool domicileContract = HasMatch(matches, DomicileContractModule.Code);

            // Ordine stabile dei segnali, indipendente dall'ordine interno del dizionario.
            var signals = new List<string>();
            if (scholarshipPayments)
                signals.Add("EROGAZIONE_BORSA");
            if (residencePermitDocuments)
                signals.Add("PERMESSO_SOGGIORNO");
            if (applicationBlocks)
                signals.Add("BLOCCHI_PRATICA");
            if (ibanUpdates)
                signals.Add("AGGIORNAMENTO_IBAN");
            if (applicationWindowInformation)
                signals.Add("FINESTRA_COMPILAZIONE_DOMANDA");
            if (domicileContract)
                signals.Add("DOMICILIO_CONTRATTO");

            return new TicketResolutionScope
            {
                ScholarshipPayments = scholarshipPayments,
                ResidencePermitDocuments = residencePermitDocuments,
                ApplicationBlocks = applicationBlocks,
                IbanUpdates = ibanUpdates,
                ApplicationWindowInformation = applicationWindowInformation,
                DomicileContract = domicileContract,
                Signals = string.Join(" + ", signals),
                Request = request,
                DomainMatches = matches
            };
        }

        public static IReadOnlyList<TicketDomainValidationResult> ValidateRequestedScopes(
            TicketResolutionScope scope,
            TicketOfficeRecord? record,
            PaymentRequestKind paymentRequest,
            DateTime? ticketCreationDate = null)
        {
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            var context = new TicketDomainValidationContext
            {
                Request = scope.Request,
                Record = record,
                PaymentRequest = paymentRequest,
                TicketCreationDate = ticketCreationDate
            };

            var results = new List<TicketDomainValidationResult>();
            foreach (TicketDomainScopeMatch match in scope.DomainMatches)
            {
                if (!match.IsRequested || !Modules.TryGetValue(match.DomainCode, out ITicketDomainModule? module))
                    continue;

                results.Add(module.Validate(context));
            }

            return results;
        }

        private static bool HasMatch(IEnumerable<TicketDomainScopeMatch> matches, string code) =>
            matches.Any(match =>
                match.IsRequested &&
                string.Equals(match.DomainCode, code, StringComparison.OrdinalIgnoreCase));
    }
}
