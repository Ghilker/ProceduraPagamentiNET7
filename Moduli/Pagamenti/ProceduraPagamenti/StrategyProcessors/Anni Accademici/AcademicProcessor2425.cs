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

        public void AdjustPendolarePayment(StudentePagam studente, ref double importoDaPagare, ref double importoMassimo, ConcurrentBag<(string CodFiscale, string Motivazione)> studentiPagatiComePendolari)
        {
            bool hasDomicilio = studente.domicilioCheck;
            bool isMoreThanHalfAbroad = studente.numeroComponentiNucleoFamiliareEstero >= (studente.numeroComponentiNucleoFamiliare / 2.0);

            // Guard clause: if status is not 'B' or there's a forced 'B', exit early
            if (studente.statusSede != "B" || studente.forzaturaStatusSede == "B")
                return;

            // Guard clause: if any of these conditions is true, exit early
            if (studente.rifugiato ||
                studente.esitoPA != 0 ||
                isMoreThanHalfAbroad ||
                studente.tipoCorso == 6)
                return;

            // Guard clause: if the student DOES have domicile and everything is valid, exit early
            // (Because that means we do NOT need to apply pendolare logic)
            bool isDomicilioValidOrNotNeeded = hasDomicilio && studente.contrattoValido && !(studente.domicilio?.prorogatoLocazione == true && !studente.prorogaValido);

            if (isDomicilioValidOrNotNeeded)
                return;

            // --- At this point, we know we must apply the pendolare reduction ---
            importoDaPagare = importoDaPagare / 3 * 2;
            importoMassimo = importoMassimo / 3 * 2;

            // Build the message for reasons why they were paid as pendolari
            string messaggio = string.Empty;

            if (studente.domicilio == null)
            {
                messaggio += "#Nessun domicilio trovato";
            }
            if (!hasDomicilio)
            {
                messaggio += "#Durata contratto minore dieci mesi";
            }
            if (!studente.contrattoValido && studente.domicilio != null)
            {
                messaggio += $"#Serie contratto non valida: {studente.domicilio.codiceSerieLocazione}";
            }
            if (studente.domicilio?.prorogatoLocazione == true && !studente.prorogaValido)
            {
                messaggio +=
                    $"#Serie proroga non valida: Contratto {studente.domicilio.codiceSerieLocazione} " +
                    $"- Proroga {studente.domicilio.codiceSerieProrogaLocazione}";
            }

            // Record the reason and mark the student as 'paid as pendolare'
            studentiPagatiComePendolari.Add((studente.codFiscale, messaggio));
            studente.SetPagatoPendolare(true);
        }

    }
}
