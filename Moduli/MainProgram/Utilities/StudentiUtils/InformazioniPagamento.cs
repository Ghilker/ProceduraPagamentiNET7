using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class InformazioniPagamento
    {
        public string NumeroImpegno { get; set; } = string.Empty;
        public string CategoriaCU {  get; set; } = string.Empty; 
        public double ImportoPagato { get; set; }
        public double ImportoDaPagareLordo { get; set; }
        public double ImportoDaPagare { get; set; }
        public double ImportoAccontoPA { get; set; }
        public double ImportoSaldoPA { get; set; }
        public string MandatoProvvisorio { get; set; } = string.Empty;
        public bool PagatoPendolare { get; set; }
        public double ValoreISEE { get; set; }
        public bool ConcessaMonetizzazioneMensa { get; set; }

        public List<Assegnazione> Assegnazioni { get; set; } = new List<Assegnazione>();
        public List<Pagamento> PagamentiEffettuati { get; set; } = new List<Pagamento>();
        public List<Reversale> Reversali { get; set; } = new List<Reversale>();
        public List<Detrazione> Detrazioni { get; set; } = new List<Detrazione>();
        public double GeneratoreFlussoReversaleNONLOTOCCAREGIACOMOTIAMMAZZO { get; set; }

    }
}
