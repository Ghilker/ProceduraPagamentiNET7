using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.Storni
{
    internal class ArgsProceduraStorni
    {
        [Required(ErrorMessage = "Selezionare il file degli storni")]
        public string _selectedFile { get; set; }

        [Required(ErrorMessage = "Inserire l'esercizio finanziario")]
        public string _esercizioFinanziario { get; set; }
    }
}
