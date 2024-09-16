using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class UniElaborazioneDati : Form
    {
        private DataGridView dataGridView;
        private List<StudenteElaborazione> studentiConErrori;

        public UniElaborazioneDati(DataTable fileStudenti, Form mainForm, List<StudenteElaborazione> studentiConErrori)
        {
            InitializeComponent();
            this.studentiConErrori = studentiConErrori;

            // Add a search box
            TextBox searchBox = new TextBox
            {
                PlaceholderText = "Search...",
                Dock = DockStyle.Top
            };
            this.Controls.Add(searchBox);

            // Create the DataGridView using the utility method
            dataGridView = Utilities.CreateDataGridView(fileStudenti, mainForm, panel1);

            // Add a button column for removing students
            DataGridViewButtonColumn removeButtonColumn = new DataGridViewButtonColumn
            {
                Name = "RemoveButton",
                HeaderText = "Remove Student",
                Text = "Remove",
                UseColumnTextForButtonValue = true,
            };
            dataGridView.Columns.Add(removeButtonColumn);

            // Hook into CellContentClick to handle button clicks
            dataGridView.CellContentClick += DataGridView_CellContentClick;

            // Hook into CellBeginEdit and CellEndEdit to manage editability and track changes
            dataGridView.CellBeginEdit += DataGridView_CellBeginEdit;
            dataGridView.CellEndEdit += DataGridView_CellEndEdit;

            // Hook into the Shown event to apply highlighting
            this.Shown += new EventHandler(UniElaborazioneDati_Shown);

            // Optional: Implement search functionality
            searchBox.TextChanged += (sender, e) =>
            {
                string searchValue = searchBox.Text.ToLower();
                (dataGridView.DataSource as DataTable).DefaultView.RowFilter = $"COD_FISCALE LIKE '%{searchValue}%'";
            };
        }

        // Handle button click events in the DataGridView
        private void DataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == dataGridView.Columns["RemoveButton"].Index && e.RowIndex >= 0)
            {
                // Get the selected row's COD_FISCALE value
                string codFiscale = dataGridView.Rows[e.RowIndex].Cells["COD_FISCALE"].Value?.ToString().ToUpper();

                // Find the student in the studentiConErrori list and mark them as "escluso"
                var studente = studentiConErrori.FirstOrDefault(s => s.codFiscale == codFiscale);
                if (studente != null)
                {
                    studente.daRimuovere = true;
                    MessageBox.Show($"Lo studente con CF: {codFiscale} è stato rimosso.");

                    // Optionally, you can remove the row from the DataGridView or leave it for reference
                    dataGridView.Rows.RemoveAt(e.RowIndex);
                }
            }
        }

        // Apply the highlighting after the form is fully shown
        private void UniElaborazioneDati_Shown(object sender, EventArgs e)
        {
            HighlightErrorCells(studentiConErrori);
        }

        // Highlight cells with errors
        private void HighlightErrorCells(List<StudenteElaborazione> studenteElaborazioneList)
        {
            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                // Find the corresponding student object
                string codFiscale = row.Cells["COD_FISCALE"].Value?.ToString().ToUpper();
                var studente = studenteElaborazioneList.FirstOrDefault(s => s.codFiscale == codFiscale);

                if (studente != null && studente.colErroriElaborazione != null)
                {
                    // Highlight each cell that contains an error
                    foreach (string erroredColumn in studente.colErroriElaborazione)
                    {
                        if (dataGridView.Columns.Contains(erroredColumn))
                        {
                            DataGridViewCell cell = row.Cells[erroredColumn];
                            cell.Style.BackColor = Color.PaleVioletRed; // Highlight the error cells
                        }
                    }
                }
            }
        }

        // Enable editing only for cells with errors
        private void DataGridView_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            var row = dataGridView.Rows[e.RowIndex];
            string codFiscale = row.Cells["COD_FISCALE"].Value?.ToString().ToUpper();
            var studente = studentiConErrori.FirstOrDefault(s => s.codFiscale == codFiscale);

            if (studente != null && studente.colErroriElaborazione != null)
            {
                string columnName = dataGridView.Columns[e.ColumnIndex].Name;
                if (!studente.colErroriElaborazione.Contains(columnName))
                {
                    e.Cancel = true; // Cancel editing if the column is not in the error list
                }
            }
            else
            {
                e.Cancel = true; // Cancel if the student is not found
            }
        }

        // Track changes and update the corresponding StudenteElaborazione object
        private void DataGridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            var row = dataGridView.Rows[e.RowIndex];
            string codFiscale = row.Cells["COD_FISCALE"].Value?.ToString().ToUpper();
            var studente = studentiConErrori.FirstOrDefault(s => s.codFiscale == codFiscale);

            if (studente != null && studente.colErroriElaborazione != null)
            {
                string columnName = dataGridView.Columns[e.ColumnIndex].Name;
                if (studente.colErroriElaborazione.Contains(columnName))
                {
                    // Update the corresponding property in the StudenteElaborazione object
                    string newValue = row.Cells[e.ColumnIndex].Value?.ToString();
                    UpdateStudentProperty(studente, columnName, newValue); // Update the student object

                    // Remove the error from the colErroriElaborazione list
                    studente.colErroriElaborazione.Remove(columnName);

                    // Reset the cell style (remove error highlight)
                    row.Cells[e.ColumnIndex].Style.BackColor = Color.White;
                }
            }
        }

        // Update the corresponding property in the StudenteElaborazione object
        private void UpdateStudentProperty(StudenteElaborazione studente, string columnName, string newValue)
        {
            switch (columnName)
            {
                case "TIPO_CORSO_UNI":
                    studente.tipoCorsoUni = newValue;
                    break;
                case "TIPO_ISCRIZIONE_UNI":
                    studente.tipoIscrizioneUni = newValue;
                    break;
                case "CONDIZIONE":
                    studente.iscrCondizione = newValue == "TRUE" || newValue == "VERO" || newValue == "SI";
                    break;
                case "DESCR_CORSO_UNI":
                    studente.descrCorsoUni = newValue;
                    break;
                case "ANNO_CORSO_UNI":
                    if (int.TryParse(newValue, out int annoCorso))
                    {
                        studente.annoCorsoUni = annoCorso;
                    }
                    break;
                case "ANNO_IMMATRICOLAZIONE_UNI":
                    studente.aaImmatricolazioneUni = newValue;
                    break;
                case "CREDITI_UNI":
                    if (int.TryParse(newValue, out int crediti))
                    {
                        studente.creditiConseguitiUni = crediti;
                    }
                    break;
                case "CREDITI_CONVALIDATI":
                    if (int.TryParse(newValue, out int creditiConvalidati))
                    {
                        studente.creditiConvalidatiUni = creditiConvalidati;
                    }
                    break;
                case "TASSA_REGIONALE":
                    studente.tassaRegionalePagata = newValue == "TRUE" || newValue == "VERO" || newValue == "SI";
                    break;
                // Add other cases if there are more fields in the StudenteElaborazione class
                default:
                    break;
            }
        }

        // Button click event to apply changes and close the form
        private void button1_Click_1(object sender, EventArgs e)
        {
            // Close the form with DialogResult.OK
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // Button click event to cancel and close the form without saving
        private void button2_Click_1(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
