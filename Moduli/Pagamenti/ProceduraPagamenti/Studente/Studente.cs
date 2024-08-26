using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class Studente
    {
        public string numeroImpegno { get; private set; }
        public string codFiscale { get; private set; }
        public string cognome { get; private set; }
        public string nome { get; private set; }
        public string dataNascita { get; private set; }
        public string sesso { get; private set; }
        public string codEnte { get; private set; }
        public Residenza residenza { get; private set; }
        public LuogoNascita luogoNascita { get; private set; }
        public bool disabile { get; private set; }
        public double importoBeneficio { get; private set; }
        public bool vincitorePA { get; private set; }
        public bool eraVincitorePA { get; private set; }
        public List<Assegnazione> assegnazioni { get; private set; }
        public int annoCorso { get; private set; }
        public int tipoCorso { get; private set; }
        public double importoPagato { get; private set; }
        public double importoDaPagareLordo { get; private set; }
        public double importoDaPagare { get; private set; }
        public double importoAccontoPA { get; private set; }
        public double importoSaldoPA { get; private set; }
        public string numDomanda { get; private set; }
        public List<Reversale> reversali { get; private set; }
        public List<Detrazione> detrazioni { get; private set; }
        public long telefono { get; private set; }
        public string indirizzoEmail { get; private set; }
        public bool superamentoEsami { get; private set; }
        public bool superamentoEsamiTassaRegionale { get; private set; }
        public string IBAN { get; private set; }
        public string swift { get; private set; }
        public List<Pagamento> pagamentiEffettuati { get; private set; }
        public string mandatoProvvisorio { get; private set; }

        public Studente(
            string numDomanda,
            string codFiscale,
            string cognome,
            string nome,
            string dataNascita,
            string sesso,
            string codEnte,
            bool disabile,
            double importoBeneficio,
            int annoCorso,
            int tipoCorso,
            bool vincitorePA,
            bool superamentoEsami,
            bool superamentoEsamiTassaRegionale
            )
        {
            this.numDomanda = numDomanda;
            this.codFiscale = codFiscale;
            this.cognome = cognome;
            this.nome = nome;
            this.dataNascita = dataNascita;
            this.sesso = sesso;
            this.codEnte = codEnte;
            this.disabile = disabile;
            this.importoBeneficio = importoBeneficio;
            this.annoCorso = annoCorso;
            this.tipoCorso = tipoCorso;
            this.vincitorePA = vincitorePA;
            eraVincitorePA = false;
            this.superamentoEsami = superamentoEsami;
            this.superamentoEsamiTassaRegionale = superamentoEsamiTassaRegionale;
            numeroImpegno = string.Empty;
            assegnazioni = new List<Assegnazione>();
            reversali = new List<Reversale>();
            detrazioni = new List<Detrazione>();
            IBAN = string.Empty;
            indirizzoEmail = string.Empty;
            swift = string.Empty;
            pagamentiEffettuati = new List<Pagamento>();
            mandatoProvvisorio = string.Empty;
            residenza = new Residenza();
            luogoNascita = new LuogoNascita();
        }

        public AssegnazioneDataCheck AddAssegnazione(
            string codPensionato,
            string codStanza,
            DateTime dataDecorrenza,
            DateTime dataFineAssegnazione,
            string codFineAssegnazione,
            string codTipoStanza,
            double costoMensile,
            DateTime minDate,
            DateTime maxDate,
            bool fuoriCorso
            )
        {
            Assegnazione nuovaAssegnazione = new Assegnazione();

            AssegnazioneDataCheck result = nuovaAssegnazione.SetAssegnazione(
                 codPensionato,
                 codStanza,
                 dataDecorrenza,
                 dataFineAssegnazione,
                 codFineAssegnazione,
                 codTipoStanza,
                 costoMensile,
                 minDate,
                 maxDate,
                 assegnazioni,
                 fuoriCorso
                 );

            if (assegnazioni == null)
            {
                assegnazioni = new List<Assegnazione>();
            }

            assegnazioni.Add(nuovaAssegnazione);

            return result;

        }

        public void AddPagamentoEffettuato(string codTipoPagam, double importoPagamento, bool ritiratoAzienda)
        {
            if (pagamentiEffettuati == null)
            {
                pagamentiEffettuati = new List<Pagamento>();
            }

            pagamentiEffettuati.Add(new Pagamento(codTipoPagam, importoPagamento, ritiratoAzienda));
        }

        public void SetLuogoNascita(string codComune, string nomeComune, string provincia)
        {
            luogoNascita = new LuogoNascita(codComune, nomeComune, provincia);
        }

        public void SetResidenza(string indirizzo, string codComune, string provincia, string CAP, string nomeComune)
        {
            residenza = new Residenza(indirizzo, codComune, provincia, CAP, nomeComune);
        }

        public void SetImpegno(string impegno)
        {
            this.numeroImpegno = impegno;
        }

        public void AddReversale(string codReversale, double importo, string nota, string codTipoPagamOld, string codTipoPagamNew)
        {
            Reversale reversale = new Reversale(codReversale, importo, nota, codTipoPagamOld, codTipoPagamNew);

            if (reversali == null)
            {
                reversali = new List<Reversale>();
            }

            reversali.Add(reversale);
        }

        public void AddDetrazione(string codReversale, double importo, string nota, bool needUpdate = false)
        {
            Detrazione detrazione = new Detrazione(codReversale, importo, nota, needUpdate);

            if (detrazioni == null)
            {
                detrazioni = new List<Detrazione>();
            }

            detrazioni.Add(detrazione);
        }

        public void RemoveAllAssegnazioni()
        {
            assegnazioni = new List<Assegnazione>();
            importoAccontoPA = 0;
            importoSaldoPA = 0;
        }

        public void SetInformations(
            long telefono,
            string email,
            string iban,
            string swift
            )
        {
            this.telefono = telefono;
            this.indirizzoEmail = email;
            this.IBAN = iban;
            this.swift = swift;
        }

        public void SetImportiPagati(double amount)
        {
            this.importoPagato = amount;
        }

        public void SetImportoDaPagare(double amount)
        {
            this.importoDaPagare = amount;
        }

        public void SetImportoDaPagareLordo(double amount)
        {
            this.importoDaPagareLordo = amount;
        }

        public void SetImportoAccontoPA(double amount)
        {
            this.importoAccontoPA = amount;
        }

        public void SetImportoSaldoPA(double amount)
        {
            this.importoSaldoPA = amount;
        }

        public void SetMandatoProvvisorio(string mandatoProvvisorio)
        {
            this.mandatoProvvisorio = mandatoProvvisorio;
        }

        public void SetEraVincitorePA(bool eraVincitorePA)
        {
            this.eraVincitorePA = eraVincitorePA;
        }
    }
}
