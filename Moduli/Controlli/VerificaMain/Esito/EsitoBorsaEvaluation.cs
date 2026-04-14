using System.Collections.Generic;

namespace ProcedureNet7
{
    internal sealed class EsitoBorsaEvaluation
    {
        public List<string> ErrorCodes { get; } = new();
        public bool HasErrors => ErrorCodes.Count > 0;

        public void Add(string code)
        {
            EsitoBorsaSupport.AddMotivoEsclusione(ErrorCodes, code);
        }
    }
}
