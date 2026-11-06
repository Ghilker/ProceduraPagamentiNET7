using System.Globalization;

namespace ProcedureNet7
{
    public partial class ProceduraPagamenti
    {
        private MotivoEsclusionePagamento? AddAssegnazioneSaldo(StudentePagamenti studente,
            AssegnazionePaDto assegnazione, DateTime minDataPA, DateTime maxDataPA,
            bool fuoriCorso, out string? rettificaDate)
        {
            rettificaDate = null;
            bool inizioValido = DateTime.TryParseExact(assegnazione.DataDecorrenza?.Trim(), "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime startDate);
            if (!inizioValido && !SimulazionePATollerante)
                return Esclusione("Data iniziale dell'assegnazione assente o non valida",
                    $"L'assegnazione {assegnazione.IdAssegnazionePa} non presenta una data di decorrenza valida.");

            bool fineValida = DateTime.TryParseExact(assegnazione.DataFineAssegnazione?.Trim(), "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime endDate);
            if (!fineValida)
                endDate = DateTime.MaxValue;

            if (SimulazionePATollerante)
            {
                DateTime dataEstrazione = DateTime.ParseExact(selectedDataRiferimento, "dd/MM/yyyy",
                    CultureInfo.InvariantCulture).Date;
                DateTime ultimoGiorno = maxDataPA.Date < dataEstrazione ? maxDataPA.Date : dataEstrazione;
                DateTime primoGiorno = minDataPA.Date < ultimoGiorno ? minDataPA.Date : ultimoGiorno;
                DateTime inizioRegistrato = startDate;
                DateTime fineRegistrata = endDate;

                startDate = !inizioValido ? primoGiorno : startDate < primoGiorno ? primoGiorno :
                    startDate > ultimoGiorno ? ultimoGiorno : startDate;
                endDate = endDate > ultimoGiorno ? ultimoGiorno : endDate < primoGiorno ? primoGiorno : endDate;
                // Un periodo invertito o non ancora maturato non genera una trattenuta.
                if (endDate < startDate) endDate = startDate;

                if (!inizioValido || !fineValida || startDate != inizioRegistrato ||
                    endDate != fineRegistrata || startDate == endDate)
                {
                    rettificaDate = $"SIMULAZIONE PA tollerante. Assegnazione {assegnazione.IdAssegnazionePa}: " +
                        $"inizio registrato {assegnazione.DataDecorrenza}; fine registrata {assegnazione.DataFineAssegnazione}. " +
                        $"Periodo calcolato: {startDate:dd/MM/yyyy} – {endDate:dd/MM/yyyy}; estrazione {dataEstrazione:dd/MM/yyyy}.";
                    if (startDate == endDate) rettificaDate += " Nessun giorno maturato: trattenuta zero.";
                }
            }

            _ = double.TryParse(assegnazione.ImportoMensileStr?.Trim(), out double importoMensile);
            AssegnazioneDataCheck result = studente.AddAssegnazione(
                assegnazione.CodPensionato?.Trim() ?? "",
                assegnazione.CodStanza?.Trim() ?? "",
                startDate, endDate,
                assegnazione.CodFineAssegnazione?.Trim() ?? "",
                assegnazione.TipoStanza?.Trim() ?? "",
                importoMensile, minDataPA, maxDataPA, fuoriCorso,
                assegnazione.IdAssegnazionePa);

            if (result == AssegnazioneDataCheck.Corretto || SimulazionePATollerante) return null;

            string message = result switch
            {
                AssegnazioneDataCheck.Eccessivo => "Durata del posto alloggio superiore a 10 mesi",
                AssegnazioneDataCheck.Incorretto => "Fine dell'assegnazione precedente all'inizio",
                AssegnazioneDataCheck.DataUguale => "Inizio e fine dell'assegnazione coincidenti",
                AssegnazioneDataCheck.DataDecorrenzaMinoreDiMin => "Inizio dell'assegnazione prima del periodo previsto",
                AssegnazioneDataCheck.DataFineAssMaggioreMax => "Fine dell'assegnazione oltre il periodo previsto",
                AssegnazioneDataCheck.MancanzaDataFineAssegnazione => "Data di fine assegnazione assente",
                _ => "Date dell'assegnazione non utilizzabili"
            };
            return Esclusione(message,
                $"{message}. Assegnazione {assegnazione.IdAssegnazionePa}: inizio {assegnazione.DataDecorrenza}; " +
                $"fine {(string.IsNullOrWhiteSpace(assegnazione.DataFineAssegnazione) ? "non registrata" : assegnazione.DataFineAssegnazione)}. " +
                $"Periodo di riferimento: {minDataPA:dd/MM/yyyy} – {maxDataPA:dd/MM/yyyy}.");
        }
    }
}
