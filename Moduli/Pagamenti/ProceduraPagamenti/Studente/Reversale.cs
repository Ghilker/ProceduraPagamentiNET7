using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class Reversale
    {
        public double importo { get; private set; }
        public string causale { get; private set; }
        public string codReversale { get; private set; }
        public string codTipoPagamOld { get; private set; }
        public string codTipoPagamNew { get; private set; }

        public Reversale(string codReversale, double importo, string causale, string codTipoPagamOld, string codTipoPagamNew)
        {
            this.causale = causale;
            this.importo = importo;
            this.codReversale = codReversale;
            this.codTipoPagamOld = codTipoPagamOld;
            this.codTipoPagamNew = codTipoPagamNew;
        }
    }
}
