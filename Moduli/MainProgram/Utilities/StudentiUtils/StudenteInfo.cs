using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class StudenteInfo
    {
        public InformazioniPersonali InformazioniPersonali { get; set; } = new InformazioniPersonali();
        public InformazioniIscrizione InformazioniIscrizione { get; set; } = new InformazioniIscrizione();
        public InformazioniBeneficio InformazioniBeneficio { get; set; } = new InformazioniBeneficio();
        public InformazioniConto InformazioniConto { get; set; } = new InformazioniConto();
        public InformazioniSede InformazioniSede { get; set; } = new InformazioniSede();
        public void SetLuogoNascita(string codComune, string nomeComune, string provincia)
        {
            InformazioniPersonali.LuogoNascita = new LuogoNascita(codComune, nomeComune, provincia);
        }

        public void SetResidenza(string indirizzo, string codComune, string provincia, string CAP, string nomeComune)
        {
            InformazioniSede.Residenza = new Residenza(indirizzo, codComune, provincia, CAP, nomeComune);
        }

        public void SetForzatura(string forzaturaStatusSede)
        {
            InformazioniSede.ForzaturaStatusSede = forzaturaStatusSede;
        }

        public void SetDomicilio(
            string codComune,
            bool titoloOneroso,
            string serieContratto,
            DateTime dataRegistrazione,
            DateTime dataDecorrenza,
            DateTime dataScadenza,
            int durataContratto,
            bool prorogato,
            int durataProroga,
            string serieProroga,
            bool contrattoValido,
            bool prorogaValido,
            bool contrattoEnte,
            string denominazioneEnte,
            double importoRataEnte
            )
        {
            InformazioniSede.Domicilio = new Domicilio();
            InformazioniSede.Domicilio.codComuneDomicilio = codComune;
            InformazioniSede.Domicilio.titoloOneroso = titoloOneroso;
            InformazioniSede.Domicilio.codiceSerieLocazione = serieContratto;
            InformazioniSede.Domicilio.dataRegistrazioneLocazione = dataRegistrazione;
            InformazioniSede.Domicilio.dataDecorrenzaLocazione = dataDecorrenza;
            InformazioniSede.Domicilio.dataScadenzaLocazione = dataScadenza;
            InformazioniSede.Domicilio.durataMesiLocazione = durataContratto;
            InformazioniSede.Domicilio.prorogatoLocazione = prorogato;
            InformazioniSede.Domicilio.durataMesiProrogaLocazione = durataProroga;
            InformazioniSede.Domicilio.codiceSerieProrogaLocazione = serieProroga;
            InformazioniSede.ContrattoValido = contrattoValido;
            InformazioniSede.ProrogaValido = prorogaValido;
            InformazioniSede.Domicilio.contrEnte = contrattoEnte;
            InformazioniSede.Domicilio.denominazioneIstituto = denominazioneEnte;
            InformazioniSede.Domicilio.importoMensileRataIstituto = importoRataEnte;
        }
        public void SetInformations(
            long telefono,
            string email,
            string iban,
            string swift,
            bool bonificoEstero
        )
        {
            InformazioniPersonali.Telefono = telefono;
            InformazioniPersonali.IndirizzoEmail = email;
            InformazioniConto.IBAN = iban;
            InformazioniConto.Swift = swift;
            InformazioniConto.BonificoEstero = bonificoEstero;
        }

        public void SetEraVincitorePA(bool eraVincitorePA)
        {
            InformazioniBeneficio.EraVincitorePA = eraVincitorePA;
        }

        public void SetNucleoFamiliare(int componentiTotale, int componentiEstero)
        {
            InformazioniPersonali.NumeroComponentiNucleoFamiliare = componentiTotale;
            InformazioniPersonali.NumeroComponentiNucleoFamiliareEstero = componentiEstero;
        }

        public void SetDomicilioCheck(bool check)
        {
            InformazioniSede.DomicilioCheck = check;
        }

        public void SetEsitoPA(int esito)
        {
            InformazioniBeneficio.EsitoPA = esito;
        }

        public void SetServizioSanitario(bool check)
        {
            InformazioniBeneficio.HaServizioSanitario = check;
        }
    }
}
