using System;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaMeritRules
    {
        public void Apply(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var iscr = context.Iscrizione;
            if (iscr == null)
                return;


            ResetCreditiUtilizzatiSeBonusNonRichiesto(iscr);

            bool richiedeMeritoStorico = RichiedeDatiMerito(context);
            bool usaRegoleCrediti = true;

            iscr.RegolaMeritoApplicata = "CREDITI";
            iscr.EsamiMinimiRichiestiMerito = null;
            iscr.CreditiMinimiRichiestiMerito = null;
            iscr.EsamiMinimiRichiestiPassaggio = null;
            iscr.CreditiMinimiRichiestiPassaggio = null;

            if (!PassaRequisitiSpecImmatricolati(context))
                evaluation.Add("MER089");

            if (!PassaRegolaCreditiSpecialisticaPrimoAnno(context))
                evaluation.Add("MER074");

            if (!richiedeMeritoStorico)
                return;

            if (!HasDatiMeritoCalcolabili(context, usaRegoleCrediti))
            {
                evaluation.Add("MER001");
                return;
            }


            if (HasCreditiDichiaratiIncongruenti(iscr))
                evaluation.Add("MER005");

            if (HasTirocinioSuperioreAiCrediti(iscr))
                evaluation.Add("MER092");

            if (!HaCreditiMinimiPerBorsa(context, iscr, evaluation))
                evaluation.Add("MER012");
        }

        private static bool RichiedeDatiMerito(EsitoBorsaStudentContext context)
        {
            if (context?.Iscrizione == null)
                return false;

            if (context.Iscrizione.TipoCorso == 6 || context.Iscrizione.TipoCorso == 7)
                return false;

            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(context);
            if (annoCorsoCalcolato != 0)
                return annoCorsoCalcolato != 1;

            return context.Iscrizione.AnnoCorso != 1;
        }

        private static bool HasDatiMeritoCalcolabili(EsitoBorsaStudentContext context, bool usaRegoleCrediti)
        {
            if (!RichiedeDatiMerito(context))
                return true;

            if (usaRegoleCrediti)
                return (context.Iscrizione.NumeroCrediti ?? 0m) > 0m;

            return false;
        }

        private static bool PassaRequisitiSpecImmatricolati(EsitoBorsaStudentContext context)
        {
            var iscr = context.Iscrizione;
            if (iscr == null || iscr.TipoCorso != 5 || iscr.AnnoCorso != 1)
                return true;

            if (!context.Facts.TipologiaStudiTitoloConseguito.HasValue)
                return true;

            int tipologiaTitolo = context.Facts.TipologiaStudiTitoloConseguito.Value;
            int durataTitolo = context.Facts.DurataLegTitoloConseguito ?? 0;

            return (tipologiaTitolo == 2 && durataTitolo == 3)
                   || tipologiaTitolo == 3
                   || tipologiaTitolo == 9;
        }

        private static bool PassaRegolaCreditiSpecialisticaPrimoAnno(EsitoBorsaStudentContext context)
        {
            var iscr = context.Iscrizione;
            if (iscr == null || iscr.TipoCorso != 5 || iscr.AnnoCorso != 1)
                return true;

            if (context.Facts.TitoloAccademicoConseguito != true)
                return true;

            if (!context.Facts.RichiedeControlloLaureaSpec)
                return true;

            decimal riconosciuti = Math.Max(iscr.CreditiRiconosciuti ?? 0m, iscr.CreditiRiconosciutiDaRinuncia ?? 0m);
            return !(riconosciuti > 110m && riconosciuti < 150m);
        }

        private static bool HaCreditiMinimiPerBorsa(EsitoBorsaStudentContext context, InformazioniIscrizione iscr, EsitoBorsaEvaluation evaluation)
        {
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(context);
            decimal creditiRichiesti = GetCreditiMinimiRichiesti(context, iscr, context.Invalido, annoCorsoCalcolato);
            iscr.CreditiMinimiRichiestiMerito = creditiRichiesti;
            if (creditiRichiesti <= 0m)
                return true;

            decimal creditiStudente = iscr.NumeroCrediti ?? 0m;
            if (creditiStudente >= creditiRichiesti)
                return true;

            if (HasBloccoBonusTitolo(context, iscr))
                evaluation.Add("MER085");

            decimal bonusUsabile = GetBonusUsabile(context, iscr, iscr.AnnoCorso);
            if (creditiStudente + bonusUsabile >= creditiRichiesti)
            {
                if (bonusUsabile > 0m)
                    iscr.CreditiUtilizzati = Math.Max(creditiRichiesti - creditiStudente, 0m);
                return true;
            }

            return false;
        }

        private static decimal GetBonusUsabile(EsitoBorsaStudentContext context, InformazioniIscrizione iscr, int annoCorsoDichiarato)
        {
            bool bonusRichiesto = iscr.UtilizzoBonus != 0 || (iscr.CreditiUtilizzati ?? 0m) > 0m;
            if (!bonusRichiesto)
                return 0m;

            if (!CanUseBonus(context, iscr))
                return 0m;

            if (HasBloccoBonusTitolo(context, iscr))
                return 0m;

            decimal? creditiRimanenti = iscr.CreditiRimanenti;
            if (!creditiRimanenti.HasValue || creditiRimanenti.Value < 0m)
            {
                if (iscr.TipoCorso == 5)
                    return 12m;

                return annoCorsoDichiarato switch
                {
                    2 => 5m,
                    3 => 12m,
                    _ => 15m
                };
            }

            decimal bonus = Math.Max(creditiRimanenti.Value, 0m);
            if (iscr.TipoCorso == 5 && bonus > 12m)
                bonus = 12m;

            return bonus;
        }

        private static bool CanUseBonus(EsitoBorsaStudentContext context, InformazioniIscrizione iscr)
        {
            if (EsitoBorsaSupport.HasDerogaAnnoCorsoDaRinuncia(context))
                return false;

            if (EsitoBorsaSupport.HasRipetenzaDaPassaggio(context))
                return false;

            int annoInizioImmatricolazione = EsitoBorsaSupport.GetAnnoInizioDaAnnoAccademico(iscr.AnnoImmatricolazione ?? 0);
            if (annoInizioImmatricolazione <= 0 || annoInizioImmatricolazione < 2000)
                return false;

            if (annoInizioImmatricolazione == 2000
                && string.Equals((iscr.CodEnte ?? string.Empty).Trim(), "01", StringComparison.OrdinalIgnoreCase)
                && !string.Equals((iscr.CodFacolta ?? string.Empty).Trim(), "I", StringComparison.OrdinalIgnoreCase)
                && !string.Equals((iscr.CodCorsoLaurea ?? string.Empty).Trim(), "UAZ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool HasBloccoBonusTitolo(EsitoBorsaStudentContext context, InformazioniIscrizione iscr)
        {
            bool bonusRichiesto = iscr.UtilizzoBonus != 0 || (iscr.CreditiUtilizzati ?? 0m) > 0m;
            if (!bonusRichiesto)
                return false;

            string sedeIstituzione = (context.Facts.SedeIstituzioneUniversitariaTitolo ?? string.Empty).Trim();
            bool sedeCompatibile = string.IsNullOrEmpty(sedeIstituzione) || sedeIstituzione == "0";
            if (!sedeCompatibile)
                return false;

            int tipologiaTitolo = context.Facts.TipologiaStudiTitoloConseguito ?? 0;
            bool isImmatricolato = iscr.AnnoCorso == 1;

            if (iscr.TipoCorso == 5 && !isImmatricolato && tipologiaTitolo == 9)
                return true;

            if (tipologiaTitolo == 9 && context.Facts.RiconoscimentoTitoloEstero == true)
                return true;

            return tipologiaTitolo == 1 || tipologiaTitolo == 2;
        }

        private static decimal GetCreditiMinimiRichiesti(EsitoBorsaStudentContext context, InformazioniIscrizione iscr, bool invalido, int annoCorsoRiferimento)
        {
            decimal value = GetCreditiMinimiBase(context, iscr, invalido, annoCorsoRiferimento);
            if (!invalido && context.Facts.NubileProle == true)
                value = Math.Floor(value * 0.9m);
            return value;
        }

        private static decimal GetCreditiMinimiBase(EsitoBorsaStudentContext context, InformazioniIscrizione iscr, bool invalido, int anno)
        {
            if (context?.Pipeline?.CreditiRichiestiCatalog == null || iscr == null)
                return 0m;

            int annoCatalogo = NormalizeAnnoCorsoCreditiRichiesti(iscr, anno);

            return context.Pipeline.CreditiRichiestiCatalog.Resolve(
                       iscr.TipoCorso,
                       annoCatalogo,
                       invalido,
                       iscr.CodCorsoLaurea,
                       iscr.CodSedeStudi)
                   ?? 0m;
        }

        private static int NormalizeAnnoCorsoCreditiRichiesti(InformazioniIscrizione iscr, int anno)
        {
            if (iscr == null)
                return anno;

            if (iscr.TipoCorso != 4)
                return anno;

            int durataLegale = EsitoBorsaSupport.GetDurataNormaleCorso(iscr);
            if (durataLegale != 5)
                return anno;

            return anno switch
            {
                -1 => 6,
                <= -2 => anno + 1,
                _ => anno
            };
        }

        private static void ResetCreditiUtilizzatiSeBonusNonRichiesto(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return;

            if (iscr.UtilizzoBonus == 0 && (iscr.CreditiUtilizzati ?? 0m) > 0m)
                iscr.CreditiUtilizzati = 0m;
        }

        private static bool HasCreditiDichiaratiIncongruenti(InformazioniIscrizione iscr)
        {
            decimal crediti = iscr.NumeroCrediti ?? 0m;
            if (crediti <= 0m)
                return false;

            int durataLegale = EsitoBorsaSupport.GetDurataNormaleCorso(iscr);

            return iscr.TipoCorso switch
            {
                5 => crediti > 120m,
                3 => crediti >= 180m,
                4 when durataLegale == 5 => crediti >= 300m,
                4 when durataLegale == 6 => crediti >= 360m,
                _ => false
            };
        }

        private static bool HasTirocinioSuperioreAiCrediti(InformazioniIscrizione iscr)
        {
            decimal tirocinio = iscr.CreditiTirocinio ?? 0m;
            decimal crediti = iscr.NumeroCrediti ?? 0m;
            return tirocinio > 0m && crediti > 0m && tirocinio > crediti;
        }
    }
}
