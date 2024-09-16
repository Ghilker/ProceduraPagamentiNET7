using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class StudenteElaborazione
    {
        public string codFiscale;

        public string tipoIscrizioneUni;
        public bool iscrCondizione;
        public string tipoCorsoUni;
        public string descrCorsoUni;
        public int annoCorsoUni;
        public string aaImmatricolazioneUni;
        public int creditiConseguitiUni;
        public int creditiConvalidatiUni;
        public bool tassaRegionalePagata;
        public bool titoloAcquisito;

        public bool disabile;
        public string tipoCorsoDic;
        public int annoCorsoDic;
        public int durataLegaleCorso;
        public int creditiConseguitiDic;
        public int creditiExtraCurrDic;
        public int creditiDaRinunciaDic;
        public int creditiTirocinioDic;

        public int creditiRichiestiDB;

        public bool seTitpo;
        public bool seAnno;
        public bool seCFU;
        public bool congruenzaAnno;

        public Dictionary<string, string> blocchiPresenti;
        public List<string> blocchiDaTogliere;
        public List<string> blocchiDaMettere;

        public List<string> colErroriElaborazione;

        public bool daRimuovere;
    }


}
