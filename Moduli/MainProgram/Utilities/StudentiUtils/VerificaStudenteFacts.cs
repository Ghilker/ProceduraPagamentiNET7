using System;
using System.Collections.Generic;

namespace ProcedureNet7
{
    public sealed class CarrieraPregressaBeneficiRiRaw
    {
        public string CodAvvenimento { get; set; } = string.Empty;
        public string BeneficiUsufruiti { get; set; } = string.Empty;
        public string ImportiRestituiti { get; set; } = string.Empty;
        public string AnniBeneficiUsufruitiLz { get; set; } = string.Empty;
        public string AnniImportiRestituitiLz { get; set; } = string.Empty;
        public string SedeIstituzioneUniversitaria { get; set; } = string.Empty;
        public string TipologiaCorso { get; set; } = string.Empty;
        public int? DurataLegTitoloConseguito { get; set; }
        public int? AnnoAvvenimento { get; set; }
    }

    public sealed class IscrizioneEsitoFactsRaw
    {
        public bool? CarrieraInterrotta { get; set; }
        public int? NumAnniInterruzione { get; set; }
        public decimal? CreditiExtraCurriculari { get; set; }
        public int? MeseImmatricolazione { get; set; }
        public int? Semestre { get; set; }
        public bool? IscrittoRipetente { get; set; }
        public bool? PassaggioTrasferimento { get; set; }
        public bool? RipetenteDaPassaggio { get; set; }
        public int? PrimaImmatricolazTs { get; set; }
        public int? AaTrasferimento { get; set; }
        public bool CreditiMeritoNormalizzati { get; set; }
    }

    public sealed class BloccoPagamentoRaw
    {
        public string CodTipologiaBlocco { get; set; } = string.Empty;
        public bool BloccoPagamentoAttivo { get; set; }
    }

    public sealed class IncongruenzaRaw
    {
        public string CodIncongruenza { get; set; } = string.Empty;
        public bool Attiva { get; set; }
        public string CodForzatura { get; set; } = string.Empty;
        public string EliminataDa { get; set; } = string.Empty;
    }

