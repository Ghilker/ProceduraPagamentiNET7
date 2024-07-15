using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.ProceduraAllegatiSpace
{
    internal class Studente
    {

        public string codFiscale { get; private set; }
        public string nome { get; private set; }
        public string cognome { get; private set; }
        public DateTime dataNascita { get; private set; }
        public string codStudente { get; private set; }
        public string numDomanda { get; private set; }
        public double importoBeneficio { get; private set; }

        public Studente(string codFiscale)
        {
            this.codFiscale = codFiscale;
            nome = string.Empty;
            cognome = string.Empty;
            codStudente = string.Empty;
            numDomanda = string.Empty;
        }

        public string GetFiscalCode()
        {
            return this.codFiscale;
        }

        public void AddInformations(
            string nome,
            string cognome,
            DateTime dataNascita,
            string codStudente,
            string numDomanda
            )
        {
            this.nome = nome;
            this.cognome = cognome;
            this.dataNascita = dataNascita;
            this.codStudente = codStudente;
            this.numDomanda = numDomanda;
        }

        public void AddImporto(double importo)
        {
            this.importoBeneficio = importo;
        }
    }
}
