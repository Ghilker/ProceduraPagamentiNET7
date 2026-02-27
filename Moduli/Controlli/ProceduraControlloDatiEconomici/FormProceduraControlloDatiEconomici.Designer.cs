namespace ProcedureNet7
{ 
    partial class FormProceduraControlloDatiEconomici
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
            button1 = new Button();
            ControlloEcoAATxt = new TextBox();
            label2 = new Label();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(397, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA CONTROLLO DATI ECONOMICI";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 32;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // ControlloEcoAATxt
            // 
            ControlloEcoAATxt.Location = new Point(138, 52);
            ControlloEcoAATxt.Name = "ControlloEcoAATxt";
            ControlloEcoAATxt.Size = new Size(100, 23);
            ControlloEcoAATxt.TabIndex = 34;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(29, 60);
            label2.Name = "label2";
            label2.Size = new Size(103, 15);
            label2.TabIndex = 35;
            label2.Text = "Anno accademico";
            // 
            // FormProceduraControlloDatiEconomici
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(label2);
            Controls.Add(ControlloEcoAATxt);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraControlloDatiEconomici";
            Text = "FormProceduraControlloDatiEconomici";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private TextBox ControlloEcoAATxt;
        private Label label2;
    }
}