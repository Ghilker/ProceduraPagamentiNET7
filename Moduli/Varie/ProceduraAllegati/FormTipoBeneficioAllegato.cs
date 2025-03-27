using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Drawing;
using System.Windows.Forms;

namespace ProcedureNet7
{
    // Represents a single "impegno" row (or pair) from the DB.
    public class ImpegnoItem
    {
        public string ImpegnoPair { get; set; }
        public string Descr { get; set; }

        // A single property that the ComboBox can display:
        public string DisplayValue => $"{ImpegnoPair} - {Descr}";
    }

    // Class to hold the user's final selection.
    public class BenefitSelection
    {
        public string BenefitKey { get; set; }    // e.g. "BS", "PL", ...
        public string BenefitText { get; set; }   // e.g. "Borsa di studio"
        public string ImpegnoPair { get; set; }   // e.g. "123 - 456" if a pair, or just "123"
        public string Description { get; set; }   // description from DB
    }

    public partial class FormTipoBeneficioAllegato : Form
    {
        // Available benefits (still a dictionary so we can display them in the combo).
        private readonly Dictionary<string, string> tipoBenefici = new()
        {
            { "BS", "Borsa di studio" },
            { "PA", "Posto alloggio" },
            { "CI", "Contributo integrativo" },
            { "PL", "Premio di laurea" },
            { "BL", "Buono libro" }
        };

        // All impegni from the database.
        private List<ImpegnoItem> impegniList = new();

        // Store the user’s final selections (instead of using a dictionary).
        private List<BenefitSelection> selectedItems = new();

        // Database connection.
        private SqlConnection _conn;

        public FormTipoBeneficioAllegato(SqlConnection conn, string selectedAA)
        {
            InitializeComponent();
            _conn = conn;

            CreateBeneficiBox();
            LoadImpegniFromDatabase(selectedAA);
        }

        // 1) Fill the ComboBox with benefits.
        private void CreateBeneficiBox()
        {
            beneficiBox.Items.Clear();

            if (beneficiBox.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                beneficiBox.Items.Add(new { Text = "Seleziona beneficio", Value = string.Empty });
            }

            // Add each benefit from our dictionary
            foreach (var kv in tipoBenefici)
            {
                beneficiBox.Items.Add(new { Text = kv.Value, Value = kv.Key });
            }

            beneficiBox.DisplayMember = "Text";
            beneficiBox.ValueMember = "Value";

            // Reset to prompt
            if (beneficiBox.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                beneficiBox.SelectedIndex = 0;
            }
            else
            {
                beneficiBox.Text = "Seleziona beneficio";
            }
        }

