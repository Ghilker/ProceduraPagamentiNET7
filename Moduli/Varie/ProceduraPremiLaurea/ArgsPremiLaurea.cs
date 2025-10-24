using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace ProcedureNet7
{
    internal class ArgsPremiLaurea : IValidatableObject
    {
        [Required(ErrorMessage = "Il percorso del file Excel di input è obbligatorio.")]
        public string FileExcelInput { get; set; } = string.Empty;

        [Required(ErrorMessage = "Il percorso del file Excel di output è obbligatorio.")]
        public string FileExcelOutput { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var errors = new List<ValidationResult>();

            // Controlla che il file di input esista
            if (string.IsNullOrWhiteSpace(FileExcelInput) || !File.Exists(FileExcelInput))
                errors.Add(new ValidationResult("Il file Excel di input non esiste o il percorso non è valido.", new[] { nameof(FileExcelInput) }));

            // Controlla che il percorso di output sia valido (non serve esista già)
            if (string.IsNullOrWhiteSpace(FileExcelOutput))
                errors.Add(new ValidationResult("Il percorso del file Excel di output non può essere vuoto.", new[] { nameof(FileExcelOutput) }));

            return errors;
        }
    }
}