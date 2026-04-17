using System;
using System.Collections.Generic;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaMeritRules
    {
        private static readonly HashSet<string> CorsiSpecialistica1FcRidotta = new(StringComparer.OrdinalIgnoreCase)
        {
            "30060", "30055", "30053", "28700", "28701", "30052", "31833", "S2-34"
        };

        public void Apply(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            var iscr = context.Iscrizione;
            if (iscr == null)
                return;

            if(context.Key.CodFiscale == "DEIRRT02D46H501T")
            {
                string test = "";
            }

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

            if (!IsAnnoCorsoCongruente(context, iscr))
                evaluation.Add("MER072");

            if (HasCreditiDichiaratiIncongruenti(iscr))
                evaluation.Add("MER005");

            if (HasTirocinioSuperioreAiCrediti(iscr))
                evaluation.Add("MER092");

            if (!HaCreditiMinimiPerBorsa(context, iscr, evaluation))
                evaluation.Add("MER012");
        }

        private static bool RichiedeDatiMerito(EsitoBorsaStudentContext context)
        {
            if (context.Iscrizione.TipoCorso == 6 || context.Iscrizione.TipoCorso == 7)
                return false;

            return context.Iscrizione.AnnoCorso != 1 && EsitoBorsaSupport.GetAnnoCorsoCalcolato(context) != 1;
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

        private static bool IsAnnoCorsoCongruente(EsitoBorsaStudentContext context, InformazioniIscrizione iscr)
        {
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(context);
            if (annoCorsoCalcolato == 0)
                return true;

            bool derogaRinuncia = context.AaNumero >= 20242025
                                 && EsitoBorsaSupport.HasRiconoscimentoCreditiDaRinuncia(iscr)
                                 && context.Facts.PassaggioTrasferimento != true;
            bool derogaRipetenza = context.AaNumero >= 20252026 && EsitoBorsaSupport.HasRipetenzaDaPassaggio(context);

            if (derogaRinuncia || derogaRipetenza)
                return true;

            return annoCorsoCalcolato == iscr.AnnoCorso;
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

            decimal bonusUsabile = GetBonusUsabile(context, iscr, annoCorsoCalcolato);
            if (creditiStudente + bonusUsabile >= creditiRichiesti)
            {
                if (bonusUsabile > 0m)
                    iscr.CreditiUtilizzati = Math.Max(creditiRichiesti - creditiStudente, 0m);
                return true;
            }

            return false;
        }

        private static decimal GetBonusUsabile(EsitoBorsaStudentContext context, InformazioniIscrizione iscr, int annoCorsoCalcolato)
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

                return annoCorsoCalcolato switch
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
            decimal value = GetCreditiMinimiBase(iscr, invalido, context.AaNumero, annoCorsoRiferimento);
            value = ApplySpecialisticaPrimoFuoriCorsoRidotta(value, iscr, invalido, annoCorsoRiferimento);
            if (!invalido && context.Facts.NubileProle == true)
                value = Math.Floor(value * 0.9m);
            return value;
        }

        private static decimal GetCreditiMinimiBase(InformazioniIscrizione iscr, bool invalido, int aaNumero, int anno)
        {
            bool ente09 = EsitoBorsaSupport.IsEnte(iscr, "09");
            bool ente07 = aaNumero >= 20252026 && EsitoBorsaSupport.IsEnte(iscr, "07");
            bool boost38 = ente09 || ente07;

            switch (anno)
            {
                case 1:
                    return 0m;

                case 2:
                    if (iscr.TipoCorso == 5)
                        return invalido ? 18m : (boost38 ? 38m : 30m);
                    return invalido ? 15m : (boost38 ? 31m : 25m);

                case 3:
                    return invalido ? 56m : (boost38 ? 100m : 80m);

                case 4:
                    if (invalido)
                        return 94m;
                    if (ente07)
                        return 160m;
                    return boost38 ? 168m : 135m;

                case 5:
                    if (invalido)
                        return 133m;
                    if (ente07)
                        return 210m;
                    return boost38 ? 230m : 190m;

                case 6:
                    if (invalido)
                        return 171m;
                    return ente07 ? 265m : 245m;

                case -1:
                    switch (iscr.TipoCorso)
                    {
                        case 3:
                            return invalido ? 94m : (boost38 ? 155m : 135m);
                        case 4:
                            if (EsitoBorsaSupport.GetDurataNormaleCorso(iscr) > 5)
                                return invalido ? 222m : (ente07 ? 315m : 300m);
                            return invalido ? 171m : (ente07 ? 315m : (boost38 ? 265m : 245m));
                        case 5:
                            return invalido ? 56m : (boost38 ? 90m : 80m);
                        default:
                            return 0m;
                    }

                case -2:
                    if (!invalido)
                        return 0m;
                    return iscr.TipoCorso switch
                    {
                        3 => 133m,
                        5 => 94m,
                        4 when EsitoBorsaSupport.GetDurataNormaleCorso(iscr) > 5 => 228m,
                        4 => 222m,
                        _ => 0m
                    };

                case -3:
                    return invalido && iscr.TipoCorso == 5 ? 94m : 0m;

                default:
                    return 0m;
            }
        }

        private static decimal ApplySpecialisticaPrimoFuoriCorsoRidotta(decimal currentValue, InformazioniIscrizione iscr, bool invalido, int annoCorsoRiferimento)
        {
            if (iscr.TipoCorso != 5 || (annoCorsoRiferimento != -1 && annoCorsoRiferimento != -2))
                return currentValue;

            string corso = (iscr.CodCorsoLaurea ?? string.Empty).Trim();
            if (!CorsiSpecialistica1FcRidotta.Contains(corso))
                return currentValue;

            if (annoCorsoRiferimento == -1)
                return invalido ? 56m : 63m;

            return invalido ? 94m : currentValue;
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

            return iscr.TipoCorso switch
            {
                5 => crediti > 120m,
                3 => crediti >= 180m,
                4 when EsitoBorsaSupport.GetDurataNormaleCorso(iscr) >= 6 => crediti >= 360m,
                4 => crediti >= 300m,
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
