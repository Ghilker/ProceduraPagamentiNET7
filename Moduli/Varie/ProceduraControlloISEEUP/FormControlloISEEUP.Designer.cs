namespace ProcedureNet7
{
    partial class FormControlloISEEUP
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
            runProcedure = new Button();
            label29 = new Label();
            iseeupAABox = new TextBox();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(306, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA CONTROLLO ISEEUP";
            // 
            // runProcedure
            // 
            runProcedure.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            runProcedure.Location = new Point(460, 291);
            runProcedure.Name = "runProcedure";
            runProcedure.Size = new Size(312, 49);
            runProcedure.TabIndex = 32;
            runProcedure.Text = "AVVIA LA PROCEDURA";
            runProcedure.UseVisualStyleBackColor = true;
            runProcedure.Click += this.RunProcedureBtnClick;
            // 
            // label29
            // 
            label29.AutoSize = true;
            label29.Location = new Point(232, 61);
            label29.Name = "label29";
            label29.Size = new Size(103, 15);
            label29.TabIndex = 35;
            label29.Text = "Anno accademico";
            // 
            // iseeupAABox
            // 
            iseeupAABox.Location = new Point(29, 53);
            iseeupAABox.Name = "iseeupAABox";
            iseeupAABox.Size = new Size(197, 23);
            iseeupAABox.TabIndex = 34;
            // 
            // FormControlloISEEUP
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(label29);
            Controls.Add(iseeupAABox);
            Controls.Add(label1);
            Controls.Add(runProcedure);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormControlloISEEUP";
            Text = "FormControlloIBAN";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button runProcedure;
        private Label label29;
        private TextBox iseeupAABox;
    }
}