using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsControlloISEEUP
    {
        [Required(ErrorMessage = "Indicare l'anno accademico di riferimento")]
        [ValidAAFormat(ErrorMessage = "Formato dell'anno accademico non valido, inserire l'anno nel formato xxxxyyyy")]
        public string _annoAccademico { get; set; }

        public ArgsControlloISEEUP()
        {
            _annoAccademico = "";
        }
    }
}
