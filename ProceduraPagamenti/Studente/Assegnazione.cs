using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class Assegnazione
    {
        public string codPensionato { get; private set; }
        public string codStanza { get; private set; }
        public DateTime dataDecorrenza { get; private set; }
        public DateTime dataFineAssegnazione { get; private set; }
        public string codFineAssegnazione { get; private set; }
        public string codTipoStanza { get; private set; }
        public double costoTotale { get; private set; }
        public double costoMensile { get; private set; }
        public AssegnazioneDataCheck statoCorrettezzaAssegnazione { get; private set; }

        public Assegnazione()
        {
            codPensionato = string.Empty;
            codStanza = string.Empty;
            codFineAssegnazione = string.Empty;
            codTipoStanza = string.Empty;
        }

        public AssegnazioneDataCheck SetAssegnazione(
                string codPensionato,
                string codStanza,
                DateTime dataDecorrenza,
                DateTime dataFineAssegnazione,
                string codFineAssegnazione,
                string codTipoStanza,
                double costoMensile,
                DateTime minDate,
                DateTime maxDate,
                List<Assegnazione> assegnazioni,
                bool fuoriCorso
            )
        {
            this.codPensionato = codPensionato;
            this.codStanza = codStanza;
            this.dataDecorrenza = dataDecorrenza;
            this.dataFineAssegnazione = dataFineAssegnazione;
            this.codFineAssegnazione = codFineAssegnazione;
            this.codTipoStanza = codTipoStanza;
            this.costoMensile = costoMensile;

            statoCorrettezzaAssegnazione = AssegnazioneDataCheck.Corretto;

            costoTotale = CalculateTotalDailyCost(dataDecorrenza, dataFineAssegnazione, costoMensile, minDate, maxDate, assegnazioni, fuoriCorso);

            return statoCorrettezzaAssegnazione;
        }

        public double CalculateTotalDailyCost(DateTime startDate, DateTime endDate, double monthlyCost, DateTime minDate, DateTime maxDate, List<Assegnazione> assegnazioni, bool fuoriCorso)
        {
            if (endDate == startDate)
            {
                statoCorrettezzaAssegnazione = AssegnazioneDataCheck.DataUguale;
                return 0;
            }
            if (startDate < minDate)
            {
                startDate = minDate;
                statoCorrettezzaAssegnazione = AssegnazioneDataCheck.DataDecorrenzaMinoreDiMin;
            }

            if (endDate < startDate)
            {
                DateTime tempTime = startDate;
                startDate = endDate > minDate ? endDate : minDate;
                endDate = tempTime;
                statoCorrettezzaAssegnazione = AssegnazioneDataCheck.Incorretto;
            }
            if (endDate > maxDate)
            {
                endDate = maxDate;
                statoCorrettezzaAssegnazione = AssegnazioneDataCheck.DataFineAssMaggioreMax;
            }

            int currentAssegnazioneDays = (endDate - startDate).Days + 1;

            if (assegnazioni != null && fuoriCorso)
            {
                int previousAssegnazioniDays = 0;
                foreach (Assegnazione assegnazione in assegnazioni)
                {
                    int assegnazioneDays = (assegnazione.dataFineAssegnazione - assegnazione.dataDecorrenza).Days + 1;
                    previousAssegnazioniDays += assegnazioneDays;
                }

                if (previousAssegnazioniDays + currentAssegnazioneDays > 165)
                {
                    currentAssegnazioneDays = 165 - previousAssegnazioniDays;
                }
            }
            else if (fuoriCorso)
            {
                if (currentAssegnazioneDays > 165)
                {
                    currentAssegnazioneDays = 165;
                }
            }


            double averageDailyCost = monthlyCost / 30.4375;
            double totalCost = currentAssegnazioneDays * averageDailyCost;

            return totalCost;
        }
    }
    public enum AssegnazioneDataCheck
    {
        Corretto,
        Eccessivo,
        Incorretto,
        DataUguale,
        DataDecorrenzaMinoreDiMin,
        DataFineAssMaggioreMax
    }
}
