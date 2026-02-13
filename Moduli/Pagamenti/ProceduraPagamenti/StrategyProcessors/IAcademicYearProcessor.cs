using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.PagamentiProcessor
{
    public interface IAcademicYearProcessor
    {
        public abstract string GetProvvedimentiQuery(string selectedAA, string tipoBeneficio);
        public abstract HashSet<string> ProcessProvvedimentiQuery(SqlDataReader reader);
        public abstract void AdjustPendolarePayment(
            StudentePagamenti studente,
            ref double importoDaPagare,
            ref double importoMassimo,
            ConcurrentBag<(string CodFiscale, string Motivazione)> studentiPagatiComePendolari,
            double limiteISEE,
            double importoPendolare
        );
    }
}
