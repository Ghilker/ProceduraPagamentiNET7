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

            if (context.Key.CodFiscale == "BFFJNF05E43F611V")
            {
                string test = "";
            }

            if (facts.IsConferma == true)
            {
                if (facts.ConfermaReddito != true)
                    evaluation.Add("RED001");
                return;
            }

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
            int? statusIsee = context.Facts.StatusIsee;
            if (!statusIsee.HasValue || statusIsee.Value == 0)
                return;

            if (statusIsee.Value == 13)
            {
                evaluation.Add("RED087");
                return;
            }

            string tipoCertificazione = EsitoBorsaSupport.NormalizeUpper(context.Facts.TipoCertificazione);
            bool redditoEstero = string.Equals((context.Info?.InformazioniEconomiche?.Raw?.TipoRedditoOrigine ?? string.Empty).Trim(), "EE", StringComparison.OrdinalIgnoreCase);

            if (redditoEstero && statusIsee.Value == 2)
                return;

            bool certificazioneValida = tipoCertificazione == "UNIV"
                                        || tipoCertificazione == "RID"
                                        || tipoCertificazione == "ORD";

            if ((statusIsee.Value != 2 && statusIsee.Value != 11)
                || (statusIsee.Value == 2 && !certificazioneValida))
            {
                evaluation.Add("RED086");
            }
        }
    }
}
