using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using ProcedureNet7.Verifica;
using System.Linq;

namespace ProcedureNet7
{
    internal static class EsitoBorsaSupport
    {
        public static readonly IReadOnlyList<string> SupportedBenefitCodes = new[] { "BS", "PA", "PK", "CI" };

        public const string MotivoAdeguatezzaOrigineCoUniversitario = "CO_UNIVERSITARIO";
        public const string MotivoAdeguatezzaOrigineCoOrdinarioConIntegrazioneEsteri = "CO_ORDINARIO_CON_INTEGRAZIONE_ESTERI";
        public const string MotivoAdeguatezzaOrigineCoOrdinarioSemestreFiltro = "CO_ORDINARIO_SEMESTRE_FILTRO";
        public const string MotivoAdeguatezzaOrigineCoOrdinarioInAttesaRegolarizzazione = "CO_ORDINARIO_IN_ATTESA_REGOLARIZZAZIONE";
        public const string MotivoAdeguatezzaOrigineMancanteIseeBase = "MANCANTE_ISEE_BASE";
        public const string MotivoAdeguatezzaOrigineMancanteCoAdeguata = "MANCANTE_CO_ADEGUATA";

        public static string NormalizeUpper(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        public static bool IsCoAdeguataOrigine(
            bool hasCoUniversitario,
            bool hasCoOrdinarioConIntegrazioneEsteri,
            bool hasCoOrdinarioSemestreFiltro)
            => hasCoUniversitario || hasCoOrdinarioConIntegrazioneEsteri || hasCoOrdinarioSemestreFiltro;

        public static bool IsEntroScadenzaRegolarizzazioneIseeOrdinario(DateTime referenceDate, int aaInizio)
            => aaInizio > 0 && referenceDate.Date <= new DateTime(aaInizio, 12, 10);

        public static string GetMotivoAdeguatezzaOrigine(
            bool hasIseeBaseEntroScadenza,
            bool hasCoUniversitarioEntroScadenza,
            bool hasCoOrdinarioConIntegrazioneEsteriEntroScadenza,
            bool hasCoOrdinarioSemestreFiltroEntroScadenza)
        {
            if (!hasIseeBaseEntroScadenza)
                return MotivoAdeguatezzaOrigineMancanteIseeBase;

            if (hasCoUniversitarioEntroScadenza)
                return MotivoAdeguatezzaOrigineCoUniversitario;

            if (hasCoOrdinarioConIntegrazioneEsteriEntroScadenza)
                return MotivoAdeguatezzaOrigineCoOrdinarioConIntegrazioneEsteri;

            if (hasCoOrdinarioSemestreFiltroEntroScadenza)
                return MotivoAdeguatezzaOrigineCoOrdinarioSemestreFiltro;

            return MotivoAdeguatezzaOrigineMancanteCoAdeguata;
        }

        public static IReadOnlyList<string> GetDiagnosticaIscrizione(EsitoBorsaStudentContext? context)
        {
            var result = new List<string>();
            if (context?.Iscrizione == null)
                return result;

            var iscr = context.Iscrizione;
            var facts = context.Facts;
            int tipoStudente = facts.TipoStudenteNormalizzato ?? 0;
            string annoImmatr = Convert.ToString(iscr.AnnoImmatricolazione ?? 0, CultureInfo.InvariantCulture) ?? string.Empty;

            if (!IsAnnoImmatricolazioneValido(annoImmatr))
                AddMotivoEsclusione(result, "ISC001");

            if (facts.CarrieraInterrotta == true && (!facts.NumAnniInterruzione.HasValue || facts.NumAnniInterruzione.Value <= 0))
                AddMotivoEsclusione(result, "ISC004");

            if (iscr.TipoCorso != 6 && iscr.TipoCorso != 7 && tipoStudente != 0 && string.IsNullOrWhiteSpace(iscr.CodCorsoLaurea))
                AddMotivoEsclusione(result, "ISC006");

            if (iscr.AnnoCorso == 0)
                AddMotivoEsclusione(result, "ISC008");

            return result;
        }

        public static bool IsAnnoImmatricolazioneValido(string? annoImmatricolazione)
        {
            string value = (annoImmatricolazione ?? string.Empty).Trim();
            if (value.Length != 8)
                return false;

            if (!int.TryParse(value.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y1))
                return false;
            if (!int.TryParse(value.Substring(4, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y2))
                return false;

            return y2 == y1 + 1;
        }

        public static string GetComuneResidenza(StudenteInfo info)
        {
            if (info?.InformazioniSede?.Residenza == null)
                return string.Empty;

            var codComune = (info.InformazioniSede.Residenza.codComune ?? string.Empty).Trim();
            if (codComune.Length > 0)
                return codComune;

            return (info.InformazioniSede.Residenza.nomeComune ?? string.Empty).Trim();
        }

        public static int GetDurataNormaleCorso(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return 0;

            if (iscr.TipoCorso == 6)
                return 3;

            if (iscr?.DurataLegaleCorso.HasValue == true && iscr.DurataLegaleCorso.Value > 0)
                return iscr.DurataLegaleCorso.Value;

            return iscr.TipoCorso switch
            {
                3 => 3,
                4 => iscr.CorsoMedicina ? 6 : 5,
                5 => 2,
                6 => 3,
                _ => 0
            };
        }

        public static int GetAnnoCorsoCalcolato(InformazioniIscrizione? iscr, int aaInizioCorrente)
        {
            if (iscr == null)
                return 0;

            if (iscr.TipoCorso == 7)
                return iscr.AnnoCorso;

            int durataNormale = GetDurataNormaleCorso(iscr);
            if (durataNormale <= 0)
                return 0;

            int aaInizioImmatricolazione = GetAnnoInizioDaAnnoAccademico(iscr.AnnoImmatricolazione ?? 0);
            if (aaInizioCorrente <= 0 || aaInizioImmatricolazione <= 0)
                return 0;

            int annoProgressivo = aaInizioCorrente - aaInizioImmatricolazione + 1;
            if (annoProgressivo > durataNormale && annoProgressivo != 1)
                return durataNormale - annoProgressivo;

            return annoProgressivo;
        }

        public static int GetAnnoCorsoCalcolato(InformazioniIscrizione? iscr, EsitoBorsaFacts? facts, int aaInizioCorrente, int aaNumero)
        {
            if (iscr == null)
                return 0;

            if (facts == null)
                return GetAnnoCorsoCalcolato(iscr, aaInizioCorrente);

            if (iscr.TipoCorso == 7)
                return iscr.AnnoCorso;

            int durataNormale = GetDurataNormaleCorso(iscr);
            if (durataNormale <= 0)
                return 0;

            int aaInizioImmatricolazione = GetAnnoInizioDaAnnoAccademico(iscr.AnnoImmatricolazione ?? 0);
            if (aaInizioCorrente <= 0 || aaInizioImmatricolazione <= 0)
                return 0;

            int annoCorsoCalcolato = aaInizioCorrente - aaInizioImmatricolazione + 1;

            if (facts.CarrieraInterrotta == true)
            {
                int anniInterruzione = Math.Max(facts.NumAnniInterruzione ?? 0, 0);
                if (anniInterruzione > 2)
                    anniInterruzione = 2;

                annoCorsoCalcolato -= anniInterruzione;
            }

            bool haRinuncia = HasRiconoscimentoCreditiDaRinuncia(iscr);
            bool passaggioTrasferimento = facts.PassaggioTrasferimento == true;
            bool ripetenteDaPassaggio = aaNumero >= 20252026 && facts.RipetenteDaPassaggio == true;

            if (aaNumero >= 20242025)
            {
                bool ricalcolaDaAaCrediti = false;
                if (aaNumero == 20242025 && haRinuncia)
                    ricalcolaDaAaCrediti = true;
                else if (aaNumero > 20242025 && haRinuncia && !passaggioTrasferimento)
                    ricalcolaDaAaCrediti = true;

                if (ripetenteDaPassaggio)
                    ricalcolaDaAaCrediti = true;

                if (ricalcolaDaAaCrediti)
                {
                    int aaCrediti = ParseAnnoAccademicoStartFromString(iscr.AACreditiRiconosciuti);
                    if (aaCrediti > 0)
                    {
                        annoCorsoCalcolato = aaInizioCorrente - aaCrediti + 1;
                    }
                    else if (ripetenteDaPassaggio)
                    {
                        int aaTs = GetAnnoInizioDaAnnoAccademico(facts.AaTrasferimento ?? 0);
                        if (aaTs > 0)
                            annoCorsoCalcolato = aaInizioCorrente - aaTs + 1;
                    }
                }
            }

            if (annoCorsoCalcolato > durataNormale && annoCorsoCalcolato != 1)
                annoCorsoCalcolato = durataNormale - annoCorsoCalcolato;

            if (!((aaNumero >= 20242025 && haRinuncia) || ripetenteDaPassaggio))
            {
                int annoDichiaratoNorm = NormalizeAnnoCorsoPerConfronto(iscr.AnnoCorso, durataNormale);
                int annoCalcolatoNorm = NormalizeAnnoCorsoPerConfronto(annoCorsoCalcolato, durataNormale);
                if (annoCalcolatoNorm < annoDichiaratoNorm)
                    return 0;
            }

            return annoCorsoCalcolato;
        }

        public static int GetAnnoCorsoCalcolato(EsitoBorsaStudentContext? context)
        {
            if (context?.Iscrizione == null)
                return 0;

            return GetAnnoCorsoCalcolato(context.Iscrizione, context.Facts, context.AaInizio, context.AaNumero);
        }

        private static int NormalizeAnnoCorsoPerConfronto(int annoCorso, int durataNormale)
            => annoCorso < 0 ? Math.Abs(annoCorso) + durataNormale : annoCorso;

        public static int ParseAnnoAccademicoStartFromString(string? value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length >= 4 && int.TryParse(normalized.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                return result;

            return 0;
        }

        public static bool HasRiconoscimentoCreditiDaRinuncia(InformazioniIscrizione? iscr)
            => (iscr?.CreditiRiconosciutiDaRinuncia ?? 0m) > 0m;

        public static bool HasDerogaAnnoCorsoDaRinuncia(EsitoBorsaStudentContext context)
        {
            if (context == null)
                return false;

            return context.AaNumero >= 20242025
                   && HasRiconoscimentoCreditiDaRinuncia(context.Iscrizione)
                   && context.Facts.PassaggioTrasferimento != true;
        }

        public static bool HasRipetenzaDaPassaggio(EsitoBorsaStudentContext context)
        {
            if (context == null || context.AaNumero < 20252026)
                return false;

            if (context.Facts.RipetenteDaPassaggio.HasValue)
                return context.Facts.RipetenteDaPassaggio.Value;

            return context.Iscrizione?.HaRipetenzaCarrieraPregressa != 0;
        }

        public static int GetAnnoCorsoRiferimentoBeneficio(EsitoBorsaStudentContext context)
        {
            if (context?.Iscrizione == null)
                return 0;

            return context.Iscrizione.AnnoCorso;
        }

        public static string GetVariazioniEscludentiBsSummary(EsitoBorsaFacts? facts)
        {
            if (facts == null)
                return string.Empty;

            var items = new List<string>();
            if (facts.RinunciaBS) items.Add("RINUNCIA_BS");
            if (facts.DecadutoBS) items.Add("DECADENZA_BS");
            if (facts.Revocato) items.Add("REVOCA_TOTALE");
            if (facts.RevocatoBandoBS) items.Add("REVOCA_BANDO_BS");
            if (facts.RevocatoMancataIscrizione) items.Add("REVOCA_MANCATA_ISCRIZIONE");
            if (facts.RevocatoIscrittoRipetente) items.Add("REVOCA_RIPETENTE");
            if (facts.RevocatoISEE) items.Add("REVOCA_ISEE");
            if (facts.RevocatoLaureato) items.Add("REVOCA_LAUREATO");
            if (facts.RevocatoPatrimonio) items.Add("REVOCA_PATRIMONIO");
            if (facts.RevocatoReddito) items.Add("REVOCA_REDDITO");
            if (facts.RevocatoEsami) items.Add("REVOCA_ESAMI");
            if (facts.RevocatoFuoriTermine) items.Add("REVOCA_FUORI_TERMINE");
            if (facts.RevocatoIseeFuoriTermine) items.Add("REVOCA_ISEE_FUORI_TERMINE");
            if (facts.RevocatoIseeNonProdotta) items.Add("REVOCA_ISEE_NON_PRODOTTA");
            if (facts.DecadutoMancataComunicazionePagamento) items.Add("DECADENZA_MANCATA_COMUNICAZIONE_PAGAMENTO");
            if (facts.RevocatoNoContrattoLocazione) items.Add("REVOCA_NO_CONTRATTO");
            return string.Join(";", items);
        }

        public static int GetAnnoInizioDaAnnoAccademico(int annoAccademico)
        {
            string valore = annoAccademico.ToString(CultureInfo.InvariantCulture);

            if (valore.Length >= 8)
                return int.TryParse(valore.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int aaInizioDa8) ? aaInizioDa8 : 0;

            if (valore.Length == 4)
                return annoAccademico;

            return 0;
        }

        public static DateTime? GetScadenzaAttesaTitolo(int aaInizio)
        {
            if (aaInizio <= 0)
                return null;

            return new DateTime(aaInizio + 1, 2, 10);
        }

        public static bool IsAttesaTitoloValidaAllaData(DateTime referenceDate, int aaInizio)
        {
            DateTime? scadenza = GetScadenzaAttesaTitolo(aaInizio);
            if (!scadenza.HasValue)
                return false;

            return referenceDate.Date <= scadenza.Value.Date;
        }

        public static bool HasTitoloAccessoValido(EsitoBorsaStudentContext? context)
        {
            if (context?.Facts == null)
                return false;

            if (context.Facts.TitoloAccademicoConseguito == true)
                return true;

            if (context.Facts.AttesaTitoloAccademicoConseguito == true)
                return context.Facts.AttesaTitoloValidaAllaDataValutazione == true;

            return false;
        }

        public static decimal? GetIseeRiferimento(StudenteInfo info)
            => info?.InformazioniEconomiche?.Calcolate?.ISEEDSU;

        public static decimal? GetIspRiferimento(StudenteInfo info)
            => info?.InformazioniEconomiche?.Calcolate?.ISPEDSU;

        public static int? GetStatusIseeDaEconomici(StudenteInfo? info)
        {
            var raw = info?.InformazioniEconomiche?.Raw;
            var calcolate = info?.InformazioniEconomiche?.Calcolate;
            if (raw == null)
                return null;

            bool redditoEstero = string.Equals(NormalizeUpper(raw.TipoRedditoOrigine), "EE", StringComparison.OrdinalIgnoreCase);
            bool integrazioneIt = string.Equals(NormalizeUpper(raw.TipoNucleo), "I", StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(NormalizeUpper(raw.TipoRedditoIntegrazione), "IT", StringComparison.OrdinalIgnoreCase);

            if (integrazioneIt && raw.StatusInpsIntegrazione == 444)
                return 13;

            if (integrazioneIt && raw.StatusInpsIntegrazione != 0 && raw.StatusInpsIntegrazione != 2)
                return 3;

            if (redditoEstero)
                return 2;

            int statusOrigine = raw.StatusInpsOrigine;
            if (statusOrigine == 5 || statusOrigine == 6 || statusOrigine == 7 || statusOrigine == 8 || statusOrigine == 9)
                statusOrigine = 2;

            if (statusOrigine == 2)
            {
                decimal? isee = calcolate?.ISEEDSU;

                if (string.Equals(NormalizeUpper(raw.TipoRedditoOrigine), "IT", StringComparison.OrdinalIgnoreCase)
                    && isee.HasValue
                    && isee.Value == 0m
                    && raw.OrigineSommaRedditi > 0m)
                {
                    return 11;
                }

                return 2;
            }

            return statusOrigine;
        }

        public static bool IsSituazioneEconomicaValidaPerEsito(StudenteInfo? info)
        {
            var raw = info?.InformazioniEconomiche?.Raw;
            if (raw == null)
                return false;

            int? statusIsee = GetStatusIseeDaEconomici(info);
            if (!statusIsee.HasValue || statusIsee.Value == 0)
                return true;

            if (statusIsee.Value == 11)
                return true;

            if (statusIsee.Value == 13)
                return false;

            string tipoOrigine = NormalizeUpper(raw.TipoRedditoOrigine);
            string origineFonte = NormalizeUpper(raw.OrigineFonte);

            if (string.Equals(tipoOrigine, "EE", StringComparison.OrdinalIgnoreCase))
                return string.Equals(origineFonte, "EE", StringComparison.OrdinalIgnoreCase);

            if (statusIsee.Value == 2)
                return string.Equals(origineFonte, "CO", StringComparison.OrdinalIgnoreCase);

            return false;
        }

        public static bool TryReadDecimal(object? value, out decimal result)
        {
            result = 0m;

            if (value == null || value == DBNull.Value)
                return false;

            try
            {
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int ParseAnnoAccademicoInizio(string aa)
        {
            if (string.IsNullOrWhiteSpace(aa) || aa.Length < 4)
                return 0;

            return int.TryParse(aa.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : 0;
        }

        public static int ParseAnnoAccademicoAsNumber(string aa)
        {
            if (string.IsNullOrWhiteSpace(aa))
                return 0;

            return int.TryParse(aa.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : 0;
        }

        public static void AddMotivoEsclusione(List<string> codiciEsclusione, string codice)
        {
            if (!codiciEsclusione.Any(x => string.Equals(x, codice, StringComparison.OrdinalIgnoreCase)))
                codiciEsclusione.Add(codice);
        }

        public static string GetMotivoEsclusione(string codice)
            => EsitoBorsaMotiviCatalog.GetDescrizione(codice);

        public static IReadOnlyList<string> GetRequestedBenefitCodes(EsitoBorsaFacts? facts)
        {
            if (facts == null || facts.BeneficiRichiesti.Count == 0)
                return Array.Empty<string>();

            return SupportedBenefitCodes
                .Where(facts.BeneficiRichiesti.Contains)
                .ToArray();
        }

        public static bool IsBenefitRequested(EsitoBorsaFacts? facts, string? beneficio)
        {
            string normalized = NormalizeUpper(beneficio);
            return facts != null
                   && SupportedBenefitCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase)
                   && facts.BeneficiRichiesti.Contains(normalized);
        }

        public static string GetSlashMotiviEsclusione(EsitoBorsaFacts? facts, string? beneficio)
        {
            if (facts == null)
                return string.Empty;

            string normalized = NormalizeUpper(beneficio);
            return facts.SlashMotiviEsclusioneByBenefit.TryGetValue(normalized, out var value)
                ? (value ?? string.Empty)
                : string.Empty;
        }

        public static bool HasLegacyFuoriCorsoInammissibile(EsitoBorsaFacts? facts, string? beneficio)
        {
            string slash = GetSlashMotiviEsclusione(facts, beneficio);
            if (string.IsNullOrWhiteSpace(slash))
                return false;

            string normalized = NormalizeUpper(slash)
                .Replace("#", string.Empty, StringComparison.Ordinal)
                .Replace(";", " ", StringComparison.Ordinal)
                .Trim();

            return normalized.Contains("ANNI DI FUORI CORSO INAMMISSIBILI PER LA BORSA DI STUDIO", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("ANNO DI FUORI CORSO INAMMISSIBILE PER LA BORSA DI STUDIO", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("FUORI CORSO INAMMISSIBILI PER LA BORSA DI STUDIO", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("FUORI CORSO INAMMISSIBILE PER LA BORSA DI STUDIO", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasRinunciaVariazione(EsitoBorsaFacts? facts, string? beneficio)
        {
            if (facts == null)
                return false;

            string normalized = NormalizeUpper(beneficio);
            return normalized switch
            {
                "BS" => facts.RinunciaBS,
                "PA" => facts.RinunciaPA,
                "PK" => facts.RinunciaPK,
                "CI" => facts.RinunciaCI,
                _ => false
            };
        }

        public static bool HasDecadenzaVariazione(EsitoBorsaFacts? facts, string? beneficio)
        {
            if (facts == null)
                return false;

            string normalized = NormalizeUpper(beneficio);
            return normalized switch
            {
                "BS" => facts.DecadutoBS,
                "PA" => facts.DecadutoPA,
                "PK" => facts.DecadutoPK,
                "CI" => facts.DecadutoCI,
                _ => false
            };
        }

        public static bool HasRevocaBandoVariazione(EsitoBorsaFacts? facts, string? beneficio)
        {
            if (facts == null)
                return false;

            string normalized = NormalizeUpper(beneficio);
            return normalized switch
            {
                "BS" => facts.RevocatoBandoBS,
                "PA" => facts.RevocatoBandoPA,
                "PK" => facts.RevocatoBandoPK,
                "CI" => facts.RevocatoBandoCI,
                _ => false
            };
        }

        public static string GetVariazioniEscludentiSummary(EsitoBorsaFacts? facts, string? beneficio)
        {
            if (facts == null)
                return string.Empty;

            string normalized = NormalizeUpper(beneficio);
            var items = new List<string>();

            if (HasRinunciaVariazione(facts, normalized)) items.Add($"RINUNCIA_{normalized}");
            if (HasDecadenzaVariazione(facts, normalized)) items.Add($"DECADENZA_{normalized}");
            if (facts.Revocato) items.Add("REVOCA_TOTALE");
            if (HasRevocaBandoVariazione(facts, normalized)) items.Add($"REVOCA_BANDO_{normalized}");
            if (facts.RevocatoMancataIscrizione) items.Add("REVOCA_MANCATA_ISCRIZIONE");
            if (facts.RevocatoIscrittoRipetente) items.Add("REVOCA_RIPETENTE");
            if (facts.RevocatoISEE) items.Add("REVOCA_ISEE");
            if (facts.RevocatoLaureato) items.Add("REVOCA_LAUREATO");
            if (facts.RevocatoPatrimonio) items.Add("REVOCA_PATRIMONIO");
            if (facts.RevocatoReddito) items.Add("REVOCA_REDDITO");
            if (facts.RevocatoEsami) items.Add("REVOCA_ESAMI");
            if (facts.RevocatoFuoriTermine) items.Add("REVOCA_FUORI_TERMINE");
            if (facts.RevocatoIseeFuoriTermine) items.Add("REVOCA_ISEE_FUORI_TERMINE");
            if (facts.RevocatoIseeNonProdotta) items.Add("REVOCA_ISEE_NON_PRODOTTA");
            if (facts.DecadutoMancataComunicazionePagamento) items.Add("DECADENZA_MANCATA_COMUNICAZIONE_PAGAMENTO");
            if (facts.RevocatoNoContrattoLocazione) items.Add("REVOCA_NO_CONTRATTO");
            return string.Join(";", items);
        }

        public static EsitoBorsaRuleConfig LoadRuleConfig(VerificaPipelineContext context)
        {
            var config = new EsitoBorsaRuleConfig
            {
                SogliaIsee = context.CalcParams?.SogliaIsee ?? 0m,
                SogliaIsp = 0m
            };

            config.SogliaIsp = LoadDatiGeneraliConValue(
                context.Connection,
                context.AnnoAccademico,
                new[] { "Soglia_Ispe", "Soglia_ISP", "Soglia_isp", "Soglia_patrimonio", "Soglia_Patrimonio" });

            config.Validate(context.AnnoAccademico);
            return config;
        }

        private static decimal LoadDatiGeneraliConValue(SqlConnection connection, string annoAccademico, IReadOnlyList<string> candidateColumns)
        {
            string? selectedColumn = null;

            const string columnsSql = @"
SELECT c.name
FROM sys.columns c
INNER JOIN sys.objects o ON o.object_id = c.object_id
WHERE o.type = 'U'
  AND o.name = 'DatiGenerali_con';";

            using (var cmdColumns = new SqlCommand(columnsSql, connection) { CommandType = CommandType.Text, CommandTimeout = 9999999 })
            using (var reader = cmdColumns.ExecuteReader())
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                    names.Add(Convert.ToString(reader[0], CultureInfo.InvariantCulture) ?? string.Empty);

                foreach (var candidate in candidateColumns)
                {
                    if (names.Contains(candidate))
                    {
                        selectedColumn = candidate;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(selectedColumn))
                throw new InvalidOperationException($"Configurazione economica da sistemare per AA {annoAccademico}: colonna Soglia_ISPE mancante in DatiGenerali_con.");

            string sql = $@"
SELECT TOP (1) TRY_CONVERT(DECIMAL(18,2), [{selectedColumn}])
FROM DatiGenerali_con
WHERE Anno_accademico = @AA;";

            using var cmd = new SqlCommand(sql, connection) { CommandType = CommandType.Text, CommandTimeout = 9999999 };
            cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = (annoAccademico ?? string.Empty).Trim();

            object? value = cmd.ExecuteScalar();
            if (!TryReadDecimal(value, out var parsed) || parsed <= 0m)
                throw new InvalidOperationException($"Configurazione economica da sistemare per AA {annoAccademico}: DatiGenerali_con.{selectedColumn} mancante o non maggiore di zero.");
            return parsed;
        }
    }
}
