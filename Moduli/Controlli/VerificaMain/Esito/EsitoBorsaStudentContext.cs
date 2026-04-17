using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaStudentContext
    {
        public EsitoBorsaStudentContext(
            VerificaPipelineContext pipeline,
            StudentKey key,
            StudenteInfo info,
            int aaInizio,
            int aaNumero,
            EsitoBorsaRuleConfig config,
            string codBeneficio)
        {
            Pipeline = pipeline;
            Key = key;
            Info = info;
            AaInizio = aaInizio;
            AaNumero = aaNumero;
            Config = config;
            CodBeneficio = EsitoBorsaSupport.NormalizeUpper(codBeneficio);
            Facts = GetFacts(pipeline, key);
        }

        public VerificaPipelineContext Pipeline { get; }
        public StudentKey Key { get; }
        public StudenteInfo Info { get; }
        public EsitoBorsaFacts Facts { get; }
        public int AaInizio { get; }
        public int AaNumero { get; }
        public EsitoBorsaRuleConfig Config { get; }
        public string CodBeneficio { get; }

        public InformazioniIscrizione? Iscrizione => Info?.InformazioniIscrizione;
        public bool Invalido => Info?.InformazioniPersonali?.Disabile == true;

        private static EsitoBorsaFacts GetFacts(VerificaPipelineContext pipeline, StudentKey key)
        {
            if (pipeline.EsitoBorsaFactsByStudent.TryGetValue(key, out var facts) && facts != null)
                return facts;

            facts = new EsitoBorsaFacts();
            pipeline.EsitoBorsaFactsByStudent[key] = facts;
            return facts;
        }
    }
}
