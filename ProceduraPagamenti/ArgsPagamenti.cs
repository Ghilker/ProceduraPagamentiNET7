using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsPagamenti : IValidatableObject
    {
        [Required(ErrorMessage = "Selezionare la cartella del salvataggio")]
        public string _selectedSaveFolder { get; set; }

        [Required(ErrorMessage = "Indicare l'anno accademico di riferimento")]
        [ValidAAFormat(ErrorMessage = "Formato dell'anno accademico non valido, inserire l'anno nel formato xxxxyyyy")]
        public string _annoAccademico { get; set; }

        [Required(ErrorMessage = "Indicare la data di riferimento della procedura")]
        [ValidDateFormat(ErrorMessage = "Formato della data di riferimento non valido, inserire la data nel formato gg/mm/aaaa")]
        public string _dataRiferimento { get; set; }

        public string _vecchioMandato { get; set; }

        [Required(ErrorMessage = "Indicare il numero del mandato")]
        public string _numeroMandato { get; set; }

        public string _tipoProcedura { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            List<string> errorMessages = new List<string>();
            if (string.IsNullOrWhiteSpace(_numeroMandato))
            {
                errorMessages.Add("Indicare il numero di mandato");
            }
            else if (!string.IsNullOrWhiteSpace(_vecchioMandato) && string.IsNullOrWhiteSpace(_numeroMandato))
            {
                errorMessages.Add("Indicare il nuovo numero di mandato per modificare il precedente");
            }

            if (string.IsNullOrWhiteSpace(_tipoProcedura))
            {
                errorMessages.Add("Indicare il tipo di procedura da eseguire");
            }

            if (errorMessages.Any())
            {
                string combinedErrors = string.Join(Environment.NewLine, errorMessages);
                yield return new ValidationResult(combinedErrors);
            }
        }
    }
}
