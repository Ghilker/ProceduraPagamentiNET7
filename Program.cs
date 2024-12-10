using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ProcedureNet7
{
    internal static class Program
    {
        private const string RegistryKeyPath = @"SOFTWARE\ProcedureNet7";
        private const string RegistryUsernameKey = "Username";
        private const string RegistryPasswordHashKey = "PasswordHash";
        private const string RegistryExpirationDateKey = "ExpirationDate";
        private const string RegistryUserIDKey = "UserID";
        private const string RegistryUserTierKey = "UserTier";

        // Booleans to enable or disable checks
        private static readonly bool DisableFileCheck = false;
        private static readonly bool DisableUserCredentialCheck = true;

        // Track login attempts
        private static int loginAttempts = 0;
        private static readonly int maxLoginAttempts = 5;
        private static readonly TimeSpan lockoutDuration = TimeSpan.FromMinutes(5);
        private static DateTime lockoutEndTime;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

#if PAGAMENTI || VARIE || VERIFICHE

            MessageBox.Show("L'applicazione non è più supportata, usare la versione Debug.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
            return;

#endif

            bool isFileValid = false;
            bool isUserValid = false;
            int userID = 0;
            string userTier = "Operatore";

            if (!DisableFileCheck)
            {
                isFileValid = Task.Run(() => VerifyFileStatus()).Result;
            }

            if (!isFileValid)
            {
                MessageBox.Show("An unknown error occurred. Closing.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }

            if (isFileValid)
            {
                MasterForm masterForm = new MasterForm(userID, userTier);
                Application.Run(masterForm);
            }
            else
            {
                MessageBox.Show("Invalid credentials or account disabled/expired.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        private static async Task<bool> VerifyFileStatus()
        {
            string apiUrl = "https://procedurelavoro.altervista.org/check_file_status.php";

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var response = await client.GetStringAsync(apiUrl);
                    dynamic jsonResponse = JsonConvert.DeserializeObject(response);

                    if (jsonResponse.status == "success" && jsonResponse.isValid == 1)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Exception: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return false;
        }
    }
}
