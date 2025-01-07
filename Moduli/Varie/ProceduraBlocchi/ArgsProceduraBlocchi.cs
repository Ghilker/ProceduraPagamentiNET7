using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ArgsProceduraBlocchi
    {
        [Required(ErrorMessage = "Selezionare il file con i dati")]
        public string _blocksFilePath { get; set; }

        [Required(ErrorMessage = "Inserire l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy.")]
        public string _blocksYear { get; set; }

        [Required(ErrorMessage = "Inserire l'utente variazione")]
        [ValidStringLenght(ErrorMessage = "Utenza troppo lunga, usare meno di 20 caratteri.")]
        public string _blocksUsername { get; set; }
        public bool _blocksGiaRimossi { get; set; }
        public bool _blocksInsertMessaggio { get; set; }

        public ArgsProceduraBlocchi()
        {
            _blocksFilePath = string.Empty;
            _blocksYear = string.Empty;
            _blocksUsername = string.Empty;
        }

    }
}
