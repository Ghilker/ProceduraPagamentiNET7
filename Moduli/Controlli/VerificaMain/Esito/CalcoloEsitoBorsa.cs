using System;
using System.Globalization;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class CalcoloEsitoBorsa : IVerificaModule
    {
        private const int EsitoEscluso = 0;
        private const int EsitoIdoneo = 1;
        private const int EsitoVincitore = 2;

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

            foreach (var pair in context.Students)
            {
                var info = pair.Value;

                context.TryGetEsitoBorsaFacts(pair.Key, out var facts);
                var benefitCodes = EsitoBorsaSupport.GetRequestedBenefitCodes(facts);
                var results = new System.Collections.Generic.Dictionary<string, EsitoBeneficioCalcolato>(StringComparer.OrdinalIgnoreCase);

                foreach (var codBeneficio in benefitCodes)
                {
                    var studentContext = new EsitoBorsaStudentContext(context, pair.Key, info, aaInizio, aaNumero, config, codBeneficio);
                    var result = CalculateBenefitResult(studentContext);
                    results[codBeneficio] = result;

                    if (result.EsitoCalcolato == EsitoEscluso)
                        esclusi++;
                    else
                        idonei++;

                    benefitRows++;
                }

                var targetResults = context.GetOrCreateEsitiCalcolatiByBenefit(pair.Key);
                targetResults.Clear();
                foreach (var resultPair in results)
                    targetResults[resultPair.Key] = resultPair.Value;
                ApplyLegacyBsEvaluation(info, results);
                info.CalcoloEsitoBorsaEseguito = results.Count > 0;
            }

            Logger.LogInfo(
                null,
                $"[Verifica.Module.{Name}] Regole applicate | students={context.Students.Count} | benefitRows={benefitRows} | idonei={idonei} | esclusi={esclusi} | sogliaIsee={config.SogliaIsee.ToString(CultureInfo.InvariantCulture)} | sogliaIsp={config.SogliaIsp.ToString(CultureInfo.InvariantCulture)}");
        }

        internal static EsitoBeneficioCalcolato CalculateBenefitResult(EsitoBorsaStudentContext context)
        {
            var evaluation = RuleEngine.Evaluate(context);
            var contextSenzaForzature = new EsitoBorsaStudentContext(
                context.Pipeline, context.Key, context.Info, context.AaInizio, context.AaNumero,
                context.Config, context.CodBeneficio, ignoraForzature: true);
            var evaluationSenzaForzature = RuleEngine.Evaluate(contextSenzaForzature);

            int esitoAmmesso = EsitoIdoneo;
            if (context.Pipeline.TryGetEsitiConcorsoByBenefit(context.Key, out var esitiAttuali) &&
                esitiAttuali.TryGetValue(context.CodBeneficio, out var esitoAttuale) &&
                esitoAttuale?.CodTipoEsito == EsitoVincitore)
            {
                esitoAmmesso = EsitoVincitore;
            }

            int esitoCalcolato = evaluation.HasErrors ? EsitoEscluso : esitoAmmesso;
            return new EsitoBeneficioCalcolato
            {
                CodBeneficio = context.CodBeneficio,
                Richiesto = EsitoBorsaSupport.IsBenefitRequested(context.Facts, context.CodBeneficio),
                EsitoCalcolato = esitoCalcolato,
                EsitoCalcolatoSenzaForzature = evaluationSenzaForzature.HasErrors ? EsitoEscluso : esitoAmmesso,
                ImportoCalcolato = context.CodBeneficio switch
                {
                    "BS" => esitoCalcolato == EsitoEscluso ? 0m : context.Info.InformazioniImportoBorsa.ImportoFinale,
                    "PA" => 0m,
                    _ => null
                },
                CodiciMotivo = evaluation.HasErrors ? string.Join(";", evaluation.ErrorCodes) : string.Empty,
                Motivi = evaluation.HasErrors ? string.Join(" | ", evaluation.ErrorCodes.ConvertAll(EsitoBorsaSupport.GetMotivoEsclusione)) : string.Empty
            };
        }

        private static void ApplyLegacyBsEvaluation(StudenteInfo info, System.Collections.Generic.IReadOnlyDictionary<string, EsitoBeneficioCalcolato> results)
        {
            if (results != null && results.TryGetValue("BS", out var bs))
            {
                info.EsitoBorsaCalcolato = bs.EsitoCalcolato;
                info.InformazioniImportoBorsa.ImportoFinale = bs.ImportoCalcolato;
                info.InformazioniImportoBorsa.CalcoloEseguito = bs.ImportoCalcolato.HasValue;
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

    }
}
