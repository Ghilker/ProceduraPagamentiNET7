using System;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaRuleEngine
    {
        private readonly EsitoBorsaGeneralRules _generalRules = new();
        private readonly EsitoBorsaIncomeRules _incomeRules = new();
        private readonly EsitoBorsaMeritRules _meritRules = new();
        private readonly EsitoBorsaBenefitRules _benefitRules = new();
        private readonly EsitoBorsaPaRules _paRules = new();
        private readonly EsitoBorsaCiRules _ciRules = new();

        public EsitoBorsaEvaluation Evaluate(EsitoBorsaStudentContext context)
        {
            var evaluation = new EsitoBorsaEvaluation();

            if (string.Equals(context.CodBeneficio, "PA", StringComparison.OrdinalIgnoreCase))
            {
                _generalRules.Apply(context, evaluation);
                _incomeRules.Apply(context, evaluation);
                _meritRules.Apply(context, evaluation);
                _paRules.Apply(context, evaluation);
                return evaluation;
            }

            if (string.Equals(context.CodBeneficio, "CI", StringComparison.OrdinalIgnoreCase))
            {
                _generalRules.Apply(context, evaluation);
                _incomeRules.Apply(context, evaluation);
                _meritRules.Apply(context, evaluation);
                _ciRules.Apply(context, evaluation);
                return evaluation;
            }

            _generalRules.Apply(context, evaluation);
            _incomeRules.Apply(context, evaluation);
            _meritRules.Apply(context, evaluation);
            _benefitRules.Apply(context, evaluation);
            return evaluation;
        }
    }
}
