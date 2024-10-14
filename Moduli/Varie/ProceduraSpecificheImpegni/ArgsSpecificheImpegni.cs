using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsSpecificheImpegni : IValidatableObject
    {
        [Required(ErrorMessage = "Selezionare il file excel")]
        public string _selectedFile { get; set; }

        public string _numDetermina { get; set; }

        public string _selectedDate { get; set; }

        [Required(ErrorMessage = "Indicare l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy")]
        public string _selectedAA { get; set; }

        public bool _soloApertura { get; set; }
        public string _descrDetermina { get; set; }
        public bool _aperturaNuovaSpecifica { get; set; }
        public string _selectedCodBeneficio { get; set; }
        public string _impegnoPR { get; set; }
        public string _impegnoSA { get; set; }
        public string _tipoFondo { get; set; }
        public string _capitolo { get; set; }
        public string _esePR { get; set; }
        public string _eseSA { get; set; }

        public ArgsSpecificheImpegni()
        {
            _selectedFile = string.Empty;
            _numDetermina = string.Empty;
            _descrDetermina = string.Empty;
            _impegnoPR = string.Empty;
            _selectedCodBeneficio = string.Empty;
            _impegnoSA = string.Empty;
            _eseSA = string.Empty;
            _esePR = string.Empty;
            _capitolo = string.Empty;
            _descrDetermina = string.Empty;
            _selectedAA = string.Empty;
            _tipoFondo = string.Empty;
            _selectedDate = string.Empty;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!_aperturaNuovaSpecifica)
            {
                yield break;
            }

            List<string> errorMessages = new List<string>();

            if (string.IsNullOrWhiteSpace(_selectedCodBeneficio))
            {
                errorMessages.Add("Indicare il codice beneficio.");
            }
            if (string.IsNullOrWhiteSpace(_impegnoPR))
            {
                errorMessages.Add("Indicare l'impegno della prima rata.");
            }
            if (string.IsNullOrWhiteSpace(_impegnoSA))
            {
                errorMessages.Add("Indicare l'impegno del saldo.");
            }
            if (string.IsNullOrWhiteSpace(_tipoFondo))
            {
                errorMessages.Add("Indicare il tipo di fondo.");
            }
            if (string.IsNullOrWhiteSpace(_esePR))
            {
                errorMessages.Add("Indicare l'esercizio finanziario della prima rata.");
            }
            if (string.IsNullOrWhiteSpace(_eseSA))
            {
                errorMessages.Add("Indicare l'esercizio finanziario del saldo.");
            }

            if (errorMessages.Any())
            {
                string combinedErrors = string.Join(Environment.NewLine, errorMessages);
                yield return new ValidationResult(combinedErrors);
            }
        }
    }
}
