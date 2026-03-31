using System;

namespace ProcedureNet7
{
    public sealed class DomicilioSnapshot
    {
        public string ComuneDomicilio { get; set; } = "";
        public bool TitoloOneroso { get; set; }
        public bool ContrattoEnte { get; set; }
        public string TipoEnte { get; set; } = "";
        public string SerieContratto { get; set; } = "";
        public DateTime DataRegistrazione { get; set; }
        public DateTime DataDecorrenza { get; set; }
        public DateTime DataScadenza { get; set; }
        public int DurataContratto { get; set; }
        public bool Prorogato { get; set; }
        public int DurataProroga { get; set; }
        public string SerieProroga { get; set; } = "";
        public string DenomEnte { get; set; } = "";
        public double ImportoRataEnte { get; set; }
    }
}
