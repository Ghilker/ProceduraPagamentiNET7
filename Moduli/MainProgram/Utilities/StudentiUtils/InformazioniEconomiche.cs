using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public sealed class InformazioniEconomiche
    {
        public string TipoRedditoOrigine { get; set; } = "";
        public string TipoRedditoIntegrazione { get; set; } = "";
        public int? CodTipoEsitoBS { get; set; }
        public double? ImportoAssegnato { get; set; }

        public int NumeroComponenti { get; set; }
        public int NumeroConviventiEstero { get; set; }
        public int NumeroComponentiIntegrazione { get; set; }
        public string TipoNucleo { get; set; } = "";

        public decimal AltriMezzi { get; set; }
        public decimal SEQ_Origine { get; set; }
        public decimal SEQ_Integrazione { get; set; }
        public decimal ISRDSU { get; set; }
        public decimal ISPDSU { get; set; }
        public decimal Detrazioni { get; set; }
        public decimal SommaRedditiStud { get; set; }

        public decimal ISEDSU { get; set; }
        public decimal ISEEDSU { get; set; }
        public decimal ISPEDSU { get; set; }
        public decimal SEQ { get; set; }

        public double ISEDSU_Attuale { get; set; }
        public double ISEEDSU_Attuale { get; set; }
        public double ISPEDSU_Attuale { get; set; }
        public double ISPDSU_Attuale { get; set; }
        public double SEQ_Attuale { get; set; }

        public int StatusInpsOrigine { get; set; }
        public int StatusInpsIntegrazione { get; set; }
        public bool CoAttestazioneOk { get; set; }
    }
}
