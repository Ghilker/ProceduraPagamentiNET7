namespace ProcedureNet7
{ 
    partial class FormGenerazioneFileRevoche
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
            genRevSaveBtn = new Button();
            genRevSaveLbl = new Label();
            label2 = new Label();
            genRevAAText = new TextBox();
            genRevEnteComboBox = new ComboBox();
            label3 = new Label();
            folderBrowserDialog = new FolderBrowserDialog();
            btnGeneraLettere = new Button();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(390, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA GENERAZIONE FILE REVOCHE";
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
            // genRevSaveBtn
            // 
            genRevSaveBtn.Location = new Point(29, 58);
            genRevSaveBtn.Name = "genRevSaveBtn";
            genRevSaveBtn.Size = new Size(170, 23);
            genRevSaveBtn.TabIndex = 34;
            genRevSaveBtn.Text = "Seleziona cartella salvataggio";
            genRevSaveBtn.UseVisualStyleBackColor = true;
            genRevSaveBtn.Click += genRevSaveBtn_Click;
            // 
            // genRevSaveLbl
            // 
            genRevSaveLbl.AutoSize = true;
            genRevSaveLbl.Location = new Point(215, 62);
            genRevSaveLbl.Name = "genRevSaveLbl";
            genRevSaveLbl.Size = new Size(32, 15);
            genRevSaveLbl.TabIndex = 35;
            genRevSaveLbl.Text = "_____";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(29, 92);
            label2.Name = "label2";
            label2.Size = new Size(180, 15);
            label2.TabIndex = 36;
            label2.Text = "Anno Accademico di riferimento";
            // 
            // genRevAAText
            // 
            genRevAAText.Location = new Point(215, 84);
            genRevAAText.Name = "genRevAAText";
            genRevAAText.Size = new Size(139, 23);
            genRevAAText.TabIndex = 37;
            // 
            // genRevEnteComboBox
            // 
            genRevEnteComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            genRevEnteComboBox.FormattingEnabled = true;
            genRevEnteComboBox.Location = new Point(215, 113);
            genRevEnteComboBox.Name = "genRevEnteComboBox";
            genRevEnteComboBox.Size = new Size(139, 23);
            genRevEnteComboBox.TabIndex = 38;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(29, 121);
            label3.Name = "label3";
            label3.Size = new Size(92, 15);
            label3.TabIndex = 39;
            label3.Text = "Ente di Gestione";
            // 
            // btnGeneraLettere
            // 
            btnGeneraLettere.Location = new Point(29, 204);
            btnGeneraLettere.Name = "btnGeneraLettere";
            btnGeneraLettere.Size = new Size(180, 35);
            btnGeneraLettere.TabIndex = 40;
            btnGeneraLettere.Text = "Genera Lettere Revoche";
            btnGeneraLettere.UseVisualStyleBackColor = true;
            btnGeneraLettere.Click += btnGeneraLettere_Click;
            // 
            // FormGenerazioneFileRevoche
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(btnGeneraLettere);
            Controls.Add(label3);
            Controls.Add(genRevEnteComboBox);
            Controls.Add(genRevAAText);
            Controls.Add(label2);
            Controls.Add(genRevSaveLbl);
            Controls.Add(genRevSaveBtn);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormGenerazioneFileRevoche";
            Text = "FormGenerazioneFileRevoche";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button genRevSaveBtn;
        private Label genRevSaveLbl;
        private Label label2;
        private TextBox genRevAAText;
        private ComboBox genRevEnteComboBox;
        private Label label3;
        private FolderBrowserDialog folderBrowserDialog;
        private Button btnGeneraLettere;
    }
}