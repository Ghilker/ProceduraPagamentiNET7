using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class CalcoloEsitoBorsa : IVerificaModule
    {
        private const int StatusCompilazioneMinimoTrasmessa = 90;
        private const int StatusCompilazioneMinimoCompilazione = 70;

        private const int EsitoEscluso = 0;
        private const int EsitoIdoneo = 1;
        private const int EsitoVincitore = 2;

        private static readonly HashSet<string> SediAccademiaVecchioOrdinamento = new(StringComparer.OrdinalIgnoreCase)
        {
            "O", "Q", "P", "L", "T", "G"
        };

        private static readonly HashSet<string> CorsiFarmaciaRiduzione = new(StringComparer.OrdinalIgnoreCase)
        {
            "FLG", "30828", "S2-13"
        };

        private static readonly HashSet<string> CorsiCtfRiduzione = new(StringComparer.OrdinalIgnoreCase)
        {
            "FLH", "29892", "29892_1", "30808", "S2-3"
        };

        private static readonly HashSet<string> CorsiIngEdileArchRiduzione = new(StringComparer.OrdinalIgnoreCase)
        {
            "IR5", "29922", "S2-11"
        };

        private static readonly HashSet<string> CorsiSpecialistica1FcRidottaPre2023 = new(StringComparer.OrdinalIgnoreCase)
        {
            "NXT", "NXL", "NXR", "NXS", "30055", "30060", "30058"
        };

        private static readonly HashSet<string> CorsiSpecialistica1FcRidottaDal2023 = new(StringComparer.OrdinalIgnoreCase)
        {
            "30060", "30055", "30053", "28700", "28701", "30052", "31833", "S2-34"
        };

        private static readonly HashSet<string> CorsiCommissioneSpecialistica2009 = new(StringComparer.OrdinalIgnoreCase)
        {
            "12382", "NXS", "NJH", "6WN"
        };

        private static readonly Dictionary<string, string> MotiviEsclusione = new(StringComparer.OrdinalIgnoreCase)
        {
            { "GEN000", "Domanda non completa" },
            { "GEN001", "Domanda completa ma non trasmessa" },
            { "RED011", "Valore ISEE assente o non valido" },
            { "RED012", "Valore ISP oltre la soglia ammessa" },
            { "RED013", "Valore ISEE oltre la soglia ammessa" },
            { "MER001", "Dati di merito assenti o non sufficienti per il calcolo" },
            { "MER005", "Crediti dichiarati incongruenti con il corso di studi" },
            { "MER072", "Anno di corso incongruente con l'anno accademico di immatricolazione" },
            { "MER074", "Crediti riconosciuti insufficienti per il primo anno di specialistica" },
            { "MER012", "Crediti insufficienti per la borsa" },
            { "MER092", "Crediti di tirocinio superiori ai crediti dichiarati" },
            { "BS001", "Anno di corso oltre il limite ammesso per la borsa" }
        };

        public string Name => "EsitoBorsa";

        public void Calculate(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            int aaInizio = ParseAnnoAccademicoInizio(context.AnnoAccademico);
            int aaNumero = ParseAnnoAccademicoAsNumber(context.AnnoAccademico);
            var config = LoadRuleConfig(context);

            int esclusi = 0;
            int idonei = 0;

            foreach (var pair in context.Students)
            {
                var info = pair.Value;
                var facts = GetFacts(context, pair.Key);

                Reset(info);
                var codiciEsclusione = new List<string>();

                ApplyRegoleGenerali(info, facts, codiciEsclusione);
                ApplyRegoleReddito(info, codiciEsclusione, config);
                ApplyRegoleMerito(info, facts, codiciEsclusione, aaInizio, aaNumero);
                ApplyRegoleSpecificheBorsa(info, facts, codiciEsclusione, aaNumero);

                if (codiciEsclusione.Count > 0)
                {
                    info.EsitoBorsaCalcolato = EsitoEscluso;
                    info.CodiciMotivoEsitoBorsaCalcolato = string.Join(";", codiciEsclusione);
                    info.MotiviEsitoBorsaCalcolato = string.Join(" | ", codiciEsclusione.Select(GetMotivoEsclusione));
                    esclusi++;
                }
                else
                {
                    info.EsitoBorsaCalcolato = EsitoIdoneo;
                    idonei++;
                }

                info.CalcoloEsitoBorsaEseguito = true;
            }

            Logger.LogInfo(
                null,
                $"[Verifica.Module.{Name}] Regole applicate | students={context.Students.Count} | idonei={idonei} | esclusi={esclusi} | sogliaIsee={config.SogliaIsee.ToString(CultureInfo.InvariantCulture)} | sogliaIsp={config.SogliaIsp.ToString(CultureInfo.InvariantCulture)}");
        }

        private static EsitoBorsaFacts GetFacts(VerificaPipelineContext context, StudentKey key)
        {
            if (context.EsitoBorsaFactsByStudent.TryGetValue(key, out var facts) && facts != null)
                return facts;

            facts = new EsitoBorsaFacts();
            context.EsitoBorsaFactsByStudent[key] = facts;
            return facts;
        }

        private static void ApplyRegoleGenerali(StudenteInfo info, EsitoBorsaFacts facts, List<string> codiciEsclusione)
        {
            if (info == null)
                return;

            if (info.StatusCompilazione < StatusCompilazioneMinimoCompilazione)
            {
                AddMotivoEsclusione(codiciEsclusione, "GEN000");
                return;
            }

            if (info.StatusCompilazione < StatusCompilazioneMinimoTrasmessa)
                AddMotivoEsclusione(codiciEsclusione, "GEN001");

            if (facts?.ForzatureGenerali != null)
            {
                foreach (var code in facts.ForzatureGenerali.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    AddMotivoEsclusione(codiciEsclusione, $"GENF{code}");
            }
        }

        private static void ApplyRegoleReddito(StudenteInfo info, List<string> codiciEsclusione, EsitoBorsaRuleConfig config)
        {
            if (info == null)
                return;

            decimal isee = GetIseeRiferimento(info);
            decimal isp = GetIspRiferimento(info);
            bool rifugiato = info.InformazioniPersonali?.Rifugiato == true;

            if (isee <= 0m && !rifugiato)
            {
                AddMotivoEsclusione(codiciEsclusione, "RED011");
                return;
            }

            if (config.SogliaIsp > 0m && isp > config.SogliaIsp)
                AddMotivoEsclusione(codiciEsclusione, "RED012");

            if (config.SogliaIsee > 0m && isee > config.SogliaIsee)
                AddMotivoEsclusione(codiciEsclusione, "RED013");
        }

        private static void ApplyRegoleMerito(StudenteInfo info, EsitoBorsaFacts facts, List<string> codiciEsclusione, int aaInizio, int aaNumero)
        {
            if (info == null)
                return;

            var iscr = info.InformazioniIscrizione;
            if (iscr == null)
                return;

            bool invalido = info.InformazioniPersonali?.Disabile == true;
            bool richiedeMerito = RichiedeDatiMerito(iscr);
            bool consentiDerogaAnnoCorso = HasDerogaAnnoCorso(info, aaNumero);

            if (richiedeMerito && !HasDatiMeritoCalcolabili(iscr))
                AddMotivoEsclusione(codiciEsclusione, "MER001");

            if (!consentiDerogaAnnoCorso && aaInizio > 0 && !IsAnnoCorsoCongruente(iscr, aaInizio))
                AddMotivoEsclusione(codiciEsclusione, "MER072");

            if (HasCreditiDichiaratiIncongruenti(iscr))
                AddMotivoEsclusione(codiciEsclusione, "MER005");

            if (HasTirocinioSuperioreAiCrediti(iscr))
                AddMotivoEsclusione(codiciEsclusione, "MER092");

            if (!PassaRegolaCreditiSpecialisticaPrimoAnno(iscr))
                AddMotivoEsclusione(codiciEsclusione, "MER074");

            if (!HaCreditiMinimiPerBorsa(iscr, invalido, aaNumero))
                AddMotivoEsclusione(codiciEsclusione, "MER012");
        }

        private static void ApplyRegoleSpecificheBorsa(StudenteInfo info, EsitoBorsaFacts facts, List<string> codiciEsclusione, int aaNumero)
        {
            if (info == null)
                return;

            var iscr = info.InformazioniIscrizione;
            if (iscr == null)
                return;

            if (!IsAnnoCorsoAmmissibile(iscr, facts, info.InformazioniPersonali?.Disabile == true, aaNumero))
                AddMotivoEsclusione(codiciEsclusione, "BS001");
        }

        private static bool RichiedeDatiMerito(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return false;

            return iscr.AnnoCorso != 1;
        }

        private static bool HasDatiMeritoCalcolabili(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return false;

            if (!RichiedeDatiMerito(iscr))
                return true;

            return (iscr.NumeroCrediti ?? 0m) > 0m;
        }

        private static bool HasDerogaAnnoCorso(StudenteInfo info, int annoAccademico)
        {
            if (info?.InformazioniIscrizione == null)
                return false;

            var iscr = info.InformazioniIscrizione;
            if (annoAccademico >= 20242025 && (iscr.CreditiRiconosciutiDaRinuncia ?? 0m) > 0m)
                return true;

            if (annoAccademico >= 20252026 && iscr.HaRipetenzaCarrieraPregressa != 0)
                return true;

            return false;
        }

        private static bool PassaRegolaCreditiSpecialisticaPrimoAnno(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return true;

            if (iscr.TipoCorso != 5 || iscr.AnnoCorso != 1)
                return true;

            decimal riconosciuti = Math.Max(iscr.CreditiRiconosciuti ?? 0m, iscr.CreditiRiconosciutiDaRinuncia ?? 0m);
            if (riconosciuti <= 0m)
                return true;

            return !(riconosciuti > 110m && riconosciuti < 150m);
        }

        private static bool IsAnnoCorsoCongruente(InformazioniIscrizione iscr, int aaInizioCorrente)
        {
            if (iscr == null)
                return false;

            if (aaInizioCorrente <= 0)
                return true;

            if (!iscr.AnnoImmatricolazione.HasValue || iscr.AnnoImmatricolazione.Value <= 0)
                return true;

            int durataNormale = GetDurataNormaleCorso(iscr);
            if (durataNormale <= 0)
                return true;

            int aaInizioImmatricolazione = GetAnnoInizioDaAnnoAccademico(iscr.AnnoImmatricolazione.Value);
            if (aaInizioImmatricolazione <= 0)
                return true;

            int anniTrascorsi = aaInizioCorrente - aaInizioImmatricolazione;
            if (anniTrascorsi < 0)
                return false;

            int annoProgressivoReale = anniTrascorsi + 1;
            int annoProgressivoAtteso = GetAnnoProgressivoAtteso(iscr.AnnoCorso, durataNormale);

            if (annoProgressivoAtteso <= 0)
                return false;

            return annoProgressivoReale == annoProgressivoAtteso;
        }

        private static int GetAnnoProgressivoAtteso(int annoCorso, int durataNormale)
        {
            if (annoCorso > 0)
                return annoCorso;

            if (annoCorso < 0)
                return durataNormale + Math.Abs(annoCorso);

            return 0;
        }

        private static int GetAnnoInizioDaAnnoAccademico(int annoAccademico)
        {
            string valore = annoAccademico.ToString(CultureInfo.InvariantCulture);

            if (valore.Length >= 8)
                return int.TryParse(valore.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int aaInizioDa8) ? aaInizioDa8 : 0;

            if (valore.Length == 4)
                return annoAccademico;

            return 0;
        }

        private static bool IsAnnoCorsoAmmissibile(InformazioniIscrizione iscr, EsitoBorsaFacts facts, bool invalido, int annoAccademico)
        {
            if (iscr == null)
                return false;

            int annoCorso = iscr.AnnoCorso;
            if (annoCorso == 0)
                return false;

            if (annoCorso > 0)
                return true;

            string codOrd = (facts?.CodTipoOrdinamento ?? string.Empty).Trim();
            bool accademiaVo = annoAccademico > 20072008
                               && SediAccademiaVecchioOrdinamento.Contains((iscr.CodSedeStudi ?? string.Empty).Trim())
                               && string.Equals(codOrd, "1", StringComparison.OrdinalIgnoreCase);

            if (!invalido)
            {
                if (accademiaVo)
                    return annoCorso >= 0;

                return annoCorso >= -1;
            }

            if (accademiaVo)
                return annoCorso >= -2;

            int limiteFuoriCorso = string.Equals(codOrd, "3", StringComparison.OrdinalIgnoreCase) ? -2 : -3;
            return annoCorso >= limiteFuoriCorso;
        }

        private static bool HaCreditiMinimiPerBorsa(InformazioniIscrizione iscr, bool invalido, int annoAccademico)
        {
            if (iscr == null)
                return false;

            decimal creditiRichiesti = GetCreditiMinimiRichiesti(iscr, invalido, annoAccademico);
            if (creditiRichiesti <= 0m)
                return true;

            decimal creditiStudente = iscr.NumeroCrediti ?? 0m;
            if (creditiStudente >= creditiRichiesti)
                return true;

            decimal bonusUsabile = GetBonusUsabile(iscr, annoAccademico);
            return creditiStudente + bonusUsabile >= creditiRichiesti;
        }

        private static decimal GetBonusUsabile(InformazioniIscrizione iscr, int annoAccademico)
        {
            if (iscr == null)
                return 0m;

            bool bonusRichiesto = iscr.UtilizzoBonus != 0 || (iscr.CreditiUtilizzati ?? 0m) > 0m;
            if (!bonusRichiesto)
                return 0m;

            if ((annoAccademico >= 20242025 && (iscr.CreditiRiconosciutiDaRinuncia ?? 0m) > 0m)
                || (annoAccademico >= 20252026 && iscr.HaRipetenzaCarrieraPregressa != 0))
            {
                return 0m;
            }

            decimal? creditiRimanenti = iscr.CreditiRimanenti;
            decimal bonus;

            if (!creditiRimanenti.HasValue || creditiRimanenti.Value < 0m)
            {
                if (iscr.TipoCorso == 5)
                {
                    bonus = 12m;
                }
                else
                {
                    bonus = iscr.AnnoCorso switch
                    {
                        2 => 5m,
                        3 => 12m,
                        _ => 15m
                    };
                }
            }
            else
            {
                bonus = Math.Max(creditiRimanenti.Value, 0m);
                if (iscr.TipoCorso == 5 && bonus > 12m)
                    bonus = 12m;
            }

            return bonus;
        }

        private static int GetDurataNormaleCorso(InformazioniIscrizione iscr)
        {
            switch (iscr.TipoCorso)
            {
                case 3:
                    return 3;
                case 4:
                    return iscr.CorsoMedicina ? 6 : 5;
                case 5:
                    return 2;
                default:
                    return 0;
            }
        }

        private static decimal GetCreditiMinimiRichiesti(InformazioniIscrizione iscr, bool invalido, int annoAccademico)
        {
            if (annoAccademico < 20082009)
                return GetCreditiMinimiLegacy(iscr, invalido);

            decimal value = GetCreditiMinimi0809(iscr, invalido, annoAccademico);
            value = ApplySpecialisticaPrimoFuoriCorsoRidotta(value, iscr, invalido, annoAccademico);
            value = ApplyCommissioneSpecialistica2009(value, iscr, invalido, annoAccademico);
            value = ApplyFarmaciaCtfEdileArchRiduzioni(value, iscr, invalido, annoAccademico);
            return value;
        }

        private static decimal GetCreditiMinimiLegacy(InformazioniIscrizione iscr, bool invalido)
        {
            switch (iscr.TipoCorso)
            {
                case 3:
                    return GetCreditiMinimiTriennaleLegacy(iscr.AnnoCorso, invalido);
                case 4:
                    return iscr.CorsoMedicina
                        ? GetCreditiMinimiMedicinaLegacy(iscr.AnnoCorso, invalido)
                        : GetCreditiMinimiCicloUnicoCinqueAnniLegacy(iscr.AnnoCorso, invalido);
                case 5:
                    return GetCreditiMinimiBiennaleLegacy(iscr.AnnoCorso, invalido);
                default:
                    return 0m;
            }
        }

        private static decimal GetCreditiMinimi0809(InformazioniIscrizione iscr, bool invalido, int annoAccademico)
        {
            int anno = iscr.AnnoCorso;
            bool enteBoost38 = (annoAccademico >= 20092010 && IsEnte(iscr, "08"))
                               || (annoAccademico >= 20202021 && IsEnte(iscr, "10"))
                               || (annoAccademico >= 20242025 && IsEnte(iscr, "09"))
                               || (annoAccademico >= 20252026 && IsEnte(iscr, "07"));

            switch (anno)
            {
                case 1:
                    return 0m;

                case 2:
                    if (iscr.TipoCorso == 5)
                        return invalido ? 18m : (enteBoost38 ? 38m : 30m);
                    return invalido ? 15m : (enteBoost38 ? 31m : 25m);

                case 3:
                    if (invalido)
                        return 56m;
                    if (string.Equals((iscr.CodCorsoLaurea ?? string.Empty).Trim(), "IMN", StringComparison.OrdinalIgnoreCase) && annoAccademico == 20102011)
                        return 75m;
                    return enteBoost38 ? 100m : 80m;

                case 4:
                    return invalido ? 94m : (annoAccademico >= 20252026 && IsEnte(iscr, "07") ? 160m : (enteBoost38 ? 168m : 135m));

                case 5:
                    return invalido ? 133m : (annoAccademico >= 20252026 && IsEnte(iscr, "07") ? 210m : (enteBoost38 ? 230m : 190m));

                case 6:
                    return invalido ? 171m : (annoAccademico >= 20252026 && IsEnte(iscr, "07") ? 265m : 245m);

                case -1:
                    switch (iscr.TipoCorso)
                    {
                        case 3:
                            return invalido ? 94m : (enteBoost38 ? 155m : 135m);
                        case 4:
                            if (GetDurataNormaleCorso(iscr) > 5)
                                return invalido ? 222m : (annoAccademico >= 20252026 && IsEnte(iscr, "07") ? 315m : 300m);
                            return invalido ? 171m : (annoAccademico >= 20252026 && IsEnte(iscr, "07") ? 315m : (enteBoost38 ? 265m : 245m));
                        case 5:
                            return invalido ? 56m : (enteBoost38 ? 90m : 80m);
                        default:
                            return 0m;
                    }

                case -2:
                    if (!invalido)
                        return 0m;
                    return iscr.TipoCorso switch
                    {
                        3 => 133m,
                        5 => 94m,
                        4 when GetDurataNormaleCorso(iscr) > 5 => 228m,
                        4 => 222m,
                        _ => 0m
                    };

                case -3:
                    return invalido && iscr.TipoCorso == 5 ? 94m : 0m;

                default:
                    return 0m;
            }
        }

        private static decimal ApplyFarmaciaCtfEdileArchRiduzioni(decimal currentValue, InformazioniIscrizione iscr, bool invalido, int annoAccademico)
        {
            if (annoAccademico >= 20232024)
                return currentValue;

            string corso = (iscr.CodCorsoLaurea ?? string.Empty).Trim();
            if (CorsiFarmaciaRiduzione.Contains(corso))
            {
                return iscr.AnnoCorso switch
                {
                    2 => invalido ? 12m : 20m,
                    3 => invalido ? 42m : 60m,
                    4 => invalido ? 75m : 108m,
                    5 => invalido ? 107m : 154m,
                    -1 => invalido ? 171m : 245m,
                    -2 => invalido ? 222m : currentValue,
                    _ => currentValue
                };
            }

            if (CorsiCtfRiduzione.Contains(corso))
            {
                return iscr.AnnoCorso switch
                {
                    2 => invalido ? 10m : 17m,
                    3 => invalido ? 42m : 60m,
                    4 => invalido ? 74m : 107m,
                    5 => invalido ? 109m : 157m,
                    -1 => invalido ? 171m : 245m,
                    -2 => invalido ? 222m : currentValue,
                    _ => currentValue
                };
            }

            if (CorsiIngEdileArchRiduzione.Contains(corso))
            {
                return iscr.AnnoCorso switch
                {
                    2 => invalido ? 12m : 20m,
                    3 => invalido ? 48m : 68m,
                    4 => invalido ? 86m : 124m,
                    5 => invalido ? 119m : 170m,
                    -1 => invalido ? 171m : 245m,
                    -2 => invalido ? 222m : currentValue,
                    _ => currentValue
                };
            }

            return currentValue;
        }

        private static decimal ApplySpecialisticaPrimoFuoriCorsoRidotta(decimal currentValue, InformazioniIscrizione iscr, bool invalido, int annoAccademico)
        {
            if (iscr.TipoCorso != 5 || (iscr.AnnoCorso != -1 && iscr.AnnoCorso != -2))
                return currentValue;

            string corso = (iscr.CodCorsoLaurea ?? string.Empty).Trim();
            if (annoAccademico < 20232024)
            {
                if (!CorsiSpecialistica1FcRidottaPre2023.Contains(corso))
                    return currentValue;

                return corso.ToUpperInvariant() switch
                {
                    "NXT" or "30060" => iscr.AnnoCorso == -1 ? (invalido ? 42m : 63m) : (invalido ? 67m : currentValue),
                    "NXL" or "30055" => iscr.AnnoCorso == -1 ? (invalido ? 41m : 58m) : (invalido ? 65m : currentValue),
                    "NXR" or "NXS" or "30058" => iscr.AnnoCorso == -1 ? (invalido ? 34m : 48m) : (invalido ? 54m : currentValue),
                    _ => currentValue
                };
            }

            if (!CorsiSpecialistica1FcRidottaDal2023.Contains(corso))
                return currentValue;

            return iscr.AnnoCorso == -1 ? (invalido ? 56m : 63m) : (invalido ? 94m : currentValue);
        }

        private static decimal ApplyCommissioneSpecialistica2009(decimal currentValue, InformazioniIscrizione iscr, bool invalido, int annoAccademico)
        {
            if (annoAccademico != 20092010 || iscr.TipoCorso != 5 || (iscr.AnnoCorso != -1 && iscr.AnnoCorso != -2))
                return currentValue;

            string corso = (iscr.CodCorsoLaurea ?? string.Empty).Trim();
            if (!CorsiCommissioneSpecialistica2009.Contains(corso))
                return currentValue;

            return corso.ToUpperInvariant() switch
            {
                "12382" => iscr.AnnoCorso == -1 ? (invalido ? 34m : 48m) : (invalido ? 54m : currentValue),
                "NXS" => iscr.AnnoCorso == -1 ? (invalido ? 34m : 48m) : (invalido ? 54m : currentValue),
                "NJH" => iscr.AnnoCorso == -1 ? (invalido ? 42m : 60m) : (invalido ? 67m : currentValue),
                "6WN" => iscr.AnnoCorso == -1 ? (invalido ? 39m : 56m) : (invalido ? 62m : currentValue),
                _ => currentValue
            };
        }

        private static bool HasCreditiDichiaratiIncongruenti(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return false;

            decimal crediti = iscr.NumeroCrediti ?? 0m;
            if (crediti <= 0m)
                return false;

            return iscr.TipoCorso switch
            {
                5 => crediti > 120m,
                3 => crediti >= 180m,
                4 when GetDurataNormaleCorso(iscr) >= 6 => crediti >= 360m,
                4 => crediti >= 300m,
                _ => false
            };
        }

        private static bool HasTirocinioSuperioreAiCrediti(InformazioniIscrizione iscr)
        {
            if (iscr == null)
                return false;

            decimal tirocinio = iscr.CreditiTirocinio ?? 0m;
            decimal crediti = iscr.NumeroCrediti ?? 0m;
            return tirocinio > 0m && crediti > 0m && tirocinio > crediti;
        }

        private static bool IsEnte(InformazioniIscrizione iscr, string codEnte)
            => string.Equals((iscr?.CodEnte ?? string.Empty).Trim(), (codEnte ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        private static decimal GetCreditiMinimiTriennaleLegacy(int annoCorso, bool invalido)
        {
            switch (annoCorso)
            {
                case 1: return 0m;
                case 2: return 25m;
                case 3: return 80m;
                case -1: return invalido ? 94m : 135m;
                case -2: return invalido ? 171m : 135m;
                default: return annoCorso < -2 ? 171m : 135m;
            }
        }

        private static decimal GetCreditiMinimiBiennaleLegacy(int annoCorso, bool invalido)
        {
            switch (annoCorso)
            {
                case 1: return 0m;
                case 2: return 30m;
                case -1: return invalido ? 56m : 80m;
                case -2: return invalido ? 94m : 80m;
                default: return annoCorso < -2 ? 94m : 80m;
            }
        }

        private static decimal GetCreditiMinimiCicloUnicoCinqueAnniLegacy(int annoCorso, bool invalido)
        {
            switch (annoCorso)
            {
                case 1: return 0m;
                case 2: return 25m;
                case 3: return 80m;
                case 4: return 135m;
                case 5: return 190m;
                case -1: return invalido ? 171m : 245m;
                case -2: return invalido ? 222m : 245m;
                default: return annoCorso < -2 ? 222m : 245m;
            }
        }

        private static decimal GetCreditiMinimiMedicinaLegacy(int annoCorso, bool invalido)
        {
            switch (annoCorso)
            {
                case 1: return 0m;
                case 2: return 25m;
                case 3: return 80m;
                case 4: return 135m;
                case 5: return 190m;
                case 6: return 245m;
                case -1: return invalido ? 171m : 300m;
                case -2: return invalido ? 222m : 315m;
                default: return annoCorso < -2 ? 222m : 315m;
            }
        }

        private static decimal GetIseeRiferimento(StudenteInfo info)
        {
            if (TryReadDecimal(info?.InformazioniEconomiche?.Calcolate?.ISEEDSU, out var ordinario) && ordinario > 0m)
                return ordinario;

            if (TryReadDecimal(info?.InformazioniEconomiche?.Attuali?.ISEEDSU, out var attuale) && attuale > 0m)
                return attuale;

            return 0m;
        }

        private static decimal GetIspRiferimento(StudenteInfo info)
        {
            if (TryReadDecimal(info?.InformazioniEconomiche?.Calcolate?.ISPDSU, out var ordinario) && ordinario > 0m)
                return ordinario;

            if (TryReadDecimal(info?.InformazioniEconomiche?.Attuali?.ISPDSU, out var attuale) && attuale > 0m)
                return attuale;

            return 0m;
        }

        private static bool TryReadDecimal(object? value, out decimal result)
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

        private static int ParseAnnoAccademicoInizio(string aa)
        {
            if (string.IsNullOrWhiteSpace(aa) || aa.Length < 4)
                return 0;

            return int.TryParse(aa.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : 0;
        }

        private static int ParseAnnoAccademicoAsNumber(string aa)
        {
            if (string.IsNullOrWhiteSpace(aa))
                return 0;

            return int.TryParse(aa.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : 0;
        }

        private static void AddMotivoEsclusione(List<string> codiciEsclusione, string codice)
        {
            if (!codiciEsclusione.Contains(codice, StringComparer.OrdinalIgnoreCase))
                codiciEsclusione.Add(codice);
        }

        private static string GetMotivoEsclusione(string codice)
        {
            if (MotiviEsclusione.TryGetValue(codice, out var motivo))
                return motivo;

            if (!string.IsNullOrWhiteSpace(codice) && codice.StartsWith("GENF", StringComparison.OrdinalIgnoreCase))
                return $"Forzatura generale attiva {codice.Substring(4)}";

            return codice;
        }

        private static void Reset(StudenteInfo info)
        {
            info.EsitoBorsaCalcolato = EsitoIdoneo;
            info.CodiciMotivoEsitoBorsaCalcolato = string.Empty;
            info.MotiviEsitoBorsaCalcolato = string.Empty;
            info.CalcoloEsitoBorsaEseguito = false;
        }

        private static EsitoBorsaRuleConfig LoadRuleConfig(VerificaPipelineContext context)
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

            string columnsSql = @"
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

        private sealed class EsitoBorsaRuleConfig
        {
            public decimal SogliaIsee { get; set; }
            public decimal SogliaIsp { get; set; }
        }
    }
}
