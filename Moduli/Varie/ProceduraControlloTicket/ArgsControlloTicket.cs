using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProcedureNet7
{
    internal class ArgsControlloTicket : IValidatableObject
    {
        /// <summary>
        /// Path to the selected CSV file. 
        /// Add more properties as needed for your procedure.
        /// </summary>
        public string? SelectedCsvPath { get; set; }

        // Example: minimal validation
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(SelectedCsvPath))
            {
                yield return new ValidationResult("Please select a valid CSV file.",
                    new[] { nameof(SelectedCsvPath) });
            }
            // Add other validation rules as needed
        }
    }
}
