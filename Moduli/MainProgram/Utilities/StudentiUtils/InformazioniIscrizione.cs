using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class InformazioniIscrizione
    {
        public int AnnoCorso { get; set; }
        public int TipoCorso { get; set; }
        public string CodCorsoLaurea { get; set; } = string.Empty;
        public string CodSedeStudi { get; set; } = string.Empty;
        public string CodFacolta { get; set; } = string.Empty;
        public string CodEnte { get; set; } = string.Empty;
        public string ComuneSedeStudi { get; set; } = string.Empty;
    }
}
