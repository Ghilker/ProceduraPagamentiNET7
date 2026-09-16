using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class FormVerifica : Form
    {
        MasterForm? _masterForm;
        string selectedFolderPath = string.Empty;
        public FormVerifica(MasterForm masterForm)
        {
            _masterForm = masterForm;
            InitializeComponent();
        }

        private void RunVerifica(SqlConnection mainConnection)
        {
            try
            {
                if (_masterForm == null)
                {
                    throw new Exception("Master form non può essere nullo a questo punto!");
                }

                string codiceFiscale = NormalizeCodiceFiscale(verificaCodiceFiscaleText.Text);
                string queryCodiciFiscali = (verificaQueryCodiciFiscaliText.Text ?? string.Empty).Trim();

                List<string>? codiciFiscali = null;

                // La query, quando presente, sostituisce il codice fiscale singolo.
                if (!string.IsNullOrWhiteSpace(queryCodiciFiscali))
                {
                    codiciFiscali = LoadCodiciFiscaliFromQuery(mainConnection, queryCodiciFiscali);
                }
                else if (!string.IsNullOrWhiteSpace(codiceFiscale))
                {
                    ValidateCodiceFiscale(codiceFiscale);
                    codiciFiscali = new List<string> { codiceFiscale };
                }
                int faseElaborativa = 0;
                bool scriviSulDatabase = false;
                Invoke(new MethodInvoker(() =>
                {
                    faseElaborativa = verificaFaseElaborativaCombo.SelectedIndex == 1 ? 2 : 1;
                    scriviSulDatabase = verificaScriviDatabaseCheck.Checked;
                }));
                ArgsValidation argsValidation = new ArgsValidation();
                ArgsVerifica argsVerifica = new ArgsVerifica
                {
                    _selectedAA = verificaAAText.Text,
                    _faseElaborativa = faseElaborativa,
                    _scriviSulDatabase = scriviSulDatabase,
                    _codiciFiscali = codiciFiscali
                };
                argsValidation.Validate(argsVerifica);
                ProcedureNet7.Verifica.Verifica verifica = new(_masterForm, mainConnection);
                verifica.RunProcedure(argsVerifica);
            }
            catch (ValidationException ex)
            {
                Logger.LogWarning(100, "Errore compilazione procedura: " + ex.Message);
            }
            catch (SqlException ex)
            {
                Logger.LogWarning(100, "Errore esecuzione query codici fiscali: " + ex.Message);
            }
            catch
            {
                throw;
            }
        }

        private static List<string> LoadCodiciFiscaliFromQuery(SqlConnection connection, string query)
        {
            using var cmd = new SqlCommand(query, connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };

            using var reader = cmd.ExecuteReader(CommandBehavior.SingleResult);

            if (reader.FieldCount != 1)
            {
                throw new ValidationException(
                    "La query dei codici fiscali deve restituire esattamente una colonna.");
            }

            var codici = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var valoriNonValidi = new List<string>();

            while (reader.Read())
            {
                string valore = reader.IsDBNull(0)
                    ? string.Empty
                    : NormalizeCodiceFiscale(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture));

                if (!IsValidCodiceFiscale(valore))
                {
                    if (valoriNonValidi.Count < 5)
                        valoriNonValidi.Add(string.IsNullOrWhiteSpace(valore) ? "<vuoto>" : valore);
                    continue;
                }

                codici.Add(valore);
            }

            if (valoriNonValidi.Count > 0)
            {
                throw new ValidationException(
                    "La query ha restituito codici fiscali non validi: " +
                    string.Join(", ", valoriNonValidi) + ".");
            }

            if (codici.Count == 0)
            {
                throw new ValidationException(
                    "La query non ha restituito alcun codice fiscale valido.");
            }

            Logger.LogInfo(null, $"[FormVerifica] Codici fiscali estratti dalla query: {codici.Count}");
            return codici.OrderBy(cf => cf, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeCodiceFiscale(string? codiceFiscale)
            => (codiceFiscale ?? string.Empty).Trim().ToUpperInvariant();

        private static void ValidateCodiceFiscale(string codiceFiscale)
        {
            if (!IsValidCodiceFiscale(codiceFiscale))
            {
                throw new ValidationException(
                    "Il codice fiscale deve contenere 16 caratteri alfanumerici.");
            }
        }

        private static bool IsValidCodiceFiscale(string codiceFiscale)
            => codiceFiscale.Length == 16 &&
               codiceFiscale.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));

        private void circularButton1_Click(object sender, EventArgs e)
        {
            if (_masterForm == null)
            {
                return;
            }

            _masterForm.RunBackgroundWorker(RunVerifica);
        }
    }
}
