using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsProceduraEstrazionePermessiSoggiorno
    {
        [Required(ErrorMessage = "Selezionare la cartella di salvataggio")]
        public string _savePath { get; set; }
        [Required(ErrorMessage = "Selezionare il file con le mail")]
        public string _mailFilePath { get; set; }
    }
}
