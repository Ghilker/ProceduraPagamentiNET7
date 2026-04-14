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
            { "BS003", "Rinuncia pregressa alla borsa di studio" }
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
            if (iscr?.DurataLegaleCorso.HasValue == true && iscr.DurataLegaleCorso.Value > 0)
                return iscr.DurataLegaleCorso.Value;

            return iscr.TipoCorso switch
            {
                3 => 3,
                4 => iscr.CorsoMedicina ? 6 : 5,
                5 => 2,
                _ => 0
            };
        }

        public static int GetAnnoCorsoCalcolato(InformazioniIscrizione? iscr, int aaInizioCorrente)
        {
            if (iscr == null)
                return 0;

            int annoCorsoDichiarato = iscr.AnnoCorso;
            if (aaInizioCorrente <= 0)
                return annoCorsoDichiarato;

            int durataNormale = GetDurataNormaleCorso(iscr);
            if (durataNormale <= 0)
                return annoCorsoDichiarato;

            int aaInizioImmatricolazione = GetAnnoInizioDaAnnoAccademico(iscr.AnnoImmatricolazione ?? 0);
            if (aaInizioImmatricolazione <= 0)
                return annoCorsoDichiarato;

            int anniTrascorsi = aaInizioCorrente - aaInizioImmatricolazione;
            if (anniTrascorsi < 0)
                return annoCorsoDichiarato;

            int annoProgressivo = anniTrascorsi + 1;
            if (annoProgressivo <= durataNormale)
                return annoProgressivo;

            return -(annoProgressivo - durataNormale);
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
            string ordPassaggio = NormalizeUpper(!string.IsNullOrWhiteSpace(iscr.CodTipoOrdinamentoPassaggio)
                ? iscr.CodTipoOrdinamentoPassaggio
                : context.Facts.CodTipoOrdinamentoPassaggio);

            int aaImmatricolazioneInizio = GetAnnoInizioDaAnnoAccademico(iscr.AnnoImmatricolazione ?? 0);

            if (ordPassaggio == "3" || string.IsNullOrWhiteSpace(ordPassaggio))
                return false;

            if (ordCorrente == "1" || ordCorrente == "2")
                return false;

            if (aaImmatricolazioneInizio > 2001 && IsEnte(iscr, "01"))
                return false;

            return true;
        }

        public static int GetAnnoCorsoCalcolatoPassaggio(EsitoBorsaStudentContext context)
        {
            if (context?.Iscrizione == null)
                return 0;

            int anno = GetAnnoCorsoCalcolato(context.Iscrizione, context.AaInizio);
            int durataPassaggio = context.Iscrizione.DurataLegalePassaggio ?? 0;
            int durataCorrente = GetDurataNormaleCorso(context.Iscrizione);

            if (anno == 0 || durataPassaggio <= 0)
                return 0;

            if (anno < 0 && durataCorrente > 0)
                anno = Math.Abs(anno) + durataCorrente;

            if (anno > durataPassaggio)
                anno = durataPassaggio - anno;

            return anno;
        }

        public static decimal? GetEsamiMinimiRichiestiPassaggio(EsitoBorsaStudentContext context)
        {
            if (context?.Pipeline == null || context.Iscrizione == null || !IsPassaggioVecchioNuovo(context))
                return null;

            var iscr = context.Iscrizione;
            int annoCorsoPassaggio = GetAnnoCorsoCalcolatoPassaggio(context);
            if (annoCorsoPassaggio == 0 || annoCorsoPassaggio == 1)
                return 0m;

            string codCorso = iscr.CodCorsoLaureaPassaggio;
            string codOrd = !string.IsNullOrWhiteSpace(iscr.CodTipoOrdinamentoPassaggio)
                ? iscr.CodTipoOrdinamentoPassaggio
                : context.Facts.CodTipoOrdinamentoPassaggio;
            string annoAccadInizio = !string.IsNullOrWhiteSpace(iscr.AnnoAccadInizioPassaggio)
                ? iscr.AnnoAccadInizioPassaggio
                : (iscr.AnnoImmatricolazione?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (string.IsNullOrWhiteSpace(codCorso) || string.IsNullOrWhiteSpace(codOrd))
                return null;

            decimal totaleEsami = annoCorsoPassaggio < 0
                ? context.Pipeline.EsamiCatalog.GetTotal(codCorso, codOrd, annoAccadInizio)
                : context.Pipeline.EsamiCatalog.GetSumBeforeYear(codCorso, codOrd, annoAccadInizio, annoCorsoPassaggio);

            if (totaleEsami <= 0m)
                return null;

            bool corsoNormale = !NormalizeUpper(codCorso).StartsWith("Q00", StringComparison.Ordinal)
                               && !NormalizeUpper(codCorso).StartsWith("U00", StringComparison.Ordinal)
                               && NormalizeUpper(iscr.CodSedeStudi) != "P"
                               && NormalizeUpper(iscr.CodSedeStudi) != "O";

            decimal richiesti;
            switch (annoCorsoPassaggio)
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
                        bool isUltimoAnno = iscr.DurataLegalePassaggio.HasValue && annoCorsoPassaggio == iscr.DurataLegalePassaggio.Value;
                        richiesti = isUltimoAnno ? Math.Floor(totaleEsami * 0.6m) : Math.Floor(totaleEsami / 2m) + 1m;
                    }
                    else
                    {
                        richiesti = totaleEsami;
                    }
                    break;
                case -1:
                    richiesti = !NormalizeUpper(codCorso).StartsWith("U00", StringComparison.Ordinal)
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

        public static decimal? GetCreditiMinimiRichiestiPassaggio(EsitoBorsaStudentContext context)
        {
            if (context?.Iscrizione == null || !IsPassaggioVecchioNuovo(context))
                return null;

            decimal? esami = GetEsamiMinimiRichiestiPassaggio(context);
            decimal conversione = context.Iscrizione.ConversioneCreditiEsamiPassaggio ?? 0m;

            if (!esami.HasValue || conversione <= 0m)
                return null;

            return esami.Value * conversione;
        }

        public static int GetAnnoCorsoRiferimentoBeneficio(EsitoBorsaStudentContext context)
        {
            if (context?.Iscrizione == null)
                return 0;

            if (HasRipetenzaDaPassaggio(context))
                return context.Iscrizione.AnnoCorso;

            return GetAnnoCorsoCalcolato(context.Iscrizione, context.AaInizio);
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

            int annoCorsoCalcolato = GetAnnoCorsoCalcolato(iscr, context.AaInizio);
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
            if (TryReadDecimal(info?.InformazioniEconomiche?.Calcolate?.ISPDSU, out var ordinario) && ordinario > 0m)
                return ordinario;

            if (TryReadDecimal(info?.InformazioniEconomiche?.Attuali?.ISPDSU, out var attuale) && attuale > 0m)
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
