using System;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class VerificaControlliDatiEconomici : IVerificaModule
    {
        public string Name => "Economici";

        public void Calculate(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var calc = context.CalcParams ?? new CalcParams();

            foreach (var info in context.Students.Values)
            {
                var eco = info.InformazioniEconomiche;
                var cal = eco.Calcolate;

                decimal seqOrigine;
                decimal seqIntegrazione;
                decimal isrDsu;
                decimal ispDsu;
                decimal sommaRedditiStud;
                int numeroComponentiIntegrazione;

                ComputeDbDerivedValues(eco, calc, out seqOrigine, out seqIntegrazione, out isrDsu, out ispDsu, out sommaRedditiStud, out numeroComponentiIntegrazione);

                cal.SEQ_Origine = seqOrigine;
                cal.SEQ_Integrazione = seqIntegrazione;
                cal.ISRDSU = isrDsu;
                cal.ISPDSU = ispDsu;
                cal.Detrazioni = eco.Raw.DetrazioniAdisu + eco.Raw.DetrazioniAltreBorse;
                cal.SommaRedditiStud = sommaRedditiStud;

                cal.SEQ = ComputeSeqFinal(eco, numeroComponentiIntegrazione);
                cal.ISRDSU = Math.Max(cal.ISRDSU - cal.Detrazioni, 0m);

                decimal isedsu = cal.ISRDSU + 0.2m * cal.ISPDSU;
                decimal iseed = cal.SEQ > 0m ? isedsu / cal.SEQ : isedsu;
                decimal ispe = (cal.ISPDSU > 0m && cal.SEQ > 0m) ? cal.ISPDSU / cal.SEQ : 0m;

                cal.ISEDSU = EconomiciFormulaSupport.RoundSql(isedsu, 2);
                cal.ISEEDSU = EconomiciFormulaSupport.RoundSql(iseed, 2);
                cal.ISPEDSU = EconomiciFormulaSupport.RoundSql(ispe, 2);
            }
        }

        private static void ComputeDbDerivedValues(
            InformazioniEconomiche eco,
            CalcParams calc,
            out decimal seqOrigine,
            out decimal seqIntegrazione,
            out decimal isrDsu,
            out decimal ispDsu,
            out decimal sommaRedditiStud,
            out int numeroComponentiIntegrazione)
        {
            ComputeOrigine(eco, calc, out seqOrigine, out isrDsu, out ispDsu, out sommaRedditiStud);
            ComputeIntegrazione(eco, calc, out seqIntegrazione, out numeroComponentiIntegrazione, ref isrDsu, ref ispDsu);
        }

        private static void ComputeOrigine(
            InformazioniEconomiche eco,
            CalcParams calc,
            out decimal seqOrigine,
            out decimal isrDsu,
            out decimal ispDsu,
            out decimal sommaRedditiStud)
        {
            var raw = eco.Raw;
            seqOrigine = 0m;
            isrDsu = 0m;
            ispDsu = 0m;
            sommaRedditiStud = 0m;

            switch ((raw.OrigineFonte ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "CO":
                case "DO":
                    seqOrigine = raw.OrigineScalaEquivalenza;
                    sommaRedditiStud = raw.OrigineSommaRedditi;
                    isrDsu = raw.OrigineISR
                                - raw.OrigineReddFratelli50
                                + raw.OrigineReddFrat50Est
                                + raw.OrigineReddFam50Est
                                + raw.AltriMezzi
                                + (raw.OriginePatrFrat50Est - raw.OriginePatrFratelli50 + raw.OriginePatrFam50Est) * calc.RendPatr;
                    ispDsu = raw.OrigineISP - raw.OriginePatrImm50FratSor + raw.OrigineMetriQuadri * 500m;
                    break;

                case "EE":
                    seqOrigine = EconomiciFormulaSupport.ScalaMin(raw.OrigineNumeroComponenti);
                    var patrAdj = Math.Max(raw.OriginePatrMobiliare + raw.OriginePatrMobFratell * 0.5m - calc.FranchigiaPatMob, 0m);
                    isrDsu = raw.OrigineRedditoComplessivo + raw.OrigineReddLordoFratell * 0.5m + patrAdj * calc.RendPatr + raw.AltriMezzi;
                    var isp = Math.Max((raw.OrigineSuperfAbitazMq + raw.OrigineSupComplAltreMq + raw.OrigineSupComplMq * 0.5m) * 500m - calc.Franchigia, 0m);
                    ispDsu = isp + patrAdj;
                    break;
            }
        }

        private static void ComputeIntegrazione(
            InformazioniEconomiche eco,
            CalcParams calc,
            out decimal seqIntegrazione,
            out int numeroComponentiIntegrazione,
            ref decimal isrDsu,
            ref decimal ispDsu)
        {
            var raw = eco.Raw;
            seqIntegrazione = 0m;
            numeroComponentiIntegrazione = 0;

            switch ((raw.IntegrazioneFonte ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "CI":
                    seqIntegrazione = raw.IntegrazioneScalaEquivalenza;
                    numeroComponentiIntegrazione = raw.IntegrazioneNumeroComponenti;
                    isrDsu += raw.IntegrazioneISR
                                - raw.IntegrazioneReddFratelli50
                                + raw.IntegrazioneReddFrat50Est
                                + raw.IntegrazioneReddFam50Est
                                - (raw.IntegrazionePatrFrat50Est - raw.IntegrazionePatrFratelli50 + raw.IntegrazionePatrFam50Est) * calc.RendPatr;
                    ispDsu += raw.IntegrazioneISP + raw.IntegrazioneMetriQuadri * 500m;
                    break;

                case "DI":
                    numeroComponentiIntegrazione = raw.IntegrazioneNumeroComponenti;
                    seqIntegrazione = EconomiciFormulaSupport.ScalaMin(raw.IntegrazioneNumeroComponenti);
                    var patrAdj = Math.Max(raw.IntegrazionePatrMobiliare + raw.IntegrazionePatrMobFratell * 0.5m - calc.FranchigiaPatMob, 0m);
                    isrDsu += raw.IntegrazioneRedditoComplessivo + raw.IntegrazioneReddLordoFratell * 0.5m + patrAdj * calc.RendPatr;
                    var ispAdd = Math.Max((raw.IntegrazioneSuperfAbitazMq + raw.IntegrazioneSupComplAltreMq + raw.IntegrazioneSupComplMq * 0.5m) * 500m - calc.Franchigia, 0m) + patrAdj;
                    ispDsu += ispAdd;
                    break;
            }
        }

        private static decimal ComputeSeqFinal(InformazioniEconomiche eco, int numeroComponentiIntegrazione)
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

            int componentiIntegrazione = Math.Max(numeroComponentiIntegrazione, 0);
            if (componentiIntegrazione <= 0)
            {
                decimal seqSoloStudente = EconomiciFormulaSupport.ScalaMin(componentiStudente) + maggiorazioneStudente;
                return EconomiciFormulaSupport.RoundSql(seqSoloStudente <= 0m ? 1m : seqSoloStudente, 2);
            }

            decimal maggiorazioneIntegrazione = 0m;
            if (string.Equals(raw.TipoRedditoIntegrazione, "it", StringComparison.OrdinalIgnoreCase))
                maggiorazioneIntegrazione = (cal.SEQ_Integrazione > 0 ? cal.SEQ_Integrazione : 0m) - EconomiciFormulaSupport.ScalaMin(componentiIntegrazione);

            int componentiTot = componentiStudente + componentiIntegrazione;
            decimal seq = EconomiciFormulaSupport.ScalaMin(componentiTot) + maggiorazioneStudente +
                          (string.Equals(raw.TipoRedditoIntegrazione, "it", StringComparison.OrdinalIgnoreCase) ? maggiorazioneIntegrazione : 0m);

            return EconomiciFormulaSupport.RoundSql(seq <= 0m ? 1m : seq, 2);
        }
    }

    internal static class EconomiciFormulaSupport
    {
        internal static decimal RoundSql(decimal value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

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
