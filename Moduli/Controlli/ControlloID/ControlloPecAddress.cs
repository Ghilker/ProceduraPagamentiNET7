using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal static class ControlloPecAddress
    {
        public static bool IsValid(string? address, ISet<string> approvedDomains)
        {
            if (string.IsNullOrWhiteSpace(address) ||
                !MailAddress.TryCreate(address, out var parsed))
                return false;

            // Match PecHelper/EmailHelper: validate the parsed address, including
            // international domain normalization for the syntax check only.
            string normalizedAddress;
            try
            {
                normalizedAddress = Regex.Replace(parsed.Address, @"(@)(.+)$",
                    match => match.Groups[1].Value + new IdnMapping().GetAscii(match.Groups[2].Value),
                    RegexOptions.None, TimeSpan.FromMilliseconds(200));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }

            try
            {
                if (!Regex.IsMatch(normalizedAddress, @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                    RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)))
                    return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }

            // The caller loads trimmed approved domains with OrdinalIgnoreCase.
            return approvedDomains.Contains(parsed.Host.Trim());
        }
    }
}
