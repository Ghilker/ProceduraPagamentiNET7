namespace ProcedureNet7
{
    partial class FormProceduraEstrazionePermessiSoggiorno
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
            savePathLbl = new Label();
            mailPathLbl = new Label();
            folderBrowserDialog1 = new FolderBrowserDialog();
            openFileDialog1 = new OpenFileDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(457, 28);
            label1.TabIndex = 31;
            label1.Text = "PROCEDURA ESTRAZIONE PERMESSI SOGGIORNO";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 30;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // savePathBTN
            // 
            savePathBTN.Location = new Point(29, 69);
            savePathBTN.Name = "savePathBTN";
            savePathBTN.Size = new Size(211, 23);
            savePathBTN.TabIndex = 32;
            savePathBTN.Text = "Selezionare la cartella di salvataggio";
            savePathBTN.UseVisualStyleBackColor = true;
            savePathBTN.Click += savePathBTN_Click;
            // 
            // mailPathBTN
            // 
            mailPathBTN.Location = new Point(29, 98);
            mailPathBTN.Name = "mailPathBTN";
            mailPathBTN.Size = new Size(211, 23);
            mailPathBTN.TabIndex = 33;
            mailPathBTN.Text = "Selezionare il file con le mail";
            mailPathBTN.UseVisualStyleBackColor = true;
            mailPathBTN.Click += mailPathBTN_Click;
            // 
            // savePathLbl
            // 
            savePathLbl.AutoSize = true;
            savePathLbl.Location = new Point(246, 77);
            savePathLbl.Name = "savePathLbl";
            savePathLbl.Size = new Size(32, 15);
            savePathLbl.TabIndex = 34;
            savePathLbl.Text = "_____";
            // 
            // mailPathLbl
            // 
            mailPathLbl.AutoSize = true;
            mailPathLbl.Location = new Point(246, 106);
            mailPathLbl.Name = "mailPathLbl";
            mailPathLbl.Size = new Size(32, 15);
            mailPathLbl.TabIndex = 35;
            mailPathLbl.Text = "_____";
            // 
            // openFileDialog1
            // 
            openFileDialog1.FileName = "openFileDialog1";
            // 
            // FormProceduraEstrazionePermessiSoggiorno
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(mailPathLbl);
            Controls.Add(savePathLbl);
            Controls.Add(mailPathBTN);
            Controls.Add(savePathBTN);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraEstrazionePermessiSoggiorno";
            Text = "FormProceduraEstrazionePermessiSoggiorno";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button savePathBTN;
        private Button mailPathBTN;
        private Label savePathLbl;
        private Label mailPathLbl;
        private FolderBrowserDialog folderBrowserDialog1;
        private OpenFileDialog openFileDialog1;
    }
}