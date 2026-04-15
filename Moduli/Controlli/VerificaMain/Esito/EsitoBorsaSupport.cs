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
        private static readonly Dictionary<string, string> MotiviEsclusione = new(StringComparer.OrdinalIgnoreCase)
        {
            { "GEN000", "Domanda non completa" },
            { "GEN001", "Domanda completa ma non trasmessa" },
            { "GEN003", "Domanda presentata oltre il termine previsto dal bando" },
            { "GEN004", "Documentazione consolare mancante" },
            { "GEN005", "Permesso di soggiorno mancante" },
            { "GEN006", "Tipologia studi incompatibile con il titolo accademico già conseguito" },
            { "GEN007", "Rinuncia a tutti i benefici" },
            { "GEN088", "Domanda non validabile nella fase elaborativa corrente" },
            { "GEN093", "Iscrizione fuori termine" },
            { "GEN094", "Domanda non trasmessa" },
            { "GENSIG", "Domanda non firmata" },
            { "GENDOC", "Documento di riconoscimento mancante" },
            { "RED001", "Conferma del reddito mancante" },
            { "RED011", "Valore ISEE assente o non valido" },
            { "RED012", "Valore ISP oltre la soglia ammessa" },
            { "RED013", "Valore ISEE oltre la soglia ammessa" },
            { "RED086", "Stato ISEE non ammesso" },
            { "RED087", "Codice fiscale dello studente indipendente presente nell'attestazione ISEE della famiglia di origine" },
            { "MER001", "Dati di merito assenti o non sufficienti per il calcolo" },
            { "MER005", "Crediti dichiarati incongruenti con il corso di studi" },
            { "MER071", "Esame complementare non valido per il merito AFAM" },
            { "MER072", "Anno di corso incongruente con l'anno accademico di immatricolazione" },
            { "MER074", "Crediti riconosciuti insufficienti per il primo anno di specialistica" },
            { "MER012", "Merito insufficiente per la borsa" },
            { "MER092", "Crediti di tirocinio superiori ai crediti dichiarati" },
            { "MER089", "Titolo di accesso non ammesso per immatricolazione alla specialistica" },
            { "MER170", "Iscrizione non ammessa per Sapienza in vecchio ordinamento" },
            { "MER171", "Passaggio vecchio→nuovo non calcolabile con i dati esposti" },
            { "BS001", "Anno di corso oltre il limite ammesso per la borsa" },
            { "BS002", "Beneficio borsa già fruito e non restituito" },
            { "BS003", "Rinuncia pregressa alla borsa di studio" },
            { "VAR003", "Revoca di tutti i benefici da variazione" },
            { "VAR004", "Decadenza della borsa di studio da variazione" },
            { "VAR011", "Revoca della borsa per incompatibilità con il bando" },
            { "VAR019", "Revoca per mancata iscrizione" },
            { "VAR020", "Revoca per iscrizione come ripetente" },
            { "VAR021", "Revoca per ISEE o anni di fuori corso inammissibili" },
            { "VAR022", "Revoca per studente già laureato" },
            { "VAR023", "Revoca per patrimonio oltre il limite" },
            { "VAR024", "Revoca per reddito oltre il limite" },
            { "VAR025", "Revoca per mancanza esami o crediti" },
            { "VAR027", "Revoca per iscrizione fuori termine" },
            { "VAR028", "Revoca per ISEE fuori termine" },
            { "VAR029", "Revoca per ISEE non prodotta" },
            { "VAR030", "Revoca per trasmissione ISEE CAF fuori termine" },
            { "VAR031", "Revoca per mancanza contratto di locazione" }
        };

        public static string NormalizeUpper(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

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
                        annoCorsoCalcolato = aaInizioCorrente - aaCrediti + 1;
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

        public static bool IsPassaggioVecchioNuovo(EsitoBorsaStudentContext context)
            => context?.Facts?.PassaggioVecchioNuovo == true;

        public static bool IsPassaggioVecchioNuovoCalcolabile(EsitoBorsaStudentContext context)
        {
            if (context?.Iscrizione == null || !IsPassaggioVecchioNuovo(context))
                return true;

            var iscr = context.Iscrizione;
            string ordCorrente = NormalizeUpper(context.Facts.CodTipoOrdinamento);
            int aaImmatricolazioneInizio = GetAnnoInizioDaAnnoAccademico(iscr.AnnoImmatricolazione ?? 0);

            if (ordCorrente != "3")
                return false;

            if (aaImmatricolazioneInizio > 2001 && IsEnte(iscr, "01"))
                return false;

            return true;
        }

        public static int GetAnnoCorsoRiferimentoBeneficio(EsitoBorsaStudentContext context)
        {
            if (context?.Iscrizione == null)
                return 0;

            if (HasRipetenzaDaPassaggio(context))
                return context.Iscrizione.AnnoCorso;

            return GetAnnoCorsoCalcolato(context);
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
            if (facts.RevocatoTrasmissioneIseeFuoriTermine) items.Add("REVOCA_TRASMISSIONE_ISEE_FUORI_TERMINE");
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

        public static bool IsEnte(InformazioniIscrizione iscr, string codEnte)
            => string.Equals((iscr?.CodEnte ?? string.Empty).Trim(), (codEnte ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        public static bool IsAccademiaVecchioOrdinamento(InformazioniIscrizione? iscr, EsitoBorsaFacts? facts)
        {
            if (iscr == null || facts == null)
                return false;

            string sede = NormalizeUpper(iscr.CodSedeStudi);
            string ord = NormalizeUpper(facts.CodTipoOrdinamento);
            return ord == "1" && (sede == "O" || sede == "Q" || sede == "P" || sede == "L" || sede == "T" || sede == "G");
        }

        public static decimal? GetEsamiMinimiRichiesti(EsitoBorsaStudentContext context, InformazioniIscrizione iscr)
        {
            if (context?.Pipeline == null || iscr == null)
                return null;

            int annoCorsoCalcolato = GetAnnoCorsoCalcolato(context);
            if (annoCorsoCalcolato == 0 || annoCorsoCalcolato == 1)
                return 0m;

            string codOrd = NormalizeUpper(context.Facts.CodTipoOrdinamento);
            string annoAccadInizio = !string.IsNullOrWhiteSpace(iscr.AnnoAccadInizioCorso)
                ? iscr.AnnoAccadInizioCorso
                : (iscr.AnnoImmatricolazione?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            decimal totaleEsami = annoCorsoCalcolato < 0
                ? context.Pipeline.EsamiCatalog.GetTotal(iscr.CodCorsoLaurea, codOrd, annoAccadInizio)
                : context.Pipeline.EsamiCatalog.GetSumBeforeYear(iscr.CodCorsoLaurea, codOrd, annoAccadInizio, annoCorsoCalcolato);

            if (totaleEsami <= 0m)
                return null;

            bool corsoNormale = !NormalizeUpper(iscr.CodCorsoLaurea).StartsWith("Q00", StringComparison.Ordinal)
                               && !NormalizeUpper(iscr.CodCorsoLaurea).StartsWith("U00", StringComparison.Ordinal)
                               && NormalizeUpper(iscr.CodSedeStudi) != "P"
                               && NormalizeUpper(iscr.CodSedeStudi) != "O";

            decimal richiesti;
            switch (annoCorsoCalcolato)
            {
                case 2:
                    richiesti = corsoNormale ? (totaleEsami > 4m ? 2m : 1m) : totaleEsami;
                    break;

                case 3:
                case 4:
                case 5:
                case 6:
                    if (corsoNormale)
                    {
                        bool isUltimoAnno = annoCorsoCalcolato == GetDurataNormaleCorso(iscr);
                        richiesti = isUltimoAnno ? Math.Floor(totaleEsami * 0.6m) : Math.Floor(totaleEsami / 2m) + 1m;
                    }
                    else
                    {
                        richiesti = totaleEsami;
                    }
                    break;

                case -1:
                    richiesti = !NormalizeUpper(iscr.CodCorsoLaurea).StartsWith("U00", StringComparison.Ordinal)
                                && NormalizeUpper(iscr.CodSedeStudi) != "P"
                                && NormalizeUpper(iscr.CodSedeStudi) != "O"
                        ? Math.Floor(totaleEsami * 0.66m)
                        : totaleEsami;
                    break;

                case -2:
                    richiesti = Math.Floor(totaleEsami * 0.9m);
                    break;

                case -3:
                    richiesti = Math.Floor(totaleEsami * 1.063636363m);
                    break;

                case -4:
                    richiesti = Math.Floor(totaleEsami * 1.227272727m);
                    break;

                default:
                    richiesti = 0m;
                    break;
            }

            if (context.Invalido)
                richiesti = Math.Floor(richiesti * 0.55m);

            return richiesti;
        }


        public static decimal GetIseeRiferimento(StudenteInfo info)
        {
            if (TryReadDecimal(info?.InformazioniEconomiche?.Calcolate?.ISEEDSU, out var ordinario) && ordinario > 0m)
                return ordinario;

            if (TryReadDecimal(info?.InformazioniEconomiche?.Attuali?.ISEEDSU, out var attuale) && attuale > 0m)
                return attuale;

            return 0m;
        }

        public static decimal GetIspRiferimento(StudenteInfo info)
        {
            if (TryReadDecimal(info?.InformazioniEconomiche?.Calcolate?.ISPEDSU, out var ordinario) && ordinario > 0m)
                return ordinario;

            if (TryReadDecimal(info?.InformazioniEconomiche?.Attuali?.ISPEDSU, out var attuale) && attuale > 0m)
                return attuale;

            return 0m;
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
        {
            if (MotiviEsclusione.TryGetValue(codice, out var motivo))
                return motivo;

            if (!string.IsNullOrWhiteSpace(codice) && codice.StartsWith("GENF", StringComparison.OrdinalIgnoreCase))
                return $"Forzatura generale attiva {codice.Substring(4)}";

            return codice;
        }

        public static EsitoBorsaRuleConfig LoadRuleConfig(VerificaPipelineContext context)
        {
            var config = new EsitoBorsaRuleConfig
            {
                SogliaIsee = context.CalcParams?.SogliaIsee ?? 0m,
                SogliaIsp = 0m
            };

            config.SogliaIsp = TryLoadDatiGeneraliConValue(
                context.Connection,
                context.AnnoAccademico,
                new[] { "Soglia_Isp", "Soglia_ISP", "Soglia_isp", "Soglia_patrimonio", "Soglia_Patrimonio" });

            return config;
        }

        private static decimal TryLoadDatiGeneraliConValue(SqlConnection connection, string annoAccademico, IReadOnlyList<string> candidateColumns)
        {
            if (connection == null || candidateColumns == null || candidateColumns.Count == 0)
                return 0m;

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
                return 0m;

            string sql = $@"
SELECT TOP (1) TRY_CONVERT(DECIMAL(18,2), [{selectedColumn}])
FROM DatiGenerali_con
WHERE Anno_accademico = @AA;";

            using var cmd = new SqlCommand(sql, connection) { CommandType = CommandType.Text, CommandTimeout = 9999999 };
            cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = (annoAccademico ?? string.Empty).Trim();

            object? value = cmd.ExecuteScalar();
            return TryReadDecimal(value, out var parsed) ? parsed : 0m;
        }
    }
}
