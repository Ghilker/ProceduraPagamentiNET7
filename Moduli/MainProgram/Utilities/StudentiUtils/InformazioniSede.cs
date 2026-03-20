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
        public string StatusSedeSuggerito { get; set; } = string.Empty;
        public string ForzaturaStatusSede { get; set; } = string.Empty;
        public DateTime? PrevScadenza { get; set; }
        public int GiorniDallaScad { get; set; }
        public string CodBlocchi { get; set; } = string.Empty;

        public string CodComuneSedeStudi {  get; set; } = string.Empty;
        public string CodProvinciaSedeStudi {  get; set; } = string.Empty;

        public string MotivoStatusSede { get; set; } = "";
        public bool DomicilioPresente { get; set; }
        public bool DomicilioValido { get; set; }
        public bool HasAlloggio12 { get; set; }
        public bool HasIstanzaDomicilio { get; set; }
        public string CodTipoIstanzaDomicilio { get; set; } = "";
        public int NumIstanzaDomicilio { get; set; }
        public bool HasUltimaIstanzaChiusaDomicilio { get; set; }
        public string CodTipoUltimaIstanzaChiusaDomicilio { get; set; } = "";
        public int NumUltimaIstanzaChiusaDomicilio { get; set; }
        public string EsitoUltimaIstanzaChiusaDomicilio { get; set; } = "";
        public string UtentePresaCaricoUltimaIstanzaChiusaDomicilio { get; set; } = "";
    }
}
