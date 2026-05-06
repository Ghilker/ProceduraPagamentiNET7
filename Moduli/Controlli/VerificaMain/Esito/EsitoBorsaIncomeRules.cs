using System;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaIncomeRules
    {
        public void Apply(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var info = context.Info;
            var facts = context.Facts;

            if (info == null)
                return;

            if (facts.IsConferma == true)
                return;

            ApplyAttestazioneEconomicaObbligatoriaRules(context, evaluation);
            ApplyStatusIseeRules(context, evaluation);

            decimal isee = EsitoBorsaSupport.GetIseeRiferimento(info);
            decimal isp = EsitoBorsaSupport.GetIspRiferimento(info);

            if (context.Config.SogliaIsp > 0m && isp > context.Config.SogliaIsp)
                evaluation.Add("RED012");

            if (context.Config.SogliaIsee > 0m && isee > context.Config.SogliaIsee)
                evaluation.Add("RED013");
        }

        private static void ApplyAttestazioneEconomicaObbligatoriaRules(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var raw = context.Info?.InformazioniEconomiche?.Raw;
            if (raw == null)
                return;

            string tipoOrigine = Normalize(raw.TipoRedditoOrigine);
            string origineFonte = Normalize(raw.OrigineFonte);

            if (context.AaNumero >= 20252026)
            {
                // La raccolta economica imposta OrigineFonte = CO solo quando sono rispettate entrambe le condizioni:
                // 1) ISEE base, anche ordinario/corrente, firmato entro il 22/07; se ConfermaSemestreFiltro=1 entro il 31/12;
                // 2) CO UNIVERSITARIA/RIDOTTA/CORRENTE firmata entro il 31/12.
                // Se OrigineFonte non è CO, il requisito economico italiano 20252026+ non è soddisfatto.
                if (tipoOrigine == "IT" && origineFonte != "CO")
                    evaluation.Add("RED031");
            }
            else if (tipoOrigine == "IT" && origineFonte != "CO" && origineFonte != "DO")
            {
                evaluation.Add("RED086");
            }

            string tipoNucleo = Normalize(raw.TipoNucleo);
            string tipoIntegrazione = Normalize(raw.TipoRedditoIntegrazione);
            string integrazioneFonte = Normalize(raw.IntegrazioneFonte);

            bool richiedeIntegrazione = tipoNucleo == "I" && !string.IsNullOrWhiteSpace(tipoIntegrazione);
            if (!richiedeIntegrazione)
                return;

            if (context.AaNumero >= 20252026)
            {
                // Per l'integrazione italiana 20252026+ serve una CI UNIVERSITARIA/RIDOTTA/CORRENTE entro il 31/12.
                // Il fallback DI non rende idoneo lo studente quando l'integrazione richiesta è italiana.
                if (tipoIntegrazione == "IT" && integrazioneFonte != "CI")
                    evaluation.Add("RED033");
            }
            else if (tipoIntegrazione == "IT" && integrazioneFonte != "CI" && integrazioneFonte != "DI")
            {
                evaluation.Add("RED086");
            }
        }

        private static void ApplyStatusIseeRules(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var info = context.Info;
            if (info == null)
                return;

            int? statusIsee = EsitoBorsaSupport.GetStatusIseeDaEconomici(info, context.AaNumero);
            if (!statusIsee.HasValue || statusIsee.Value == 0)
                return;

            if (statusIsee.Value == 13)
            {
                evaluation.Add("RED087");
                return;
            }

            if (statusIsee.Value == 11)
                return;

            if (!EsitoBorsaSupport.IsSituazioneEconomicaValidaPerEsito(info, context.AaNumero))
                evaluation.Add("RED086");
        }

        private static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}
