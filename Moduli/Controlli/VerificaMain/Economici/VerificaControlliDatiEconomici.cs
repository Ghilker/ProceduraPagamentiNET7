using System;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class VerificaControlliDatiEconomici
    {
        public void Calculate(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var calc = context.CalcParams ?? new CalcParams();

            foreach (var info in context.Students.Values)
            {
                var eco = info.InformazioniEconomiche;
                ResetCalcolate(eco.Calcolate);
                ApplyDbDerivedValues(eco, calc);

                eco.Calcolate.SEQ = ComputeSeqFinal(eco);
                eco.Calcolate.ISRDSU = Math.Max(eco.Calcolate.ISRDSU - eco.Calcolate.Detrazioni, 0m);

                decimal isedsu = eco.Calcolate.ISRDSU + 0.2m * eco.Calcolate.ISPDSU;
                decimal iseed = eco.Calcolate.SEQ > 0 ? isedsu / eco.Calcolate.SEQ : isedsu;
                decimal ispe = (eco.Calcolate.ISPDSU > 0 && eco.Calcolate.SEQ > 0) ? eco.Calcolate.ISPDSU / eco.Calcolate.SEQ : 0m;

                eco.Calcolate.ISEDSU = EconomiciFormulaSupport.RoundSql(isedsu, 2);
                eco.Calcolate.ISEEDSU = EconomiciFormulaSupport.RoundSql(iseed, 2);
                eco.Calcolate.ISPEDSU = EconomiciFormulaSupport.RoundSql(ispe, 2);
            }
        }

        private static void ResetCalcolate(InformazioniEconomiche.InformazioniEconomicheCalcolate calcolate)
        {
            calcolate.SEQ_Origine = 0m;
            calcolate.SEQ_Integrazione = 0m;
            calcolate.ISRDSU = 0m;
            calcolate.ISPDSU = 0m;
            calcolate.Detrazioni = 0m;
            calcolate.SommaRedditiStud = 0m;
            calcolate.ISEDSU = 0m;
            calcolate.ISEEDSU = 0m;
            calcolate.ISPEDSU = 0m;
            calcolate.SEQ = 0m;
        }

        private static void ApplyDbDerivedValues(InformazioniEconomiche eco, CalcParams calc)
        {
            eco.Calcolate.Detrazioni = eco.Raw.DetrazioniAdisu + eco.Raw.DetrazioniAltreBorse;
            ApplyOrigine(eco, calc);
            ApplyIntegrazione(eco, calc);
        }

        private static void ApplyOrigine(InformazioniEconomiche eco, CalcParams calc)
        {
            var raw = eco.Raw;
            var cal = eco.Calcolate;

            switch ((raw.OrigineFonte ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "CO":
                case "DO":
                    cal.SEQ_Origine = raw.OrigineScalaEquivalenza;
                    cal.SommaRedditiStud = raw.OrigineSommaRedditi;
                    cal.ISRDSU = raw.OrigineISR
                                - raw.OrigineReddFratelli50
                                + raw.OrigineReddFrat50Est
                                + raw.OrigineReddFam50Est
                                + raw.AltriMezzi
                                + (raw.OriginePatrFrat50Est - raw.OriginePatrFratelli50 + raw.OriginePatrFam50Est) * calc.RendPatr;
                    cal.ISPDSU = raw.OrigineISP - raw.OriginePatrImm50FratSor + raw.OrigineMetriQuadri * 500m;
                    break;

                case "EE":
                    cal.SEQ_Origine = EconomiciFormulaSupport.ScalaMin(raw.OrigineNumeroComponenti);
                    var patrAdj = Math.Max(raw.OriginePatrMobiliare + raw.OriginePatrMobFratell * 0.5m - calc.FranchigiaPatMob, 0m);
                    cal.ISRDSU = raw.OrigineRedditoComplessivo + raw.OrigineReddLordoFratell * 0.5m + patrAdj * calc.RendPatr + raw.AltriMezzi;
                    var isp = Math.Max((raw.OrigineSuperfAbitazMq + raw.OrigineSupComplAltreMq + raw.OrigineSupComplMq * 0.5m) * 500m - calc.Franchigia, 0m);
                    cal.ISPDSU = isp + patrAdj;
                    break;
            }
        }

        private static void ApplyIntegrazione(InformazioniEconomiche eco, CalcParams calc)
        {
            var raw = eco.Raw;
            var cal = eco.Calcolate;

            switch ((raw.IntegrazioneFonte ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "CI":
                    cal.SEQ_Integrazione = raw.IntegrazioneScalaEquivalenza;
                    raw.NumeroComponentiIntegrazione = raw.IntegrazioneNumeroComponenti;
                    cal.ISRDSU += raw.IntegrazioneISR
                                - raw.IntegrazioneReddFratelli50
                                + raw.IntegrazioneReddFrat50Est
                                + raw.IntegrazioneReddFam50Est
                                - (raw.IntegrazionePatrFrat50Est - raw.IntegrazionePatrFratelli50 + raw.IntegrazionePatrFam50Est) * calc.RendPatr;
                    cal.ISPDSU += raw.IntegrazioneISP + raw.IntegrazioneMetriQuadri * 500m;
                    break;

                case "DI":
                    raw.NumeroComponentiIntegrazione = raw.IntegrazioneNumeroComponenti;
                    cal.SEQ_Integrazione = EconomiciFormulaSupport.ScalaMin(raw.IntegrazioneNumeroComponenti);
                    var patrAdj = Math.Max(raw.IntegrazionePatrMobiliare + raw.IntegrazionePatrMobFratell * 0.5m - calc.FranchigiaPatMob, 0m);
                    cal.ISRDSU += raw.IntegrazioneRedditoComplessivo + raw.IntegrazioneReddLordoFratell * 0.5m + patrAdj * calc.RendPatr;
                    var ispAdd = Math.Max((raw.IntegrazioneSuperfAbitazMq + raw.IntegrazioneSupComplAltreMq + raw.IntegrazioneSupComplMq * 0.5m) * 500m - calc.Franchigia, 0m) + patrAdj;
                    cal.ISPDSU += ispAdd;
                    break;
            }
        }

        private static decimal ComputeSeqFinal(InformazioniEconomiche eco)
        {
            var raw = eco.Raw;
            var cal = eco.Calcolate;

            int componentiTotali = raw.NumeroComponenti > 0 ? raw.NumeroComponenti : 1;
            int conviventiEstero = Math.Max(raw.NumeroConviventiEstero, 0);

            decimal maggiorazioneStudente = 0m;
            int componentiStudente = componentiTotali;

            if (string.Equals(raw.TipoRedditoOrigine, "it", StringComparison.OrdinalIgnoreCase))
            {
                int baseComponenti = Math.Max(componentiTotali - conviventiEstero, 1);
                maggiorazioneStudente = (cal.SEQ_Origine > 0 ? cal.SEQ_Origine : 0m) - EconomiciFormulaSupport.ScalaMin(baseComponenti);
                componentiStudente = baseComponenti + conviventiEstero;
            }

            int componentiIntegrazione = Math.Max(raw.NumeroComponentiIntegrazione, 0);
            if (componentiIntegrazione <= 0)
            {
                var seqSoloStudente = EconomiciFormulaSupport.ScalaMin(componentiStudente) + maggiorazioneStudente;
                return EconomiciFormulaSupport.RoundSql(seqSoloStudente <= 0 ? 1m : seqSoloStudente, 2);
            }

            decimal maggiorazioneIntegrazione = 0m;
            if (string.Equals(raw.TipoRedditoIntegrazione, "it", StringComparison.OrdinalIgnoreCase))
                maggiorazioneIntegrazione = (cal.SEQ_Integrazione > 0 ? cal.SEQ_Integrazione : 0m) - EconomiciFormulaSupport.ScalaMin(componentiIntegrazione);

            int componentiTot = componentiStudente + componentiIntegrazione;
            decimal seq = EconomiciFormulaSupport.ScalaMin(componentiTot) + maggiorazioneStudente +
                          (string.Equals(raw.TipoRedditoIntegrazione, "it", StringComparison.OrdinalIgnoreCase) ? maggiorazioneIntegrazione : 0m);

            return EconomiciFormulaSupport.RoundSql(seq <= 0 ? 1m : seq, 2);
        }
    }

    internal static class EconomiciFormulaSupport
    {
        internal static decimal RoundSql(decimal value, int decimals) => Math.Round(value, decimals);

        internal static decimal ScalaMin(int componentCount)
        {
            if (componentCount < 1) componentCount = 1;
            return componentCount switch
            {
                1 => 1.00m,
                2 => 1.57m,
                3 => 2.04m,
                4 => 2.46m,
                5 => 2.85m,
                _ => 2.85m + (componentCount - 5) * 0.35m
            };
        }

        internal static int GetEseFinanziario(string aa) => aa switch
        {
            "20252026" => 2023,
            "20242025" => 2022,
            "20232024" => 2021,
            "20222023" => 2018,
            "20212022" => 2019,
            _ => int.TryParse(aa?.Substring(0, 4), out var year) ? year - 2 : 0
        };

        internal static string GetFiltroCodTipoPagam(string aa) =>
            aa == "20252026"
                ? "(p.Cod_tipo_pagam IN ('01','06','09','34','39','41','R1','R3','R4','R9','RR','S0','S1','S3','S5') OR p.Cod_tipo_pagam LIKE 'BSA%' OR p.Cod_tipo_pagam LIKE 'BSI%' OR p.Cod_tipo_pagam LIKE 'BSS%' OR p.Cod_tipo_pagam LIKE 'PL%')"
                : "p.Cod_tipo_pagam IN ('01','06','09','34','39','41','R1','R3','R4','R9','RR','S0','S1','S3','S5')";
    }
}
