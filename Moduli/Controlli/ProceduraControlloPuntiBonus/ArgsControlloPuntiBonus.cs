using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsControlloPuntiBonus
    {
        [Required(ErrorMessage = "Selezionare la cartella di salvataggio")]
        public string _selectedSaveFolder { get; set; }
    }
}
