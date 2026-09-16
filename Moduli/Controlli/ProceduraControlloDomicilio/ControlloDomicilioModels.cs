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
        public string AnnoAccademico { get; init; } = string.Empty;
        public decimal NumDomanda { get; init; }
        public string CodFiscale { get; init; } = string.Empty;
        public string Studente { get; init; } = string.Empty;
        public string CodComune { get; init; } = string.Empty;
        public string Comune { get; init; } = string.Empty;
        public string Indirizzo { get; init; } = string.Empty;
        public string NumeroCivico { get; init; } = string.Empty;
        public string Cap { get; init; } = string.Empty;
        public string TipoDomicilio { get; init; } = string.Empty;
        public DateTime? DataInizio { get; init; }
        public DateTime? DataFine { get; init; }
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
        public int IdDomicilio { get; init; }
        public int TipoContratto { get; init; }
        public string NumeroSerie { get; init; } = string.Empty;
        public DateTime? DataRegistrazione { get; init; }
        public DateTime DataInizio { get; init; }
        public DateTime? DataFine { get; init; }
        public string TipoEnte { get; init; } = string.Empty;
        public string DenominazioneEnte { get; init; } = string.Empty;
        public string ImportoRata { get; init; } = string.Empty;
        public string NumeroSerieSubentro { get; init; } = string.Empty;
        public DateTime? DataInizioSubentro { get; init; }
        public DateTime? DataCessazione { get; init; }
        public List<ProrogaControlRecord> Proroghe { get; } = new();
        public List<DomicilioAttachmentRecord> AllegatiContratto { get; } = new();
        public List<DomicilioAttachmentRecord> AllegatiSubentro { get; } = new();
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
        public int IdContratto { get; init; }
        public string NumeroSerie { get; init; } = string.Empty;
        public DateTime DataDecorrenza { get; init; }
        public DateTime DataScadenza { get; init; }
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
        Proroga
    }

    internal sealed record DomicilioPdfExpectation(
        DomicilioPdfDocumentType Type,
        string CodFiscale,
        string? Reference,
        DateTime? RegistrationDate,
        DateTime? StartDate,
        DateTime? EndDate,
        string? ContractReference,
        bool RequireFiscalCode = true);

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
        public DomicilioControlState State { get; } = new();

        public string ExtractedData => string.Join("; ", new[]
        {
            string.IsNullOrWhiteSpace(ExtractedReference) ? null : $"Estremi={ExtractedReference}",
            ExtractedRegistrationDate.HasValue ? $"Registrazione={ExtractedRegistrationDate:dd/MM/yyyy}" : null,
            ExtractedStartDate.HasValue ? $"Decorrenza={ExtractedStartDate:dd/MM/yyyy}" : null,
            ExtractedEndDate.HasValue ? $"Scadenza={ExtractedEndDate:dd/MM/yyyy}" : null,
            string.IsNullOrWhiteSpace(ExtractedFiscalCodes) ? null : $"Codici fiscali={ExtractedFiscalCodes}"
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    internal sealed class DomicilioCsvRow
    {
        public string AnnoAccademico { get; init; } = string.Empty;
        public string NumDomanda { get; init; } = string.Empty;
        public string CodFiscale { get; init; } = string.Empty;
        public string Studente { get; init; } = string.Empty;
        public string Elemento { get; init; } = string.Empty;
        public string IdDomicilio { get; init; } = string.Empty;
        public string ComuneDomicilio { get; init; } = string.Empty;
        public string IndirizzoDomicilio { get; init; } = string.Empty;
        public string TitoloDomicilio { get; init; } = string.Empty;
        public string DataInizioDomicilio { get; init; } = string.Empty;
        public string DataFineDomicilio { get; init; } = string.Empty;
        public string IdContratto { get; init; } = string.Empty;
        public string TipoContratto { get; init; } = string.Empty;
        public string SerieContratto { get; init; } = string.Empty;
        public string DataRegistrazioneContratto { get; init; } = string.Empty;
        public string DataDecorrenzaContratto { get; init; } = string.Empty;
        public string DataScadenzaContratto { get; init; } = string.Empty;
        public string SerieSubentro { get; init; } = string.Empty;
        public string DataSubentro { get; init; } = string.Empty;
        public string DataCessazione { get; init; } = string.Empty;
        public string IdProroga { get; init; } = string.Empty;
        public string SerieProroga { get; init; } = string.Empty;
        public string DataDecorrenzaProroga { get; init; } = string.Empty;
        public string DataScadenzaProroga { get; init; } = string.Empty;
        public string IdAllegato { get; init; } = string.Empty;
        public string StatoAllegato { get; init; } = string.Empty;
        public string NomeFile { get; init; } = string.Empty;
        public string PdfValido { get; init; } = string.Empty;
        public string PdfLeggibile { get; init; } = string.Empty;
        public string TipoDocumentoRiconosciuto { get; init; } = string.Empty;
        public string CodiceFiscaleNelPdf { get; init; } = string.Empty;
        public string DatiPdfCongruenti { get; init; } = string.Empty;
        public string DatiEstrattiDalPdf { get; init; } = string.Empty;
        public string Esito { get; init; } = string.Empty;
        public string Priorita { get; init; } = string.Empty;
        public string CodiciAnomalia { get; init; } = string.Empty;
        public string Anomalie { get; init; } = string.Empty;
        public string AzioneUfficio { get; init; } = string.Empty;

        public static readonly string[] Headers =
        {
            "ANNO_ACCADEMICO", "NUM_DOMANDA", "COD_FISCALE", "STUDENTE", "ELEMENTO",
            "ID_DOMICILIO", "COMUNE_DOMICILIO", "INDIRIZZO_DOMICILIO", "TITOLO_DOMICILIO",
            "DATA_INIZIO_DOMICILIO", "DATA_FINE_DOMICILIO", "ID_CONTRATTO", "TIPO_CONTRATTO",
            "SERIE_CONTRATTO", "DATA_REGISTRAZIONE_CONTRATTO", "DATA_DECORRENZA_CONTRATTO",
            "DATA_SCADENZA_CONTRATTO", "SERIE_SUBENTRO", "DATA_SUBENTRO", "DATA_CESSAZIONE",
            "ID_PROROGA", "SERIE_PROROGA", "DATA_DECORRENZA_PROROGA", "DATA_SCADENZA_PROROGA",
            "ID_ALLEGATO", "STATO_ALLEGATO", "NOME_FILE", "PDF_VALIDO", "PDF_LEGGIBILE",
            "TIPO_DOCUMENTO_RICONOSCIUTO", "CODICE_FISCALE_NEL_PDF", "DATI_PDF_CONGRUENTI",
            "DATI_ESTRATTI_DAL_PDF", "ESITO", "PRIORITA", "CODICI_ANOMALIA", "ANOMALIE",
            "AZIONE_UFFICIO"
        };

        public string[] Values => new[]
        {
            AnnoAccademico, NumDomanda, CodFiscale, Studente, Elemento, IdDomicilio,
            ComuneDomicilio, IndirizzoDomicilio, TitoloDomicilio, DataInizioDomicilio,
            DataFineDomicilio, IdContratto, TipoContratto, SerieContratto,
            DataRegistrazioneContratto, DataDecorrenzaContratto, DataScadenzaContratto,
            SerieSubentro, DataSubentro, DataCessazione, IdProroga, SerieProroga,
            DataDecorrenzaProroga, DataScadenzaProroga, IdAllegato, StatoAllegato,
            NomeFile, PdfValido, PdfLeggibile, TipoDocumentoRiconosciuto,
            CodiceFiscaleNelPdf, DatiPdfCongruenti, DatiEstrattiDalPdf, Esito, Priorita,
            CodiciAnomalia, Anomalie, AzioneUfficio
        };

        public static string Date(DateTime? value) => value?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        public static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        public static string Bool(bool? value) => value.HasValue ? (value.Value ? "SI" : "NO") : "NON_VERIFICABILE";
    }
}
