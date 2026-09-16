using System;
using System.Collections.Generic;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaPaRules
    {
        private static readonly HashSet<string> SediAfamConLimiteSpeciale = new(StringComparer.OrdinalIgnoreCase)
        {
            "O", "Q", "P", "L", "T", "G"
        };

        public void Apply(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            ApplyClassificabilita(context, evaluation);
            ApplyStatusSede(context, evaluation);
            ApplyComunePensionato(context, evaluation);
            ApplyLimiteAnniFuoriCorso(context, evaluation);
            ApplyBeneficioPregresso(context, evaluation);
            ApplyVariazioniSpecifiche(context, evaluation);
        }

        private static void ApplyClassificabilita(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            if (context.Iscrizione == null)
            {
                evaluation.Add("ISC008");
                return;
            }

            if (string.IsNullOrWhiteSpace(context.Iscrizione.ComuneSedeStudi))
                evaluation.Add("ISC009");

            if (context.Facts.Straniero != true
                && string.IsNullOrWhiteSpace(EsitoBorsaSupport.GetComuneResidenza(context.Info)))
            {
                evaluation.Add("ISC010");
            }
        }

        private static void ApplyStatusSede(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            string status = EsitoBorsaSupport.NormalizeUpper(
                context.Info?.InformazioniSede?.StatusSedeSuggerito);

            if (!string.Equals(status, "B", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "D", StringComparison.OrdinalIgnoreCase))
            {
                evaluation.Add("PA001");
            }
        }

        private static void ApplyComunePensionato(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            string comuneSedeStudi = EsitoBorsaSupport.NormalizeUpper(context.Iscrizione?.ComuneSedeStudi);
            if (comuneSedeStudi.Length == 0 || !context.Pipeline.ComuniPensionatiAttivi.Contains(comuneSedeStudi))
                evaluation.Add("PA002");
        }

        private static void ApplyLimiteAnniFuoriCorso(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            if (context.Iscrizione == null)
                return;

            int annoCorso = EsitoBorsaSupport.HasRipetenzaDaPassaggio(context)
                ? context.Iscrizione.AnnoCorso
                : EsitoBorsaSupport.GetAnnoCorsoCalcolato(context);

            if (annoCorso == 0 || annoCorso < GetLimiteAnniFuoriCorso(context))
                evaluation.Add("PA003");
        }

        private static int GetLimiteAnniFuoriCorso(EsitoBorsaStudentContext context)
        {
            var iscrizione = context.Iscrizione;
            if (iscrizione == null)
                return 0;

            string codSede = EsitoBorsaSupport.NormalizeUpper(iscrizione.CodSedeStudi);
            string codOrdinamento = EsitoBorsaSupport.NormalizeUpper(
                string.IsNullOrWhiteSpace(context.Facts.CodTipoOrdinamento)
                    ? iscrizione.CodTipoOrdinamentoCorso
                    : context.Facts.CodTipoOrdinamento);

            bool sedeAfamConLimiteSpeciale = context.AaNumero > 20072008
                && SediAfamConLimiteSpeciale.Contains(codSede)
                && codOrdinamento == "1";

            if (sedeAfamConLimiteSpeciale)
                return context.Invalido ? -3 : 0;

            bool ordinamentoRidottoOPassaggio = codOrdinamento == "3"
                || context.Facts.PassaggioTrasferimento == true;

            if (context.Invalido)
                return ordinamentoRidottoOPassaggio ? -2 : -4;

            return ordinamentoRidottoOPassaggio ? -1 : -2;
        }

        private static void ApplyBeneficioPregresso(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            if (context.Facts.ForzaturaRinunciaNoEsclusione || context.Facts.RinunciaInCorso)
                return;

            if (context.Facts.UsufruitoBeneficioBorsaNonRestituito
                || context.Facts.BeneficiPregressiNonRestituiti.Contains("PA"))
            {
                evaluation.Add("PA004");
            }
        }

        private static void ApplyVariazioniSpecifiche(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            bool rinunciaPregressa = !context.Facts.ForzaturaRinunciaNoEsclusione
                && context.Facts.BeneficiRinunciaPregressa.Contains("PA");

            if (rinunciaPregressa || EsitoBorsaSupport.HasRinunciaVariazione(context.Facts, "PA"))
                evaluation.Add("PA005");

            if (EsitoBorsaSupport.HasDecadenzaVariazione(context.Facts, "PA"))
                evaluation.Add("PA006");

            if (EsitoBorsaSupport.HasRevocaBandoVariazione(context.Facts, "PA"))
                evaluation.Add("PA007");
        }
    }
}
