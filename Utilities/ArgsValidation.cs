using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ArgsValidation
    {
        public void Validate<T>(T instance)
        {
            List<ValidationResult> results = new();
            ValidationContext context = new(instance);
            if (!Validator.TryValidateObject(instance, context, results, true))
            {
                // Aggregate all error messages into a single string
                string errorMessage = string.Join(Environment.NewLine, results.Select(r => r.ErrorMessage));
                throw new ValidationException(errorMessage);
            }
        }
    }
}
