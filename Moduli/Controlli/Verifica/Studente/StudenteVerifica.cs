using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class StudenteVerifica
    {
        public string codFiscale;
        public string numDomanda;

        public int esitoVERIFICATO;
        public VerificaAnagrafica verificaAnagrafica;
        public VerificaResidenza verificaResidenza;
        public VerificaDomicilio verificaDomicilio;
        public VerificaIscrizione verificaIscrizione;
        public VerificaNucleoFamiliare verificaNucleoFamiliare;
        public VerificaDatiEconomici verificaDatiEconomici;
        public VerificaBeneficiRichiesti verificaBeneficiRichiesti;
    }

    public class VerificaAnagrafica
    {
        public string cognome;//studente
        public string nome;//studente
        public DateTime dataNascita;//studente
        public string sesso;//studente
        public string comuneNascita;//studente
        public string cittadinanza;//vCittadinanza
        public bool invalidità;//vDatiGenerali_dom
    }
    public class VerificaResidenza
    {
        public bool residenzaItaliana;//vResidenza (vero se provincia_residenza <> 'EE')

        public string provinciaResidenzaItaliana;
        public string comuneResidenzaItaliana;


    }
    public class VerificaDomicilio
    {
        public bool diversoDaResidenza; //vDomicilio (vero se esiste o se TITOLO_ONEROSO IS NOT NULL) in generale, per le graduatorie è vero se in LUOGO_REPERIBILITA_STUDENTE INSERIMENTO_DATI_CONTRATTO è 1 e tipo_luogo = 'dom'
        public string comuneDomicilio;//vDomicilio

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
    public class VerificaIscrizione
    {
        public string codSedeStudi;//vIscrizioni cod_sede_studi
        public string comuneSedeStudi;//corsi_laurea.comune_sede_studi

        public string codTipoCorso;//vIscrizioni
        public string codCorsoLaurea;//vIscrizioni
        public string aaPrimaImmatricolazione;//vMerito
        public string annoCorso;//vIscrizioni

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
    public class VerificaNucleoFamiliare //bestemmie
    {
        public int numeroComponentiNF;
        public int numeroComponentiEsteroNF;
        public bool almenoUnGenitore; //controllo cod_status_genit nella tabella vNucleo_familiare (status N = falso, altrimenti sempre vero)
        public bool orfano; //Cod_tipologia_nucleo = 'A' and motivo_assenza_genit = 1 and cod_status_genit = 'N' == vero
        public bool indipendente;//cod_tipologia_nucleo = 'B' and num_componenti = 1 and motivo_assenza_genit = 2
        public DateTime dataInizioResidenzaIndipendente; //Residenza_est_da
        public bool redditoSuperiore; //Reddito_2_anni
    }
    public class VerificaDatiEconomici //vNucleo_fam_stranieri_DO
    {
        public string tipologiaReddito;
        public string tipologiaRedditoIntegrazione;

        public double ISP;
        public double ISR;
        public double SEQ;
        public double patrimonioMobiliare;
        public double detrazioni;

        public double ISEDSU;
        public double ISEEDSU;
        public double ISPDSU;
        public double ISPEDSU;
    }
    public class VerificaBeneficiRichiesti
    {
        public bool richiestoBorsaDiStudio; //vBenefici_richiesti
        public bool richiestoPostoAlloggio;//vBenefici_richiesti
        public bool richiestoPostoAlloggioComfort; //DatiGenerali_dom
        public bool richiestoContributoInternazionale;//vBenefici_richiesti

        public bool inAttesaCI; //vspecifiche_CI
        public string codNazioneCI;//vspecifiche_CI
        public DateTime dataPartenzaCI;//vspecifiche_CI
        public int durataMesiCI;//vspecifiche_CI

        public bool beneficiAltriEnti; //datiGenerali_dom.possesso_altra_borsa

        public bool beneficiOspitalitaResidenziale; //vBenefici_altri_enti (vero se presente)

        public bool beneficiPercepitiPrecedenti; //vImporti_borsa_percepiti (vero se presente e > 0)
        public double sommaImportiBeneficiPercepitiPrecedenti;
    }
}
