using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class Detrazione
    {
        public double importo { get; private set; }
        public string causale { get; private set; }
        public string codReversale { get; private set; }
        public string codTipoPagamOld { get; private set; }
        public string codTipoPagamNew { get; private set; }
        public bool daContabilizzare { get; private set; }

        public Detrazione(string codReversale, double importo, string causale, string codTipoPagamOld, string codTipoPagamNew, bool daContabilizzare = false)
        {
            this.causale = causale;
            this.importo = importo;
            this.codReversale = codReversale;
            this.codTipoPagamOld = codTipoPagamOld;
            this.codTipoPagamNew = codTipoPagamNew;
            this.daContabilizzare = daContabilizzare;
        }

        public void DisableContabilizzare()
        {
            daContabilizzare = false;
        }

    }
}
