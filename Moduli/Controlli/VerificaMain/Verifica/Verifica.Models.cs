using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    internal enum VerificaFaseElaborativa
    {
        Unknown = 0,
        GraduatorieProvvisorie = 1,
        GraduatorieDefinitive = 2
    }

    internal sealed class VerificaPipelineContext
    {
        public VerificaPipelineContext(SqlConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public SqlConnection Connection { get; }
        public string AnnoAccademico { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public bool IncludeEsclusi { get; set; }
        public bool IncludeNonTrasmesse { get; set; }
        public string TempPipelineTable { get; set; } = "#VerificaPipelineTargets";
        public DateTime ReferenceDate { get; set; } = DateTime.Now;
        public VerificaFaseElaborativa FaseElaborativa { get; set; } = VerificaFaseElaborativa.Unknown;

        public Dictionary<StudentKey, StudenteInfo> Students { get; } = new();
        public Dictionary<StudentKey, EsitoBorsaFacts> EsitoBorsaFactsByStudent { get; } = new();
        public Dictionary<StudentKey, HashSet<string>> BeneficiRichiestiByStudent { get; } = new();
        public Dictionary<StudentKey, Dictionary<string, EsitoConcorsoBenefitRaw>> EsitiConcorsoByStudent { get; } = new();
        public Dictionary<StudentKey, Dictionary<string, EsitoBorsaBenefitEvaluation>> EsitiBorsaPerBeneficioByStudent { get; } = new();
        public HashSet<(string ComuneA, string ComuneB)> ComuniEquiparati { get; } = new();
        public CalcParams CalcParams { get; set; } = new();
        public List<string> CodiciFiscaliFiltro { get; } = new();
        public EsamiCatalog EsamiCatalog { get; } = new();
    }

    internal sealed class EsitoBorsaFacts
    {
        public HashSet<string> ForzatureGenerali { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ForzaturaRinunciaNoEsclusione { get; set; }
        public bool UsufruitoBeneficioBorsaNonRestituito { get; set; }
        public bool RinunciaBorsa { get; set; }
        public string CodTipoOrdinamento { get; set; } = string.Empty;

        public bool? CartaceoInviato { get; set; }
        public bool? IscrizioneFuoriTermine { get; set; }
        public bool? PermessoSoggiorno { get; set; }
        public bool? RinunciaBenefici { get; set; }
        public bool? DomandaTrasmessa { get; set; }
        public bool? DomandaTrasmessaPin { get; set; }
        public int? StatusIsee { get; set; }
        public string TipoCertificazione { get; set; } = string.Empty;
        public bool? TitoloAccademicoConseguito { get; set; }
        public bool? AttesaTitoloAccademicoConseguito { get; set; }
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
    }


    internal sealed class EsitoConcorsoBenefitRaw
    {
        public string CodBeneficio { get; set; } = string.Empty;
        public int CodTipoEsito { get; set; }
        public decimal ImportoBeneficio { get; set; }
    }

    internal sealed class EsitoBorsaBenefitEvaluation
    {
        public string CodBeneficio { get; set; } = string.Empty;
        public int EsitoCalcolato { get; set; }
        public string CodiciMotivo { get; set; } = string.Empty;
        public string Motivi { get; set; } = string.Empty;
        public bool HasErrors { get; set; }
    }

    internal sealed class EsamiCatalog
    {
        private readonly Dictionary<string, SortedDictionary<int, decimal>> _items = new(StringComparer.OrdinalIgnoreCase);

        public void Clear() => _items.Clear();

        public void Add(string? codCorsoLaurea, string? codTipoOrdinamento, string? annoAccadInizio, int? annoCorso, decimal numeroEsami)
        {
            if (string.IsNullOrWhiteSpace(codCorsoLaurea) || string.IsNullOrWhiteSpace(codTipoOrdinamento) || !annoCorso.HasValue || annoCorso.Value <= 0)
                return;

            string key = BuildKey(codCorsoLaurea, codTipoOrdinamento, annoAccadInizio);
            if (!_items.TryGetValue(key, out var years))
            {
                years = new SortedDictionary<int, decimal>();
                _items[key] = years;
            }

            if (years.TryGetValue(annoCorso.Value, out var current))
                years[annoCorso.Value] = current + numeroEsami;
            else
                years[annoCorso.Value] = numeroEsami;
        }

        public decimal GetTotal(string? codCorsoLaurea, string? codTipoOrdinamento, string? annoAccadInizio)
        {
            foreach (string key in BuildCandidateKeys(codCorsoLaurea, codTipoOrdinamento, annoAccadInizio))
            {
                if (_items.TryGetValue(key, out var years))
                    return years.Values.Sum();
            }

            return 0m;
        }

        public decimal GetSumBeforeYear(string? codCorsoLaurea, string? codTipoOrdinamento, string? annoAccadInizio, int annoCorsoExclusive)
        {
            if (annoCorsoExclusive <= 0)
                return 0m;

            foreach (string key in BuildCandidateKeys(codCorsoLaurea, codTipoOrdinamento, annoAccadInizio))
            {
                if (_items.TryGetValue(key, out var years))
                    return years.Where(pair => pair.Key < annoCorsoExclusive).Sum(pair => pair.Value);
            }

            return 0m;
        }

        private static IEnumerable<string> BuildCandidateKeys(string? codCorsoLaurea, string? codTipoOrdinamento, string? annoAccadInizio)
        {
            string corso = Normalize(codCorsoLaurea);
            string ord = Normalize(codTipoOrdinamento);
            var starts = new List<string>();
            string start = Normalize(annoAccadInizio);

            if (!string.IsNullOrWhiteSpace(start))
                starts.Add(start);

            if (start.Length >= 4)
            {
                string first4 = start.Substring(0, 4);
                if (!starts.Contains(first4, StringComparer.OrdinalIgnoreCase))
                    starts.Add(first4);

                if (int.TryParse(first4, out int year))
                {
                    string aa8 = string.Concat(first4, (year + 1).ToString());
                    if (!starts.Contains(aa8, StringComparer.OrdinalIgnoreCase))
                        starts.Add(aa8);
                }
            }

            if (starts.Count == 0)
                starts.Add(string.Empty);

            foreach (string s in starts)
                yield return BuildKey(corso, ord, s);
        }

        private static string BuildKey(string? codCorsoLaurea, string? codTipoOrdinamento, string? annoAccadInizio)
            => string.Concat(Normalize(codCorsoLaurea), "|", Normalize(codTipoOrdinamento), "|", Normalize(annoAccadInizio));

        private static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();
    }

}
