using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class PopulateStudentiImpegniDTO
    {
        public string CodFiscale { get; set; } = string.Empty;
        public string ImpegnoPrimaRata { get; set; } = string.Empty;
        public string ImpegnoSaldo { get; set; } = string.Empty;
        public double ImportoAssegnato { get; set; }
        public string CategoriaCU { get; set; } = string.Empty;
    }
    public class AssegnazionePaDto
    {
        public string CodFiscale { get; set; } = string.Empty;
        public string CodPensionato { get; set; } = string.Empty;
        public string CodStanza { get; set; } = string.Empty;
        public string DataDecorrenza { get; set; } = string.Empty;
        public string DataFineAssegnazione { get; set; } = string.Empty;
        public string CodFineAssegnazione { get; set; } = string.Empty;
        public string TipoStanza { get; set; } = string.Empty;
        public string ImportoMensileStr { get; set; } = string.Empty;
        public string IdAssegnazionePa { get; set; } = string.Empty;
    }
    public class StudentiDomicilioDTO
    {
        public string CodFiscale { get; set; } = string.Empty;
        public bool TitoloOneroso { get; set; }
        public bool ContrattoEnte { get; set; }
        public string SerieContratto { get; set; } = string.Empty;
        public string DataRegistrazioneString { get; set; } = string.Empty;
        public string DataDecorrenzaString { get; set; } = string.Empty;
        public string DataScadenzaString { get; set; } = string.Empty;
        public int DurataContratto { get; set; }
        public bool? Prorogato { get; set; }
        public int DurataProroga { get; set; }
        public string SerieProroga { get; set; } = string.Empty;
        public string DenominazioneEnte { get; set; } = string.Empty;
        public double ImportoRataEnte { get; set; }
        public string StatusSede { get; set; } = string.Empty;
        public string CodBlocchi { get; set; } = string.Empty;

        public string ComuneDomicilio { get; set; } = string.Empty;
        public string ComuneResidenza { get; set; } = string.Empty;
        public string ComuneSedeStudi { get; set; } = string.Empty;

        public DateTime AAStart { get; set; }
        public DateTime AAEnd { get; set; }

        public int GiorniDallaScad { get; set; }   // NEW
        public DateTime? PrevScadenza { get; set; }
    }
}
