using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class ConnectionForm : Form
    {
        public string CONNECTION_STRING = string.Empty;
        public MasterForm? _masterForm;
        SqlConnection? outConnection;
        public ConnectionForm(MasterForm? masterForm)
        {
            InitializeComponent();
            LoadCredentialsDropdown();
            _masterForm = masterForm;
        }

        private void LoadCredentialsDropdown()
        {
            credentialDropdownCombo.Items.Clear(); // Clear existing items
            var allCredentials = SaveCredentials.LoadCredentialsFromFile();
            int maxWidth = 0; // Initialize a variable to keep track of the maximum width

            if (allCredentials != null)
            {
                using (Graphics g = credentialDropdownCombo.CreateGraphics())
                {
                    foreach (var entry in allCredentials)
                    {
                        if (entry.Value.ContainsKey("databaseName"))
                        {
                            string itemText = $"{entry.Key}: {entry.Value["databaseName"]}";
                            credentialDropdownCombo.Items.Add(itemText);

                            // Measure the width of the text
                            int itemWidth = TextRenderer.MeasureText(g, itemText, credentialDropdownCombo.Font).Width;

                            // Update the maximum width if this item's width is greater
                            if (itemWidth > maxWidth)
                            {
                                maxWidth = itemWidth;
                            }
                        }
                        else
                        {
                            Logger.LogDebug(null, $"Invalid credential format for key: {entry.Key}");
                        }
                    }
                }
            }
            else
            {
                Logger.LogDebug(null, "No credentials loaded from file");
            }

            // Set the dropdown width, adding some padding
            credentialDropdownCombo.DropDownWidth = maxWidth + SystemInformation.VerticalScrollBarWidth;

            if (credentialDropdownCombo.Items.Count > 0)
            {
                credentialDropdownCombo.SelectedIndex = 0; // Optionally, select the first item by default
            }
        }

        private void CredentialDropdownCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (outConnection != null && Utilities.CanConnectToDatabase(outConnection))
            {
                outConnection.Close();
                connectionLabel.Text = $"Non connesso";
                Logger.LogInfo(null, $"Disconnesso dal server");
                connectionLabel.ForeColor = Color.Red;
            }

            string? nullableIdentifier = credentialDropdownCombo.SelectedItem.ToString();
            if (nullableIdentifier == null)
            {
                Logger.LogDebug(null, "No credentials found");
                return;
            }
            // Split the selected item to get the key part
            string[] parts = nullableIdentifier.Split(':');
            if (parts.Length < 2)
            {
                Logger.LogDebug(null, "Invalid credential format");
                return;
            }
            string selectedIdentifier = parts[0].Trim();
            var allCredentials = SaveCredentials.LoadCredentialsFromFile();
            if (allCredentials != null && allCredentials.ContainsKey(selectedIdentifier))
            {
                Hashtable credentials = allCredentials[selectedIdentifier];
                // Now set your text fields based on `credentials` Hashtable
                serverIP.Text = credentials["serverIP"]?.ToString();
                databaseName.Text = credentials["databaseName"]?.ToString();
                userID.Text = credentials["userID"]?.ToString();
                password.Text = credentials["password"]?.ToString();
            }
        }

        private void ConnectionButton_Click(object sender, EventArgs e)
        {
            Dictionary<string, Hashtable> credentials = new();
            Hashtable credential = new();

            // Load credentials from file if text fields are empty
            if (string.IsNullOrEmpty(serverIP.Text) &&
                string.IsNullOrEmpty(databaseName.Text) &&
                string.IsNullOrEmpty(userID.Text) &&
                string.IsNullOrEmpty(password.Text))
            {
                Dictionary<string, Hashtable>? nullableCredentials = SaveCredentials.LoadCredentialsFromFile();
                if (nullableCredentials == null)
                {
                    Logger.LogWarning(null, "No saved credentials found. Please enter the connection details.");
                    return;
                }
                credentials = nullableCredentials;
            }
            else
            {
                // Update credentials from text boxes
                credential["serverIP"] = serverIP.Text;
                credential["databaseName"] = databaseName.Text;
                credential["userID"] = userID.Text;
                credential["password"] = password.Text;
            }

            // Save the credentials to a file if the checkbox is checked
            if (memorizeConnectionCheckBox.Checked)
            {
                SaveCredentials.SaveCredentialsToFile(userID.Text, credential);
            }

            // Construct the connection string
            CONNECTION_STRING = $"Server={credential["serverIP"]};Database={credential["databaseName"]};User Id={credential["userID"]};Password={credential["password"]};";

            // Check if the database can be reached
            if (!Utilities.CanConnectToDatabase(CONNECTION_STRING, out outConnection))
            {
                Logger.LogWarning(null, $"Impossibile connettersi al database: {databaseName.Text}");
                return;
            }

            connectionLabel.Text = $"Connesso {databaseName.Text}";
            Logger.LogDebug(null, $"Connesso al server {databaseName.Text}");
            connectionLabel.ForeColor = Color.DarkGreen;

            _masterForm.SetupConnection(outConnection);
        }
    }
}
