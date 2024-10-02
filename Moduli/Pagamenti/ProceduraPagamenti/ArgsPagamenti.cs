using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ArgsPagamenti : IValidatableObject
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

        public string _numeroMandato { get; set; }

        public string _tipoProcedura { get; set; }

        public bool _filtroManuale { get; set; }

        public bool _elaborazioneMassivaCheck { get; set; }

        public string _elaborazioneMassivaString { get; set; }

        public bool _forzareStudenteCheck { get; set; }

        public string _forzareStudenteString { get; set; }

        public ArgsPagamenti()
        {
            _annoAccademico = string.Empty;
            _selectedSaveFolder = string.Empty;
            _dataRiferimento = string.Empty;
            _vecchioMandato = string.Empty;
            _numeroMandato = string.Empty;
            _tipoProcedura = string.Empty;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            List<string> errorMessages = new List<string>();
            if (string.IsNullOrWhiteSpace(_tipoProcedura))
            {
                errorMessages.Add("Indicare il tipo di procedura da eseguire");
            }

            if (_elaborazioneMassivaCheck && string.IsNullOrWhiteSpace(_elaborazioneMassivaString))
            {
                errorMessages.Add("Selezionare il tipo di elaborazione massiva");
            }

            if (_forzareStudenteCheck && string.IsNullOrWhiteSpace(_forzareStudenteString))
            {
                errorMessages.Add("Indicare il codice fiscale dello studente da forzare");
            }

            if (errorMessages.Any())
            {
                string combinedErrors = string.Join(Environment.NewLine, errorMessages);
                yield return new ValidationResult(combinedErrors);
            }
        }
    }
}
