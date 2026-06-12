using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.PagamentiProcessor
{
    public class AcademicProcessor2425 : IAcademicYearProcessor
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

            double iseeStudente = studente.InformazioniPagamento.ValoreISEE;

            double halfThreshold = sogliaISEE / 2.0;
            double twoThirdsThreshold = sogliaISEE * 2.0 / 3.0;

            double nuovoImportoMassimoPendolare;

            if (iseeStudente <= halfThreshold)
            {
                nuovoImportoMassimoPendolare = importoPendolare * 1.15;
            }
            else if (iseeStudente < twoThirdsThreshold)
            {
                nuovoImportoMassimoPendolare = importoPendolare;
            }
            else
            {
                double iseeClamped = Math.Min(iseeStudente, sogliaISEE);

                if (twoThirdsThreshold >= sogliaISEE)
                {
                    nuovoImportoMassimoPendolare = importoPendolare * 0.5;
                }
                else
                {
                    double t = (iseeClamped - twoThirdsThreshold) / (sogliaISEE - twoThirdsThreshold);
                    double fattore = 1.0 - 0.5 * t;
                    nuovoImportoMassimoPendolare = importoPendolare * fattore;
                }
            }

            bool studenteFuoriCorso = studente.InformazioniIscrizione.AnnoCorso == -1 && !studente.InformazioniPersonali.Disabile;
            bool studenteDisabileFuoriCorso = studente.InformazioniIscrizione.AnnoCorso == -2 && studente.InformazioniPersonali.Disabile;

            double importoMensa = 600;
            if (studenteFuoriCorso || studenteDisabileFuoriCorso)
                importoMensa = 300;

            if (!studente.InformazioniPagamento.ConcessaMonetizzazioneMensa)
                importoMensa = 0;

            nuovoImportoMassimoPendolare += importoMensa;

            importoMassimo = nuovoImportoMassimoPendolare;
            importoDaPagare = nuovoImportoMassimoPendolare;

            string codPag = (categoriaPagamento ?? "").Trim().ToUpperInvariant();
            string saldoInfo = codPag == "SA"
                ? $"; saldo SA: requisito fuori sede certo={statusSede.FuoriSedeCertoPerSaldo}"
                : string.Empty;

            string messaggio = $"CodTipoPagamento={codPag}; StatusSede attuale={studente.InformazioniSede.StatusSede}; StatusSede calcolato={statusSede.SuggestedStatus}; {statusSede.Reason}{saldoInfo}";

            studentiPagatiComePendolari.Add((studente.InformazioniPersonali.CodFiscale, messaggio));
            studente.SetPagatoPendolare(true);
        }
    }
}
