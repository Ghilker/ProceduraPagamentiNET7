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
            proceduraAllegatiBeneficiBtn = new Button();
            label2 = new Label();
            btnScaricaModello = new Button();
            SuspendLayout();
            // 
            // label24
            // 
            label24.AutoSize = true;
            label24.Location = new Point(317, 137);
            label24.Name = "label24";
            label24.Size = new Size(179, 15);
            label24.TabIndex = 8;
            label24.Text = "Anno accademico (es. 20242025)";
            // 
            // proceduraAllegatiAA
            // 
            proceduraAllegatiAA.Location = new Point(45, 133);
            proceduraAllegatiAA.Name = "proceduraAllegatiAA";
            proceduraAllegatiAA.Size = new Size(266, 23);
            proceduraAllegatiAA.TabIndex = 7;
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Location = new Point(317, 108);
            label23.Name = "label23";
            label23.Size = new Size(144, 15);
            label23.TabIndex = 6;
            label23.Text = "Selezionare il tipo allegato";
            // 
            // proceduraAllegatiTipoCombo
            // 
            proceduraAllegatiTipoCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            proceduraAllegatiTipoCombo.FormattingEnabled = true;
            proceduraAllegatiTipoCombo.Location = new Point(45, 104);
            proceduraAllegatiTipoCombo.Name = "proceduraAllegatiTipoCombo";
            proceduraAllegatiTipoCombo.Size = new Size(266, 23);
            proceduraAllegatiTipoCombo.TabIndex = 5;
            // 
            // proceduraAllegatiSavelbl
            // 
            proceduraAllegatiSavelbl.AutoSize = true;
            proceduraAllegatiSavelbl.Location = new Point(317, 79);
            proceduraAllegatiSavelbl.Name = "proceduraAllegatiSavelbl";
            proceduraAllegatiSavelbl.Size = new Size(32, 15);
            proceduraAllegatiSavelbl.TabIndex = 4;
            proceduraAllegatiSavelbl.Text = "_____";
            // 
            // proceduraAllegatiSavebtn
            // 
            proceduraAllegatiSavebtn.Location = new Point(44, 75);
            proceduraAllegatiSavebtn.Name = "proceduraAllegatiSavebtn";
            proceduraAllegatiSavebtn.Size = new Size(267, 23);
            proceduraAllegatiSavebtn.TabIndex = 3;
            proceduraAllegatiSavebtn.Text = "Seleziona Cartella Salvataggio";
            proceduraAllegatiSavebtn.UseVisualStyleBackColor = true;
            proceduraAllegatiSavebtn.Click += ProceduraAllegatiSavebtn_Click;
            // 
            // proceduraAllegatiCFlbl
            // 
            proceduraAllegatiCFlbl.AutoSize = true;
            proceduraAllegatiCFlbl.Location = new Point(317, 50);
            proceduraAllegatiCFlbl.Name = "proceduraAllegatiCFlbl";
            proceduraAllegatiCFlbl.Size = new Size(32, 15);
            proceduraAllegatiCFlbl.TabIndex = 2;
            proceduraAllegatiCFlbl.Text = "_____";
            // 
            // proceduraAllegatiCFbtn
            // 
            proceduraAllegatiCFbtn.Location = new Point(44, 46);
            proceduraAllegatiCFbtn.Name = "proceduraAllegatiCFbtn";
            proceduraAllegatiCFbtn.Size = new Size(267, 23);
            proceduraAllegatiCFbtn.TabIndex = 1;
            proceduraAllegatiCFbtn.Text = "Seleziona File Excel";
            proceduraAllegatiCFbtn.UseVisualStyleBackColor = true;
            proceduraAllegatiCFbtn.Click += ProceduraAllegatiCFbtn_Click;
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F);
            button1.Location = new Point(476, 289);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 12;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(45, 9);
            label1.Name = "label1";
            label1.Size = new Size(321, 28);
            label1.TabIndex = 0;
            label1.Text = "PROCEDURA CREAZIONE ALLEGATI";
            // 
            // proceduraAllegatiBeneficiBtn
            // 
            proceduraAllegatiBeneficiBtn.Location = new Point(45, 162);
            proceduraAllegatiBeneficiBtn.Name = "proceduraAllegatiBeneficiBtn";
            proceduraAllegatiBeneficiBtn.Size = new Size(266, 23);
            proceduraAllegatiBeneficiBtn.TabIndex = 9;
            proceduraAllegatiBeneficiBtn.Text = "Seleziona benefici";
            proceduraAllegatiBeneficiBtn.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(317, 166);
            label2.Name = "label2";
            label2.Size = new Size(155, 15);
            label2.TabIndex = 10;
            label2.Text = "Seleziona uno o più benefici";
            // 
            // btnScaricaModello
            // 
            btnScaricaModello.Location = new Point(476, 104);
            btnScaricaModello.Name = "btnScaricaModello";
            btnScaricaModello.Size = new Size(266, 23);
            btnScaricaModello.TabIndex = 11;
            btnScaricaModello.Text = "SCARICA MODELLO PER ALLEGATO";
            btnScaricaModello.UseVisualStyleBackColor = true;
            // 
            // FormProceduraAllegati
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(label1);
            Controls.Add(proceduraAllegatiCFbtn);
            Controls.Add(proceduraAllegatiCFlbl);
            Controls.Add(proceduraAllegatiSavebtn);
            Controls.Add(proceduraAllegatiSavelbl);
            Controls.Add(proceduraAllegatiTipoCombo);
            Controls.Add(label23);
            Controls.Add(proceduraAllegatiAA);
            Controls.Add(label24);
            Controls.Add(proceduraAllegatiBeneficiBtn);
            Controls.Add(label2);
            Controls.Add(btnScaricaModello);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraAllegati";
            Text = "FormProceduraAllegati";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
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
        private Button proceduraAllegatiBeneficiBtn;
        private Label label2;
        private Button btnScaricaModello;
    }
}