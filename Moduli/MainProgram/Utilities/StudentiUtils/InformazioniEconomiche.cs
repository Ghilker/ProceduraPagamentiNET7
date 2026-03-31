using System;

namespace ProcedureNet7
{
    public sealed class InformazioniEconomiche
    {
        public InformazioniEconomicheRaw Raw { get; set; } = new InformazioniEconomicheRaw();
        public InformazioniEconomicheAttuali Attuali { get; set; } = new InformazioniEconomicheAttuali();
        public InformazioniEconomicheCalcolate Calcolate { get; set; } = new InformazioniEconomicheCalcolate();

        public string TipoRedditoOrigine { get => Raw.TipoRedditoOrigine; set => Raw.TipoRedditoOrigine = value; }
        public string TipoRedditoIntegrazione { get => Raw.TipoRedditoIntegrazione; set => Raw.TipoRedditoIntegrazione = value; }
        public int? CodTipoEsitoBS { get => Raw.CodTipoEsitoBS; set => Raw.CodTipoEsitoBS = value; }
        public double? ImportoAssegnato { get => Raw.ImportoAssegnato; set => Raw.ImportoAssegnato = value; }
        public int NumeroComponenti { get => Raw.NumeroComponenti; set => Raw.NumeroComponenti = value; }
        public int NumeroConviventiEstero { get => Raw.NumeroConviventiEstero; set => Raw.NumeroConviventiEstero = value; }
        public int NumeroComponentiIntegrazione { get => Raw.NumeroComponentiIntegrazione; set => Raw.NumeroComponentiIntegrazione = value; }
        public string TipoNucleo { get => Raw.TipoNucleo; set => Raw.TipoNucleo = value; }
        public decimal AltriMezzi { get => Raw.AltriMezzi; set => Raw.AltriMezzi = value; }
        public decimal SEQ_Origine { get => Calcolate.SEQ_Origine; set => Calcolate.SEQ_Origine = value; }
        public decimal SEQ_Integrazione { get => Calcolate.SEQ_Integrazione; set => Calcolate.SEQ_Integrazione = value; }
        public decimal ISRDSU { get => Calcolate.ISRDSU; set => Calcolate.ISRDSU = value; }
        public decimal ISPDSU { get => Calcolate.ISPDSU; set => Calcolate.ISPDSU = value; }
        public decimal Detrazioni { get => Calcolate.Detrazioni; set => Calcolate.Detrazioni = value; }
        public decimal SommaRedditiStud { get => Calcolate.SommaRedditiStud; set => Calcolate.SommaRedditiStud = value; }
        public decimal ISEDSU { get => Calcolate.ISEDSU; set => Calcolate.ISEDSU = value; }
        public decimal ISEEDSU { get => Calcolate.ISEEDSU; set => Calcolate.ISEEDSU = value; }
        public decimal ISPEDSU { get => Calcolate.ISPEDSU; set => Calcolate.ISPEDSU = value; }
        public decimal SEQ { get => Calcolate.SEQ; set => Calcolate.SEQ = value; }
        public double ISEDSU_Attuale { get => Attuali.ISEDSU; set => Attuali.ISEDSU = value; }
        public double ISEEDSU_Attuale { get => Attuali.ISEEDSU; set => Attuali.ISEEDSU = value; }
        public double ISPEDSU_Attuale { get => Attuali.ISPEDSU; set => Attuali.ISPEDSU = value; }
        public double ISPDSU_Attuale { get => Attuali.ISPDSU; set => Attuali.ISPDSU = value; }
        public double SEQ_Attuale { get => Attuali.SEQ; set => Attuali.SEQ = value; }
        public int StatusInpsOrigine { get => Raw.StatusInpsOrigine; set => Raw.StatusInpsOrigine = value; }
        public int StatusInpsIntegrazione { get => Raw.StatusInpsIntegrazione; set => Raw.StatusInpsIntegrazione = value; }
        public bool CoAttestazioneOk { get => Raw.CoAttestazioneOk; set => Raw.CoAttestazioneOk = value; }
        public decimal DetrazioniAdisu { get => Raw.DetrazioniAdisu; set => Raw.DetrazioniAdisu = value; }
        public decimal DetrazioniAltreBorse { get => Raw.DetrazioniAltreBorse; set => Raw.DetrazioniAltreBorse = value; }
        public string OrigineFonte { get => Raw.OrigineFonte; set => Raw.OrigineFonte = value; }
        public decimal OrigineSommaRedditi { get => Raw.OrigineSommaRedditi; set => Raw.OrigineSommaRedditi = value; }
        public decimal OrigineISR { get => Raw.OrigineISR; set => Raw.OrigineISR = value; }
        public decimal OrigineISP { get => Raw.OrigineISP; set => Raw.OrigineISP = value; }
        public decimal OrigineScalaEquivalenza { get => Raw.OrigineScalaEquivalenza; set => Raw.OrigineScalaEquivalenza = value; }
        public decimal OrigineReddFratelli50 { get => Raw.OrigineReddFratelli50; set => Raw.OrigineReddFratelli50 = value; }
        public decimal OriginePatrFratelli50 { get => Raw.OriginePatrFratelli50; set => Raw.OriginePatrFratelli50 = value; }
        public decimal OriginePatrFrat50Est { get => Raw.OriginePatrFrat50Est; set => Raw.OriginePatrFrat50Est = value; }
        public decimal OrigineReddFrat50Est { get => Raw.OrigineReddFrat50Est; set => Raw.OrigineReddFrat50Est = value; }
        public decimal OriginePatrFam50Est { get => Raw.OriginePatrFam50Est; set => Raw.OriginePatrFam50Est = value; }
        public decimal OrigineMetriQuadri { get => Raw.OrigineMetriQuadri; set => Raw.OrigineMetriQuadri = value; }
        public decimal OrigineReddFam50Est { get => Raw.OrigineReddFam50Est; set => Raw.OrigineReddFam50Est = value; }
        public decimal OriginePatrImm50FratSor { get => Raw.OriginePatrImm50FratSor; set => Raw.OriginePatrImm50FratSor = value; }
        public int OrigineNumeroComponenti { get => Raw.OrigineNumeroComponenti; set => Raw.OrigineNumeroComponenti = value; }
        public decimal OrigineRedditoComplessivo { get => Raw.OrigineRedditoComplessivo; set => Raw.OrigineRedditoComplessivo = value; }
        public decimal OriginePatrMobiliare { get => Raw.OriginePatrMobiliare; set => Raw.OriginePatrMobiliare = value; }
        public decimal OrigineSuperfAbitazMq { get => Raw.OrigineSuperfAbitazMq; set => Raw.OrigineSuperfAbitazMq = value; }
        public decimal OrigineSupComplAltreMq { get => Raw.OrigineSupComplAltreMq; set => Raw.OrigineSupComplAltreMq = value; }
        public decimal OrigineSupComplMq { get => Raw.OrigineSupComplMq; set => Raw.OrigineSupComplMq = value; }
        public decimal OrigineReddLordoFratell { get => Raw.OrigineReddLordoFratell; set => Raw.OrigineReddLordoFratell = value; }
        public decimal OriginePatrMobFratell { get => Raw.OriginePatrMobFratell; set => Raw.OriginePatrMobFratell = value; }
        public string IntegrazioneFonte { get => Raw.IntegrazioneFonte; set => Raw.IntegrazioneFonte = value; }
        public decimal IntegrazioneISR { get => Raw.IntegrazioneISR; set => Raw.IntegrazioneISR = value; }
        public decimal IntegrazioneISP { get => Raw.IntegrazioneISP; set => Raw.IntegrazioneISP = value; }
        public decimal IntegrazioneScalaEquivalenza { get => Raw.IntegrazioneScalaEquivalenza; set => Raw.IntegrazioneScalaEquivalenza = value; }
        public int IntegrazioneNumeroComponenti { get => Raw.IntegrazioneNumeroComponenti; set => Raw.IntegrazioneNumeroComponenti = value; }
        public decimal IntegrazioneReddFratelli50 { get => Raw.IntegrazioneReddFratelli50; set => Raw.IntegrazioneReddFratelli50 = value; }
        public decimal IntegrazionePatrFratelli50 { get => Raw.IntegrazionePatrFratelli50; set => Raw.IntegrazionePatrFratelli50 = value; }
        public decimal IntegrazionePatrFrat50Est { get => Raw.IntegrazionePatrFrat50Est; set => Raw.IntegrazionePatrFrat50Est = value; }
        public decimal IntegrazioneReddFrat50Est { get => Raw.IntegrazioneReddFrat50Est; set => Raw.IntegrazioneReddFrat50Est = value; }
        public decimal IntegrazionePatrFam50Est { get => Raw.IntegrazionePatrFam50Est; set => Raw.IntegrazionePatrFam50Est = value; }
        public decimal IntegrazioneMetriQuadri { get => Raw.IntegrazioneMetriQuadri; set => Raw.IntegrazioneMetriQuadri = value; }
        public decimal IntegrazioneReddFam50Est { get => Raw.IntegrazioneReddFam50Est; set => Raw.IntegrazioneReddFam50Est = value; }
        public decimal IntegrazioneRedditoComplessivo { get => Raw.IntegrazioneRedditoComplessivo; set => Raw.IntegrazioneRedditoComplessivo = value; }
        public decimal IntegrazionePatrMobiliare { get => Raw.IntegrazionePatrMobiliare; set => Raw.IntegrazionePatrMobiliare = value; }
        public decimal IntegrazioneSuperfAbitazMq { get => Raw.IntegrazioneSuperfAbitazMq; set => Raw.IntegrazioneSuperfAbitazMq = value; }
        public decimal IntegrazioneSupComplAltreMq { get => Raw.IntegrazioneSupComplAltreMq; set => Raw.IntegrazioneSupComplAltreMq = value; }
        public decimal IntegrazioneSupComplMq { get => Raw.IntegrazioneSupComplMq; set => Raw.IntegrazioneSupComplMq = value; }
        public decimal IntegrazioneReddLordoFratell { get => Raw.IntegrazioneReddLordoFratell; set => Raw.IntegrazioneReddLordoFratell = value; }
        public decimal IntegrazionePatrMobFratell { get => Raw.IntegrazionePatrMobFratell; set => Raw.IntegrazionePatrMobFratell = value; }

