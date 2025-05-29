using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class InformazioniSede
    {
        public Domicilio Domicilio { get; set; } = new Domicilio();
        public bool ContrattoValido { get; set; }
        public bool ProrogaValido { get; set; }
        public bool ContrattoEnte { get; set; }
        public bool DomicilioDefinito { get; set; }
        public bool DomicilioCheck { get; set; }

        public Residenza Residenza { get; set; } = new Residenza();

        public string StatusSede { get; set; } = string.Empty;
        public string ForzaturaStatusSede { get; set; } = string.Empty;
        public DateTime? PrevScadenza { get; set; }
        public int GiorniDallaScad { get; set; }
        public string CodBlocchi { get; set; } = string.Empty;
    }
}