    public sealed class EsitoBorsaFacts
    {
        public HashSet<string> ForzatureGenerali { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ForzaturaRinunciaNoEsclusione { get; set; }
        public bool UsufruitoBeneficioBorsaNonRestituito { get; set; }
        public bool RinunciaBorsa { get; set; }
        public string CodTipoOrdinamento { get; set; } = string.Empty;

        public bool? IscrizioneFuoriTermine { get; set; }
        public bool? PermessoSoggiorno { get; set; }
        public bool? RinunciaBenefici { get; set; }
        public bool? DomandaTrasmessa { get; set; }
        public bool? TitoloAccademicoConseguito { get; set; }
        public bool? AttesaTitoloAccademicoConseguito { get; set; }
        public bool? AttesaTitoloValidaAllaDataValutazione { get; set; }
        public bool? AttesaTitoloScaduta { get; set; }
        public DateTime? ScadenzaAttesaTitolo { get; set; }
        public bool? TitoloAccessoValidoPerIscrizione { get; set; }
        public string CodAvvenimentoTitoloAccesso { get; set; } = string.Empty;
        public bool? AttesaTitoloCicloUnicoPresente { get; set; }
        public bool? TitoloAccessoTriennaleConseguito { get; set; }
        public bool? TitoloGiaConseguitoConAttesaCicloUnico { get; set; }
        public List<BloccoPagamentoRaw> BlocchiPagamento { get; } = new();
        public bool HasBloccoPagamentoBISBSTRimosso { get; set; }
        public bool HasBloccoPagamentoBISBSTAttivo { get; set; }
        public bool? AttesaTitoloValidataDaBloccoPagamentoRimosso { get; set; }

        public bool HasIncongruenza27NonAttiva { get; set; }
        public bool HasIncongruenza27Attiva { get; set; }
        public bool? AttesaTitoloValidataDaIncongruenza27 { get; set; }
        public List<IncongruenzaRaw> Incongruenze { get; } = new();
        public bool HasIncongruenza27 { get; set; }
        public int? AnnoAvvenimentoTitoloAccesso { get; set; }
        public int? AnnoAvvenimentoTitoloAtteso { get; set; }
        public int? TipoStudenteNormalizzato { get; set; }
        public bool? IsConferma { get; set; }
        public bool? Straniero { get; set; }
        public bool? CittadinanzaUe { get; set; }
        public bool? ResidenzaUe { get; set; }
        public bool? RedditoUe { get; set; }
        public bool RichiedeControlloLaureaSpec { get; set; }
        public bool? PassaggioTrasferimento { get; set; }
        public bool? RipetenteDaPassaggio { get; set; }
        public int? PrimaImmatricolazTs { get; set; }
        public int? AaTrasferimento { get; set; }
        public bool? CarrieraInterrotta { get; set; }
        public int? NumAnniInterruzione { get; set; }
        public decimal? CreditiExtraCurriculari { get; set; }
        public int? MeseImmatricolazione { get; set; }
        public int? Semestre { get; set; }
        public bool? IscrittoRipetente { get; set; }
        public bool? IsAnnoClassificabile { get; set; }
        public string DiagnosticaIscrizione { get; set; } = string.Empty;
        public bool? NubileProle { get; set; }
        public bool? RichiestaCS { get; set; }
        public HashSet<string> BeneficiRichiesti { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BeneficiPregressiNonRestituiti { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BeneficiRinunciaPregressa { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool BorsaPregressaNonRestituitaConfliggente { get; set; }
        public int? AnnoBorsaRichiestoNormalizzato { get; set; }
        public string AnniBorsaPregressaUsufruitiNormalizzati { get; set; } = string.Empty;
        public string AnniBorsaPregressaRestituitiNormalizzati { get; set; } = string.Empty;
        public string AnniBorsaPregressaNonRestituitaConfliggenti { get; set; } = string.Empty;
        public bool BorsaPregressaEsteraNonRichiedeRestituzione { get; set; }
        public string DiagnosticaBorsaPregressaRestituzioni { get; set; } = string.Empty;
        public bool HasIseeBaseEntroScadenza { get; set; }
        public bool HasCoUniversitarioEntroScadenza { get; set; }
        public bool HasCoOrdinarioConIntegrazioneEsteriEntroScadenza { get; set; }
        public bool HasCoOrdinarioSemestreFiltroEntroScadenza { get; set; }
        public bool HasCiUniversitarioEntroScadenza { get; set; }
        public bool OrigineEconomicaAdeguata { get; set; }
        public string MotivoAdeguatezzaOrigine { get; set; } = string.Empty;
        public Dictionary<string, string> SlashMotiviEsclusioneByBenefit { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool? RiconoscimentoTitoloEstero { get; set; }
        public string SedeIstituzioneUniversitariaTitolo { get; set; } = string.Empty;

        public bool RinunciaBS { get; set; }
        public bool RinunciaPA { get; set; }
        public bool RinunciaCM { get; set; }
        public bool RinunciaCT { get; set; }
        public bool RinunciaCI { get; set; }

        public bool Revocato { get; set; }
        public bool RevocatoBandoBS { get; set; }
        public bool RevocatoBandoPA { get; set; }
        public bool RevocatoBandoCM { get; set; }
        public bool RevocatoBandoCT { get; set; }
        public bool RevocatoBandoCI { get; set; }
        public bool RevocatoSedeDistaccata { get; set; }
        public bool RevocatoMancataIscrizione { get; set; }
        public bool RevocatoIscrittoRipetente { get; set; }
        public bool RevocatoISEE { get; set; }
        public bool RevocatoLaureato { get; set; }
        public bool RevocatoPatrimonio { get; set; }
        public bool RevocatoReddito { get; set; }
        public bool RevocatoEsami { get; set; }
        public bool RevocatoFuoriTermine { get; set; }
        public bool RevocatoIseeFuoriTermine { get; set; }
        public bool RevocatoIseeNonProdotta { get; set; }
        public bool RevocatoTrasmissioneIseeFuoriTermine { get; set; }
        public bool RevocatoNoContrattoLocazione { get; set; }

        public bool DecadutoBS { get; set; }
        public bool DecadutoPA { get; set; }
        public bool DecadutoCM { get; set; }
        public bool DecadutoCT { get; set; }
        public bool DecadutoCI { get; set; }

        public string SlashMotiviEsclusioneBS { get; set; } = string.Empty;

        public int? TipologiaStudiTitoloConseguito { get; set; }
        public int? DurataLegTitoloConseguito { get; set; }
        public int? TipologiaStudiTitoloAtteso { get; set; }
        public int? DurataLegTitoloAtteso { get; set; }
    }

    public sealed class EsitoConcorsoBenefitRaw
    {
        public string CodBeneficio { get; set; } = string.Empty;
        public int? CodTipoEsito { get; set; }
        public decimal? ImportoAssegnato { get; set; }
    }

    public sealed class EsitoBeneficioCalcolato
    {
        public string CodBeneficio { get; set; } = string.Empty;
        public bool Richiesto { get; set; }
        public int EsitoCalcolato { get; set; }
        public string CodiciMotivo { get; set; } = string.Empty;
        public string Motivi { get; set; } = string.Empty;
    }

}
