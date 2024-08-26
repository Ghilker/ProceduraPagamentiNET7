using System;
using System.Collections;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class ListForm : Form
    {
        public ListForm(IList list, Point viewButtonLocation)
        {
            InitializeComponent();
            PopulateDataGridView(list);
            AdjustFormPosition(viewButtonLocation);
        }

        private void PopulateDataGridView(IList list)
        {
            DataTable dt = new DataTable();
            if (list != null && list.Count > 0)
            {
                var properties = list[0].GetType().GetProperties();
                foreach (var prop in properties)
                {
                    dt.Columns.Add(prop.Name, prop.PropertyType);
                }

                foreach (var item in list)
                {
                    var values = new object[properties.Length];
                    for (int i = 0; i < properties.Length; i++)
                    {
                        values[i] = properties[i].GetValue(item, null);
                    }
                    dt.Rows.Add(values);
                }
            }

            DataGridView dataGridView = new DataGridView
            {
                DataSource = dt,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill
            };

            Panel panel = new Panel
            {
                AutoScroll = true,
                Dock = DockStyle.Fill
            };
            panel.Controls.Add(dataGridView);
            this.Controls.Add(panel);

            this.Load += (s, e) => AdjustFormSize(dataGridView);
        }

        private void AdjustFormSize(DataGridView dataGridView)
        {
            int maxHeight = 5000; // Maximum height limit
            int maxWidth = 8000; // Maximum width limit
            int padding = 60; // Padding to account for borders and margins

            // Calculate the preferred size of the DataGridView
            int preferredHeight = dataGridView.Rows.GetRowsHeight(DataGridViewElementStates.Visible) + dataGridView.ColumnHeadersHeight + padding;
            int preferredWidth = dataGridView.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) + dataGridView.RowHeadersWidth + padding;

            // Set the form size, limiting it to the maximum values
            this.Height = Math.Min(preferredHeight, maxHeight);
            this.Width = Math.Min(preferredWidth, maxWidth);

            // Set the panel size
            dataGridView.Parent.Height = this.Height;
            dataGridView.Parent.Width = this.Width;
        }

        private void AdjustFormPosition(Point viewButtonLocation)
        {
            // Get the screen coordinates of the view button location
            Point screenLocation = PointToScreen(viewButtonLocation);

            // Move the form to align its close button with the view button
            int closeButtonOffsetX = this.Width - SystemInformation.CaptionButtonSize.Width;
            int closeButtonOffsetY = SystemInformation.CaptionHeight / 2;

            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(screenLocation.X - closeButtonOffsetX, screenLocation.Y - closeButtonOffsetY);
        }
    }
}
