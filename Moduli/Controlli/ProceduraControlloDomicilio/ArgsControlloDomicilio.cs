using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsControlloDomicilio 
    {
        [Required(ErrorMessage = "Inserire l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy.")]
        public string _selectedAA {  get; set; }

        public string _folderPath { get; set; }
    }
}
