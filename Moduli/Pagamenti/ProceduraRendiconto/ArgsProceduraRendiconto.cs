using ProcedureNet7;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsProceduraRendiconto
    {
        [Required(ErrorMessage = "Selezionare la cartella per il salvataggio file")]
        public string _selectedSaveFolder { get; set; }

        [Required(ErrorMessage = "Indicare l'anno accademico di inizio")]
        [ValidAAFormat(ErrorMessage = "Formato dell'anno accademico di inizio non valido, inserire l'anno nel formato xxxxyyyy")]
        public string _annoAccademicoInizio { get; set; }

        [Required(ErrorMessage = "Indicare l'anno accademico di fine")]
        [ValidAAFormat(ErrorMessage = "Formato dell'anno accademico di fine non valido, inserire l'anno nel formato xxxxyyyy")]
        public string _annoAccademicoFine { get; set; }

        public ArgsProceduraRendiconto()
        {
            _selectedSaveFolder = "";
            _annoAccademicoInizio = "";
            _annoAccademicoFine = "";
        }
    }
}
