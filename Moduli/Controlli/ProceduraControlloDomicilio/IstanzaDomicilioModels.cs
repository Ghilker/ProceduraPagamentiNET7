namespace ProcedureNet7
{
    internal sealed record IstanzaDomicilioComune(string Descrizione, string CodProvincia);

    internal sealed class IstanzaDomicilioRecord
    {
        public int NumIstanza { get; init; }
        public string AnnoAccademico { get; init; } = string.Empty;
        public decimal NumDomanda { get; init; }
        public string CodFiscale { get; init; } = string.Empty;
        public string Studente { get; init; } = string.Empty;
        public DateTime DataPresentazione { get; init; }
        public bool PresaCarico { get; init; }
        public string Operatore { get; init; } = string.Empty;
        public bool ValidazioneCompletata { get; set; }
        public string? ExpectedFingerprint { get; set; }
        public IstanzaDomicilioRecord? CorrectionCandidate { get; set; }
        public List<DomicilioAutomation.DomicilioAutomationOperationCorrection> ProposedCorrections { get; } = new();
        public List<IstanzaDomicilioOperation> Operazioni { get; } = new();
        public List<DomicilioControlRecord> DomiciliCorrenti { get; } = new();
        public DomicilioControlState State { get; } = new();
    }

    internal sealed class IstanzaDomicilioOperation
    {
        public int Id { get; init; }
        public int Ordine { get; init; }
        public int Entita { get; init; }
        public int Operazione { get; init; }
        public int? IdOperazionePadre { get; init; }
        public int? IdDomicilio { get; init; }
        public int? IdContratto { get; init; }
        public int? IdProroga { get; init; }
        public decimal? IdAllegatoContratto { get; init; }
        public decimal? IdAllegatoSubentro { get; init; }
        public decimal? IdAllegatoProroga { get; init; }
        public decimal? IdAllegatoLearningAgreement { get; init; }
        public DateTime? DataValiditaOrigine { get; init; }
        public DateTime? DataModificaOrigine { get; init; }
        public string PayloadJson { get; init; } = string.Empty;
        public string DatiProposti { get; set; } = string.Empty;
        public DomicilioControlState State { get; } = new();
        public List<IstanzaDomicilioDocument> Documenti { get; } = new();
        public string EntitaDescrizione => Entita switch { 1 => "DOMICILIO", 2 => "CONTRATTO", 3 => "PROROGA", _ => "SCONOSCIUTA" };
        public string OperazioneDescrizione => Operazione switch { 1 => "INSERIMENTO", 2 => "MODIFICA", 3 => "CHIUSURA", _ => "SCONOSCIUTA" };
    }

    internal sealed class IstanzaDomicilioDocument
    {
        public string Tipo { get; init; } = string.Empty;
        public decimal? IdAllegato { get; init; }
        public DomicilioPdfExpectation? Expectation { get; init; }
        public DomicilioAttachmentRecord? Attachment { get; set; }
    }
}
