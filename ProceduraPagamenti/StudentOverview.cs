using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class StudentOverview : Form
    {
        private Dictionary<string, Studente> daPagare;

        public StudentOverview(List<Studente> studenti, ref Dictionary<string, Studente> daPagare, Form mainForm)
        {
            InitializeComponent();
            this.daPagare = daPagare; // Keep a reference to the dictionary
            PopulateDataGridView(studenti, mainForm);
        }

        private void PopulateDataGridView(List<Studente> studenti, Form mainForm)
        {
            DataTable studentiDt = Utilities.ConvertListToDataTable(studenti);

            DataGridView dgv = Utilities.CreateDataGridView(studentiDt, mainForm, panel1, OnFiscalCodeClick);
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
