using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class InformazioniConto
    {
        public string IBAN { get; set; } = string.Empty;
        public string Swift { get; set; } = string.Empty;
        public bool BonificoEstero { get; set; }
    }
}
