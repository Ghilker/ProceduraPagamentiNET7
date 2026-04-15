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

            if (facts.DomandaTrasmessa == false)
            {
                evaluation.Add("GEN001");
                evaluation.Add("GEN094");
            }

            ApplyPhaseSensitiveRules(context, evaluation);
            ApplyForzatureGenerali(context, evaluation);

            if (facts.FuoriTermine == true)
                evaluation.Add("GEN003");

            if (facts.IscrizioneFuoriTermine == true)
                evaluation.Add("GEN093");

            if (facts.RinunciaBenefici == true)
                evaluation.Add("GEN007");

            ApplyVariazioniEscludenti(facts, evaluation);

            if (facts.TitoloAccademicoConseguito == true && !PassaRequisitiIscrizione(context))
                evaluation.Add("GEN006");

            bool faseProvvisoria = context.Pipeline.FaseElaborativa == VerificaFaseElaborativa.GraduatorieProvvisorie;

            if (context.AaNumero >= 20092010
                && !faseProvvisoria
                && RichiedeDocumentazioneConsolare(context)
                && facts.RedditoUe != true
                && facts.IsConferma != true
                && context.Info?.InformazioniPersonali?.Rifugiato != true
                && facts.DocConsolare == false)
            {
                evaluation.Add("GEN004");
            }

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

                    if (facts.CartaceoInviato == true)
                    {
                        if (facts.Firmata == false)
                            evaluation.Add("GENSIG");

                        if (facts.FotocopiaDocumento == false)
                            evaluation.Add("GENDOC");
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

        private static bool RichiedeDocumentazioneConsolare(EsitoBorsaStudentContext context)
        {
            var facts = context.Facts;
            var info = context.Info;
            string comuneResidenza = EsitoBorsaSupport.GetComuneResidenza(info);
            bool residenzaEstera = !string.IsNullOrWhiteSpace(comuneResidenza)
                                   && comuneResidenza.StartsWith("Z", StringComparison.OrdinalIgnoreCase);

            return ((facts.Straniero == true && facts.CittadinanzaUe == false && facts.FamigliaResidenteItalia != true && residenzaEstera)
                    || (facts.CittadinanzaUe == true && facts.ResidenzaUe == false && facts.FamigliaResidenteItalia != true)
                    || (residenzaEstera && facts.Straniero != true));
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
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(context);
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
                if (durataTitolo == 4 && annoCorsoCalcolato == 1)
                    return false;
                if (durataTitolo > 4)
                    return false;
            }

            if (tipoCorso == 4 && (tipologiaTitolo == 1 || tipologiaTitolo == 2))
            {
                if (durataTitolo == 3 && (annoCorsoCalcolato == 1 || annoCorsoCalcolato == 2 || annoCorsoCalcolato == 3))
                    return false;
                if (durataTitolo == 4 && (annoCorsoCalcolato == 1 || annoCorsoCalcolato == 2 || annoCorsoCalcolato == 3 || annoCorsoCalcolato == 4))
                    return false;
                if (durataTitolo == 5)
                {
                    if (durataCorso == 5)
                        return false;
                    if (durataCorso == 6 && annoCorsoCalcolato != 6 && annoCorsoCalcolato != -1 && annoCorsoCalcolato != -2)
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
                else if (annoCorsoCalcolato == 1 || annoCorsoCalcolato == 2 || annoCorsoCalcolato == 3)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
