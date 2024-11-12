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
        private static readonly bool DisableUserCredentialCheck = false;

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

            if (!DisableUserCredentialCheck)
            {
                if (DateTime.Now < lockoutEndTime)
                {
                    MessageBox.Show($"Too many failed login attempts. Please try again after {lockoutEndTime}.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                    return;
                }

                string username, passwordHash;

                if (TryGetStoredCredentials(out username, out passwordHash, out userID, out userTier))
                {
                    isUserValid = Task.Run(() => ValidateStoredUser(username, passwordHash)).Result;
                }

                if (!isUserValid)
                {
                    username = Microsoft.VisualBasic.Interaction.InputBox("Enter your username:", "Login", "");
                    if (string.IsNullOrEmpty(username)) Application.Exit();

                    string password = Microsoft.VisualBasic.Interaction.InputBox("Enter your password:", "Login", "", -1, -1);
                    if (string.IsNullOrEmpty(password)) Application.Exit();

                    passwordHash = ComputeHash(password);
                    var validationResult = Task.Run(() => ValidateUser(username, passwordHash)).Result;

                    isUserValid = validationResult.Item1;
                    userID = validationResult.Item2;
                    userTier = validationResult.Item3;

                    if (isUserValid)
                    {
                        StoreCredentials(username, passwordHash, userID, userTier);
                        loginAttempts = 0; // Reset attempts on successful login
                    }
                    else
                    {
                        loginAttempts++;
                        if (loginAttempts >= maxLoginAttempts)
                        {
                            lockoutEndTime = DateTime.Now.Add(lockoutDuration);
                            MessageBox.Show($"Too many failed login attempts. Please try again after {lockoutEndTime}.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Application.Exit();
                        }
                    }
                }
            }

            if (isFileValid && isUserValid)
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


        private static bool TryGetStoredCredentials(out string username, out string passwordHash, out int userID, out string userTier)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                if (key != null)
                {
                    username = key.GetValue(RegistryUsernameKey) as string;
                    passwordHash = key.GetValue(RegistryPasswordHashKey) as string;
                    string expirationDateStr = key.GetValue(RegistryExpirationDateKey) as string;
                    userID = (int)(key.GetValue(RegistryUserIDKey) ?? 0);
                    userTier = key.GetValue(RegistryUserTierKey) as string;

                    if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(passwordHash) && DateTime.TryParse(expirationDateStr, out DateTime expirationDate))
                    {
                        return true;
                    }
                }
            }
            username = null;
            passwordHash = null;
            userID = 0;
            userTier = "Operatore";
            return false;
        }

        private static void StoreCredentials(string username, string passwordHash, int userID, string userTier)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
            {
                key.SetValue(RegistryUsernameKey, username);
                key.SetValue(RegistryPasswordHashKey, passwordHash);
                key.SetValue(RegistryExpirationDateKey, DateTime.Now.AddYears(10).ToString("yyyy-MM-dd HH:mm:ss"));
                key.SetValue(RegistryUserIDKey, userID);
                key.SetValue(RegistryUserTierKey, userTier);
            }
        }

        private static async Task<(bool, int, string)> ValidateUser(string username, string passwordHash)
        {
            string apiKey = await RetrieveApiKey(username, passwordHash);
            if (apiKey == null)
            {
                return (false, 0, "basic");
            }

            string apiUrl = "https://procedurelavoro.altervista.org/validate_user.php";

            using (HttpClient client = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "username", username },
                    { "password_hash", passwordHash },
                    { "api_key", apiKey }
                };

                var content = new FormUrlEncodedContent(values);

                try
                {
                    var response = await client.PostAsync(apiUrl, content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    dynamic jsonResponse = JsonConvert.DeserializeObject(responseString);
                    bool success = jsonResponse.status == "success";
                    int userID = success ? jsonResponse.userID : 0;
                    string userTier = success ? jsonResponse.tier : "basic";


                    return (success, userID, userTier);
                }
                catch
                {
                    return (false, 0, "basic");
                }
            }
        }

        private static async Task<string> RetrieveApiKey(string username, string passwordHash)
        {
            string apiUrl = "https://procedurelavoro.altervista.org/get_api_key.php";

            using (HttpClient client = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "username", username },
                    { "password_hash", passwordHash }
                };

                var content = new FormUrlEncodedContent(values);

                try
                {
                    var response = await client.PostAsync(apiUrl, content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    dynamic jsonResponse = JsonConvert.DeserializeObject(responseString);
                    if (jsonResponse.status == "success")
                    {
                        return jsonResponse.apiKey;
                    }
                }
                catch
                {
                    Application.Exit();
                }
            }

            return null;
        }


        private static async Task<bool> ValidateStoredUser(string username, string passwordHash)
        {
            string apiKey = await RetrieveApiKey(username, passwordHash);
            if (apiKey == null) return false;

            string apiUrl = "https://procedurelavoro.altervista.org/validate_user.php";

            using (HttpClient client = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "username", username },
                    { "password_hash", passwordHash },
                    { "api_key", apiKey }
                };

                var content = new FormUrlEncodedContent(values);

                try
                {
                    var response = await client.PostAsync(apiUrl, content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    dynamic jsonResponse = JsonConvert.DeserializeObject(responseString);
                    return jsonResponse.status == "success";
                }
                catch (Exception ex)
                {
                    // Handle exception (logging, etc.)
                    return false;
                }
            }
        }


        private static string ComputeHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static async Task<bool> VerifyFileHash(string fileUrl, string storedHash)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string fileContent = await client.GetStringAsync(fileUrl);

                    // Assuming the file contains only the hash
                    string fileHash = fileContent.Trim();

                    return fileHash == storedHash;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }

    }
}
