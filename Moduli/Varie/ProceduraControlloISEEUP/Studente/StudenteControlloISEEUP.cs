using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class StudenteControlloISEEUP
    {
        public string codFiscale = string.Empty;
        public string numDomanda = string.Empty;
        public string id_domanda = string.Empty;
        public bool hasIseeup;
        public double sommaRedditi;
        public double ISEEU;
        public double ISEEUP;
        public double ISPEU;
        public double ISPEUP;
        public double SEQ;

        public List<string> incongruenzePresenti = new();
        public List<string> incongruenzeDaTogliere = new();
        public List<string> incongruenzeDaMettere = new();
    }
}
