namespace ProcedureNet7
{ 
    partial class FormControlloDomicilio
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
            folderBrowserDialog1 = new FolderBrowserDialog();
            folderPathBtn = new Button();
            folderPathlbl = new Label();
            selectedAA = new TextBox();
            label3 = new Label();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(343, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA CONTROLLO DOMICILIO";
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
            // folderPathBtn
            // 
            folderPathBtn.Location = new Point(29, 53);
            folderPathBtn.Name = "folderPathBtn";
            folderPathBtn.Size = new Size(176, 23);
            folderPathBtn.TabIndex = 34;
            folderPathBtn.Text = "Seleziona cartella salvataggio";
            folderPathBtn.UseVisualStyleBackColor = true;
            folderPathBtn.Click += folderPathBtn_Click;
            // 
            // folderPathlbl
            // 
            folderPathlbl.AutoSize = true;
            folderPathlbl.Location = new Point(211, 57);
            folderPathlbl.Name = "folderPathlbl";
            folderPathlbl.Size = new Size(32, 15);
            folderPathlbl.TabIndex = 35;
            folderPathlbl.Text = "_____";
            // 
            // selectedAA
            // 
            selectedAA.Location = new Point(149, 86);
            selectedAA.Name = "selectedAA";
            selectedAA.Size = new Size(100, 23);
            selectedAA.TabIndex = 36;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(29, 89);
            label3.Name = "label3";
            label3.Size = new Size(103, 15);
            label3.TabIndex = 37;
            label3.Text = "Anno accademico";
            // 
            // FormControlloDomicilio
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(label3);
            Controls.Add(selectedAA);
            Controls.Add(folderPathlbl);
            Controls.Add(folderPathBtn);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormControlloDomicilio";
            Text = "Form_procedure_name_";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private FolderBrowserDialog folderBrowserDialog1;
        private Button folderPathBtn;
        private Label folderPathlbl;
        private TextBox selectedAA;
        private Label label3;
    }
}