
using System.Globalization;

namespace ProcedureNet7
{
    internal enum DomicilioControlSeverity
    {
        Conforme = 0,
        VerificaManuale = 1,
        NonConforme = 2
    }

    internal sealed record DomicilioControlFinding(
        DomicilioControlSeverity Severity,
        string Code,
        string Description,
        string OfficeAction);

    internal sealed class DomicilioControlState
    {
        private readonly List<DomicilioControlFinding> _findings = new();

        public IReadOnlyList<DomicilioControlFinding> Findings => _findings;

        public DomicilioControlSeverity Severity => _findings.Count == 0
            ? DomicilioControlSeverity.Conforme
            : _findings.Max(x => x.Severity);

        public string Outcome => Severity switch
        {
            DomicilioControlSeverity.NonConforme => "NON_CONFORME",
            DomicilioControlSeverity.VerificaManuale => "VERIFICA_MANUALE",
            _ => "CONFORME"
        };

        public string Priority => Severity switch
        {
            DomicilioControlSeverity.NonConforme => "ALTA",
            DomicilioControlSeverity.VerificaManuale => "MEDIA",
            _ => "NESSUNA"
        };

        public string Codes => string.Join(" | ", _findings.Select(x => x.Code).Distinct());

        public string Descriptions => string.Join(" | ", _findings
            .Select(x => x.Description)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        public string OfficeActions => _findings.Count == 0
            ? "Nessuna azione."
            : string.Join(" | ", _findings
                .Select(x => x.OfficeAction)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        public void Add(
            DomicilioControlSeverity severity,
            string code,
            string description,
            string officeAction)
        {
            if (_findings.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(x.Description, description, StringComparison.OrdinalIgnoreCase)))
                return;

            _findings.Add(new DomicilioControlFinding(severity, code, description, officeAction));
        }

        public void Merge(DomicilioControlState? other)
        {
            if (other == null)
                return;

            foreach (var finding in other.Findings)
                Add(finding.Severity, finding.Code, finding.Description, finding.OfficeAction);
        }

        public DomicilioControlState Copy()
        {
            var copy = new DomicilioControlState();
            copy.Merge(this);
            return copy;
        }
    }

    internal sealed class DomicilioControlRecord
    {
        public int Id { get; init; }
        public DateTime DataValidita { get; set; }
        public DateTime? DataModifica { get; set; }
        public string AnnoAccademico { get; init; } = string.Empty;
        public decimal NumDomanda { get; init; }
        public string CodFiscale { get; init; } = string.Empty;
        public string Studente { get; init; } = string.Empty;
        public bool InserimentoDatiContratto { get; set; }
        public string CodProvincia { get; set; } = string.Empty;
        public string CodComune { get; set; } = string.Empty;
        public string Comune { get; set; } = string.Empty;
        public string Indirizzo { get; set; } = string.Empty;
        public string NumeroCivico { get; set; } = string.Empty;
        public string Cap { get; set; } = string.Empty;
        public string TipoDomicilio { get; set; } = string.Empty;
        public DateTime? DataInizio { get; set; }
        public DateTime? DataFine { get; set; }
        public List<ContrattoControlRecord> Contratti { get; } = new();
        public DomicilioControlState State { get; } = new();

        public string TitoloDescrizione => TipoDomicilio switch
        {
            "0" => "A titolo gratuito",
            "1" => "A titolo oneroso",
            _ => "Non riconosciuto"
        };
    }

    internal sealed class ContrattoControlRecord
    {
        public int Id { get; init; }
        public DateTime DataValidita { get; set; }
        public DateTime? DataModifica { get; set; }
        public int IdDomicilio { get; init; }
        public int TipoContratto { get; set; }
        public string NumeroSerie { get; set; } = string.Empty;
        public DateTime? DataRegistrazione { get; set; }
        public DateTime DataInizio { get; set; }
        public DateTime? DataFine { get; set; }
        public string TipoEnte { get; set; } = string.Empty;
        public string DenominazioneEnte { get; set; } = string.Empty;
        public string ImportoRata { get; set; } = string.Empty;
        public string NumeroSerieSubentro { get; set; } = string.Empty;
        public DateTime? DataInizioSubentro { get; set; }
        public DateTime? DataCessazione { get; set; }
        public List<ProrogaControlRecord> Proroghe { get; } = new();
        public List<DomicilioAttachmentRecord> AllegatiContratto { get; } = new();
        public List<DomicilioAttachmentRecord> AllegatiSubentro { get; } = new();
        public List<DomicilioAttachmentRecord> AllegatiLearningAgreement { get; } = new();
        public DomicilioControlState State { get; } = new();
        public DomicilioControlState SubentroState { get; } = new();

        public bool IsLocazione => TipoContratto == 0;
        public bool HasSubentro => DataInizioSubentro.HasValue || !string.IsNullOrWhiteSpace(NumeroSerieSubentro);
        public DateTime EffectiveStart => DataInizioSubentro?.Date ?? DataInizio.Date;

