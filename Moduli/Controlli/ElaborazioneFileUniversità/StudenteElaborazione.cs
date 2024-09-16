using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class StudenteElaborazione
    {
        public string codFiscale = string.Empty;

        public string tipoIscrizioneUni = string.Empty;
        public bool iscrCondizione;
        public string tipoCorsoUni = string.Empty;
        public string descrCorsoUni = string.Empty;
        public int annoCorsoUni;
        public string aaImmatricolazioneUni = string.Empty;
        public int creditiConseguitiUni;
        public int creditiConvalidatiUni;
        public bool tassaRegionalePagata;
        public bool titoloAcquisito;

        public bool disabile;
        public string tipoCorsoDic = string.Empty;
        public int annoCorsoDic;
        public int durataLegaleCorso;
        public int creditiConseguitiDic;
        public int creditiExtraCurrDic;
        public int creditiDaRinunciaDic;
        public int creditiTirocinioDic;
        public bool stemDic;
        public string sessoDic = string.Empty;
        public string descrCorsoDic = string.Empty;

        public int creditiRichiestiDB;

        public bool seTitpo;
        public bool seAnno;
        public bool seCFU;
        public bool congruenzaAnno;

        public Dictionary<string, string> blocchiPresenti = new();
        public List<string> blocchiDaTogliere = new();
        public List<string> blocchiDaMettere = new();

        public List<string> colErroriElaborazione = new();

        public bool daRimuovere;
    }


}