        public sealed class InformazioniEconomicheRaw
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
            public int StatusInpsOrigine { get; set; }
            public int StatusInpsIntegrazione { get; set; }
            public bool CoAttestazioneOk { get; set; }
            public decimal DetrazioniAdisu { get; set; }
            public decimal DetrazioniAltreBorse { get; set; }
            public string OrigineFonte { get; set; } = "";
            public decimal OrigineSommaRedditi { get; set; }
            public decimal OrigineISR { get; set; }
            public decimal OrigineISP { get; set; }
            public decimal OrigineScalaEquivalenza { get; set; }
            public decimal OrigineReddFratelli50 { get; set; }
            public decimal OriginePatrFratelli50 { get; set; }
            public decimal OriginePatrFrat50Est { get; set; }
            public decimal OrigineReddFrat50Est { get; set; }
            public decimal OriginePatrFam50Est { get; set; }
            public decimal OrigineMetriQuadri { get; set; }
            public decimal OrigineReddFam50Est { get; set; }
            public decimal OriginePatrImm50FratSor { get; set; }
            public int OrigineNumeroComponenti { get; set; }
            public decimal OrigineRedditoComplessivo { get; set; }
            public decimal OriginePatrMobiliare { get; set; }
            public decimal OrigineSuperfAbitazMq { get; set; }
            public decimal OrigineSupComplAltreMq { get; set; }
            public decimal OrigineSupComplMq { get; set; }
            public decimal OrigineReddLordoFratell { get; set; }
            public decimal OriginePatrMobFratell { get; set; }
            public string IntegrazioneFonte { get; set; } = "";
            public decimal IntegrazioneISR { get; set; }
            public decimal IntegrazioneISP { get; set; }
            public decimal IntegrazioneScalaEquivalenza { get; set; }
            public int IntegrazioneNumeroComponenti { get; set; }
            public decimal IntegrazioneReddFratelli50 { get; set; }
            public decimal IntegrazionePatrFratelli50 { get; set; }
            public decimal IntegrazionePatrFrat50Est { get; set; }
            public decimal IntegrazioneReddFrat50Est { get; set; }
            public decimal IntegrazionePatrFam50Est { get; set; }
            public decimal IntegrazioneMetriQuadri { get; set; }
            public decimal IntegrazioneReddFam50Est { get; set; }
            public decimal IntegrazioneRedditoComplessivo { get; set; }
            public decimal IntegrazionePatrMobiliare { get; set; }
            public decimal IntegrazioneSuperfAbitazMq { get; set; }
            public decimal IntegrazioneSupComplAltreMq { get; set; }
            public decimal IntegrazioneSupComplMq { get; set; }
            public decimal IntegrazioneReddLordoFratell { get; set; }
            public decimal IntegrazionePatrMobFratell { get; set; }
        }

        public sealed class InformazioniEconomicheAttuali
        {
            public double ISEDSU { get; set; }
            public double ISEEDSU { get; set; }
            public double ISPEDSU { get; set; }
            public double ISPDSU { get; set; }
            public double SEQ { get; set; }
        }

        public sealed class InformazioniEconomicheCalcolate
        {
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
        }
    }
}
