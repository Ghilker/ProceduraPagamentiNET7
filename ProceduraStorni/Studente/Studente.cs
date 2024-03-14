using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.Storni
{
    internal class Studente
    {
        public string codFiscale { get; private set; }
        public string IBAN { get; private set; }
        public string mandatoPagamento { get; private set; }
        public string impegnoReintroito { get; private set; }
        public string studenteAA { get; private set; }
        public Studente(string codFiscale, string IBAN, string mandatoPagamento, string impegnoReintroito)
        {
            this.codFiscale = codFiscale;
            this.mandatoPagamento = mandatoPagamento;
            this.IBAN = IBAN;
            this.impegnoReintroito = impegnoReintroito;
        }

        public void SetStudenteAA(string studenteAA)
        {
            this.studenteAA = studenteAA;
        }
    }
}
