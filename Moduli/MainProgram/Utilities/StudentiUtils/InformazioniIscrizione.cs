using System;
using System.Collections.Generic;

namespace ProcedureNet7
{
    public class InformazioniIscrizione
    {
        public int AnnoCorso { get; set; }
        public int TipoCorso { get; set; }
        public string CodCorsoLaurea { get; set; } = string.Empty;
        public bool CorsoStem {  get; set; }
        public string CodSedeStudi { get; set; } = string.Empty;
        public string CodFacolta { get; set; } = string.Empty;
        public string AnnoAccadInizioCorso { get; set; } = string.Empty;
        public string CodEnte { get; set; } = string.Empty;
        public string ComuneSedeStudi { get; set; } = string.Empty;
        public string ProvinciaSedeStudi { get; set; } = string.Empty;
        public int ConfermaSemestreFiltro { get; set; }
        public bool CorsoMedicina { get; set; }
        public string TipoBando { get; set; } = string.Empty;
        public string CodSedeDistaccata { get; set; } = string.Empty;
        public decimal? CreditiTirocinio { get; set; }
        public decimal? CreditiRiconosciuti { get; set; }
        public int? AnnoImmatricolazione { get; set; }
        public int? NumeroEsami { get; set; }
        public decimal? NumeroCrediti { get; set; }
        public decimal? SommaVoti { get; set; }
        public int UtilizzoBonus { get; set; }
        public decimal? CreditiUtilizzati { get; set; }
        public decimal? CreditiRimanenti { get; set; }
        public decimal? CreditiRiconosciutiDaRinuncia { get; set; }
        public string AACreditiRiconosciuti { get; set; } = string.Empty;
        public int? DurataLegaleCorso { get; set; }
        public string CodTipoOrdinamentoCorso { get; set; } = string.Empty;


        public decimal? EsamiMinimiRichiestiMerito { get; set; }
        public decimal? CreditiMinimiRichiestiMerito { get; set; }
        public decimal? EsamiMinimiRichiestiPassaggio { get; set; }
        public decimal? CreditiMinimiRichiestiPassaggio { get; set; }
        public string RegolaMeritoApplicata { get; set; } = string.Empty;

        public int NumeroEventiCarrieraPregressa { get; set; }
        public int? UltimoAnnoAvvenimentoCarrieraPregressa { get; set; }
        public decimal TotaleCreditiCarrieraPregressa { get; set; }
        public int HaPassaggioCorsoEsteroCarrieraPregressa { get; set; }
        public int HaRipetenzaCarrieraPregressa { get; set; }
        public string CodiciAvvenimentoCarrieraPregressa { get; set; } = string.Empty;

        public List<InformazioniCarrieraPregressa> CarrierePregresse { get; } = new();
    }

    public class InformazioniCarrieraPregressa
    {
        public string CodAvvenimento { get; set; } = string.Empty;
        public int? AnnoAvvenimento { get; set; }
        public string UnivDiConseguim { get; set; } = string.Empty;
        public string UnivProvenienza { get; set; } = string.Empty;
        public int? PrimaImmatricolaz { get; set; }
        public string TipologiaCorso { get; set; } = string.Empty;
        public int? DurataLegTitoloConseguito { get; set; }
        public int PassaggioCorsoEstero { get; set; }
        public string SedeIstituzioneUniversitaria { get; set; } = string.Empty;
        public string BeneficiUsufruiti { get; set; } = string.Empty;
        public string ImportiRestituiti { get; set; } = string.Empty;
        public decimal? NumeroCrediti { get; set; }
        public int? AnnoCorso { get; set; }
        public int Ripetente { get; set; }
        public int ConfermaSemestreFiltroDi { get; set; }
    }
}
