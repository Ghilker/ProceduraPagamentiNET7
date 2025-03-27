using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.Verifica
{

    public class VerDomanda
    {
        public string AnnoAccademico;
        public string NumDomanda;
        public string TipoBando;




    }

    /*
    public class StudenteVerifica
    {
        public string codFiscale;
        public string numDomanda;

        public ValoriCalcolati valoriCalcolati;

        public StatusDomanda statusDomanda;
        public Anagrafica anagrafica;
        public Residenza residenza;
        public Domicilio domicilio;
        public Merito merito;
        public BeneficiRichiesti beneficiRichiesti;
        public Variazioni variazioni;
        public Incongruenze incongruenze;
    }
    public class StatusDomanda
    {
        public int statusDomanda;
        public bool _domandaCompleta;
        public bool _domandaTrasmessa;

        public StatusDomanda(int statusDomanda, int statusCompleto = 70, int statusTrasmesso = 90)
        {
            this.statusDomanda = statusDomanda;
            if (statusDomanda >= statusCompleto)
            {
                _domandaCompleta = true;
            }
            if (statusDomanda >= statusTrasmesso)
            {
                _domandaTrasmessa = true;
            }
        }
    }
    public class Anagrafica
    {
        public string nome;
        public string cognome;
        public DateTime dataNascita;
        public string codComuneNascita;
        public string codCittadinanza;
        public string sesso;
        public bool invalido;

        public bool rifugiatoPolitico;

        public bool straniero;
        public bool cittadinoUE;
    }
    public class Residenza
    {
        public string codComuneResidenza;

        public bool residenzaItaliana;
        public string codProvinciaResidenzaItaliana;
        public bool residenzaUE;
    }
    public class Domicilio
    {
        public bool possiedeDomicilio;
        public string codComuneDomicilio;
        public bool titoloOneroso;

        public bool contrOneroso;//vDomicilio TITOLO_ONEROSO (vero se 1)
        public bool contrLocazione;//TIPO_CONTRATTO_TITOLO_ONEROSO (vero se vuoto o 0)
        public bool contrEnte;
        public bool conoscenzaDatiContratto;//vero se N_SERIE_CONTRATTO non è null

        public DateTime dataRegistrazioneLocazione;
        public DateTime dataDecorrenzaLocazione;
        public DateTime dataScadenzaLocazione;
        public string codiceSerieLocazione;
        public int durataMesiLocazione;
        public bool prorogatoLocazione;
        public int durataMesiProrogaLocazione;
        public string codiceSerieProrogaLocazione;

        public enum TipologiaEnteIstituto
        {
            EntePubblicoPrivato,//ep
            IstitutoReligioso,//ir
            FondazioneAssociazione,//fa
            ErasmusSocrates//se
        }

        public TipologiaEnteIstituto tipologiaEnteIstituto;
        public string denominazioneIstituto;
        public int durataMesiContrattoIstituto;//vDomicilio.durata_contratto
        public double importoMensileRataIstituto;
    }
    public class Merito
    {
        public string codSedeStudi;//vIscrizioni cod_sede_studi
        public string comuneSedeStudi;//corsi_laurea.comune_sede_studi

        public string codTipoCorso;//vIscrizioni
        public string codCorsoLaurea;//vIscrizioni
        public string aaPrimaImmatricolazione;//vMerito
        public string annoCorsoDichiarato;//vIscrizioni

        public bool matricola;//vIscrizioni vero se anno_corso = 1

        public int numeroEsamiSostenuti;//vMerito
        public int sommaTotaleCrediti;//vMerito
        public int creditiTirocinio;//vIscrizioni
        public int creditiRinuncia;//vMerito
        public int creditiExtracurr;//vMerito
        public int sommaVoti;//vMerito
        public bool utilizzoBonus;//vMerito
        public int creditiBonusRimanenti; //vMerito

        public bool titoloPregresso;//vCarriera_pregressa

        public int sedeIstitutoFrequentatoTitoloPregresso; //0 = lazio, 1 = italia no lazio, 2 = estero
        public string codTipoCorsoTitoloPregresso;
        public int durataTitoloPregresso;

        public bool rinunciaStudi;//vCarriera_pregressa
        public int sedeIstitutoFrequentatoRinunciaStudi; //0 = lazio, 1 = italia no lazio, 2 = estero
        //0 = cod_sede_studi (Tabella Sede_studi),1 = codSedeStudi (tabella Atenei), 2 = stringa libera
        public string codTipoCorsoRinunciaStudi;
        public bool beneficiarioBSRinunciaStudi; //Tabella Benefici_usufruiti_LZ
        public List<int> anniBeneficiarioBSRinunciaStudi;
        public bool restituitiImportiBSRinunciaStudi; //Tabella Importi_restituiti_LZ
        public List<int> anniRestituitiBSRinunciaStudi;

        public bool trasferimento;//vCarriera_pregressa
        public int sedeIstitutoFrequentatoTrasferimento; //0 = lazio, 1 = italia no lazio, 2 = estero
        //0 = cod_sede_studi (Tabella Sede_studi),1 = codSedeStudi (tabella Atenei), 2 = stringa libera
        public string codTipoCorsoTrasferimento;
        public string aaPrimaImmatricolazioneTrasferimento;
        public string aaConseguimentoTrasferimento;

        public bool attesaTitolo;//vCarriera_pregressa
        public int sedeIstitutoFrequentatoAttesaTitolo; //0 = lazio, 1 = italia no lazio, 2 = estero
        //0 = cod_sede_studi (Tabella Sede_studi),1 = codSedeStudi (tabella Atenei), 2 = stringa libera
        public string codTipoCorsoAttesaTitolo;
        public string aaConseguimentoAttesaTitolo;

        public bool doppiaIscrizione;//vCarriera_pregressa
        public int sedeIstitutoFrequentatoDoppiaIscrizione; //0 = lazio, 1 = italia no lazio, 2 = estero
        //0 = cod_sede_studi (Tabella Sede_studi),1 = codSedeStudi (tabella Atenei), 2 = stringa libera
        public string codTipoCorsoDoppiaIscrizione;
        public string aaPrimaImmatricolazioneDoppiaIscrizione;
        public int annoCorsoDoppiaIscrizione;
        public int sommaCreditiDoppiaIscrizione;
    }
    public class BeneficiRichiesti
    {
        public bool richiestaBS;
        public bool richiestaPA;
        public bool richiestaCI;

        public bool beneficiarioAltriEnti;
        public double importoBeneficiPrecedenti;

        public bool beneficiOspitalitaResidenziale;

        public bool inAttesaCI;
        public string codNazioneCI;
        public DateTime dataPartenzaCI;
        public int durataMesiCI;

        public bool vincitoreBS;
        public bool vincitorePA;
        public bool vincitoreCI;

        public bool rinunciaBS;
        public bool rinunciaPA;
        public bool rinunciaCI;
        public bool rinunciaBenefici;

        public bool decadutoBS;
        public bool decadutoPA;
        public bool decadutoCI;
        public bool decadutoBenefici;

        public bool revocatoBS;
        public bool revocatoPA;
        public bool revocatoCI;
        public bool revocatoBenefici;

        public bool revocatoSedeDistaccata;
        public bool revocatoMancataIscrizione;
        public bool revocatoIscrittoRipetente;
        public bool revocatoISEE;
        public bool revocatoLaureato;
        public bool revocatoPatrimonio;
        public bool revocatoReddito;
        public bool revocatoEsami;
        public bool revocatoFuoriTermine;
        public bool revocatoIseeFuoriTermine;
        public bool revocatoIseeNonProdotta;
        public bool revocatoTrasmissioneIseeFuoriTermine;
        public bool revocatoNoContrattoLocazione;
    }
    public class Variazioni
    {
        public List<Variazione> variazioni = new();

        public class Variazione
        {
            public string codTipoVariazione;
            public string codBeneficio;
            public DateTime dataValidita;
        }
    }
    public class Incongruenze
    {
        public bool incongruenzaIscrizione;
        public bool incongruenzaAnnoImmatricolazione;
        public bool incongruenzaAnnoCorso;
    }

    public class ValoriCalcolati
    {
        public int annoCorsoCalcolato;
    }
    */
}
