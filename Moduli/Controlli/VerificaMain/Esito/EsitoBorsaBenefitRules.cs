using System;
using System.Collections.Generic;
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

            if (!IsAnnoCorsoAmmissibile(context))
                evaluation.Add("BS001");

            if (HasBeneficioPregressoNonRestituito(context.Facts))
                evaluation.Add("BS002");

            if (context.Facts.RinunciaBorsa == true || context.Facts.RinunciaBS)
                evaluation.Add("BS003");

            if (context.Facts.DecadutoBS)
                evaluation.Add("VAR004");

            if (context.Facts.RevocatoBandoBS)
                evaluation.Add("VAR011");
        }

        private static bool IsAnnoCorsoAmmissibile(EsitoBorsaStudentContext context)
        {
            var iscr = context.Iscrizione;
            if (iscr == null)
                return false;

            int annoCorsoRiferimento = EsitoBorsaSupport.GetAnnoCorsoRiferimentoBeneficio(context);
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(context);
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

        private static bool HasBeneficioPregressoNonRestituito(EsitoBorsaFacts facts)
        {
            if (facts.ForzaturaRinunciaNoEsclusione)
                return false;

            return facts.UsufruitoBeneficioBorsaNonRestituito;
        }
    }
}
