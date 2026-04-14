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

            foreach (var pair in context.Students)
            {
                var info = pair.Value;
                Reset(info);

                var studentContext = new EsitoBorsaStudentContext(context, pair.Key, info, aaInizio, aaNumero, config);
                var evaluation = RuleEngine.Evaluate(studentContext);
                ApplyEvaluation(info, evaluation);

                if (evaluation.HasErrors)
                    esclusi++;
                else
                    idonei++;
            }

            Logger.LogInfo(
                null,
                $"[Verifica.Module.{Name}] Regole applicate | students={context.Students.Count} | idonei={idonei} | esclusi={esclusi} | sogliaIsee={config.SogliaIsee.ToString(CultureInfo.InvariantCulture)} | sogliaIsp={config.SogliaIsp.ToString(CultureInfo.InvariantCulture)}");
        }

        private static void ApplyEvaluation(StudenteInfo info, EsitoBorsaEvaluation evaluation)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            if (evaluation == null)
                throw new ArgumentNullException(nameof(evaluation));

            if (evaluation.HasErrors)
            {
                info.EsitoBorsaCalcolato = EsitoEscluso;
                info.CodiciMotivoEsitoBorsaCalcolato = string.Join(";", evaluation.ErrorCodes);
                info.MotiviEsitoBorsaCalcolato = string.Join(" | ", evaluation.ErrorCodes.ConvertAll(EsitoBorsaSupport.GetMotivoEsclusione));
            }
            else
            {
                info.EsitoBorsaCalcolato = EsitoIdoneo;
                info.CodiciMotivoEsitoBorsaCalcolato = string.Empty;
                info.MotiviEsitoBorsaCalcolato = string.Empty;
            }

            info.CalcoloEsitoBorsaEseguito = true;
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
