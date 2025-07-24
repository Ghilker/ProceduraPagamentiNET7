using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class StudentePagamenti : StudenteInfo
    {
        public InformazioniPagamento InformazioniPagamento { get; set; } = new InformazioniPagamento();
        public StudentePagamenti() { }
        public StudentePagamenti(
            string numDomanda,
            string codFiscale,
            string cognome,
            string nome,
            string dataNascita,
            string sesso,
            string codEnte,
            string codCittadinanza,
            bool disabile,
            double importoBeneficio,
            int annoCorso,
            int tipoCorso,
            int esitoPA,
            bool superamentoEsami,
            bool superamentoEsamiTassaRegionale,
            string statusSede,
            bool rifugiato
            )
        {
            InformazioniPersonali.NumDomanda = numDomanda;
            InformazioniPersonali.CodFiscale = codFiscale;
            InformazioniPersonali.Cognome = cognome;
            InformazioniPersonali.Nome = nome;
            InformazioniPersonali.DataNascita = dataNascita;
            InformazioniPersonali.Sesso = sesso;
            InformazioniIscrizione.CodEnte = codEnte;
            InformazioniPersonali.CodCittadinanza = codCittadinanza;
            InformazioniPersonali.Disabile = disabile;
            InformazioniBeneficio.ImportoBeneficio = importoBeneficio;
            InformazioniIscrizione.AnnoCorso = annoCorso;
            InformazioniIscrizione.TipoCorso = tipoCorso;
            InformazioniBeneficio.EsitoPA = esitoPA;
            InformazioniBeneficio.EraVincitorePA = false;
            InformazioniBeneficio.SuperamentoEsami = superamentoEsami;
            InformazioniBeneficio.SuperamentoEsamiTassaRegionale = superamentoEsamiTassaRegionale;
            InformazioniPagamento.NumeroImpegno = string.Empty;
            InformazioniPagamento.Assegnazioni = new List<Assegnazione>();
            InformazioniPagamento.Reversali = new List<Reversale>();
            InformazioniPagamento.Detrazioni = new List<Detrazione>();
            InformazioniConto.IBAN = string.Empty;
            InformazioniPersonali.IndirizzoEmail = string.Empty;
            InformazioniConto.Swift = string.Empty;
            InformazioniPagamento.PagamentiEffettuati = new List<Pagamento>();
            InformazioniPagamento.MandatoProvvisorio = string.Empty;
            InformazioniSede.Residenza = new Residenza();
            InformazioniPersonali.LuogoNascita = new LuogoNascita();
            InformazioniSede.StatusSede = statusSede;
            InformazioniPersonali.Rifugiato = rifugiato;
            InformazioniSede.ForzaturaStatusSede = string.Empty;
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
            bool fuoriCorso,
            string idAssegnazione
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
                 InformazioniPagamento.Assegnazioni,
                 fuoriCorso,
                 idAssegnazione
                 );

            if (InformazioniPagamento.Assegnazioni == null)
            {
                InformazioniPagamento.Assegnazioni = new List<Assegnazione>();
            }

            InformazioniPagamento.Assegnazioni.Add(nuovaAssegnazione);

            return result;

        }

        public void AddPagamentoEffettuato(string codTipoPagam, double importoPagamento, bool ritiratoAzienda)
        {
            if (InformazioniPagamento.PagamentiEffettuati == null)
            {
                InformazioniPagamento.PagamentiEffettuati = new List<Pagamento>();
            }

            InformazioniPagamento.PagamentiEffettuati.Add(new Pagamento(codTipoPagam, importoPagamento, ritiratoAzienda));
        }

        public void SetImpegno(string impegno)
        {
            InformazioniPagamento.NumeroImpegno = impegno;
        }

        public void SetCategoriaCU(string categoriaCU)
        {
            InformazioniPagamento.CategoriaCU = categoriaCU;
        }

        public void AddReversale(string codReversale, double importo, string nota, string codTipoPagamOld, string codTipoPagamNew)
        {
            Reversale reversale = new Reversale(codReversale, importo, nota, codTipoPagamOld, codTipoPagamNew);

            if (InformazioniPagamento.Reversali == null)
            {
                InformazioniPagamento.Reversali = new List<Reversale>();
            }

            InformazioniPagamento.Reversali.Add(reversale);
        }

        public void AddDetrazione(string codReversale, double importo, string nota, bool needUpdate = false)
        {
            Detrazione detrazione = new Detrazione(codReversale, importo, nota, needUpdate);

            if (InformazioniPagamento.Detrazioni == null)
            {
                InformazioniPagamento.Detrazioni = new List<Detrazione>();
            }

            InformazioniPagamento.Detrazioni.Add(detrazione);
        }

        public void RemoveAllAssegnazioni()
        {
            InformazioniPagamento.Assegnazioni = new List<Assegnazione>();
            InformazioniPagamento.ImportoAccontoPA = 0;
            InformazioniPagamento.ImportoSaldoPA = 0;
        }

        public void SetImportiPagati(double amount)
        {
            InformazioniPagamento.ImportoPagato = amount;
        }

        public void SetImportoDaPagare(double amount)
        {
            InformazioniPagamento.ImportoDaPagare = amount;
        }

        public void SetImportoDaPagareLordo(double amount)
        {
            InformazioniPagamento.ImportoDaPagareLordo = amount;
        }

        public void SetImportoAccontoPA(double amount)
        {
            InformazioniPagamento.ImportoAccontoPA = amount;
        }

        public void SetImportoSaldoPA(double amount)
        {
            InformazioniPagamento.ImportoSaldoPA = amount;
        }

        public void SetMandatoProvvisorio(string mandatoProvvisorio)
        {
            InformazioniPagamento.MandatoProvvisorio = mandatoProvvisorio;
        }

        public void SetPagatoPendolare(bool pagatoPendolare)
        {
            InformazioniPagamento.PagatoPendolare = pagatoPendolare;
        }
    }
}
