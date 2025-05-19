using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class PopulateStudentiImpegniDTO
    {
        public string CodFiscale { get; set; } = "";
        public string ImpegnoPrimaRata { get; set; } = "";
        public string ImpegnoSaldo { get; set; } = "";
        public double ImportoAssegnato { get; set; }
    }
    public class AssegnazionePaDto
    {
        public string CodFiscale { get; set; } = "";
        public string CodPensionato { get; set; } = "";
        public string CodStanza { get; set; } = "";
        public string DataDecorrenza { get; set; } = "";
        public string DataFineAssegnazione { get; set; } = "";
        public string CodFineAssegnazione { get; set; } = "";
        public string TipoStanza { get; set; } = "";
        public string ImportoMensileStr { get; set; } = "";
        public string IdAssegnazionePa { get; set; } = "";
    }
    public class StudentiDomicilioDTO
    {
        public string CodFiscale { get; set; } = "";
        public bool TitoloOneroso { get; set; }
        public bool ContrattoEnte { get; set; }
        public string SerieContratto { get; set; } = "";
        public string DataRegistrazioneString { get; set; } = "";
        public string DataDecorrenzaString { get; set; } = "";
        public string DataScadenzaString { get; set; } = "";
        public int DurataContratto { get; set; }
        public bool? Prorogato { get; set; }
        public int DurataProroga { get; set; }
        public string SerieProroga { get; set; } = "";
        public string DenominazioneEnte { get; set; } = "";
        public double ImportoRataEnte { get; set; }
        public string StatusSede { get; set; }
        public string CodBlocchi { get; set; }

        public string ComuneDomicilio { get; set; }
        public string ComuneResidenza { get; set; }
        public string ComuneSedeStudi { get; set; }

        public DateTime AAStart { get; set; }
        public DateTime AAEnd { get; set; }

        public int GiorniDallaScad { get; set; }   // NEW
        public DateTime? PrevScadenza { get; set; }
    }
}
