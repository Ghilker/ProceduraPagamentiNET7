using System;
using System.Globalization;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class CalcoloEsitoBorsa : IVerificaModule
    {
        private const int EsitoEscluso = 0;
        private const int EsitoIdoneo = 1;

        private static readonly EsitoBorsaRuleEngine RuleEngine = new();

        public string Name => "EsitoBorsa";

        public void Calculate(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            int aaInizio = EsitoBorsaSupport.ParseAnnoAccademicoInizio(context.AnnoAccademico);
            int aaNumero = EsitoBorsaSupport.ParseAnnoAccademicoAsNumber(context.AnnoAccademico);
            var config = EsitoBorsaSupport.LoadRuleConfig(context);

            int esclusi = 0;
            int idonei = 0;
            int benefitRows = 0;

            context.EsitiCalcolatiByStudentBenefit.Clear();

            foreach (var pair in context.Students)
            {
                var info = pair.Value;
                Reset(info);

                context.EsitoBorsaFactsByStudent.TryGetValue(pair.Key, out var facts);
                var benefitCodes = EsitoBorsaSupport.GetBenefitCodes(context, pair.Key, facts);
                var results = new System.Collections.Generic.Dictionary<string, EsitoBeneficioCalcolato>(StringComparer.OrdinalIgnoreCase);

                foreach (var codBeneficio in benefitCodes)
                {
                    var studentContext = new EsitoBorsaStudentContext(context, pair.Key, info, aaInizio, aaNumero, config, codBeneficio);
                    var evaluation = RuleEngine.Evaluate(studentContext);
                    var result = BuildBenefitResult(studentContext, evaluation);
                    results[codBeneficio] = result;

                    if (evaluation.HasErrors)
                        esclusi++;
                    else
                        idonei++;

                    benefitRows++;
                }

                context.EsitiCalcolatiByStudentBenefit[pair.Key] = results;
                ApplyLegacyBsEvaluation(info, results);
                info.CalcoloEsitoBorsaEseguito = results.Count > 0;
            }

            Logger.LogInfo(
                null,
                $"[Verifica.Module.{Name}] Regole applicate | students={context.Students.Count} | benefitRows={benefitRows} | idonei={idonei} | esclusi={esclusi} | sogliaIsee={config.SogliaIsee.ToString(CultureInfo.InvariantCulture)} | sogliaIsp={config.SogliaIsp.ToString(CultureInfo.InvariantCulture)}");
        }

        private static EsitoBeneficioCalcolato BuildBenefitResult(EsitoBorsaStudentContext context, EsitoBorsaEvaluation evaluation)
        {
            return new EsitoBeneficioCalcolato
            {
                CodBeneficio = context.CodBeneficio,
                Richiesto = EsitoBorsaSupport.IsBenefitRequested(context.Facts, context.CodBeneficio),
                EsitoCalcolato = evaluation.HasErrors ? EsitoEscluso : EsitoIdoneo,
                CodiciMotivo = evaluation.HasErrors ? string.Join(";", evaluation.ErrorCodes) : string.Empty,
                Motivi = evaluation.HasErrors ? string.Join(" | ", evaluation.ErrorCodes.ConvertAll(EsitoBorsaSupport.GetMotivoEsclusione)) : string.Empty
            };
        }

        private static void ApplyLegacyBsEvaluation(StudenteInfo info, System.Collections.Generic.IReadOnlyDictionary<string, EsitoBeneficioCalcolato> results)
        {
            if (results != null && results.TryGetValue("BS", out var bs))
            {
                info.EsitoBorsaCalcolato = bs.EsitoCalcolato;
                info.CodiciMotivoEsitoBorsaCalcolato = bs.CodiciMotivo ?? string.Empty;
                info.MotiviEsitoBorsaCalcolato = bs.Motivi ?? string.Empty;
                return;
            }

            if (results != null && results.Count > 0)
            {
                foreach (var item in results.Values)
                {
                    info.EsitoBorsaCalcolato = item.EsitoCalcolato;
                    info.CodiciMotivoEsitoBorsaCalcolato = item.CodiciMotivo ?? string.Empty;
                    info.MotiviEsitoBorsaCalcolato = item.Motivi ?? string.Empty;
                    return;
                }
            }

            info.EsitoBorsaCalcolato = EsitoIdoneo;
            info.CodiciMotivoEsitoBorsaCalcolato = string.Empty;
            info.MotiviEsitoBorsaCalcolato = string.Empty;
        }

        private static void Reset(StudenteInfo info)
        {
            info.EsitoBorsaCalcolato = EsitoIdoneo;
            info.CodiciMotivoEsitoBorsaCalcolato = string.Empty;
            info.MotiviEsitoBorsaCalcolato = string.Empty;
            info.CalcoloEsitoBorsaEseguito = false;
        }
    }
}
