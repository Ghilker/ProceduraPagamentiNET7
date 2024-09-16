using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class StudentOverview : Form
    {
        private Dictionary<string, StudentePagam> daPagare;
        private List<StudentePagam> studenti;
        private DataGridView dataGridView;

        public StudentOverview(List<StudentePagam> studenti, ref Dictionary<string, StudentePagam> daPagare, Form mainForm)
        {
            InitializeComponent();
            this.daPagare = daPagare;
            this.studenti = studenti;

            // Add a search box
            TextBox searchBox = new TextBox
            {
                PlaceholderText = "Search...",
                Dock = DockStyle.Top
            };
            searchBox.TextChanged += SearchBox_TextChanged;
            this.Controls.Add(searchBox);

            PopulateDataGridView(studenti, mainForm);
        }

        private void PopulateDataGridView(List<StudentePagam> studenti, Form mainForm)
        {
            DataTable studentiDt = Utilities.ConvertListToDataTable(studenti);

            // Add necessary columns for buttons
            studentiDt.Columns.Add("assegnazioniCheck");
            studentiDt.Columns.Add("reversaliCheck");
            studentiDt.Columns.Add("detrazioniCheck");
            studentiDt.Columns.Add("pagamentiEffettuatiCheck");

            dataGridView = Utilities.CreateDataGridView(studentiDt, mainForm, panel1, OnFiscalCodeClick);

            // Add custom button columns based on list availability
            this.Load += (s, e) => AddCustomButtonColumns();
            dataGridView.CellClick += DataGridView_CellClick;
        }

        private void AddCustomButtonColumns()
        {
            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                if (row.Index < studenti.Count)
                {
                    var studente = studenti[row.Index];

                    if (studente.assegnazioni != null && studente.assegnazioni.Count > 0)
                    {
                        var finalStatus = GetAssegnazioneStatus(studente.assegnazioni);
                        AddButtonCell(row, "Assegnazioni", "assegnazioniCheck", finalStatus);
                    }
                    if (studente.reversali != null && studente.reversali.Count > 0)
                    {
                        AddButtonCell(row, "Reversali", "reversaliCheck");
                    }
                    if (studente.detrazioni != null && studente.detrazioni.Count > 0)
                    {
                        AddButtonCell(row, "Detrazioni", "detrazioniCheck");
                    }
                    if (studente.pagamentiEffettuati != null && studente.pagamentiEffettuati.Count > 0)
                    {
                        AddButtonCell(row, "Pagamenti", "pagamentiEffettuatiCheck");
                    }
                }
            }
        }

        private AssegnazioneDataCheck GetAssegnazioneStatus(List<Assegnazione> assegnazioni)
        {
            AssegnazioneDataCheck finalStatus = AssegnazioneDataCheck.Corretto;

            foreach (var assegnazione in assegnazioni)
            {
                if (assegnazione.statoCorrettezzaAssegnazione != AssegnazioneDataCheck.Corretto)
                {
                    finalStatus = assegnazione.statoCorrettezzaAssegnazione;
                    break; // Exit as soon as we find the first non-Corretto status
                }
            }

            return finalStatus;
        }
        private void AddButtonCell(DataGridViewRow row, string buttonText, string columnName, AssegnazioneDataCheck? statoCorrettezzaAssegnazione = null)
        {
            if (!dataGridView.Columns.Contains(columnName))
            {
                DataGridViewButtonColumn buttonColumn = new DataGridViewButtonColumn
                {
                    HeaderText = buttonText,
                    Text = "View",
                    UseColumnTextForButtonValue = true,
                    Name = columnName,
                    FlatStyle = FlatStyle.Flat
                };
                dataGridView.Columns.Add(buttonColumn);
            }

            DataGridViewButtonCell buttonCell = new DataGridViewButtonCell
            {
                Value = "View",
                UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat
            };

            // Determine the color based on statoCorrettezzaAssegnazione
            switch (statoCorrettezzaAssegnazione)
            {
                case AssegnazioneDataCheck.Corretto:
                    buttonCell.Style.BackColor = Color.LightGreen;
                    buttonCell.Style.ForeColor = Color.Black;
                    break;
                case AssegnazioneDataCheck.Eccessivo:
                    buttonCell.Style.BackColor = Color.LightSalmon;
                    buttonCell.Style.ForeColor = Color.Black;
                    break;
                case AssegnazioneDataCheck.Incorretto:
                    buttonCell.Style.BackColor = Color.IndianRed;
                    buttonCell.Style.ForeColor = Color.Black;
                    break;
                case AssegnazioneDataCheck.DataUguale:
                    buttonCell.Style.BackColor = Color.LightYellow;
                    buttonCell.Style.ForeColor = Color.Black;
                    break;
                case AssegnazioneDataCheck.DataDecorrenzaMinoreDiMin:
                    buttonCell.Style.BackColor = Color.LightBlue;
                    buttonCell.Style.ForeColor = Color.Black;
                    break;
                case AssegnazioneDataCheck.DataFineAssMaggioreMax:
                    buttonCell.Style.BackColor = Color.LightSteelBlue;
                    buttonCell.Style.ForeColor = Color.Black;
                    break;
                case null:
                    buttonCell.Style.BackColor = Color.LightGray;
                    buttonCell.Style.ForeColor = Color.Black;
                    break;
            }

            row.Cells[dataGridView.Columns[columnName].Index] = buttonCell;
        }


        private void DataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                var columnName = dataGridView.Columns[e.ColumnIndex].Name;
                if (e.RowIndex < studenti.Count)
                {
                    var studente = studenti[e.RowIndex];
                    var list = GetListByColumnName(studente, columnName);
                    if (list != null && list.Count > 0)
                    {
                        // Get the location of the view button
                        Point viewButtonLocation = dataGridView.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false).Location;
                        viewButtonLocation = dataGridView.PointToScreen(viewButtonLocation);

                        var listForm = new ListForm(list, viewButtonLocation);
                        listForm.ShowDialog();
                    }
                }
            }
        }

        private IList? GetListByColumnName(StudentePagam studente, string columnName)
        {
            return columnName switch
            {
                "assegnazioniCheck" => studente.assegnazioni,
                "reversaliCheck" => studente.reversali,
                "detrazioniCheck" => studente.detrazioni,
                "pagamentiEffettuatiCheck" => studente.pagamentiEffettuati,
                _ => null
            };
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            TextBox searchBox = sender as TextBox;
            if (dataGridView.DataSource is DataTable dt)
            {
                dt.DefaultView.RowFilter = string.Format("cognome LIKE '%{0}%' OR nome LIKE '%{0}%' OR codFiscale LIKE '%{0}%'", searchBox.Text);
            }
        }

        private void OnFiscalCodeClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                DataGridView? dgv = sender as DataGridView;
                if (dgv != null)
                {
                    // Get the value of the clicked cell
                    string cellValue = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString();

                    // Regex to match a typical fiscal code format (adjust the pattern as necessary)
                    string fiscalCodePattern = @"^[A-Z]{6}\d{2}[A-Z]\d{2}[A-Z]\d{3}[A-Z]$";
                    if (Regex.IsMatch(cellValue, fiscalCodePattern))
                    {
                        // Confirm removal
                        var confirmResult = MessageBox.Show($"Vuoi davvero cancellare dal pagamento lo studente con codice fiscale: {cellValue}?",
                                                            "Confirm Delete",
                                                            MessageBoxButtons.YesNo);
                        if (confirmResult == DialogResult.Yes)
                        {
                            // Remove from dictionary
                            if (daPagare.ContainsKey(cellValue))
                            {
                                daPagare.Remove(cellValue);
                            }

                            // Remove from DataGridView
                            dgv.Rows.RemoveAt(e.RowIndex);
                        }
                    }
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }

}

