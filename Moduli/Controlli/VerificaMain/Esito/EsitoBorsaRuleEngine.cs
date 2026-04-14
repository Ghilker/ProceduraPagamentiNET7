namespace ProcedureNet7
{
    internal sealed class EsitoBorsaRuleEngine
    {
        private readonly EsitoBorsaGeneralRules _generalRules = new();
        private readonly EsitoBorsaIncomeRules _incomeRules = new();
        private readonly EsitoBorsaMeritRules _meritRules = new();
        private readonly EsitoBorsaBenefitRules _benefitRules = new();

        public EsitoBorsaEvaluation Evaluate(EsitoBorsaStudentContext context)
        {
            var evaluation = new EsitoBorsaEvaluation();
            _generalRules.Apply(context, evaluation);
            _incomeRules.Apply(context, evaluation);
            _meritRules.Apply(context, evaluation);
            _benefitRules.Apply(context, evaluation);
            return evaluation;
        }
    }
}
