using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class Detrazione
    {
        public double importo { get; private set; }
        public string causale { get; private set; }
        public string codReversale { get; private set; }
        public bool needUpdate { get; private set; }

        public Detrazione(string codReversale, double importo, string causale, bool needUpdate = false)
        {
            this.causale = causale;
            this.importo = importo;
            this.codReversale = codReversale;
            this.needUpdate = needUpdate;
        }
    }
}
