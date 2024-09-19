using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ArgsElaborazioneFileUni
    {
        [Required(ErrorMessage = "Selezionare l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy")]
        public string _selectedAA { get; set; }

        [Required(ErrorMessage = "Indicare la cartella dei files dell'università")]
        public string _selectedUniFolder { get; set; }
    }
}
