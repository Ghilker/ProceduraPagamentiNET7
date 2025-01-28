using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class FormTipoBeneficioAllegato : Form
    {
        // Your dictionary
        private readonly Dictionary<string, string> decodTipoBando = new()
        {
            { "BS", "Borsa di studio" },
            { "PA", "Posto alloggio" },
            { "CI", "Contributo integrativo" },
            { "PL", "Premio di laurea" },
            { "BL", "Buono libro" }
        };

        // Controls we'll create in code
        private Button btnAddBeneficio;
        private ContextMenuStrip dropdownMenu;   // The "dropdown" that appears on button click
        private Button btnAddMore;

        // To keep track of selected items
        private List<string> alreadySelected = new List<string>();

        public FormTipoBeneficioAllegato()
        {
            InitializeComponent();
            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            // 1. "Add Beneficio" button
            btnAddBeneficio = new Button
            {
                Text = "+ Add Beneficio",
                Location = new Point(10, 10),      // top-left, plus some padding
                AutoSize = true
            };
            btnAddBeneficio.Click += BtnAddBeneficio_Click;
            Controls.Add(btnAddBeneficio);

            // 2. ContextMenuStrip that will serve as the drop-down
            dropdownMenu = new ContextMenuStrip();

            // 3. "Add More" button, hidden initially
            btnAddMore = new Button
            {
                Text = "Add More Tipo Beneficio",
                Location = new Point(10, 50),
                Visible = false,                  // hidden until first item is selected
                AutoSize = true
            };
            btnAddMore.Click += BtnAddMore_Click;
            Controls.Add(btnAddMore);
        }

        private void BtnAddBeneficio_Click(object sender, EventArgs e)
        {
            // Rebuild the context menu each time, omitting already-selected items
            dropdownMenu.Items.Clear();

            foreach (var kvp in decodTipoBando)
            {
                // Skip if we have already selected this tipo
                if (alreadySelected.Contains(kvp.Key)) continue;

                var menuItem = new ToolStripMenuItem($"{kvp.Key} - {kvp.Value}")
                {
                    Tag = kvp.Key  // Store the dictionary key for reference
                };
                menuItem.Click += MenuItem_Click;
                dropdownMenu.Items.Add(menuItem);
            }

            // Show the menu just beneath the "Add Beneficio" button
            dropdownMenu.Show(btnAddBeneficio, new Point(0, btnAddBeneficio.Height));
        }

        private void MenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem item && item.Tag is string selectedKey)
            {
                // Mark this key as selected
                if (!alreadySelected.Contains(selectedKey))
                {
                    alreadySelected.Add(selectedKey);
                }

                // Make the "Add More" button visible
                btnAddMore.Visible = true;
            }
        }

        private void BtnAddMore_Click(object sender, EventArgs e)
        {
            // You could do various things here, e.g., open the drop-down again 
            // or handle some custom logic for multiple selections.
            // For example, re-show the same dropdown (minus selected ones) immediately:
            BtnAddBeneficio_Click(sender, e);
        }
    }
}
