using System;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaBenefitRules
    {
        public void Apply(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var iscr = context.Iscrizione;
            if (iscr == null)
                return;

            string beneficio = context.CodBeneficio;

            if (string.Equals(beneficio, "BS", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAnnoCorsoAmmissibile(context))
                    evaluation.Add("BS001");

                if (HasBeneficioPregressoNonRestituito(context.Facts, beneficio))
                    evaluation.Add("BS002");

                if (HasRinunciaPregressa(context.Facts, beneficio) || context.Facts.RinunciaBorsa == true || context.Facts.RinunciaBS)
                    evaluation.Add("BS003");

                if (context.Facts.DecadutoBS)
                    evaluation.Add("VAR004");

                if (context.Facts.RevocatoBandoBS)
                    evaluation.Add("VAR011");
            }
        }

        private static bool IsAnnoCorsoAmmissibile(EsitoBorsaStudentContext context)
        {
            var iscr = context.Iscrizione;
            if (iscr == null)
                return false;

            int annoCorsoRiferimento = EsitoBorsaSupport.GetAnnoCorsoRiferimentoBeneficio(context);
            if (annoCorsoRiferimento == 0)
                return false;

            if (iscr.TipoCorso == 6 || iscr.TipoCorso == 7)
                return annoCorsoRiferimento > 0;

            if (annoCorsoRiferimento > 0)
                return true;

            if (!context.Invalido)
                return annoCorsoRiferimento >= -1;

            return annoCorsoRiferimento >= -2;
        }

        private static bool HasBeneficioPregressoNonRestituito(EsitoBorsaFacts facts, string beneficio)
        {
            if (facts.ForzaturaRinunciaNoEsclusione)
                return false;

            if (string.Equals(beneficio, "BS", StringComparison.OrdinalIgnoreCase))
                return facts.UsufruitoBeneficioBorsaNonRestituito || facts.BeneficiPregressiNonRestituiti.Contains("BS");

            return facts.BeneficiPregressiNonRestituiti.Contains(beneficio);
        }

        private static bool HasRinunciaPregressa(EsitoBorsaFacts facts, string beneficio)
        {
            if (facts.ForzaturaRinunciaNoEsclusione)
                return false;

            if (string.Equals(beneficio, "BS", StringComparison.OrdinalIgnoreCase))
                return facts.BeneficiRinunciaPregressa.Contains("BS");

            return facts.BeneficiRinunciaPregressa.Contains(beneficio);
        }
    }
}
