using System;
using System.Collections.Generic;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaCiRules
    {
        private static readonly HashSet<string> SediAfamConLimiteSpeciale = new(StringComparer.OrdinalIgnoreCase)
        {
            "O", "Q", "P", "L", "T", "G"
        };

        public void Apply(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            ApplyClassificabilita(context, evaluation);
            ApplyTipologiaEPrimoAnno(context, evaluation);
            ApplyLimiteAnniFuoriCorso(context, evaluation);
            ApplyBeneficioPregresso(context, evaluation);
            ApplySelezioneUe(context, evaluation);
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

        private static void ApplyTipologiaEPrimoAnno(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var iscrizione = context.Iscrizione;
            if (iscrizione == null)
                return;

            if (iscrizione.TipoCorso == 7)
                evaluation.Add("CI002");

            if (iscrizione.AnnoCorso == 1 && iscrizione.TipoCorso != 5)
                evaluation.Add("CI001");
        }

        private static void ApplyLimiteAnniFuoriCorso(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            if (context.Iscrizione == null || context.Iscrizione.AnnoCorso == 1)
                return;

            int annoCorso = EsitoBorsaSupport.HasRipetenzaDaPassaggio(context)
                ? context.Iscrizione.AnnoCorso
                : EsitoBorsaSupport.GetAnnoCorsoCalcolato(context);

            if (annoCorso == 0 || annoCorso < GetLimiteAnniFuoriCorso(context))
                evaluation.Add("CI003");
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
                || context.Facts.BeneficiPregressiNonRestituiti.Contains("CI"))
            {
                evaluation.Add("CI004");
            }
        }

        private static void ApplySelezioneUe(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            if (context.Pipeline.FaseElaborativa == VerificaFaseElaborativa.GraduatorieProvvisorie
                || context.AaNumero == 20112012)
            {
                return;
            }

            string ente = NormalizeCodEnte(context.Iscrizione?.CodEnte);
            bool enteRichiedeSelezione = ente == "2" || ente == "9";
            if (enteRichiedeSelezione && !context.Pipeline.SelezionatiCiUe.Contains(context.Key))
                evaluation.Add("CI006");
        }

        private static string NormalizeCodEnte(string? value)
        {
            string normalized = EsitoBorsaSupport.NormalizeUpper(value).TrimStart('0');
            return normalized.Length == 0 ? "0" : normalized;
        }

        private static void ApplyVariazioniSpecifiche(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            bool rinunciaPregressa = !context.Facts.ForzaturaRinunciaNoEsclusione
                && context.Facts.BeneficiRinunciaPregressa.Contains("CI");

            if (rinunciaPregressa || EsitoBorsaSupport.HasRinunciaVariazione(context.Facts, "CI"))
                evaluation.Add("CI005");

            if (EsitoBorsaSupport.HasDecadenzaVariazione(context.Facts, "CI"))
                evaluation.Add("CI007");

            if (EsitoBorsaSupport.HasRevocaBandoVariazione(context.Facts, "CI"))
                evaluation.Add("CI008");
        }
    }
}
