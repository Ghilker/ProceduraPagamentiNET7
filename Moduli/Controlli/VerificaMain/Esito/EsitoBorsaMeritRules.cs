using System;
using System.Collections.Generic;
using System.Globalization;
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

            bool richiedeMerito = RichiedeDatiMerito(iscr);
            bool usaRegoleCrediti = UsesCreditoRules(context);
            bool consentiDerogaAnnoCorso = HasDerogaAnnoCorso(context);
            bool meritoCalcolabile = HasDatiMeritoCalcolabili(iscr, usaRegoleCrediti);
            iscr.RegolaMeritoApplicata = usaRegoleCrediti ? "CREDITI" : "ESAMI";
            iscr.EsamiMinimiRichiestiMerito = null;
            iscr.CreditiMinimiRichiestiMerito = null;
            iscr.EsamiMinimiRichiestiPassaggio = null;
            iscr.CreditiMinimiRichiestiPassaggio = null;

            if (EsitoBorsaSupport.IsPassaggioVecchioNuovo(context) && !EsitoBorsaSupport.IsPassaggioVecchioNuovoCalcolabile(context))
            {
                iscr.RegolaMeritoApplicata = "PASSAGGIO_VO_NUOVO_NON_CALCOLABILE";
                evaluation.Add("MER171");
                return;
            }

            if (richiedeMerito && !meritoCalcolabile)
            {
                evaluation.Add("MER001");
                return;
            }

            if (!consentiDerogaAnnoCorso
                && context.AaNumero >= 20092010
                && IsNuovoOrdinamento(context.Facts)
                && context.AaInizio > 0
                && !IsAnnoCorsoCongruente(iscr, context.AaInizio))
            {
                evaluation.Add("MER072");
            }

            if (EsitoBorsaSupport.IsAccademiaVecchioOrdinamento(iscr, context.Facts) && richiedeMerito)
            {
                iscr.RegolaMeritoApplicata = "AFAM_ESAME_COMPLEMENTARE";

                if (context.Facts.EsameComplementare == false)
                    evaluation.Add("MER071");

                if (context.AaInizio > 0 && !IsAnnoCorsoCongruente(iscr, context.AaInizio))
                    evaluation.Add("MER072");

                if (context.Facts.EsameComplementare.HasValue)
                    return;
            }

            if (usaRegoleCrediti)
            {
                if (HasCreditiDichiaratiIncongruenti(iscr))
                    evaluation.Add("MER005");

                if (HasTirocinioSuperioreAiCrediti(iscr))
                    evaluation.Add("MER092");
            }

            if (!PassaRequisitiSpecImmatricolati(context))
                evaluation.Add("MER089");

            if (IsVecchioOrdinamentoSapienzaNonAmmesso(context))
                evaluation.Add("MER170");

            if (!PassaRegolaCreditiSpecialisticaPrimoAnno(context))
                evaluation.Add("MER074");

            if (EsitoBorsaSupport.IsPassaggioVecchioNuovo(context))
            {
                if (!PassaMeritoPassaggioVecchioNuovo(context, iscr))
                    evaluation.Add("MER012");
            }
            else if (usaRegoleCrediti)
            {
                if (!HaCreditiMinimiPerBorsa(context, iscr, context.Invalido, context.AaNumero))
                    evaluation.Add("MER012");
            }
            else if (!HaEsamiMinimiPerBorsa(context, iscr))
            {
                evaluation.Add("MER012");
            }
        }

        private static bool RichiedeDatiMerito(InformazioniIscrizione iscr)
        {
            if (iscr.TipoCorso == 6 || iscr.TipoCorso == 7)
                return false;

            return iscr.AnnoCorso != 1;
        }

        private static bool HasDatiMeritoCalcolabili(InformazioniIscrizione iscr, bool usaRegoleCrediti)
        {
            if (!RichiedeDatiMerito(iscr))
                return true;

            if (usaRegoleCrediti)
                return (iscr.NumeroCrediti ?? 0m) > 0m;

            return (iscr.NumeroEsami ?? 0) > 0;
        }

        private static bool UsesCreditoRules(EsitoBorsaStudentContext context)
        {
            var iscr = context.Iscrizione;
            if (iscr == null)
                return false;

            if (IsNuovoOrdinamento(context.Facts))
                return true;

            if (context.Facts.IsConferma != true)
                return false;

            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(iscr, context.AaInizio);
            if (annoCorsoCalcolato != 2)
                return false;

            if (!EsitoBorsaSupport.IsEnte(iscr, "01"))
                return false;

            string codCorso = (iscr.CodCorsoLaurea ?? string.Empty).Trim().ToUpperInvariant();
            return !(codCorso.StartsWith("U00", StringComparison.Ordinal)
                     || codCorso.StartsWith("Q00", StringComparison.Ordinal)
                     || codCorso.StartsWith("S00", StringComparison.Ordinal)
                     || codCorso.StartsWith("T00", StringComparison.Ordinal));
        }

        private static bool HasDerogaAnnoCorso(EsitoBorsaStudentContext context)
        {
            if (EsitoBorsaSupport.HasDerogaAnnoCorsoDaRinuncia(context))
                return true;

            return EsitoBorsaSupport.HasRipetenzaDaPassaggio(context);
        }

        private static bool IsNuovoOrdinamento(EsitoBorsaFacts facts)
            => string.Equals((facts.CodTipoOrdinamento ?? string.Empty).Trim(), "3", StringComparison.OrdinalIgnoreCase);

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

        private static bool IsVecchioOrdinamentoSapienzaNonAmmesso(EsitoBorsaStudentContext context)
        {
            var iscr = context.Iscrizione;
            var persona = context.Info?.InformazioniPersonali;
            if (iscr == null || persona == null)
                return false;

            string codOrd = (context.Facts.CodTipoOrdinamento ?? string.Empty).Trim();
            bool immatricolato = iscr.AnnoCorso == 1;

            return string.Equals((iscr.CodSedeStudi ?? string.Empty).Trim(), "B", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(codOrd, "3", StringComparison.OrdinalIgnoreCase)
                   && !immatricolato
                   && !string.Equals((persona.CodFiscale ?? string.Empty).Trim(), "VLPMRN69E43H501R", StringComparison.OrdinalIgnoreCase);
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

        private static bool IsAnnoCorsoCongruente(InformazioniIscrizione iscr, int aaInizioCorrente)
        {
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(iscr, aaInizioCorrente);
            return annoCorsoCalcolato == 0 || annoCorsoCalcolato == iscr.AnnoCorso;
        }

        private static bool PassaMeritoPassaggioVecchioNuovo(EsitoBorsaStudentContext context, InformazioniIscrizione iscr)
        {
            iscr.RegolaMeritoApplicata = "PASSAGGIO_VO_NUOVO";

            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(iscr, context.AaInizio);
            decimal creditiMinimiCorrenti = GetCreditiMinimiRichiesti(iscr, context.Invalido, context.AaNumero, annoCorsoCalcolato);
            iscr.CreditiMinimiRichiestiMerito = creditiMinimiCorrenti > 0m ? creditiMinimiCorrenti : null;
            iscr.CreditiMinimiRichiestiPassaggio = creditiMinimiCorrenti > 0m ? creditiMinimiCorrenti : null;

            decimal creditiStudente = iscr.NumeroCrediti ?? 0m;
            if (creditiMinimiCorrenti > 0m && creditiStudente >= creditiMinimiCorrenti)
            {
                iscr.RegolaMeritoApplicata = "PASSAGGIO_VO_NUOVO_CREDITI_CORRENTE";
                return true;
            }

            decimal? esamiMinimiCorrenti = EsitoBorsaSupport.GetEsamiMinimiRichiesti(context, iscr);
            iscr.EsamiMinimiRichiestiPassaggio = esamiMinimiCorrenti;

            if (esamiMinimiCorrenti.HasValue)
            {
                iscr.RegolaMeritoApplicata = "PASSAGGIO_VO_NUOVO_ESAMI_CORRENTE";
                int esamiStudente = iscr.NumeroEsami ?? 0;
                return esamiStudente >= esamiMinimiCorrenti.Value;
            }

            iscr.RegolaMeritoApplicata = "PASSAGGIO_VO_NUOVO_DATI_CORRENTE";
            return creditiMinimiCorrenti <= 0m;
        }

        private static bool HaCreditiMinimiPerBorsa(EsitoBorsaStudentContext context, InformazioniIscrizione iscr, bool invalido, int aaNumero)
        {
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(iscr, context.AaInizio);
            decimal creditiRichiesti = GetCreditiMinimiRichiesti(iscr, invalido, aaNumero, annoCorsoCalcolato);
            iscr.CreditiMinimiRichiestiMerito = creditiRichiesti;
            if (creditiRichiesti <= 0m)
                return true;

            decimal creditiStudente = iscr.NumeroCrediti ?? 0m;
            if (creditiStudente >= creditiRichiesti)
                return true;

            decimal bonusUsabile = GetBonusUsabile(context, iscr, aaNumero, annoCorsoCalcolato);
            return creditiStudente + bonusUsabile >= creditiRichiesti;
        }

        private static decimal GetBonusUsabile(EsitoBorsaStudentContext context, InformazioniIscrizione iscr, int aaNumero, int annoCorsoCalcolato)
        {
            bool bonusRichiesto = iscr.UtilizzoBonus != 0 || (iscr.CreditiUtilizzati ?? 0m) > 0m;
            if (!bonusRichiesto)
                return 0m;

            if (!CanUseBonus(context, iscr))
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

        private static decimal GetCreditiMinimiRichiesti(InformazioniIscrizione iscr, bool invalido, int aaNumero, int annoCorsoRiferimento)
        {
            decimal value = GetCreditiMinimiBase(iscr, invalido, aaNumero, annoCorsoRiferimento);
            return ApplySpecialisticaPrimoFuoriCorsoRidotta(value, iscr, invalido, annoCorsoRiferimento);
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

            return annoCorsoRiferimento == -1 ? (invalido ? 56m : 63m) : (invalido ? 94m : currentValue);
        }



        private static bool HaEsamiMinimiPerBorsa(EsitoBorsaStudentContext context, InformazioniIscrizione iscr)
        {
            decimal? esamiRichiesti = EsitoBorsaSupport.GetEsamiMinimiRichiesti(context, iscr);
            iscr.EsamiMinimiRichiestiMerito = esamiRichiesti;
            if (!esamiRichiesti.HasValue || esamiRichiesti.Value <= 0m)
                return true;

            int esamiStudente = iscr.NumeroEsami ?? 0;
            return esamiStudente >= esamiRichiesti.Value;
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
