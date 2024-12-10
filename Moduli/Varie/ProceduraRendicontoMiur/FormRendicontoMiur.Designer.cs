namespace ProcedureNet7
{ 
    partial class FormRendicontoMiur
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
            rendicontoFolderBTN = new Button();
            folderLBL = new Label();
            folderBrowserDialog1 = new FolderBrowserDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(305, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA RENDICONTO MIUR";
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
            // rendicontoFolderBTN
            // 
            rendicontoFolderBTN.Location = new Point(29, 51);
            rendicontoFolderBTN.Name = "rendicontoFolderBTN";
            rendicontoFolderBTN.Size = new Size(207, 23);
            rendicontoFolderBTN.TabIndex = 34;
            rendicontoFolderBTN.Text = "Seleziona la cartella con i modelli";
            rendicontoFolderBTN.UseVisualStyleBackColor = true;
            rendicontoFolderBTN.Click += rendicontoFolderBTN_Click;
            // 
            // folderLBL
            // 
            folderLBL.AutoSize = true;
            folderLBL.Location = new Point(242, 55);
            folderLBL.Name = "folderLBL";
            folderLBL.Size = new Size(32, 15);
            folderLBL.TabIndex = 35;
            folderLBL.Text = "_____";
            // 
            // FormRendicontoMiur
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(folderLBL);
            Controls.Add(rendicontoFolderBTN);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormRendicontoMiur";
            Text = "FormRendicontoMiur";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button rendicontoFolderBTN;
        private Label folderLBL;
        private FolderBrowserDialog folderBrowserDialog1;
    }
}