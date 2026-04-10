using System;
using System.Collections.Generic;
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

        private static readonly Dictionary<string, string> MotiviEsclusione = new()
        {
            { "000", "Domanda non completa" },
            { "001", "Domanda completa ma non trasmessa" },
            { "010", "Anno di corso non congruente con l'anno di prima immatricolazione" },
            { "011", "Anno di corso oltre il massimo consentito" },
            { "012", "Crediti insufficienti per la borsa" }
        };

        public string Name => "EsitoBorsa";

        public void Calculate(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            int esclusi = 0;
            int aaInizio = ParseAnnoAccademicoInizio(context.AnnoAccademico);

            foreach (var info in context.Students.Values)
            {
                Reset(info);

                var codiciEsclusione = new List<string>();

                if (info.StatusCompilazione < StatusCompilazioneMinimoCompilazione)
                {
                    AddMotivoEsclusione(codiciEsclusione, "000");
                }
                else if (info.StatusCompilazione < StatusCompilazioneMinimoTrasmessa)
                {
                    AddMotivoEsclusione(codiciEsclusione, "001");
                }

                CalcoloMerito(info, codiciEsclusione, aaInizio);

                if (codiciEsclusione.Count > 0)
                {
                    info.EsitoBorsaCalcolato = EsitoEscluso;
                    info.CodiciMotivoEsitoBorsaCalcolato = string.Join(";", codiciEsclusione);
                    info.MotiviEsitoBorsaCalcolato = string.Join(" | ", codiciEsclusione.Select(c => MotiviEsclusione[c]));
                    esclusi++;
                }
                else
                {
                    info.EsitoBorsaCalcolato = EsitoIdoneo;
                }

                info.CalcoloEsitoBorsaEseguito = true;
            }

            Logger.LogInfo(
                null,
                $"[Verifica.Module.{Name}] Regole applicate | students={context.Students.Count} | esclusi={esclusi}");
        }

        private void CalcoloMerito(StudenteInfo info, List<string> codiciEsclusione, int aaInizio)
        {
            if (info == null)
                return;

            var iscr = info.InformazioniIscrizione;
            if (iscr == null)
                return;

            if (aaInizio > 0 && !IsAnnoCorsoCongruente(iscr, aaInizio))
                AddMotivoEsclusione(codiciEsclusione, "010");

            if (!IsAnnoCorsoAmmissibile(iscr, info.InformazioniPersonali.Disabile))
                AddMotivoEsclusione(codiciEsclusione, "011");

            if (!HaCreditiMinimiPerBorsa(iscr, info.InformazioniPersonali.Disabile))
                AddMotivoEsclusione(codiciEsclusione, "012");
        }

        private static bool IsAnnoCorsoCongruente(InformazioniIscrizione iscr, int aaInizioCorrente)
        {
            if (iscr == null)
                return false;

            if (aaInizioCorrente <= 0)
                return true;

            if (iscr.AnnoImmatricolazione == null || iscr.AnnoImmatricolazione <= 0)
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
            string valore = annoAccademico.ToString();

            if (valore.Length >= 8)
            {
                return int.TryParse(valore.Substring(0, 4), out int aaInizioDa8)
                    ? aaInizioDa8
                    : 0;
            }

            if (valore.Length == 4)
                return annoAccademico;

            return 0;
        }

        private static bool IsAnnoCorsoAmmissibile(InformazioniIscrizione iscr, bool invalido)
        {
            if (iscr.AnnoCorso <= 0)
                return false;

            int durataNormale = GetDurataNormaleCorso(iscr);
            if (durataNormale <= 0)
                return false;

            int maxAnnoAmmissibile = durataNormale + (invalido ? 2 : 1);
            return iscr.AnnoCorso <= maxAnnoAmmissibile;
        }

        private static bool HaCreditiMinimiPerBorsa(InformazioniIscrizione iscr, bool invalido)
        {
            decimal creditiRichiesti = GetCreditiMinimiRichiesti(iscr, invalido);
            decimal creditiStudente = iscr.NumeroCrediti ?? 0m;

            return creditiStudente >= creditiRichiesti;
        }

        private static int GetDurataNormaleCorso(InformazioniIscrizione iscr)
        {
            switch (iscr.TipoCorso)
            {
                case 3:
                    return 3; // triennale

                case 4:
                    return iscr.CorsoMedicina ? 6 : 5; // ciclo unico

                case 5:
                    return 2; // biennale

                default:
                    return 0;
            }
        }

        private static decimal GetCreditiMinimiRichiesti(InformazioniIscrizione iscr, bool invalido)
        {
            switch (iscr.TipoCorso)
            {
                case 3:
                    return GetCreditiMinimiTriennale(iscr.AnnoCorso, invalido);

                case 4:
                    return iscr.CorsoMedicina
                        ? GetCreditiMinimiMedicina(iscr.AnnoCorso, invalido)
                        : GetCreditiMinimiCicloUnicoCinqueAnni(iscr.AnnoCorso, invalido);

                case 5:
                    return GetCreditiMinimiBiennale(iscr.AnnoCorso, invalido);

                default:
                    return 0m;
            }
        }

        private static decimal GetCreditiMinimiTriennale(int annoCorso, bool invalido)
        {
            switch (annoCorso)
            {
                case 1: return 0m;
                case 2: return 25m;
                case 3: return 80m;
                default: return 135m; // primo fuori corso, e per ora stesso valore anche per secondo fuori corso invalidi
            }
        }

        private static decimal GetCreditiMinimiBiennale(int annoCorso, bool invalido)
        {
            switch (annoCorso)
            {
                case 1: return 0m;
                case 2: return 30m;
                default: return 80m; // primo fuori corso, e per ora stesso valore anche per secondo fuori corso invalidi
            }
        }

        private static decimal GetCreditiMinimiCicloUnicoCinqueAnni(int annoCorso, bool invalido)
        {
            switch (annoCorso)
            {
                case 1: return 0m;
                case 2: return 25m;
                case 3: return 80m;
                case 4: return 135m;
                case 5: return 190m;
                default: return 245m; // primo fuori corso, e per ora stesso valore anche per secondo fuori corso invalidi
            }
        }

        private static decimal GetCreditiMinimiMedicina(int annoCorso, bool invalido)
        {
            switch (annoCorso)
            {
                case 1: return 0m;
                case 2: return 25m;
                case 3: return 80m;
                case 4: return 135m;
                case 5: return 190m;
                case 6: return 245m;
                default: return 300m; // primo fuori corso, e per ora stesso valore anche per secondo fuori corso invalidi
            }
        }

        private static int ParseAnnoAccademicoInizio(string aa)
        {
            if (string.IsNullOrWhiteSpace(aa) || aa.Length < 4)
                return 0;

            return int.TryParse(aa.Substring(0, 4), out int result) ? result : 0;
        }

        private static void AddMotivoEsclusione(List<string> codiciEsclusione, string codice)
        {
            if (!codiciEsclusione.Contains(codice))
                codiciEsclusione.Add(codice);
        }

        private static void Reset(StudenteInfo info)
        {
            info.EsitoBorsaCalcolato = EsitoIdoneo;
            info.CodiciMotivoEsitoBorsaCalcolato = string.Empty;
            info.MotiviEsitoBorsaCalcolato = string.Empty;
            info.CalcoloEsitoBorsaEseguito = false;
        }
    }
}