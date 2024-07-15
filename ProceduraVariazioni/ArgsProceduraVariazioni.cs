using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ArgsProceduraVariazioni
    {
        [Required(ErrorMessage = "Selezionare il file con i dati")]
        public string _selectedFilePath { get; set; }

        [Required(ErrorMessage = "Selezionare tipo variazione")]
        public string _selectedVariazioniValue { get; set; }

        [Required(ErrorMessage = "Selezionare tipo beneficio")]
        public string _selectedBeneficioValue { get; set; }

        [Required(ErrorMessage = "Inserire la nota")]
        public string _variazNotaText { get; set; }

        [Required(ErrorMessage = "Inserire la data della variazione")]
        [ValidDateFormat(ErrorMessage = "La data della variazione deve essere nel formato GG/MM/AAAA.")]
        public string _variazDataVariazioneText { get; set; }

        [Required(ErrorMessage = "Inserire l'utente variazione")]
        public string _variazUtenzaText { get; set; }

        [Required(ErrorMessage = "Inserire l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy.")]
        public string _variazAAText { get; set; }

        public ArgsProceduraVariazioni()
        {
            _selectedFilePath = string.Empty;
            _selectedVariazioniValue = string.Empty;
            _selectedBeneficioValue = string.Empty;
            _variazNotaText = string.Empty;
            _variazDataVariazioneText = string.Empty;
            _variazUtenzaText = string.Empty;
            _variazAAText = string.Empty;
        }
    }
}
