using System;
using System.Collections.Generic;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaBenefitRules
    {
        private static readonly HashSet<string> SediAccademiaVecchioOrdinamento = new(StringComparer.OrdinalIgnoreCase)
        {
            "O", "Q", "P", "L", "T", "G"
        };

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
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(iscr, context.AaInizio);
            if (annoCorsoRiferimento == 0)
                return false;

            if (iscr.TipoCorso == 6 || iscr.TipoCorso == 7)
                return annoCorsoRiferimento > 0;

            if (annoCorsoRiferimento > 0)
                return true;

            string codOrd = (context.Facts.CodTipoOrdinamento ?? string.Empty).Trim();
            bool accademiaVo = SediAccademiaVecchioOrdinamento.Contains((iscr.CodSedeStudi ?? string.Empty).Trim())
                               && string.Equals(codOrd, "1", StringComparison.OrdinalIgnoreCase);

            if (!context.Invalido)
            {
                if (accademiaVo)
                    return annoCorsoCalcolato >= 0;

                return annoCorsoRiferimento >= -1;
            }

            int limiteFuoriCorso = string.Equals(codOrd, "3", StringComparison.OrdinalIgnoreCase)
                                   || EsitoBorsaSupport.IsPassaggioVecchioNuovo(context)
                ? -2
                : -3;

            return annoCorsoRiferimento >= limiteFuoriCorso;
        }

        private static bool HasBeneficioPregressoNonRestituito(EsitoBorsaFacts facts)
        {
            if (facts.ForzaturaRinunciaNoEsclusione)
                return false;

            return facts.UsufruitoBeneficioBorsaNonRestituito;
        }
    }
}
