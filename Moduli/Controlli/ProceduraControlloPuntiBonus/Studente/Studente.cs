using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class StudenteControlliBonus
    {
        public string codFiscale { get; set; }
        public string codiceStudente { get; set; }
        public string numDomanda { get; set; }
        public string codTipologiaStudi { get; set; }
        public string annoCorso { get; set; }
        public int numCrediti { get; set; }
        public int creditiBonusRimanentiDaTabella { get; set; }
        public int creditiBonusUsatiDaTabella { get; set; }
        public int currentEsitoBS { get; set; }
        public List<StoricoBonus> storicoBonus { get; set; }
        public List<Avvenimento> storicoAvvenimenti { get; set; }
        public int annoCorsoPrimaRichiestaBonus { get; set; }
        public int creditiRimanentiCalcolati { get; set; }

        public List<string> incongruenzeDaAggiungere { get; set; }

        public bool controlloManualeStudente { get; set; }

        public StudenteControlliBonus()
        {
            storicoAvvenimenti = new();
            storicoBonus = new();
            incongruenzeDaAggiungere = new();
            controlloManualeStudente = false;
        }

    }

    public class StoricoBonus
    {
        public string annoAccademico;
        public int annoCorsoRichiestaBonus;
        public string codTipologiaStudiBonus;
        public bool richiestoBonus;
        public bool esclusoBorsa;
        public int creditiUtilizzati;
    }

    public class Avvenimento
    {
        public string codAvvenimento;
        public string annoAccademicoAvvenimento;
        public string AAPrimaImmatricolazione;
        public int sedeIstituzioneUniversitariaAvvenimento;
        public int creditiDI;
        public int annoCorsoDI;
        public bool ripetenteDI;
        public string ateneoAvvenimento;
        public int creditiRiconosciutiAvvenimento;
        public string AACreditiRiconosciuti;



    }
}