        // 2) Load all impegni from DB, storing them in a typed list of ImpegnoItem.
        private void LoadImpegniFromDatabase(string selectedAA)
        {
            impegniList.Clear();

            string sql = @"
SELECT impegno_pair, descr
FROM (
    -- 1. Individual rows: all impegni (including those that are part of a pair)
    SELECT 
        CAST(num_impegno AS VARCHAR(50)) AS impegno_pair,
        descr,
        0 AS sort_order
    FROM Impegni
    WHERE anno_accademico = @AA
      AND cod_beneficio = 'BS'
    
    UNION ALL
    
    -- 2. Paired rows: concatenates impegno and descr from both rows (only for valid pairs)
    SELECT 
        CAST(i1.num_impegno AS VARCHAR(50)) + ' - ' + CAST(i2.num_impegno AS VARCHAR(50)) AS impegno_pair,
        i1.descr + ' - ' + i2.descr AS descr,
        1 AS sort_order
    FROM Impegni i1
    JOIN Impegni i2 
      ON i1.id_coppia_impegni = i2.id_coppia_impegni
    WHERE i1.anno_accademico = @AA
      AND i1.cod_beneficio = 'BS'
      AND i2.anno_accademico = @AA
      AND i2.cod_beneficio = 'BS'
      AND i1.categoria_pagamento = 'PR'
      AND i2.categoria_pagamento = 'SA'
      AND i1.id_coppia_impegni IS NOT NULL
) AS CombinedResults
ORDER BY sort_order, impegno_pair;
";

            using (var cmd = new SqlCommand(sql, _conn))
            {
                cmd.Parameters.AddWithValue("@AA", selectedAA);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var impegnoPair = reader["impegno_pair"].ToString();
                        var descr = reader["descr"].ToString();

                        var item = new ImpegnoItem
                        {
                            ImpegnoPair = impegnoPair,
                            Descr = descr
                        };
                        impegniList.Add(item);
                    }
                }
            }
        }

        // 3) Handle user selection of a benefit from the main ComboBox.
        private void beneficiBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (beneficiBox.SelectedItem == null) return;

            // If using DropDownList, ignore the prompt if it’s selected.
            if (beneficiBox.DropDownStyle == ComboBoxStyle.DropDownList && beneficiBox.SelectedIndex == 0)
                return;

            // Extract the chosen item
            dynamic item = beneficiBox.SelectedItem;
            string chosenBenefitText = item.Text;
            string chosenBenefitKey = item.Value;

            if (string.IsNullOrEmpty(chosenBenefitKey)) return;

            // Remove from the ComboBox so it can't be chosen again
            beneficiBox.Items.Remove(beneficiBox.SelectedItem);

            // Reset the selection to the prompt
            if (beneficiBox.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                beneficiBox.SelectedIndex = 0;
            }
            else
            {
                beneficiBox.SelectedIndex = -1;
                beneficiBox.Text = "Seleziona beneficio";
            }

            // If it's "BS" or "PL", we need to pick an Impegno from the DB
            if (chosenBenefitKey == "BS" || chosenBenefitKey == "PL")
            {
                ShowImpegnoSelector(chosenBenefitKey, chosenBenefitText);
            }
            else
            {
                // For everything else, we don't need an impegno => create a selection with empty fields
                var selection = new BenefitSelection
                {
                    BenefitKey = chosenBenefitKey,
                    BenefitText = chosenBenefitText,
                    ImpegnoPair = "",      // no impegno
                    Description = ""       // no description
                };

                selectedItems.Add(selection);
                // Show in the panel
                AddBeneficioToPanel(selection);
            }
        }

        // 4) Show a small panel in the middle of the form, letting the user pick from impegniList.
        private void ShowImpegnoSelector(string benefitKey, string benefitText)
        {
            var impegnoPanel = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.LightYellow,
                Size = new Size(300, 110)
            };
            // Center the panel
            impegnoPanel.Location = new Point(
                (this.ClientSize.Width - impegnoPanel.Width) / 2,
                (this.ClientSize.Height - impegnoPanel.Height) / 2
            );
            impegnoPanel.Anchor = AnchorStyles.None;

            var lbl = new Label
            {
                Text = "Seleziona impegno:",
                Location = new Point(10, 10),
                Size = new Size(150, 20)
            };

            var impegniComboBox = new ComboBox
            {
                Location = new Point(10, 35),
                Size = new Size(280, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            // Bind our typed impegniList to the ComboBox
            impegniComboBox.DataSource = new List<ImpegnoItem>(impegniList);
            impegniComboBox.DisplayMember = "DisplayValue"; // shows "impegnoPair - descr"
            // If you want to store or retrieve the actual object, just do .SelectedItem as ImpegnoItem

            if (impegniComboBox.Items.Count > 0)
                impegniComboBox.SelectedIndex = 0;

            var btnOk = new Button
            {
                Text = "OK",
                Size = new Size(70, 25),
                Location = new Point(150, 75)
            };
            btnOk.Click += (s, e) =>
            {
                // The selected item is an ImpegnoItem
                var selectedImpegnoItem = impegniComboBox.SelectedItem as ImpegnoItem;
                if (selectedImpegnoItem == null)
                {
                    return;
                }

                // Build a new BenefitSelection
                var selection = new BenefitSelection
                {
                    BenefitKey = benefitKey,
                    BenefitText = benefitText,
                    ImpegnoPair = selectedImpegnoItem.ImpegnoPair,
                    Description = selectedImpegnoItem.Descr
                };

                selectedItems.Add(selection);
                AddBeneficioToPanel(selection);

                // Clean up the panel
                this.Controls.Remove(impegnoPanel);
                impegnoPanel.Dispose();
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(70, 25),
                Location = new Point(230, 75)
            };
            btnCancel.Click += (s, e) =>
            {
                // Restore the benefit to the main ComboBox (since we canceled)
                beneficiBox.Items.Add(new { Text = benefitText, Value = benefitKey });
                if (beneficiBox.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    beneficiBox.SelectedIndex = 0;
                }
                else
                {
                    beneficiBox.SelectedIndex = -1;
                    beneficiBox.Text = "Seleziona beneficio";
                }

                // Clean up
                this.Controls.Remove(impegnoPanel);
                impegnoPanel.Dispose();
            };

            impegnoPanel.Controls.Add(lbl);
            impegnoPanel.Controls.Add(impegniComboBox);
            impegnoPanel.Controls.Add(btnOk);
            impegnoPanel.Controls.Add(btnCancel);

            this.Controls.Add(impegnoPanel);
            impegnoPanel.BringToFront();
        }

        // 5) Create a visual row in the panel for each chosen BenefitSelection.
        private void AddBeneficioToPanel(BenefitSelection selection)
        {
            // Container panel for a single row
            var itemPanel = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(BeneficiSelezionatiPanel.Width - 10, 35)
            };

            // The text to display: "BenefitText - ImpegnoPair - Description"
            // or pick your desired display (some folks might skip repeating the description).
            string displayText = $"{selection.BenefitText} - {selection.ImpegnoPair}";
            if (!string.IsNullOrWhiteSpace(selection.Description))
            {
                displayText += $" - {selection.Description}";
            }

            var lbl = new Label
            {
                Text = displayText,
                Location = new Point(5, 5),
                Size = new Size(itemPanel.Width - 50, 25),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Removal button
            var removeButton = new Button
            {
                Text = "X",
                Size = new Size(30, 25),
                Location = new Point(itemPanel.Width - 35, 5),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                // Store the entire selection as a tag
                Tag = selection
            };
            removeButton.Click += RemoveBeneficioButton_Click;

            itemPanel.Controls.Add(lbl);
            itemPanel.Controls.Add(removeButton);

            BeneficiSelezionatiPanel.Controls.Add(itemPanel);
        }

        // 6) Remove from the UI and from selectedItems if user clicks "X".
        private void RemoveBeneficioButton_Click(object sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                var selection = btn.Tag as BenefitSelection;
                if (selection != null)
                {
                    // Remove from our in-memory list
                    selectedItems.Remove(selection);

                    // Also remove the itemPanel from the parent
                    if (btn.Parent is Panel parentPanel)
                    {
                        BeneficiSelezionatiPanel.Controls.Remove(parentPanel);
                    }

                    // (Optional) restore the benefit to the main ComboBox if you want
                    // the user to be able to pick it again. 
                    // If you prefer once-removed means it's done, skip this.
                    beneficiBox.Items.Add(new { Text = selection.BenefitText, Value = selection.BenefitKey });
                }
            }
        }

        // 7) Expose the final list of selections when the form closes.
        public List<BenefitSelection> SelectedBenefici => selectedItems;

        private void buttonMain_OK_Click(object sender, EventArgs e)
        {
            // Pressing OK returns the final list of selections.
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void buttonMain_Cancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
