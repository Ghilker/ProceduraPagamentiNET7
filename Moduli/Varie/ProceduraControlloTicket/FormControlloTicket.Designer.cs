namespace ProcedureNet7
{
    partial class FormControlloTicket
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button button1;          // "AVVIA LA PROCEDURA"
        private System.Windows.Forms.Button buttonSelectCSV;  // "Select CSV"
        private System.Windows.Forms.TextBox txtSelectedFile;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code
        private void InitializeComponent()
        {
            label1 = new Label();
            button1 = new Button();
            buttonSelectCSV = new Button();
            txtSelectedFile = new TextBox();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(125, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 32;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            // 
            // buttonSelectCSV
            // 
            buttonSelectCSV.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            buttonSelectCSV.Location = new Point(29, 50);
            buttonSelectCSV.Name = "buttonSelectCSV";
            buttonSelectCSV.Size = new Size(120, 30);
            buttonSelectCSV.TabIndex = 34;
            buttonSelectCSV.Text = "Select CSV";
            buttonSelectCSV.UseVisualStyleBackColor = true;
            // 
            // txtSelectedFile
            // 
            txtSelectedFile.Location = new Point(160, 55);
            txtSelectedFile.Name = "txtSelectedFile";
            txtSelectedFile.ReadOnly = true;
            txtSelectedFile.Size = new Size(300, 23);
            txtSelectedFile.TabIndex = 35;
            txtSelectedFile.Text = "No file selected...";
            // 
            // FormControlloTicket
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(txtSelectedFile);
            Controls.Add(buttonSelectCSV);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormControlloTicket";
            Text = "FormControlloTicket";
            ResumeLayout(false);
            PerformLayout();
        }
        #endregion
    }
}
