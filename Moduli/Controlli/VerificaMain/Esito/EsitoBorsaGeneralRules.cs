using System;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaGeneralRules
    {
        private const int StatusCompilazioneMinimoCompilazione = 70;

        public void Apply(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var info = context.Info;
            var facts = context.Facts;

            if (info == null)
                return;

            if (info.StatusCompilazione < StatusCompilazioneMinimoCompilazione)
            {
                evaluation.Add("GEN000");
                return;
            }
            var diagnosticaIscrizione = EsitoBorsaSupport.GetDiagnosticaIscrizione(context);
            facts.IsAnnoClassificabile = diagnosticaIscrizione.Count == 0;
            facts.DiagnosticaIscrizione = string.Join(";", diagnosticaIscrizione);
            foreach (string code in diagnosticaIscrizione)
                evaluation.Add(code);

            if (facts.DomandaTrasmessa == false)
            {
                evaluation.Add("GEN001");
                evaluation.Add("GEN094");
            }

            ApplyPhaseSensitiveRules(context, evaluation);
            ApplyForzatureGenerali(context, evaluation);

            if (facts.IscrizioneFuoriTermine == true)
                evaluation.Add("GEN093");

            if (facts.RinunciaBenefici == true)
                evaluation.Add("GEN007");

            ApplyVariazioniEscludenti(facts, evaluation);

            if (facts.TitoloAccademicoConseguito == true && !PassaRequisitiIscrizione(context))
                evaluation.Add("GEN006");

            bool faseProvvisoria = context.Pipeline.FaseElaborativa == VerificaFaseElaborativa.GraduatorieProvvisorie;

            if (!faseProvvisoria && RichiedePermessoSoggiorno(facts) && facts.PermessoSoggiorno == false)
            {
                if (context.Pipeline.AnnoAccademico != "20122013" && context.Pipeline.AnnoAccademico != "20162017")
                    evaluation.Add("GEN005");
            }
        }

        private static void ApplyPhaseSensitiveRules(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var facts = context.Facts;

            switch (context.Pipeline.FaseElaborativa)
            {
                case VerificaFaseElaborativa.GraduatorieProvvisorie:
                    if (facts.DomandaTrasmessaPin == false)
                        evaluation.Add("GEN088");
                    break;

                case VerificaFaseElaborativa.GraduatorieDefinitive:
                    if (facts.CartaceoInviato == false)
                    {
                        evaluation.Add("GEN088");
                        break;
                    }
                    break;
            }
        }

        private static void ApplyVariazioniEscludenti(EsitoBorsaFacts facts, EsitoBorsaEvaluation evaluation)
        {
            if (facts.Revocato)
                evaluation.Add("VAR003");
            if (facts.RevocatoMancataIscrizione)
                evaluation.Add("VAR019");
            if (facts.RevocatoIscrittoRipetente)
                evaluation.Add("VAR020");
            if (facts.RevocatoISEE)
                evaluation.Add("VAR021");
            if (facts.RevocatoLaureato)
                evaluation.Add("VAR022");
            if (facts.RevocatoPatrimonio)
                evaluation.Add("VAR023");
            if (facts.RevocatoReddito)
                evaluation.Add("VAR024");
            if (facts.RevocatoEsami)
                evaluation.Add("VAR025");
            if (facts.RevocatoFuoriTermine)
                evaluation.Add("VAR027");
            if (facts.RevocatoIseeFuoriTermine)
                evaluation.Add("VAR028");
            if (facts.RevocatoIseeNonProdotta)
                evaluation.Add("VAR029");
            if (facts.RevocatoTrasmissioneIseeFuoriTermine)
                evaluation.Add("VAR030");
            if (facts.RevocatoNoContrattoLocazione)
                evaluation.Add("VAR031");
        }

        private static void ApplyForzatureGenerali(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            foreach (var code in context.Facts.ForzatureGenerali)
                evaluation.Add($"GENF{code}");
        }
        private static bool RichiedePermessoSoggiorno(EsitoBorsaFacts facts)
        {
            if (!facts.Straniero.HasValue || !facts.CittadinanzaUe.HasValue)
                return false;

            return facts.Straniero.Value && !facts.CittadinanzaUe.Value;
        }

        private static bool PassaRequisitiIscrizione(EsitoBorsaStudentContext context)
        {
            var iscr = context.Iscrizione;
            var facts = context.Facts;

            if (iscr == null || !facts.TipologiaStudiTitoloConseguito.HasValue)
                return true;

            int tipoCorso = iscr.TipoCorso;
            int tipologiaTitolo = facts.TipologiaStudiTitoloConseguito.Value;
            int durataTitolo = facts.DurataLegTitoloConseguito ?? 0;
            int annoCorsoDichiarato = iscr.AnnoCorso;
            int annoCorsoRiferimento = annoCorsoDichiarato;
            int durataCorso = EsitoBorsaSupport.GetDurataNormaleCorso(iscr);

            switch (tipoCorso)
            {
                case 1:
                case 2:
                case 3:
                    if (tipologiaTitolo != 9)
                        return false;
                    break;

                case 4:
                case 5:
                    if (tipologiaTitolo >= 4 && tipologiaTitolo != 9)
                        return false;
                    break;

                case 6:
                case 7:
                    if (tipologiaTitolo >= 6 && tipologiaTitolo != 9)
                        return false;
                    break;
            }

            if (tipoCorso == 5 && (tipologiaTitolo == 1 || tipologiaTitolo == 2))
            {
                if (durataTitolo == 4 && annoCorsoRiferimento == 1)
                    return false;
                if (durataTitolo > 4)
                    return false;
            }

            if (tipoCorso == 4 && (tipologiaTitolo == 1 || tipologiaTitolo == 2))
            {
                if (durataTitolo == 3 && (annoCorsoRiferimento == 1 || annoCorsoRiferimento == 2 || annoCorsoRiferimento == 3))
                    return false;
                if (durataTitolo == 4 && (annoCorsoRiferimento == 1 || annoCorsoRiferimento == 2 || annoCorsoRiferimento == 3 || annoCorsoRiferimento == 4))
                    return false;
                if (durataTitolo == 5)
                {
                    if (durataCorso == 5)
                        return false;
                    if (durataCorso == 6 && annoCorsoRiferimento != 6 && annoCorsoRiferimento != -1 && annoCorsoRiferimento != -2)
                        return false;
                }
                else if (durataTitolo > 0)
                {
                    return false;
                }
            }

            if (tipoCorso == 4 && tipologiaTitolo == 3)
            {
                if (EsitoBorsaSupport.HasDerogaAnnoCorsoDaRinuncia(context))
                {
                    if (annoCorsoDichiarato > 0 && annoCorsoDichiarato < 4)
                        return false;
                }
                else if (annoCorsoRiferimento == 1 || annoCorsoRiferimento == 2 || annoCorsoRiferimento == 3)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
