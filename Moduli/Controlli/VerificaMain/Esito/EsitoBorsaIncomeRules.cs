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

            ApplyStatusIseeRules(context, evaluation);

            decimal isee = EsitoBorsaSupport.GetIseeRiferimento(info);
            decimal isp = EsitoBorsaSupport.GetIspRiferimento(info);

            if (context.Config.SogliaIsp > 0m && isp > context.Config.SogliaIsp)
                evaluation.Add("RED012");

            if (context.Config.SogliaIsee > 0m && isee > context.Config.SogliaIsee)
                evaluation.Add("RED013");
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
    }
}
