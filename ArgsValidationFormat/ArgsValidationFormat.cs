using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ValidDateFormatAttribute : ValidationAttribute
    {
        public override bool IsValid(object value)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return false;

            return Regex.IsMatch(value.ToString(), @"^\d{2}/\d{2}/\d{4}$");
        }
    }

    public class ValidAAFormatAttribute : ValidationAttribute
    {
        public override bool IsValid(object value)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return false;

            if (!Regex.IsMatch(value.ToString(), @"^[0-9]{8}$"))
            {
                return false;
            }

            return true;
        }
    }
}
