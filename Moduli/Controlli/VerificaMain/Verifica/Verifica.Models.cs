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
        public bool ScriviSulDatabase { get; set; }

        public Dictionary<StudentKey, StudenteInfo> Students { get; } = new();
        public HashSet<(string ComuneA, string ComuneB)> ComuniEquiparati { get; } = new();
        public HashSet<string> ComuniPensionatiAttivi { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<StudentKey> SelezionatiCiUe { get; } = new();
        public CalcParams CalcParams { get; set; } = new();
        public List<string> CodiciFiscaliFiltro { get; } = new();
        public EsamiCatalog EsamiCatalog { get; } = new();
        public CreditiRichiestiCatalog CreditiRichiestiCatalog { get; } = new();
        public HashSet<StudentKey> BorsaStoricaStessoAnnoConflitti { get; } = new();
        public HashSet<StudentKey> BorsaStoricaRichiedeRevisione { get; } = new();
        public Dictionary<StudentKey, List<string>> DiagnosticaBorsaStoricaStessoAnno { get; } = new();

        public void ResetBorsaStoricaStessoAnno()
        {
            BorsaStoricaStessoAnnoConflitti.Clear();
            BorsaStoricaRichiedeRevisione.Clear();
            DiagnosticaBorsaStoricaStessoAnno.Clear();
        }

        public void AddDiagnosticaBorsaStoricaStessoAnno(StudentKey key, string diagnostica, bool richiedeRevisione)
        {
            if (!DiagnosticaBorsaStoricaStessoAnno.TryGetValue(key, out var items))
            {
                items = new List<string>();
                DiagnosticaBorsaStoricaStessoAnno[key] = items;
            }

            if (!string.IsNullOrWhiteSpace(diagnostica))
                items.Add(diagnostica);

            if (richiedeRevisione)
                BorsaStoricaRichiedeRevisione.Add(key);
        }

        public string GetDiagnosticaBorsaStoricaStessoAnno(StudentKey key)
            => DiagnosticaBorsaStoricaStessoAnno.TryGetValue(key, out var items)
                ? string.Join(" || ", items)
                : string.Empty;

        public StudenteInfo GetOrCreateStudent(StudentKey key)
        {
            if (Students.TryGetValue(key, out var info) && info != null)
                return info;

            info = new StudenteInfo();
            info.InformazioniPersonali.CodFiscale = key.CodFiscale;
            info.InformazioniPersonali.NumDomanda = key.NumDomanda;
            Students[key] = info;
            return info;
        }

        public EsitoBorsaFacts GetOrCreateEsitoBorsaFacts(StudentKey key)
            => GetOrCreateStudent(key).InformazioniBeneficio.EsitoBorsaFacts;

        public bool TryGetEsitoBorsaFacts(StudentKey key, out EsitoBorsaFacts? facts)
        {
            if (Students.TryGetValue(key, out var info) && info != null)
            {
                facts = info.InformazioniBeneficio.EsitoBorsaFacts;
                return true;
            }

            facts = null;
            return false;
        }

        public Dictionary<string, EsitoConcorsoBenefitRaw> GetOrCreateEsitiConcorsoByBenefit(StudentKey key)
            => GetOrCreateStudent(key).InformazioniBeneficio.EsitiConcorsoByBenefit;

        public bool TryGetEsitiConcorsoByBenefit(StudentKey key, out Dictionary<string, EsitoConcorsoBenefitRaw>? byBenefit)
        {
            if (Students.TryGetValue(key, out var info) && info != null)
            {
                byBenefit = info.InformazioniBeneficio.EsitiConcorsoByBenefit;
                return true;
            }

            byBenefit = null;
            return false;
        }

        public Dictionary<string, EsitoBeneficioCalcolato> GetOrCreateEsitiCalcolatiByBenefit(StudentKey key)
            => GetOrCreateStudent(key).InformazioniBeneficio.EsitiCalcolatiByBenefit;

        public bool TryGetEsitiCalcolatiByBenefit(StudentKey key, out Dictionary<string, EsitoBeneficioCalcolato>? byBenefit)
        {
            if (Students.TryGetValue(key, out var info) && info != null)
            {
                byBenefit = info.InformazioniBeneficio.EsitiCalcolatiByBenefit;
                return true;
            }

            byBenefit = null;
            return false;
        }

        public IscrizioneEsitoFactsRaw GetOrCreateIscrizioneEsitoFacts(StudentKey key)
            => GetOrCreateStudent(key).InformazioniIscrizione.EsitoFacts;

        public bool TryGetIscrizioneEsitoFacts(StudentKey key, out IscrizioneEsitoFactsRaw? facts)
        {
            if (Students.TryGetValue(key, out var info) && info != null)
            {
                facts = info.InformazioniIscrizione.EsitoFacts;
                return true;
            }

            facts = null;
            return false;
        }

        public List<CarrieraPregressaBeneficiRiRaw> GetOrCreateCarrieraPregressaBeneficiRi(StudentKey key)
            => GetOrCreateStudent(key).InformazioniIscrizione.CarrieraPregressaBeneficiRi;

        public bool TryGetCarrieraPregressaBeneficiRi(StudentKey key, out List<CarrieraPregressaBeneficiRiRaw>? rows)
        {
            if (Students.TryGetValue(key, out var info) && info != null)
            {
                rows = info.InformazioniIscrizione.CarrieraPregressaBeneficiRi;
                return true;
            }

            rows = null;
            return false;
        }
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


    internal sealed class CreditoRichiestoRow
    {
        public int TipologiaCorso { get; set; }
        public int AnnoCorso { get; set; }
        public string CodCorsoLaurea { get; set; } = string.Empty;
        public decimal CreditiRichiesti { get; set; }
        public bool Disabile { get; set; }
        public int IdCreditiRichiesti { get; set; }
    }

    internal sealed class CreditiRichiestiCatalog
    {
        private readonly List<CreditoRichiestoRow> _items = new();

        private static readonly Dictionary<string, string> AteneoBySede = new(StringComparer.OrdinalIgnoreCase)
        {
            ["J"] = "LUISS",
            ["X"] = "UNINT",
            ["K"] = "LUMSA",
            ["R"] = "CAMPUSBIO"
        };

        public void Clear() => _items.Clear();

        public void Add(int? tipologiaCorso, int? annoCorso, string? codCorsoLaurea, decimal? creditiRichiesti, bool? disabile, int? idCreditiRichiesti)
        {
            if (!tipologiaCorso.HasValue || !annoCorso.HasValue || !creditiRichiesti.HasValue || !disabile.HasValue)
                return;

            _items.Add(new CreditoRichiestoRow
            {
                TipologiaCorso = tipologiaCorso.Value,
                AnnoCorso = annoCorso.Value,
                CodCorsoLaurea = NormalizeCodCorsoLaurea(codCorsoLaurea),
                CreditiRichiesti = creditiRichiesti.Value,
                Disabile = disabile.Value,
                IdCreditiRichiesti = idCreditiRichiesti ?? 0
            });
        }

        public CreditoRichiestoRow? Resolve(
            int tipologiaCorso,
            int annoCorsoNormalizzato,
            int annoCorsoOriginale,
            bool invalido,
            string? codCorsoLaurea,
            string? codSedeStudi)
        {
            string corso = NormalizeCodCorsoLaurea(codCorsoLaurea);
            string gruppoAteneo = ResolveGruppoAteneo(codSedeStudi);
            bool hasAnnoAlternativo = annoCorsoOriginale != annoCorsoNormalizzato;

            CreditoRichiestoRow? best = null;
            int bestPriority = int.MaxValue;
            int bestId = int.MinValue;

            foreach (var item in _items)
            {
                if (item.TipologiaCorso != tipologiaCorso || item.Disabile != invalido)
                    continue;

                int priority;
                if (!IsDefaultCodCorso(corso)
                    && string.Equals(item.CodCorsoLaurea, corso, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.AnnoCorso == annoCorsoNormalizzato)
                        priority = 0;
                    else if (hasAnnoAlternativo && item.AnnoCorso == annoCorsoOriginale)
                        priority = 1;
                    else
                        continue;
                }
                else if (!string.IsNullOrEmpty(gruppoAteneo) && string.Equals(item.CodCorsoLaurea, gruppoAteneo, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.AnnoCorso == annoCorsoNormalizzato)
                        priority = 2;
                    else if (hasAnnoAlternativo && item.AnnoCorso == annoCorsoOriginale)
                        priority = 3;
                    else
                        continue;
                }
                else if (IsDefaultCodCorso(item.CodCorsoLaurea)
                         && item.AnnoCorso == annoCorsoNormalizzato)
                {
                    // Quando l'anno viene normalizzato, la riga generica relativa
                    // all'anno originale può rappresentare una diversa durata legale.
                    priority = 4;
                }
                else
                {
                    continue;
                }

                if (best == null || priority < bestPriority || (priority == bestPriority && item.IdCreditiRichiesti > bestId))
                {
                    best = item;
                    bestPriority = priority;
                    bestId = item.IdCreditiRichiesti;
                }
            }

            return best;
        }

        private static string ResolveGruppoAteneo(string? codSedeStudi)
        {
            string sede = Normalize(codSedeStudi);
            return AteneoBySede.TryGetValue(sede, out var gruppo) ? gruppo : string.Empty;
        }

        private static bool IsDefaultCodCorso(string? codCorsoLaurea)
        {
            string value = NormalizeCodCorsoLaurea(codCorsoLaurea);
            return string.IsNullOrEmpty(value) || value == "0";
        }

        private static string NormalizeCodCorsoLaurea(string? value)
        {
            string normalized = Normalize(value);
            return normalized == "00000" ? "0" : normalized;
        }

        private static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();
    }

}
