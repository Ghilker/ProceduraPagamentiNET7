using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ArgsAggiuntaProvvedimenti
    {
        [Required]
        public string _selectedFolderPath { get; set; }

        [Required(ErrorMessage = "Numero provvedimento richiesto")]
        public string _numProvvedimento { get; set; }

        [Required(ErrorMessage = "Anno accademico provvedimento richiesto")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy.")]
        public string _aaProvvedimento { get; set; }

        [Required(ErrorMessage = "Data provvedimento richiesto")]
        [ValidDateFormat(ErrorMessage = "La data della variazione deve essere nel formato GG/MM/AAAA.")]
        public string _dataProvvedimento { get; set; }

        [Required(ErrorMessage = "Selezionare il tipo di provvedimento")]
        public string _provvedimentoSelezionato { get; set; }

        [Required(ErrorMessage = "Nota provvedimento richiesta")]
        public string _notaProvvedimento { get; set; }

        [Required(ErrorMessage = "Selezionare il tipo di beneficio")]
        public string _beneficioProvvedimento { get; set; }

        public bool _requireNuovaSpecifica { get; set; }

        public string _impegnoPR { get; set; }
        public string _impegnoSA { get; set; }
        public string _tipoFondo { get; set; }
        public string _capitolo { get; set; }
        public string _esePR { get; set; }
        public string _eseSA { get; set; }

        public ArgsAggiuntaProvvedimenti()
        {
            _aaProvvedimento = string.Empty;
            _dataProvvedimento = string.Empty;
            _notaProvvedimento = string.Empty;
            _numProvvedimento = string.Empty;
            _provvedimentoSelezionato = string.Empty;
            _selectedFolderPath = string.Empty;
            _beneficioProvvedimento = string.Empty;

            _impegnoPR = string.Empty;
            _impegnoSA = string.Empty;
            _eseSA = string.Empty;
            _esePR = string.Empty;
            _capitolo = string.Empty;
            _tipoFondo = string.Empty;
        }
    }
}
