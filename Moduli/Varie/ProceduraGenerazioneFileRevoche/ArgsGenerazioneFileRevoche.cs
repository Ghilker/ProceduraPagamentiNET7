using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsGenerazioneFileRevoche 
    {
        [Required(ErrorMessage = "Anno accademico provvedimento richiesto")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy.")]
        public string _aaGenerazioneRev { get; set; } = string.Empty;

        [Required(ErrorMessage = "Selezionare l'ente di gestione")]
        public string _selectedCodEnte { get; set; } = string.Empty;
        [Required]
        public string _selectedFolderPath { get; set; } = string.Empty;
    }
}
