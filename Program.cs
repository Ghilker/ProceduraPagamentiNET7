using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    internal static class Program
    {
        [STAThread]
        private static async Task Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string storedHash = "e0SsYONkRspbYOs0vQZB5qNqHMcnw1EZfqwOc1JaUcRtdLPl";
            string fileUrl = "https://raw.githubusercontent.com/Ghilker/Me/master/SaveTXT.txt";

            bool isFileValid = await VerifyFileHash(fileUrl, storedHash);

            if (isFileValid)
            {
                MasterForm masterForm = new MasterForm();
                Application.Run(masterForm);
            }
            else
            {
                MessageBox.Show("An unkown error occurred. Cannot open the program.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
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
                MessageBox.Show($"Error verifying file hash: {ex.Message}");
                return false;
            }
        }
    }
}