        public DateTime? EffectiveEnd
        {
            get
            {
                if (DataCessazione.HasValue)
                    return DataCessazione.Value.Date;

                var dates = Proroghe.Select(x => x.DataScadenza.Date).ToList();
                if (DataFine.HasValue)
                    dates.Add(DataFine.Value.Date);

                return dates.Count == 0 ? null : dates.Max();
            }
        }

        public string TipoDescrizione => TipoContratto switch
        {
            0 => "Contratto di locazione",
            1 => "Altro tipo di rapporto",
            _ => "Non riconosciuto"
        };
    }

    internal sealed class ProrogaControlRecord
    {
        public int Id { get; init; }
        public DateTime DataValidita { get; set; }
        public DateTime? DataModifica { get; set; }
        public int IdContratto { get; init; }
        public string NumeroSerie { get; set; } = string.Empty;
        public DateTime DataDecorrenza { get; set; }
        public DateTime DataScadenza { get; set; }
        public List<DomicilioAttachmentRecord> Allegati { get; } = new();
        public DomicilioControlState State { get; } = new();
    }

    internal sealed class DomicilioAttachmentRecord
    {
        public decimal IdAllegato { get; init; }
        public string CodTipoAllegato { get; init; } = string.Empty;
        public string CodStatus { get; init; } = string.Empty;
        public string StatusDescription { get; init; } = string.Empty;
        public DateTime DataValidita { get; init; }
        public int? IdContratto { get; init; }
        public int? IdProroga { get; init; }
        public string CodFiscale { get; init; } = string.Empty;
        public decimal? NumDomanda { get; init; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public DomicilioPdfAnalysis? Analysis { get; set; }
    }

    internal enum DomicilioPdfDocumentType
    {
        Contratto,
        Subentro,
        Proroga,
        ContrattoEnte,
        LearningAgreement
    }

    internal sealed record DomicilioPdfExpectation(
        DomicilioPdfDocumentType Type,
        string CodFiscale,
        string? Reference,
        DateTime? RegistrationDate,
        DateTime? StartDate,
        DateTime? EndDate,
        string? ContractReference,
        bool RequireFiscalCode = true, string ComuneDomicilio = "",
        string TipoEnte = "", string DenominazioneEnte = "", decimal? ImportoMensileRata = null,
        bool ReadAllPages = false);

    internal sealed record DomicilioPdfFieldEvidence(string Field, string Value, int Page, string Label);

    internal sealed class DomicilioPdfAnalysis
    {
        public bool FileAvailable { get; set; }
        public bool PdfValid { get; set; }
        public bool Readable { get; set; }
        public bool TypeRecognized { get; set; }
        public bool? FiscalCodePresent { get; set; }
        public bool? DataCoherent { get; set; }
        public int PageCount { get; set; }
        public int PagesRead { get; set; }
        public string ExtractedReference { get; set; } = string.Empty;
        public DateTime? ExtractedRegistrationDate { get; set; }
        public DateTime? ExtractedStartDate { get; set; }
        public DateTime? ExtractedEndDate { get; set; }
        public string ExtractedFiscalCodes { get; set; } = string.Empty;
        public string ExtractedEnteType { get; set; } = string.Empty;
        public string ExtractedEnteName { get; set; } = string.Empty;
        public decimal? ExtractedMonthlyAmount { get; set; }
        public string ExtractedMunicipality { get; set; } = string.Empty;
        public List<DomicilioPdfFieldEvidence> DocumentedCorrections { get; } = new();
        public bool CanProposeCorrections { get; set; }
        public DomicilioControlState State { get; } = new();

        public string ExtractedData => string.Join("; ", new[]
        {
            string.IsNullOrWhiteSpace(ExtractedReference) ? null : $"Estremi={ExtractedReference}",
            ExtractedRegistrationDate.HasValue ? $"Registrazione={ExtractedRegistrationDate:dd/MM/yyyy}" : null,
            ExtractedStartDate.HasValue ? $"Decorrenza={ExtractedStartDate:dd/MM/yyyy}" : null,
            ExtractedEndDate.HasValue ? $"Scadenza={ExtractedEndDate:dd/MM/yyyy}" : null,
            string.IsNullOrWhiteSpace(ExtractedFiscalCodes) ? null : $"Codici fiscali={ExtractedFiscalCodes}",
            string.IsNullOrWhiteSpace(ExtractedEnteType) ? null : $"Tipo ente={ExtractedEnteType}",
            string.IsNullOrWhiteSpace(ExtractedEnteName) ? null : $"Denominazione ente={ExtractedEnteName}",
            ExtractedMonthlyAmount.HasValue ? $"Rata mensile={ExtractedMonthlyAmount.Value.ToString("0.00", CultureInfo.InvariantCulture)}" : null,
            string.IsNullOrWhiteSpace(ExtractedMunicipality) ? null : $"Comune immobile={ExtractedMunicipality}"
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

}
