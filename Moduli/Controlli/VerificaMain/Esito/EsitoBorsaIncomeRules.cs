using System;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaIncomeRules
    {
        public void Apply(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var info = context.Info;
            if (info == null)
                return;

            ApplyAttestazioneEconomicaObbligatoriaRules(context, evaluation);
            ApplyStatusIseeRules(context, evaluation);

            decimal? isee = EsitoBorsaSupport.GetIseeRiferimento(info);
            decimal? isp = EsitoBorsaSupport.GetIspRiferimento(info);

            if (!isee.HasValue)
                evaluation.Add("RED011");

            if (context.Config.SogliaIsp > 0m && isp.HasValue && isp.Value > context.Config.SogliaIsp)
                evaluation.Add("RED012");

            if (context.Config.SogliaIsee > 0m && isee.HasValue && isee.Value > context.Config.SogliaIsee)
                evaluation.Add("RED013");
        }

        private static void ApplyAttestazioneEconomicaObbligatoriaRules(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var raw = context.Info?.InformazioniEconomiche?.Raw;
            if (raw == null)
                return;

            string tipoOrigine = Normalize(raw.TipoRedditoOrigine);
            string origineFonte = Normalize(raw.OrigineFonte);

            bool origineAdeguata = context.Facts.OrigineEconomicaAdeguata || origineFonte == "CO";

            if (tipoOrigine == "IT" && !origineAdeguata)
                evaluation.Add("RED031");

            string tipoNucleo = Normalize(raw.TipoNucleo);
            string tipoIntegrazione = Normalize(raw.TipoRedditoIntegrazione);
            string integrazioneFonte = Normalize(raw.IntegrazioneFonte);

            bool richiedeIntegrazione = tipoNucleo == "I" && !string.IsNullOrWhiteSpace(tipoIntegrazione);
            if (!richiedeIntegrazione)
                return;

            if (tipoIntegrazione == "IT" && integrazioneFonte != "CI")
                evaluation.Add("RED033");
        }

        private static void ApplyStatusIseeRules(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var info = context.Info;
            if (info == null)
                return;

            int? statusIsee = EsitoBorsaSupport.GetStatusIseeDaEconomici(info);
            if (!statusIsee.HasValue || statusIsee.Value == 0)
                return;

            if (statusIsee.Value == 13)
            {
                evaluation.Add("RED087");
                return;
            }

            if (statusIsee.Value == 11)
                return;

            if (!EsitoBorsaSupport.IsSituazioneEconomicaValidaPerEsito(info))
                evaluation.Add("RED086");
        }

        private static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}
