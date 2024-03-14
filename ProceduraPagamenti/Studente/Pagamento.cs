using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class Pagamento
    {
        public string codTipoPagam { get; private set; }
        public bool ritiratoAzienda { get; private set; }

        public Pagamento(string codTipoPagam, bool ritiratoAzienda)
        {
            this.codTipoPagam = codTipoPagam;
            this.ritiratoAzienda = ritiratoAzienda;
        }
    }
}
