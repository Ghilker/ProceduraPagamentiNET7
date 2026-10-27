using System.Data.SqlClient;

namespace ProcedureNet7
{
    internal static class DomicilioAutomaticConnectionSettings
    {
        private const string InvalidSettingsMessage = "Connessione SQL originale non disponibile o incompleta. Riconnettersi al database prima di avviare la lavorazione.";

        public static string FromOriginal(string? original)
        {
            if (string.IsNullOrWhiteSpace(original))
                throw new InvalidOperationException(InvalidSettingsMessage);

            SqlConnectionStringBuilder settings;
            try
            {
                settings = new SqlConnectionStringBuilder(original);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException(InvalidSettingsMessage);
            }

            if (string.IsNullOrWhiteSpace(settings.InitialCatalog) ||
                !settings.IntegratedSecurity && (string.IsNullOrWhiteSpace(settings.UserID) || string.IsNullOrEmpty(settings.Password)))
                throw new InvalidOperationException(InvalidSettingsMessage);

            // Keep the legacy provider defaults when the worker uses Microsoft.Data.SqlClient.
            settings["Encrypt"] = settings.Encrypt;
            settings["TrustServerCertificate"] = settings.TrustServerCertificate;
            return settings.ConnectionString;
        }
    }
}
