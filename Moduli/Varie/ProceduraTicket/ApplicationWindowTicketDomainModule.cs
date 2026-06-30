using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProcedureNet7
{
    /// <summary>
    /// Dominio finestra compilazione domanda: riconosce richieste informative su come
    /// compilare/presentare domanda per un AA e verifica se la finestra temporale è chiusa.
    /// </summary>
    internal sealed class ApplicationWindowTicketDomainModule : ITicketDomainModule
    {
        public const string DomainCode = "FINESTRA_DOMANDA";
        private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

        private static readonly string[] ApplicationTokens =
        {
            "domanda", "domande", "richiesta benefici", "benefici", "application"
        };

        private static readonly string[] CompileInformationTokens =
        {
            "come compilare", "come posso compilare", "come fare domanda",
            "presentare domanda", "presentare la domanda", "posso presentare",
            "fare domanda", "far domanda", "inserire domanda", "compilazione domanda",
            "compilazione della domanda", "compilare la domanda", "compilarla", "presentarla",
            "come trasmettere", "trasmettere la domanda", "invio domanda", "inoltrare domanda",
            "cosa devo caricare", "cosa caricare", "dove devo inserire", "dove inserire",
            "quale documento", "quali documenti", "documentazione da caricare",
            "bando", "informazioni sul bando", "date del bando", "quando apre", "quando posso fare",
            "scadenza domanda", "scadenze", "finestra", "termine domanda", "application window",
            "how to apply", "how should i fill", "what should i upload", "which document"
        };

        private static readonly string[] ExplicitCompileTokens =
        {
            "come compilare", "come posso compilare", "come fare domanda",
            "presentare domanda", "presentare la domanda", "fare domanda",
            "compilazione domanda", "compilazione della domanda", "compilare la domanda",
            "come trasmettere", "trasmettere la domanda", "invio domanda", "inoltrare domanda",
            "cosa devo caricare", "cosa caricare", "dove devo inserire", "dove inserire",
            "scadenza domanda", "termine domanda", "informazioni sul bando", "date del bando",
            "quando apre", "quando posso fare", "application window", "how to apply"
        };

        private static readonly string[] PaymentOrAmountTokens =
        {
            "contributo integrativo", "contributo straordinario", "erogazione", "erogazioni",
            "importo", "importi", "accredito", "pagamento", "pagamenti", "liquidazione",
            "rata", "rate", "saldo", "tranche", "mandato", "mandati", "rimborso",
            "tassa regionale", "mensa", "mobilita", "mobilità", "quota monetaria"
        };

        public string Code => DomainCode;

        public TicketDomainScopeMatch DetectScope(TicketDomainRequest request)
        {
            string text = request.NormalizedText;
            TicketAcademicYearResolver.Resolution academicYear = ResolveAcademicYear(request);
            bool mentionsApplication = TicketDomainText.ContainsAny(text, ApplicationTokens);
            bool asksCompileInfo = TicketDomainText.ContainsAny(text, CompileInformationTokens);
            bool paymentOrAmountPriority = HasPaymentOrAmountPriority(text);
            bool intentCompatible =
                request.RecognisedIntent == TicketIntent.PRE_DOMANDA ||
                request.RecognisedIntent == TicketIntent.INFORMAZIONE ||
                request.RecognisedIntent == TicketIntent.ALTRO_DA_VERIFICARE;

            return new TicketDomainScopeMatch
            {
                DomainCode = Code,
                Signal = "FINESTRA_COMPILAZIONE_DOMANDA",
                IsRequested = academicYear.HasAcademicYear &&
                              mentionsApplication &&
                              asksCompileInfo &&
                              !paymentOrAmountPriority &&
                              intentCompatible
            };
        }

        public TicketDomainValidationResult Validate(TicketDomainValidationContext context)
        {
            TicketAcademicYearResolver.Resolution academicYear = ResolveAcademicYear(context.Request);
            if (!academicYear.HasAcademicYear)
            {
                return new TicketDomainValidationResult
                {
                    DomainCode = Code,
                    CheckSummary = "FINESTRA DOMANDA: ANNO ACCADEMICO NON IDENTIFICATO",
                    UnresolvedReason = "anno accademico della domanda non riconosciuto nel testo",
                    PendingResponseCode = "DOM_03"
                };
            }

            ApplicationWindow window = BuildWindow(academicYear.AcademicYear.Value);
            DateTime today = DateTime.Today;
            bool closed = today.Date > window.End.Date;

            return new TicketDomainValidationResult
            {
                DomainCode = Code,
                IsResolved = closed,
                CheckSummary = closed
                    ? $"FINESTRA DOMANDA {academicYear.FormattedAcademicYear}: RISOLTA - finestra chiusa il {FormatDate(window.End)}"
                    : $"FINESTRA DOMANDA {academicYear.FormattedAcademicYear}: NON RISOLTA - finestra {FormatDate(window.Start)}-{FormatDate(window.End)} non ancora chiusa",
                Evidence = closed
                    ? $"AA {academicYear.FormattedAcademicYear}; finestra compilazione {FormatDate(window.Start)}-{FormatDate(window.End)}; oggi {FormatDate(today)}"
                    : string.Empty,
                UnresolvedReason = closed
                    ? string.Empty
                    : "finestra di compilazione attiva o futura: serve riscontro informativo, non chiusura per situazione già risolta",
                ResolvedCondition = "FINESTRA_COMPILAZIONE_DOMANDA_CHIUSA",
                ResolvedResponseCode = "DOM_02",
                PendingResponseCode = "DOM_03"
            };
        }

        public string BuildEvidence(TicketOfficeRecord record) => string.Empty;

        private static TicketAcademicYearResolver.Resolution ResolveAcademicYear(TicketDomainRequest request) =>
            TicketAcademicYearResolver.Resolve(
                string.Empty,
                $"{request.Subject} {request.Message}");

        private static ApplicationWindow BuildWindow(int academicYear)
        {
            int startYear = academicYear / 10000;
            return new ApplicationWindow(
                new DateTime(startYear, 6, 1),
                new DateTime(startYear, 8, 31));
        }

        private static string FormatDate(DateTime date) =>
            date.ToString("dd/MM/yyyy", ItalianCulture);

        private static bool HasPaymentOrAmountPriority(string text)
        {
            bool paymentOrAmount = TicketDomainText.ContainsAny(text, PaymentOrAmountTokens);
            if (!paymentOrAmount)
                return false;

            return !TicketDomainText.ContainsAny(text, ExplicitCompileTokens);
        }

        private readonly struct ApplicationWindow
        {
            public ApplicationWindow(DateTime start, DateTime end)
            {
                Start = start;
                End = end;
            }

            public DateTime Start { get; }
            public DateTime End { get; }
        }
    }
}
