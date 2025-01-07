namespace ProcedureNet7
{
    partial class FormProceduraAllegati
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
            label25 = new Label();
            label24 = new Label();
            proceduraAllegatiAA = new TextBox();
            label23 = new Label();
            proceduraAllegatiTipoCombo = new ComboBox();
            proceduraAllegatiSavelbl = new Label();
            proceduraAllegatiSavebtn = new Button();
            proceduraAllegatiCFlbl = new Label();
            proceduraAllegatiCFbtn = new Button();
            button1 = new Button();
            label1 = new Label();
            excelFileDialog = new OpenFileDialog();
            saveFolderDialog = new FolderBrowserDialog();
            filtroBeneficioBTN = new Button();
            SuspendLayout();
            // 
            // label25
            // 
            label25.AutoSize = true;
            label25.Location = new Point(317, 136);
            label25.Name = "label25";
            label25.Size = new Size(164, 15);
            label25.TabIndex = 9;
            label25.Text = "Selezionare il tipo di beneficio";
            // 
            // label24
            // 
            label24.AutoSize = true;
            label24.Location = new Point(317, 165);
            label24.Name = "label24";
            label24.Size = new Size(257, 15);
            label24.TabIndex = 7;
            label24.Text = "Indicare l'anno accademico (formato xxxxyyyy)";
            // 
            // proceduraAllegatiAA
            // 
            proceduraAllegatiAA.Location = new Point(44, 157);
            proceduraAllegatiAA.Name = "proceduraAllegatiAA";
            proceduraAllegatiAA.Size = new Size(267, 23);
            proceduraAllegatiAA.TabIndex = 6;
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Location = new Point(317, 106);
            label23.Name = "label23";
            label23.Size = new Size(157, 15);
            label23.TabIndex = 5;
            label23.Text = "Selezionare il tipo di allegato";
            // 
            // proceduraAllegatiTipoCombo
            // 
            proceduraAllegatiTipoCombo.BackColor = SystemColors.Window;
            proceduraAllegatiTipoCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            proceduraAllegatiTipoCombo.ForeColor = SystemColors.WindowText;
            proceduraAllegatiTipoCombo.FormattingEnabled = true;
            proceduraAllegatiTipoCombo.Location = new Point(45, 98);
            proceduraAllegatiTipoCombo.Name = "proceduraAllegatiTipoCombo";
            proceduraAllegatiTipoCombo.Size = new Size(266, 23);
            proceduraAllegatiTipoCombo.TabIndex = 4;
            // 
            // proceduraAllegatiSavelbl
            // 
            proceduraAllegatiSavelbl.AutoSize = true;
            proceduraAllegatiSavelbl.Location = new Point(317, 79);
            proceduraAllegatiSavelbl.Name = "proceduraAllegatiSavelbl";
            proceduraAllegatiSavelbl.Size = new Size(32, 15);
            proceduraAllegatiSavelbl.TabIndex = 3;
            proceduraAllegatiSavelbl.Text = "_____";
            // 
            // proceduraAllegatiSavebtn
            // 
            proceduraAllegatiSavebtn.Location = new Point(44, 71);
            proceduraAllegatiSavebtn.Name = "proceduraAllegatiSavebtn";
            proceduraAllegatiSavebtn.Size = new Size(267, 23);
            proceduraAllegatiSavebtn.TabIndex = 2;
            proceduraAllegatiSavebtn.Text = "Seleziona Cartella salvataggio";
            proceduraAllegatiSavebtn.UseVisualStyleBackColor = true;
            proceduraAllegatiSavebtn.Click += ProceduraAllegatiSavebtn_Click;
            // 
            // proceduraAllegatiCFlbl
            // 
            proceduraAllegatiCFlbl.AutoSize = true;
            proceduraAllegatiCFlbl.Location = new Point(317, 54);
            proceduraAllegatiCFlbl.Name = "proceduraAllegatiCFlbl";
            proceduraAllegatiCFlbl.Size = new Size(32, 15);
            proceduraAllegatiCFlbl.TabIndex = 1;
            proceduraAllegatiCFlbl.Text = "_____";
            // 
            // proceduraAllegatiCFbtn
            // 
            proceduraAllegatiCFbtn.Location = new Point(44, 46);
            proceduraAllegatiCFbtn.Name = "proceduraAllegatiCFbtn";
            proceduraAllegatiCFbtn.Size = new Size(267, 23);
            proceduraAllegatiCFbtn.TabIndex = 0;
            proceduraAllegatiCFbtn.Text = "Seleziona File excel con CF";
            proceduraAllegatiCFbtn.UseVisualStyleBackColor = true;
            proceduraAllegatiCFbtn.Click += ProceduraAllegatiCFbtn_Click;
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(476, 289);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 26;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(45, 9);
            label1.Name = "label1";
            label1.Size = new Size(321, 28);
            label1.TabIndex = 27;
            label1.Text = "PROCEDURA CREAZIONE ALLEGATI";
            // 
            // excelFileDialog
            // 
            excelFileDialog.FileName = "Excel con CF";
            // 
            // filtroBeneficioBTN
            // 
            filtroBeneficioBTN.Location = new Point(46, 127);
            filtroBeneficioBTN.Name = "filtroBeneficioBTN";
            filtroBeneficioBTN.Size = new Size(265, 24);
            filtroBeneficioBTN.TabIndex = 29;
            filtroBeneficioBTN.UseVisualStyleBackColor = true;
            // 
            // FormProceduraAllegati
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(filtroBeneficioBTN);
            Controls.Add(label1);
            Controls.Add(label25);
            Controls.Add(button1);
            Controls.Add(label24);
            Controls.Add(proceduraAllegatiCFbtn);
            Controls.Add(proceduraAllegatiAA);
            Controls.Add(proceduraAllegatiCFlbl);
            Controls.Add(label23);
            Controls.Add(proceduraAllegatiSavebtn);
            Controls.Add(proceduraAllegatiTipoCombo);
            Controls.Add(proceduraAllegatiSavelbl);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraAllegati";
            Text = "FormProceduraAllegati";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label label25;
        private Label label24;
        private TextBox proceduraAllegatiAA;
        private Label label23;
        private ComboBox proceduraAllegatiTipoCombo;
        private Label proceduraAllegatiSavelbl;
        private Button proceduraAllegatiSavebtn;
        private Label proceduraAllegatiCFlbl;
        private Button proceduraAllegatiCFbtn;
        private Button button1;
        private Label label1;
        private OpenFileDialog excelFileDialog;
        private FolderBrowserDialog saveFolderDialog;
        private Button filtroBeneficioBTN;
    }
}