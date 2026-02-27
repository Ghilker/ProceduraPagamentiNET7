using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.PagamentiProcessor
{
    public class AcademicProcessor2122 : IAcademicYearProcessor
    {
        string IAcademicYearProcessor.GetProvvedimentiQuery(string selectedAA, string tipoBeneficio)
        {
            return $@"
                select distinct specifiche_impegni.Cod_fiscale, Importo_assegnato, bs.Imp_beneficio
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

        public void AdjustPendolarePayment(StudentePagamenti studente, ref double importoDaPagare, ref double importoMassimo, ConcurrentBag<(string CodFiscale, string Motivazione)> studentiPagatiComePendolari, double sogliaISEE, double importoPendolare) { }
    }
}
