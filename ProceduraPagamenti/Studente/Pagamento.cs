using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class Pagamento
    {
        public string codTipoPagam { get; private set; }
        public double importoPagamento { get; private set; }
        public bool ritiratoAzienda { get; private set; }

        public Pagamento(string codTipoPagam, double importoPagamento, bool ritiratoAzienda)
        {
            this.codTipoPagam = codTipoPagam;
            this.importoPagamento = importoPagamento;
            this.ritiratoAzienda = ritiratoAzienda;
        }
    }
}