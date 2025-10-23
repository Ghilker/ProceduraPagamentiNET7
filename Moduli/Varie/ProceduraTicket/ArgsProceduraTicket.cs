using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ArgsProceduraTicket : IValidatableObject
    {

        public List<bool> _ticketChecks { get; set; }
        [Required(ErrorMessage = "Selezionare il file dei ticket")]
        public string _ticketFilePath { get; set; }
        public string _mailFilePath { get; set; }

        public ArgsProceduraTicket()
        {
            _ticketChecks = new List<bool>();
            _ticketFilePath = string.Empty;
            _mailFilePath = string.Empty;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (_ticketChecks == null || _ticketChecks.Count == 0)
            {
                yield return new ValidationResult("Errore nella costruzione della lista.");
            }

            bool mailFilePathLoaded = !string.IsNullOrWhiteSpace(_mailFilePath);

            if (!(mailFilePathLoaded && _ticketChecks[0]))
            {
                yield return new ValidationResult("Indicare il file mail.");
            }
        }
    }
}
