using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.ProceduraAllegatiSpace
{
    public class ArgsProceduraAllegati
    {
        [Required(ErrorMessage = "Selezionare l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy")]
        public string _selectedAA { get; set; }

        [Required(ErrorMessage = "Indicare il file excel con i codici fiscali")]
        public string _selectedFileExcel { get; set; }

        [Required(ErrorMessage = "Indicare la cartella di salvataggio")]
        public string _selectedSaveFolder { get; set; }

        [Required(ErrorMessage = "Selezionare il tipo di allegato da produrre")]
        public string _selectedTipoAllegato { get; set; }
        public string _selectedTipoAllegatoName { get; set; }

        public string _selectedTipoBeneficio { get; set; }

        public ArgsProceduraAllegati()
        {
            _selectedAA = string.Empty;
            _selectedFileExcel = string.Empty;
            _selectedSaveFolder = string.Empty;
            _selectedTipoAllegato = string.Empty;
            _selectedTipoAllegatoName = string.Empty;
            _selectedTipoBeneficio = string.Empty;
        }

    }
}
