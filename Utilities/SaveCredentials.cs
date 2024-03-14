using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal static class SaveCredentials
    {
        // Method to save credentials to a file securely
        public static void SaveCredentialsToFile(string identifier, Hashtable credentials)
        {
            string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string filePath = Path.Combine(folderPath, "Procedures", "connectionCredentials.bin");

            Dictionary<string, Hashtable> allCredentials = new Dictionary<string, Hashtable>();

            // Load existing credentials if they exist
            if (File.Exists(filePath))
            {
                allCredentials = LoadCredentialsFromFile();
            }

            // Add or update the credentials for the given identifier
            allCredentials[identifier] = credentials;

            try
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                string jsonCredentials = System.Text.Json.JsonSerializer.Serialize(allCredentials);
                byte[] credentialsBytes = Encoding.UTF8.GetBytes(jsonCredentials);

                using (Aes aes = Aes.Create())
                {
                    using FileStream fileStream = new(filePath, FileMode.Create);
                    using (CryptoStream cryptoStream = new(fileStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(credentialsBytes, 0, credentialsBytes.Length);
                        cryptoStream.FlushFinalBlock();
                    }

                    // Save the encryption key and IV
                    SaveEncryptionKeyAndIV(aes.Key, aes.IV);
                }
                _ = MessageBox.Show($"Credentials saved securely to {filePath}");
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Failed to save credentials securely. Error: {ex.Message}");
            }
        }

        public static Dictionary<string, Hashtable>? LoadCredentialsFromFile()
        {
            string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string filePath = Path.Combine(folderPath, "Procedures", "connectionCredentials.bin");

            try
            {
                if (File.Exists(filePath))
                {
                    using FileStream fileStream = new(filePath, FileMode.Open);
                    using Aes aes = Aes.Create();
                    aes.Key = LoadEncryptionKey();
                    aes.IV = LoadEncryptionIV();

                    using MemoryStream memoryStream = new();
                    using (CryptoStream cryptoStream = new(fileStream, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        cryptoStream.CopyTo(memoryStream);
                    }

                    string jsonCredentials = Encoding.UTF8.GetString(memoryStream.ToArray());
                    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Hashtable>>(jsonCredentials);
                }
                else
                {
                    return new Dictionary<string, Hashtable>();
                }
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Failed to load credentials. Error: {ex.Message}");
                return new Dictionary<string, Hashtable>();
            }
        }

        private static void SaveEncryptionKeyAndIV(byte[] key, byte[] iv)
        {
            string keyFilePath = GetAppDataPath("encryptionKey.bin");
            string ivFilePath = GetAppDataPath("encryptionIV.bin");

            File.WriteAllBytes(keyFilePath, key);
            File.WriteAllBytes(ivFilePath, iv);
        }

        private static byte[]? LoadEncryptionKey()
        {
            string keyFilePath = GetAppDataPath("encryptionKey.bin");
            return File.Exists(keyFilePath) ? File.ReadAllBytes(keyFilePath) : null;
        }

        private static byte[]? LoadEncryptionIV()
        {
            string ivFilePath = GetAppDataPath("encryptionIV.bin");
            return File.Exists(ivFilePath) ? File.ReadAllBytes(ivFilePath) : null;
        }

        private static string GetAppDataPath(string filename)
        {
            string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appSpecificFolder = Path.Combine(folderPath, "Procedures");
            _ = Directory.CreateDirectory(appSpecificFolder);
            return Path.Combine(appSpecificFolder, filename);
        }
    }
}
