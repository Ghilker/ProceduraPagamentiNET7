using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class InformazioniPersonali
    {
        public string CodFiscale { get; set; } = string.Empty;
        public string NumDomanda { get; set; } = string.Empty;
        public string Cognome { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string DataNascita { get; set; } = string.Empty;  // or DateTime
        public string Sesso { get; set; } = string.Empty;
        public bool Disabile { get; set; }
        public string CodCittadinanza { get; set; } = string.Empty;
        public long Telefono { get; set; }
        public string IndirizzoEmail { get; set; } = string.Empty;
        public int NumeroComponentiNucleoFamiliare { get; set; }
        public int NumeroComponentiNucleoFamiliareEstero { get; set; }
        public LuogoNascita LuogoNascita { get; set; } = new LuogoNascita();
        public bool Rifugiato { get; set; }
    }
}
