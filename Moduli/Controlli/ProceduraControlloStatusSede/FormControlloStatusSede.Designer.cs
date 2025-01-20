namespace ProcedureNet7
{ 
    partial class FormControlloStatusSede
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
            saveFolderBTN = new Button();
            saveFolderLbl = new Label();
            label3 = new Label();
            selectedAAText = new TextBox();
            folderBrowserDialog1 = new FolderBrowserDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(362, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA CONTROLLO STATUS SEDE";
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
            button1.Click += RunProcedureBtnClick;
            // 
            // saveFolderBTN
            // 
            saveFolderBTN.Location = new Point(30, 56);
            saveFolderBTN.Name = "saveFolderBTN";
            saveFolderBTN.Size = new Size(191, 23);
            saveFolderBTN.TabIndex = 35;
            saveFolderBTN.Text = "Selezionare cartella salvataggio";
            saveFolderBTN.UseVisualStyleBackColor = true;
            saveFolderBTN.Click += saveFolderBTN_Click;
            // 
            // saveFolderLbl
            // 
            saveFolderLbl.AutoSize = true;
            saveFolderLbl.Location = new Point(227, 64);
            saveFolderLbl.Name = "saveFolderLbl";
            saveFolderLbl.Size = new Size(27, 15);
            saveFolderLbl.TabIndex = 36;
            saveFolderLbl.Text = "____";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(30, 85);
            label3.Name = "label3";
            label3.Size = new Size(152, 15);
            label3.TabIndex = 38;
            label3.Text = "Indicare l'anno accademico";
            // 
            // selectedAAText
            // 
            selectedAAText.Location = new Point(227, 82);
            selectedAAText.Name = "selectedAAText";
            selectedAAText.Size = new Size(100, 23);
            selectedAAText.TabIndex = 39;
            // 
            // FormControlloStatusSede
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(selectedAAText);
            Controls.Add(label3);
            Controls.Add(saveFolderLbl);
            Controls.Add(saveFolderBTN);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormControlloStatusSede";
            Text = "Form_procedure_name_";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button saveFolderBTN;
        private Label saveFolderLbl;
        private Label label3;
        private TextBox selectedAAText;
        private FolderBrowserDialog folderBrowserDialog1;
    }
}