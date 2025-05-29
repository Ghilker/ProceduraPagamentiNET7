using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
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
        public double importoMensileRataIstituto;
    }
}
