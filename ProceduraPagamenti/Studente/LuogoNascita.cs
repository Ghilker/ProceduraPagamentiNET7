using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class LuogoNascita
    {
        public string codComune;
        public string nomeComune;
        public string provincia;

        public LuogoNascita()
        {
            this.codComune = string.Empty;
            this.nomeComune = string.Empty;
            this.provincia = string.Empty;
        }

        public LuogoNascita(
                string codComune,
                string nomeComune,
                string provincia
            )
        {
            this.codComune = codComune;
            this.nomeComune = nomeComune;
            this.provincia = provincia;
        }
    }
}
