using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class CalcoloEsitoBorsa : IVerificaModule
    {
        public string Name => "EsitoBorsa";

        public void Calculate(VerificaPipelineContext context)
        {
            throw new NotImplementedException();
        }
    }
}
