using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsRendicontoMiur
    {
        [Required(ErrorMessage = "Selezionare la cartella con i modelli MIUR")]
        public string _folderPath = string.Empty;
    }
}
