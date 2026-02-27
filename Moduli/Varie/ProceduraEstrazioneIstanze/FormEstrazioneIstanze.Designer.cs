namespace ProcedureNet7
{ 
    partial class FormEstrazioneIstanze
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
            savePathBTN = new Button();
            mailPathBTN = new Button();
            savePathlbl = new Label();
            mailPathlbl = new Label();
            folderBrowserDialog1 = new FolderBrowserDialog();
            openFileDialog1 = new OpenFileDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(322, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA ESTRAZIONE ISTANZE";
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
            // savePathBTN
            // 
            savePathBTN.Location = new Point(29, 61);
            savePathBTN.Name = "savePathBTN";
            savePathBTN.Size = new Size(211, 23);
            savePathBTN.TabIndex = 34;
            savePathBTN.Text = "Selezionare la cartella di salvataggio";
            savePathBTN.UseVisualStyleBackColor = true;
            savePathBTN.Click += SavePathButtonClick;
            // 
            // mailPathBTN
            // 
            mailPathBTN.Location = new Point(29, 90);
            mailPathBTN.Name = "mailPathBTN";
            mailPathBTN.Size = new Size(211, 23);
            mailPathBTN.TabIndex = 35;
            mailPathBTN.Text = "Selezionare il file con le mail";
            mailPathBTN.UseVisualStyleBackColor = true;
            mailPathBTN.Click += MailPathButtonClick;
            // 
            // savePathlbl
            // 
            savePathlbl.AutoSize = true;
            savePathlbl.Location = new Point(246, 65);
            savePathlbl.Name = "savePathlbl";
            savePathlbl.Size = new Size(32, 15);
            savePathlbl.TabIndex = 36;
            savePathlbl.Text = "_____";
            // 
            // mailPathlbl
            // 
            mailPathlbl.AutoSize = true;
            mailPathlbl.Location = new Point(246, 94);
            mailPathlbl.Name = "mailPathlbl";
            mailPathlbl.Size = new Size(32, 15);
            mailPathlbl.TabIndex = 37;
            mailPathlbl.Text = "_____";
            // 
            // openFileDialog1
            // 
            openFileDialog1.FileName = "openFileDialog1";
            // 
            // FormEstrazioneIstanze
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(mailPathlbl);
            Controls.Add(savePathlbl);
            Controls.Add(mailPathBTN);
            Controls.Add(savePathBTN);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormEstrazioneIstanze";
            Text = "Form_procedure_name_";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button savePathBTN;
        private Button mailPathBTN;
        private Label savePathlbl;
        private Label mailPathlbl;
        private FolderBrowserDialog folderBrowserDialog1;
        private OpenFileDialog openFileDialog1;
    }
}