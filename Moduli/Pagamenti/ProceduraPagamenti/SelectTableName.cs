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
    public partial class SelectTableName : Form
    {
        public SelectTableName()
        {
            InitializeComponent();
        }

        public string InputText
        {
            get { return promptTableNameTxt.Text; }
        }

        private void promptTableNameYesBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
        }

        private void promptTableNameNoBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }
    }
}
