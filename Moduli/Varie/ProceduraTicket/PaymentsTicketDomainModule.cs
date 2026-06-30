using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed class PaymentOperationalData
    {
        public decimal AssignedAmount { get; init; }
        public decimal PaidAmount { get; init; }
        public decimal ResidualAmount { get; init; }
        public decimal FirstInstallmentAmount { get; init; }
        public decimal BalanceAmount { get; init; }
        public decimal IntegrationAmount { get; init; }
        public bool HasBlockingPaymentEvidence { get; init; }
        public string Mandates { get; init; } = string.Empty;
        public string FirstInstallmentMandates { get; init; } = string.Empty;
        public string BalanceMandates { get; init; } = string.Empty;
        public string IntegrationMandates { get; init; } = string.Empty;
    }

    /// <summary>
    /// Dominio Pagamenti: riconoscimento della richiesta, estrazione contabile,
    /// validazione delle rate e indicazioni operative del foglio Pagamenti.
    /// </summary>
    internal sealed class PaymentsTicketDomainModule : ITicketDomainModule
    {
        public const string DomainCode = "PAGAMENTI";
        private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

        private static readonly string[] StrongTokens =
        {
            "pagamento borsa", "pagamento della borsa", "pagamento del beneficio",
            "accredito borsa", "accredito della borsa", "erogazione borsa",
            "erogazione della borsa", "liquidazione borsa", "mandato di pagamento",
            "bonifico della borsa", "saldo della borsa", "saldo borsa",
            "rata della borsa", "tranche della borsa", "seconda tranche", "riaccredito della borsa",
            "integrazione borsa", "integrazione della borsa", "mancata integrazione bds",
            "mancata integrazione della borsa",
            "scholarship payment", "scholarship bank transfer"
        };

        private static readonly string[] PaymentActionTokens =
        {
            "accredit", "erog", "liquid", "mandato", "bonifico", "pagament",
            "pagamento non ricevuto", "non ho ricevuto la borsa", "quando arriva il pagamento",
            "quando arriva l accredito", "quando verra pagata la borsa", "quando sara pagata la borsa",
            "mancata integrazione", "pagamento integrazione", "pagamento dell integrazione",
            "when will scholarship be paid", "payment not received"
        };

        private static readonly string[] InstallmentTokens =
        {
            "prima rata", "seconda rata", "seconda tranche", "tranche", "saldo", "rate rimanenti", "rata rimanente",
            "first installment", "second installment", "balance"
        };

        private static readonly string[] IncomingPaymentTokens =
        {
            "non ricevut", "non ho ricevuto", "not received", "non accredit", "mancato accredito",
            "quando arriva il pagamento", "quando arriva l accredito", "quando arriva l'accredito", "quando ricevero il pagamento",
            "quando potro ricevere la borsa", "attendo il pagamento", "attendo l accredito", "attendo l'accredito",
            "in attesa del pagamento", "in attesa dell accredito", "in attesa dell'accredito", "ritardo nel pagamento",
            "ritardo dell accredito", "ritardo dell'accredito", "stato pagamento", "stato del pagamento",
            "verifica pagamento", "verifica del pagamento", "verificare il pagamento",
            "verifica accredito", "verifica dell accredito", "verifica dell'accredito",
            "verifica erogazione", "verifica dell erogazione", "verifica dell'erogazione",
            "richiedo una verifica", "chiedo verifica",
            "pagamento effettuato", "pagamento risulta", "risulta pagata la borsa",
            "is scholarship paid", "banca ha bloccato il pagamento",
            "restituito l importo al mittente", "restituito l'importo al mittente", "riaccredito", "pagamento in entrata"
        };

        private static readonly string[] PaymentEvidenceRequestTokens =
        {
            "ricevuta di pagamento", "attestazione", "certificato", "dichiarazione",
            "documento ufficiale", "prova dei mezzi", "ricevuta del pagamento"
        };

        private static readonly string[] PaymentReturnedOrReissueTokens =
        {
            "respinto", "respinta", "verra respinto", "verrà respinto", "restituito al mittente",
            "restituita al mittente", "ritornato al mittente", "stornato", "stornata", "storno",
            "riemissione", "riemettere", "ripetuto il bonifico", "conto bloccato", "carta bloccata",
            "temporaneamente bloccato", "temporaneamente bloccata", "banca ha bloccato",
            "bonifico bloccato", "reverted back", "returned to sender", "payment reverted",
            "bank rejected", "rejected transfer", "reissue payment"
        };

        private static readonly string[] PaymentClarificationTokens =
        {
            "natura di tale importo", "natura dell importo", "quota di", "pagamento visualizzato",
            "importo visualizzato", "confermare la natura", "problemi tecnici nell invio",
            "problemi tecnici nell'invio"
        };

        private static readonly string[] PreviousFirstInstallmentTokens =
        {
            "ho ricevuto la prima rata", "ricevuto la prima rata", "prima rata in precedenza",
            "regolarmente ricevuto la prima rata", "first installment already received"
        };

        private static readonly string[] ScholarshipContextTokens =
        {
            "borsa", "beneficio", "contributo", "scholarship", "grant"
        };

        private static readonly string[] StudentPaidTaxTokens =
        {
            "tassa regionale", "tassa universitaria", "tassa di iscrizione",
            "retta universitaria", "pagopa", "iuv", "ricevuta della tassa"
        };

        private static readonly string[] NonScholarshipContributionTokens =
        {
            "contributo mobilita", "contributo mobilità", "mobilita internazionale",
            "mobilità internazionale", "erasmus", "scambio", "exchange", "mobility grant"
        };

        private static readonly string[] FirstInstallmentTokens =
        {
            "prima rata", "1 rata", "1a rata", "primo pagamento", "acconto"
        };

        private static readonly string[] BalanceTokens =
        {
            "saldo", "seconda rata", "seconda tranche", "2 rata", "2a rata", "rata finale"
        };

        private static readonly string[] IntegrationTokens =
        {
            "integrazione", "integrazione prima rata", "integrazione saldo",
            "integrazione bds", "integrazione borsa", "integrazione della borsa",
            "mancata integrazione", "mancata integrazione bds",
            "integrazione alla borsa", "integrazione come fuori sede",
            "pagamento integrazione", "pagamento dell integrazione"
        };

        public string Code => DomainCode;

        public TicketDomainScopeMatch DetectScope(TicketDomainRequest request)
        {
            string text = request.NormalizedText;
            bool informationalOnly = request.RecognisedIntent == TicketIntent.PRE_DOMANDA ||
                                     request.RecognisedIntent == TicketIntent.INFORMAZIONE;
            bool strongScholarshipPayment = TicketDomainText.ContainsAny(text, StrongTokens);
            bool scholarshipContext = TicketDomainText.ContainsAny(text, ScholarshipContextTokens);
            bool explicitPaymentAction = TicketDomainText.ContainsAny(text, PaymentActionTokens);
            bool explicitInstallment = TicketDomainText.ContainsAny(text, InstallmentTokens);
            bool explicitPaymentIssue = TicketDomainText.ContainsAny(text, IncomingPaymentTokens);
            bool contextualScholarshipPayment = scholarshipContext &&
                                                (explicitPaymentAction || explicitInstallment);
            bool onlyStudentPaidTax =
                TicketDomainText.ContainsAny(text, StudentPaidTaxTokens) &&
                !strongScholarshipPayment &&
                !TicketDomainText.ContainsAny(
                    text,
                    "accredito",
                    "erogazione",
                    "liquidazione",
                    "rata della borsa",
                    "saldo della borsa");
            bool otherContribution =
                TicketDomainText.ContainsAny(text, NonScholarshipContributionTokens) &&
                !strongScholarshipPayment &&
                !TicketDomainText.ContainsAny(text, "borsa di studio", "scholarship");

            bool requested =
                !informationalOnly &&
                !onlyStudentPaidTax &&
                !otherContribution &&
                (strongScholarshipPayment || contextualScholarshipPayment) &&
                explicitPaymentIssue &&
                !TicketDomainText.ContainsAny(text, PaymentEvidenceRequestTokens);

            return new TicketDomainScopeMatch
            {
                DomainCode = Code,
                Signal = "EROGAZIONE_BORSA",
                IsRequested = requested
            };
        }

        public PaymentRequestKind ClassifyPaymentRequest(
            string subject,
            string message,
            string secondaryTopicCode = "")
        {
            string text = TicketDomainText.Normalize($"{subject} {message}");
            if (TicketDomainText.ContainsAny(text, IntegrationTokens))
                return PaymentRequestKind.INTEGRAZIONE;
            if (TicketDomainText.ContainsAny(text, BalanceTokens) ||
                TicketDomainText.TopicContainsAny(secondaryTopicCode, "SALDO"))
            {
                return PaymentRequestKind.SALDO;
            }
            if (TicketDomainText.ContainsAny(text, FirstInstallmentTokens) &&
                !TicketDomainText.ContainsAny(text, PreviousFirstInstallmentTokens))
            {
                return PaymentRequestKind.PRIMA_RATA;
            }
            return PaymentRequestKind.GENERICO;
        }

        public PaymentOperationalData Extract(TicketOfficeRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            decimal assignedAmount = record.SpecificheAmount > 0m
                ? record.SpecificheAmount
                : record.BenefitAmount;
            decimal residual = Math.Max(
                0m,
                assignedAmount - record.BsPaidAmount + record.BsReversalAmount - record.DeductionAmount);

            return new PaymentOperationalData
            {
                AssignedAmount = assignedAmount,
                PaidAmount = record.BsPaidAmount,
                ResidualAmount = residual,
                FirstInstallmentAmount = record.BsFirstInstallmentOriginalAmount + record.BsFirstInstallmentReissueAmount,
                BalanceAmount = record.BsBalanceOriginalAmount + record.BsBalanceReissueAmount,
                IntegrationAmount =
                    record.BsIntegrationFirstInstallmentOriginalAmount +
                    record.BsIntegrationFirstInstallmentReissueAmount +
                    record.BsIntegrationBalanceOriginalAmount +
                    record.BsIntegrationBalanceReissueAmount,
                HasBlockingPaymentEvidence =
                    record.HasUnclassifiedBsPaymentCodes || record.HasBsStornedPayments,
                Mandates = record.BsMandates,
                FirstInstallmentMandates = record.BsFirstInstallmentMandates,
                BalanceMandates = record.BsBalanceMandates,
                IntegrationMandates = record.BsIntegrationMandates
            };
        }

        public bool IsFullScholarshipPaid(TicketOfficeRecord record)
        {
            if (record == null)
                return false;

            PaymentOperationalData data = Extract(record);
            return data.AssignedAmount > 0m &&
                   data.PaidAmount > 0m &&
                   data.ResidualAmount <= 0.01m;
        }

        public TicketDomainValidationResult Validate(TicketDomainValidationContext context)
        {
            if (context.Record == null)
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "PAGAMENTO: DATI OPERATIVI NON DISPONIBILI",
                    UnresolvedReason = "record operativo assente",
                    PendingResponseCode = "GEN_01"
                };

            string text = context.Request.NormalizedText;
            if (TicketDomainText.ContainsAny(text, PaymentReturnedOrReissueTokens))
            {
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "PAGAMENTO: DA VERIFICARE - il ticket cita storno, bonifico respinto o riemissione",
                    UnresolvedReason = "pagamento non chiudibile automaticamente: il testo segnala possibile storno, restituzione al mittente o necessità di riemissione",
                    PendingResponseCode = "PAG_09"
                };
            }

            if (TicketDomainText.ContainsAny(text, PaymentClarificationTokens))
            {
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "PAGAMENTO: DA VERIFICARE - richiesta di chiarimento su importo specifico",
                    UnresolvedReason = "il ticket chiede chiarimenti su un importo o su una quota specifica, non la sola verifica di rata/saldo pagati",
                    PendingResponseCode = "PAG_16"
                };
            }

            PaymentOperationalData data = Extract(context.Record);
            return context.PaymentRequest switch
            {
                PaymentRequestKind.PRIMA_RATA => BuildComponentValidation(
                    "PRIMA_RATA",
                    data.FirstInstallmentAmount,
                    data.FirstInstallmentMandates,
                    data.ResidualAmount,
                    requireTotalResidualZero: false,
                    "PRIMA_RATA_BORSA_PAGATA",
                    "PAG_02",
                    "PAG_17"),
                PaymentRequestKind.SALDO => BuildComponentValidation(
                    "SALDO",
                    data.BalanceAmount,
                    data.BalanceMandates,
                    data.ResidualAmount,
                    requireTotalResidualZero: true,
                    "SALDO_BORSA_PAGATO",
                    "PAG_03",
                    "PAG_18"),
                PaymentRequestKind.INTEGRAZIONE => BuildComponentValidation(
                    "INTEGRAZIONE",
                    data.IntegrationAmount,
                    data.IntegrationMandates,
                    data.ResidualAmount,
                    requireTotalResidualZero: false,
                    "INTEGRAZIONE_BORSA_PAGATA",
                    "PAG_12",
                    "PAG_19"),
                _ => BuildFullPaymentValidation(data)
            };
        }


        public string BuildEvidence(TicketOfficeRecord record)
        {
            PaymentOperationalData data = Extract(record);
            var values = new List<string>
            {
                $"esito BS {FormatOutcome(record.BsOutcome)}",
                $"assegnato {FormatAmount(data.AssignedAmount)}",
                $"pagato {FormatAmount(data.PaidAmount)}",
                $"residuo {FormatAmount(data.ResidualAmount)}"
            };

            if (!string.IsNullOrWhiteSpace(data.Mandates))
                values.Add($"mandati BS validi {data.Mandates}");
            if (data.HasBlockingPaymentEvidence)
                values.Add("movimenti BS stornati o non classificati presenti");
            return string.Join("; ", values);
        }

        public string BuildOperatorIndication(TicketOfficeRecord record)
        {
            PaymentOperationalData data = Extract(record);
            var indications = new List<string>();

            if (record.CompilationStatus > 0 && record.CompilationStatus < 90)
                indications.Add("domanda non trasmessa/completa");
            if (record.BsOutcome == "0")
                indications.Add("studente escluso dalla borsa");
            if (string.IsNullOrWhiteSpace(record.BsOutcome))
                indications.Add("esito BS assente");
            if (!string.IsNullOrWhiteSpace(record.Blocks))
                indications.Add("blocco potenzialmente rilevante per il pagamento BS");
            if (string.IsNullOrWhiteSpace(record.Iban))
                indications.Add("IBAN assente");
            if (record.HasBsStornedPayments)
                indications.Add($"pagamenti BS stornati/non accreditati {record.BsStornedAmount:0.00}");
            if (record.HasUnclassifiedBsPaymentCodes)
                indications.Add($"codici BS non classificati: {record.BsUnclassifiedPaymentTypes}");
            if (record.BenefitAmount > 0m &&
                record.SpecificheAmount > 0m &&
                Math.Abs(record.BenefitAmount - record.SpecificheAmount) >= 0.01m)
            {
                indications.Add("importo esito diverso dalle specifiche impegni");
            }
            if (record.BenefitAmount > 0m && data.ResidualAmount <= 0.01m)
                indications.Add("pagamento BS completato");
            else if (record.BsPaidAmount > 0m)
                indications.Add($"pagamento BS parziale; residuo BS stimato {data.ResidualAmount:0.00}");
            else if (record.BsOutcome == "2")
                indications.Add("vincitore senza pagamenti BS registrati");

            return indications.Count == 0
                ? "Verificare la richiesta rispetto ai dati contabili riportati."
                : string.Join("; ", indications);
        }

        public static string FormatOutcome(string outcome) => outcome switch
        {
            "0" => "Escluso",
            "1" => "Idoneo",
            "2" => "Vincitore",
            _ => outcome ?? string.Empty
        };

        private static TicketDomainValidationResult BuildFullPaymentValidation(PaymentOperationalData data)
        {
            bool resolved = data.AssignedAmount > 0m &&
                            data.PaidAmount > 0m &&
                            data.ResidualAmount <= 0.01m;

            return new TicketDomainValidationResult
            {
                DomainCode = PaymentsTicketDomainModule.DomainCode,
                IsResolved = resolved,
                CheckSummary = resolved
                    ? $"PAGAMENTO COMPLETO: RISOLTO - assegnato {FormatAmount(data.AssignedAmount)}, pagato {FormatAmount(data.PaidAmount)}, residuo {FormatAmount(data.ResidualAmount)}"
                    : $"PAGAMENTO COMPLETO: NON RISOLTO - assegnato {FormatAmount(data.AssignedAmount)}, pagato {FormatAmount(data.PaidAmount)}, residuo {FormatAmount(data.ResidualAmount)}",
                Evidence = resolved
                    ? BuildPaymentEvidence(
                        "pagamento completo",
                        data.PaidAmount,
                        data.Mandates,
                        data.HasBlockingPaymentEvidence)
                    : string.Empty,
                UnresolvedReason = data.HasBlockingPaymentEvidence
                    ? "pagamento BS con storni o codici non classificati"
                    : "pagamento completo della borsa non risulta effettuato o residuo non azzerato",
                ResolvedCondition = "PAGAMENTO_COMPLETO_STESSO_AA",
                ResolvedResponseCode = "PAG_01",
                PendingResponseCode = "PAG_15"
            };
        }

        private static TicketDomainValidationResult BuildComponentValidation(
            string component,
            decimal amount,
            string mandates,
            decimal totalResidual,
            bool requireTotalResidualZero,
            string resolvedCondition,
            string resolvedResponseCode,
            string pendingResponseCode)
        {
            bool hasPayment = amount > 0m;
            bool totalResidualOk = !requireTotalResidualZero || totalResidual <= 0.01m;
            bool resolved = hasPayment && totalResidualOk;
            string residualText = requireTotalResidualZero
                ? $", residuo totale {FormatAmount(totalResidual)}"
                : ", residuo richiesta 0,00";

            return new TicketDomainValidationResult
            {
                DomainCode = PaymentsTicketDomainModule.DomainCode,
                IsResolved = resolved,
                CheckSummary = resolved
                    ? $"{component}: RISOLTO - pagato {FormatAmount(amount)}{FormatMandates(mandates)}{residualText}"
                    : $"{component}: NON RISOLTO - pagato {FormatAmount(amount)}{FormatMandates(mandates)}, residuo totale {FormatAmount(totalResidual)}",
                Evidence = resolved
                    ? BuildPaymentEvidence(component.ToLowerInvariant(), amount, mandates, hasWarnings: false)
                    : string.Empty,
                UnresolvedReason = !hasPayment
                    ? $"{component.ToLowerInvariant()} non risulta pagata"
                    : $"{component.ToLowerInvariant()} risulta pagata ma il residuo richiesto non è azzerato",
                ResolvedCondition = resolvedCondition,
                ResolvedResponseCode = resolvedResponseCode,
                PendingResponseCode = pendingResponseCode
            };
        }

        private static string BuildPaymentEvidence(
            string label,
            decimal amount,
            string mandates,
            bool hasWarnings)
        {
            var values = new List<string> { $"{label} pagato: {FormatAmount(amount)}" };
            if (!string.IsNullOrWhiteSpace(mandates))
                values.Add($"mandati: {mandates}");
            if (hasWarnings)
                values.Add("presenti movimenti stornati o codici non classificati non usati nel pagato valido");
            return string.Join("; ", values);
        }

        private static string FormatMandates(string mandates) =>
            string.IsNullOrWhiteSpace(mandates)
                ? string.Empty
                : $" con mandato {mandates}";

        private static string FormatAmount(decimal amount) => amount.ToString("N2", ItalianCulture);
    }
}
