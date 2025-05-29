using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class Assegnazione
    {
        public string codPensionato { get; private set; } = string.Empty;
        public string codStanza { get; private set; } = string.Empty;
        public DateTime dataDecorrenza { get; private set; }
        public DateTime dataFineAssegnazione { get; private set; }
        public string codFineAssegnazione { get; private set; } = string.Empty;
        public string codTipoStanza { get; private set; } = string.Empty;
        public double costoTotale { get; private set; }
        public double costoMensile { get; private set; }
        public AssegnazioneDataCheck statoCorrettezzaAssegnazione { get; private set; }
        public string idAssegnazione { get; private set; } = string.Empty;

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
                bool fuoriCorso,
                string idAssegnazione
            )
        {
            this.codPensionato = codPensionato;
            this.codStanza = codStanza;
            this.dataDecorrenza = dataDecorrenza;
            this.dataFineAssegnazione = dataFineAssegnazione;
            this.codFineAssegnazione = codFineAssegnazione;
            this.codTipoStanza = codTipoStanza;
            this.costoMensile = costoMensile;
            this.idAssegnazione = idAssegnazione;

            statoCorrettezzaAssegnazione = AssegnazioneDataCheck.Corretto;

            costoTotale = CalculateTotalDailyCost(dataDecorrenza, dataFineAssegnazione, costoMensile, minDate, maxDate, assegnazioni, fuoriCorso);

            return statoCorrettezzaAssegnazione;
        }

        public void SetAssegnazioneDataCheck(AssegnazioneDataCheck toSet)
        {
            this.statoCorrettezzaAssegnazione = toSet;
        }

        public double CalculateTotalDailyCost(DateTime startDate, DateTime endDate, double monthlyCost, DateTime minDate, DateTime maxDate, List<Assegnazione> assegnazioni, bool fuoriCorso)
        {
            if (endDate == DateTime.MaxValue)
            {
                endDate = maxDate;
                dataFineAssegnazione = maxDate;
                statoCorrettezzaAssegnazione = AssegnazioneDataCheck.MancanzaDataFineAssegnazione;
            }
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
            int previousAssegnazioniDays = 0;

            // If assegnazioni are provided and the user is fuoriCorso, adjust the days calculation
            if (assegnazioni != null && fuoriCorso)
            {
                foreach (Assegnazione assegnazione in assegnazioni)
                {
                    // Calculate the days for this particular Assegnazione
                    int assegnazioneDays = (assegnazione.dataFineAssegnazione - assegnazione.dataDecorrenza).Days + 1;

                    // Accumulate the days from previous assegnazioni
                    previousAssegnazioniDays += assegnazioneDays;

                    // Check if the total days exceed 165, and adjust accordingly
                    if (previousAssegnazioniDays >= 165)
                    {
                        return 0; // No more days allowed if we hit or exceed 165
                    }
                }

                // After processing previous assegnazioni, check how many days are left for the current one
                if (previousAssegnazioniDays + currentAssegnazioneDays > 165)
                {
                    currentAssegnazioneDays = 165 - previousAssegnazioniDays;
                }
            }
            else if (fuoriCorso)
            {
                // If fuoriCorso and there are no previous assegnazioni, apply the 165 days limit
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
        DataFineAssMaggioreMax,
        MancanzaDataFineAssegnazione,
        UscitaPrecedenteAlLimite,
        ErroreControlloData
    }
}
