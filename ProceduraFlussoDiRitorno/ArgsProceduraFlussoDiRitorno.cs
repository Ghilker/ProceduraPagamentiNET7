using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsProceduraFlussoDiRitorno
    {
        [Required(ErrorMessage = "Selezionare il file di flusso di ritorno")]
        public string _selectedFileFlusso { get; set; }
        [Required(ErrorMessage = "Indicare il codice del mandato opzionale inserito in precedenza")]
        public string _selectedImpegnoProvv { get; set; }
        [Required(ErrorMessage = "Indicare il tipo di bando")]
        public string _selectedTipoBando { get; set; }

        public ArgsProceduraFlussoDiRitorno()
        {
            _selectedFileFlusso = "";
            _selectedImpegnoProvv = "";
            _selectedTipoBando = "";
        }
    }
}
