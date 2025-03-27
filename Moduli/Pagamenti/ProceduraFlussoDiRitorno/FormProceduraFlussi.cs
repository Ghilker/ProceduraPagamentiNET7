using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class FormProceduraFlussi : Form
    {
        MasterForm? _masterForm;
        string selectedFilePath = string.Empty;
        public FormProceduraFlussi(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
        }

        private void ProceduraFlussoRitornoFileBtn_Click(object sender, EventArgs e)
        {
            Utilities.ChooseFileAndSetPath(proceduraFlussoRitornoFileLbl, openFileDialog, ref selectedFilePath);
        }

        private void RunProcedureBtnClick(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunFlussoRitorno);
        }

        private void RunFlussoRitorno(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }
                ArgsValidation argsValidation = new ArgsValidation();
                bool useFlussoNome = proceduraFlussoRitornoNomeFileCheck.Checked;
                string nomeFile = Path.GetFileNameWithoutExtension(selectedFilePath);

                ArgsProceduraFlussoDiRitorno argsFlusso = new()
                {
                    _selectedFileFlusso = selectedFilePath,
                    _selectedImpegnoProvv = useFlussoNome ? nomeFile : proceduraFlussoRitornoNumMandatoTxt.Text
                };
                argsValidation.Validate(argsFlusso);
                using ProceduraFlussoDiRitorno flusso = new(_masterForm, mainConnection);
                flusso.RunProcedure(argsFlusso);
            }
            catch (ValidationException ex)
            {
                Logger.LogWarning(100, "Errore compilazione procedura: " + ex.Message);
            }
            catch
            {
                throw;
            }
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {

                    string password = EncryptionHelper.Decrypt("Ov+poxP4FNAUOx2iPfWC2kYWydnZF/0yBeW0Szp2GKLHeHkBGBkIjM4/BdvTX0lD");

                    // Create the request object
                    StudentDataRequest studentDataRequest = new StudentDataRequest
                    {
                        logonName = "laziodisco.api.user",
                        password = password,
                        StudentId = "349409"
                    };

                    // POST the object as JSON to your endpoint
                    // Adjust the URL to match your route exactly.
                    // If your controller is named 'StudentDataController' and the method is [HttpPost][Route("[action]")]
                    // then the actual route often ends up being: ".../StudentData/GetStudentDataInfo"
                    HttpResponseMessage response = await client.PostAsJsonAsync(
                        "https://mensadisco-api.laziodisco.it/StudentData/GetStudentDataInfo",
                        studentDataRequest
                    );

                    // Check if the response is successful
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        MessageBox.Show($"Response:\n{responseBody}");
                    }
                    else
                    {
                        MessageBox.Show($"Error: {response.StatusCode} - {response.ReasonPhrase}");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred: {ex.Message}");
                }
            }
        }

        public class StudentDataRequest : ItemBaseRequest
        {
            public string StudentId { get; set; }
        }

        public class ItemBaseRequest
        {
            /// <summary>
            /// Logon
            /// </summary>
            public string logonName { get; set; }

            /// <summary>
            /// Password
            /// </summary>
            public string password { get; set; }
        }
        public static class EncryptionHelper
        {
            static readonly string EncriptionKey = "$C&F)J@NcRfUjWnZr4u7x!A%D*G-KaPdSgVkYp2s5v8y/B?E(H+MbQeThWmZq4t6";

            public static string Encrypt(string clearText, string encriptionKey = "")
            {
                if (string.IsNullOrEmpty(encriptionKey))
                {
                    encriptionKey = EncriptionKey;
                }

                byte[] clearBytes = Encoding.Unicode.GetBytes(clearText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(encriptionKey, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(clearBytes, 0, clearBytes.Length);
                            cs.Close();
                        }
                        clearText = Convert.ToBase64String(ms.ToArray());
                    }
                }
                return clearText;
            }
            public static string Decrypt(string cipherText, string encriptionKey = "")
            {
                if (string.IsNullOrEmpty(encriptionKey))
                {
                    encriptionKey = EncriptionKey;
                }

                cipherText = cipherText.Replace(" ", "+");
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(encriptionKey, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, 0, cipherBytes.Length);
                            cs.Close();
                        }
                        cipherText = Encoding.Unicode.GetString(ms.ToArray());
                    }
                }
                return cipherText;
            }
        }
    }
}
