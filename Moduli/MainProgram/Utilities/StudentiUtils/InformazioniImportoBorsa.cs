using System;

namespace ProcedureNet7
{
    public class InformazioniImportoBorsa
    {
        public string StatusSedeRiferimento { get; set; } = string.Empty;
        public decimal? ImportoBase { get; set; }
        public decimal? ImportoFinale { get; set; }
        public decimal? ImportoSpecificheImpegniBs { get; set; }
        public bool CalcoloEseguito { get; set; }
    }
}
