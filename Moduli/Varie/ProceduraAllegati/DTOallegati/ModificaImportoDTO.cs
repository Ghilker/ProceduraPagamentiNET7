using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ModificaImportoDTO
    {
        public string NumDomanda {  get; set; } = string.Empty;
        public string CodFiscale {  get; set; } = string.Empty;
        public string Nome {  get; set; } = string.Empty;
        public string Cognome {  get; set; } = string.Empty;
        public double ImpPrecedente { get; set; }
        public double ImpAttuale { get; set; }
        public double DifferenzaImp { get; set; }
        public string ImpegnoSaldo { get; set; } = string.Empty;
        public bool CambioAnno {  get; set; }
        public bool CambioStem { get; set; }
        public bool CambioISEE { get; set; }
        public bool DoppiaIscr {  get; set; }
        public bool Invalido { get; set; }
    }
}
