using System;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaGeneralRules
    {
        private const int StatusCompilazioneMinimoTrasmessa = 90;
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

            if (info.StatusCompilazione < StatusCompilazioneMinimoTrasmessa)
                evaluation.Add("GEN001");

            if (facts.DomandaTrasmessa == false)
                evaluation.Add("GEN094");

            ApplyPhaseSensitiveRules(context, evaluation);
            ApplyForzatureGenerali(context, evaluation);

            if (facts.FuoriTermine == true)
                evaluation.Add("GEN003");

            if (facts.IscrizioneFuoriTermine == true)
                evaluation.Add("GEN093");

            if (facts.RinunciaBenefici == true)
                evaluation.Add("GEN007");

            if (facts.TitoloAccademicoConseguito == true && !PassaRequisitiIscrizione(context))
                evaluation.Add("GEN006");

            bool faseProvvisoria = context.Pipeline.FaseElaborativa == VerificaFaseElaborativa.GraduatorieProvvisorie;

            if (!faseProvvisoria && RichiedeDocumentazioneConsolare(context) && facts.DocConsolare == false)
                evaluation.Add("GEN004");

            if (!faseProvvisoria && RichiedePermessoSoggiorno(facts) && facts.PermessoSoggiorno == false)
                evaluation.Add("GEN005");
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

        private static void ApplyForzatureGenerali(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            foreach (var code in context.Facts.ForzatureGenerali)
                evaluation.Add($"GENF{code}");
        }

        private static bool RichiedeDocumentazioneConsolare(EsitoBorsaStudentContext context)
        {
            var info = context.Info;
            var facts = context.Facts;

            if (info == null)
                return false;

            if (info.InformazioniPersonali?.Rifugiato == true)
                return false;

            if (facts.IsConferma == true || facts.RedditoUe == true)
                return false;

            if (!facts.Straniero.HasValue || !facts.CittadinanzaUe.HasValue || !facts.FamigliaResidenteItalia.HasValue)
                return false;

            string comuneResidenza = EsitoBorsaSupport.GetComuneResidenza(info);
            bool comuneEstero = comuneResidenza.StartsWith("Z", StringComparison.OrdinalIgnoreCase);
            bool straniero = facts.Straniero.Value;
            bool cittadinanzaUe = facts.CittadinanzaUe.Value;
            bool famigliaResidenteItalia = facts.FamigliaResidenteItalia.Value;
            bool residenzaUe = facts.ResidenzaUe ?? false;

            bool casoExtraUe = straniero && !cittadinanzaUe && !famigliaResidenteItalia && comuneEstero;
            bool casoUeNonResidente = cittadinanzaUe && !residenzaUe && !famigliaResidenteItalia;
            bool casoItalianoEstero = comuneEstero && !straniero;

            return casoExtraUe || casoUeNonResidente || casoItalianoEstero;
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
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(iscr, context.AaInizio);
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
