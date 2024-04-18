using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class Residenza
    {
        public string indirizzo;
        public string codComune;
        public string provincia;
        public string CAP;
        public string nomeComune;

        public Residenza()
        {
            this.indirizzo = string.Empty;
            this.codComune = string.Empty;
            this.provincia = string.Empty;
            this.CAP = string.Empty;
            this.nomeComune = string.Empty;
        }

        public Residenza(string indirizzo, string codComune, string provincia, string CAP, string nomeComune)
        {
            this.indirizzo = indirizzo;
            this.codComune = codComune;
            this.provincia = provincia;
            this.CAP = CAP;
            this.nomeComune = nomeComune;
        }
    }
}
