using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsProceduraGeneratoreFlussi : IValidatableObject
    {
        [Required(ErrorMessage = "Il percorso del file è obbligatorio.")]
        public string FilePath { get; set; } = string.Empty;

        [Required(ErrorMessage = "Il percorso della cartella è obbligatorio.")]
        public string FolderPath { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateProperty(FilePath,
                new ValidationContext(this, null, null) { MemberName = nameof(FilePath) },
                results);
            Validator.TryValidateProperty(FolderPath,
                new ValidationContext(this, null, null) { MemberName = nameof(FolderPath) },
                results);

            return results;
        }
    }
}
