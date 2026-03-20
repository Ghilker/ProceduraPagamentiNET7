namespace ProcedureNet7
{
    partial class FormVerifica
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            circularButton1 = new CircularButton();
            label2 = new Label();
            verificaAAText = new TextBox();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(119, 28);
            label1.TabIndex = 59;
            label1.Text = "LA VERIFICA";
            // 
            // circularButton1
            // 
            circularButton1.BackColor = Color.FromArgb(210, 40, 10);
            circularButton1.FlatAppearance.BorderSize = 0;
            circularButton1.FlatStyle = FlatStyle.Flat;
            circularButton1.Font = new Font("Impact", 27.75F);
            circularButton1.ForeColor = Color.White;
            circularButton1.Location = new Point(595, 145);
            circularButton1.Name = "circularButton1";
            circularButton1.Size = new Size(193, 193);
            circularButton1.TabIndex = 60;
            circularButton1.Text = "VERIFICA";
            circularButton1.UseVisualStyleBackColor = false;
            circularButton1.Click += circularButton1_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(12, 61);
            label2.Name = "label2";
            label2.Size = new Size(103, 15);
            label2.TabIndex = 61;
            label2.Text = "Anno accademico";
            // 
            // verificaAAText
            // 
            verificaAAText.Location = new Point(121, 53);
            verificaAAText.Name = "verificaAAText";
            verificaAAText.Size = new Size(100, 23);
            verificaAAText.TabIndex = 62;
            // 
            // FormVerifica
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(verificaAAText);
            Controls.Add(label2);
            Controls.Add(circularButton1);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormVerifica";
            Text = "FormVerifica";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private CircularButton circularButton1;
        private Label label2;
        private TextBox verificaAAText;
    }
}