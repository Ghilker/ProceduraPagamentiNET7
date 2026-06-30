using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.PagamentiProcessor
{
    public class AcademicProcessor2526 : IAcademicYearProcessor
    {
        string IAcademicYearProcessor.GetProvvedimentiQuery(string selectedAA, string tipoBeneficio)
        {
            return $@"
                select distinct specifiche_impegni.Cod_fiscale, Importo_assegnato, bs.Imp_beneficio, monetizzazione_concessa, COALESCE(importo_servizio_mensa,0) as importo_servizio_mensa
                from specifiche_impegni 
                inner join vEsiti_concorsi bs on specifiche_impegni.Anno_accademico = bs.Anno_accademico and specifiche_impegni.Num_domanda = bs.Num_domanda and specifiche_impegni.Cod_beneficio = bs.Cod_beneficio
                inner join #CFEstrazione cfe on specifiche_impegni.cod_fiscale = cfe.cod_fiscale
                where bs.Anno_accademico = '{selectedAA}' and specifiche_impegni.Cod_beneficio = '{tipoBeneficio}' and data_fine_validita is null and Cod_tipo_esito = 2
                order by Cod_fiscale";
        }
        public HashSet<string> ProcessProvvedimentiQuery(SqlDataReader reader)
        {
            HashSet<string> listaStudentiDaMantenere = new();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                double importoAttuale = Utilities.SafeGetDouble(reader, "Imp_beneficio");
                double importoAssegnato = Utilities.SafeGetDouble(reader, "Importo_assegnato");

                if (importoAssegnato == importoAttuale)
                {
                    listaStudentiDaMantenere.Add(codFiscale);
                }
            }
            return listaStudentiDaMantenere;
        }

        public void AdjustPendolarePayment(
            StudentePagamenti studente,
            ref double importoDaPagare,
            ref double importoMassimo,
            ConcurrentBag<(string CodFiscale, string Motivazione)> studentiPagatiComePendolari,
            double sogliaISEE,
            double importoPendolare,
            string? categoriaPagamento = null,
            string? annoAccademico = null,
            HashSet<(string ComuneA, string ComuneB)>? comuniEquiparatiStatusSede = null,
            DateTime? referenceDate = null)
        {
            bool pagaComePendolare;
            ProcedureNet7.ControlloStatusSede.StatusSedeResult statusSede;

            if (!string.IsNullOrWhiteSpace(annoAccademico))
            {
                pagaComePendolare = ProcedureNet7.ControlloStatusSede.DevePagareComePendolarePerPagamento(
                    studente,
                    annoAccademico,
                    categoriaPagamento,
                    comuniEquiparatiStatusSede,
                    (referenceDate ?? DateTime.Today).Date,
                    out statusSede);
            }
            else
            {
                pagaComePendolare = ProcedureNet7.ControlloStatusSede.DevePagareComePendolareDaStatusCalcolato(
                    studente,
                    out statusSede);
            }

            studente.SetDomicilioCheck(statusSede.DomicilioValido);

            if (!pagaComePendolare)
                return;

            decimal importoBase = Convert.ToDecimal(importoPendolare);
            decimal isee = Convert.ToDecimal(studente.InformazioniPagamento.ValoreISEE);
            decimal soglia = Convert.ToDecimal(sogliaISEE);

            if (importoBase <= 0m)
                return;

            decimal valoreFinale = ApplyIseeRule(importoBase, isee, soglia);

            if (IsDonnaStem(studente))
                valoreFinale += RoundMoney(importoBase * 0.20m);

            bool riduzioneMeta = DeveRidurreAMetaPerFuoriCorso(studente);

            if (riduzioneMeta)
                valoreFinale = RoundMoney(valoreFinale / 2m);

            if (studente.InformazioniPagamento.ConcessaMonetizzazioneMensa)
                valoreFinale += riduzioneMeta ? 300m : 600m;

            valoreFinale = RoundMoney(valoreFinale);

            importoMassimo = (double)valoreFinale;
            importoDaPagare = (double)valoreFinale;

            string codPag = (categoriaPagamento ?? "").Trim().ToUpperInvariant();
            string saldoInfo = codPag == "SA"
                ? $"; saldo SA: requisito fuori sede certo={statusSede.FuoriSedeCertoPerSaldo}"
                : string.Empty;

            string messaggio =
                $"CodTipoPagamento={codPag}; " +
                $"StatusSede attuale={studente.InformazioniSede.StatusSede}; " +
                $"StatusSede calcolato={statusSede.SuggestedStatus}; " +
                $"{statusSede.Reason}; " +
                $"Importo base={RoundMoney(importoBase)}; " +
                $"ISEE={isee}; " +
                $"Importo finale={valoreFinale}" +
                saldoInfo;

            studentiPagatiComePendolari.Add((
                studente.InformazioniPersonali.CodFiscale,
                messaggio));

            studente.SetPagatoPendolare(true);
        }

        private static decimal ApplyIseeRule(
    decimal importoBase,
    decimal isee,
    decimal sogliaIsee)
        {
            if (importoBase <= 0m || sogliaIsee <= 0m)
                return RoundMoney(importoBase);

            decimal metaSoglia = sogliaIsee / 2m;
            decimal dueTerziSoglia = sogliaIsee * 2m / 3m;

            // Deve essere < e non <=.
            if (isee >= 0m && isee < metaSoglia)
                return RoundMoney(importoBase * 1.15m);

            if (isee >= sogliaIsee)
                return RoundMoney(importoBase * 0.50m);

            if (isee > dueTerziSoglia)
            {
                decimal ampiezza = sogliaIsee - dueTerziSoglia;

                if (ampiezza <= 0m)
                    return RoundMoney(importoBase * 0.50m);

                decimal progresso = (isee - dueTerziSoglia) / ampiezza;
                decimal coefficiente = 1m - (0.50m * progresso);

                return RoundMoney(importoBase * coefficiente);
            }

            return RoundMoney(importoBase);
        }

        private static bool IsDonnaStem(StudentePagamenti studente)
        {
            bool donna = string.Equals(
                (studente.InformazioniPersonali?.Sesso ?? string.Empty).Trim(),
                "F",
                StringComparison.OrdinalIgnoreCase);

            bool stem = studente.InformazioniIscrizione?.CorsoStem == true;

            return donna && stem;
        }

        private static bool DeveRidurreAMetaPerFuoriCorso(StudentePagamenti studente)
        {
            int annoCorso = studente.InformazioniIscrizione?.AnnoCorso ?? 0;
            bool disabile = studente.InformazioniPersonali?.Disabile == true;

            return (annoCorso == -1 && !disabile)
                || (annoCorso == -2 && disabile);
        }

        private static decimal RoundMoney(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
