using IbanNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public static class IbanValidatorUtil
    {
        private static readonly IIbanValidator _ibanValidator = new IbanValidator();

        /// <summary>
        /// Validates an IBAN using the IbanNet package.
        /// </summary>
        /// <param name="iban">The IBAN to validate</param>
        /// <returns>true if the IBAN is valid, false otherwise.</returns>
        public static bool ValidateIban(string iban)
        {
            if (string.IsNullOrWhiteSpace(iban))
                return false;

            var result = _ibanValidator.Validate(iban);
            return result.IsValid;
        }
    }
}
