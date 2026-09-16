namespace ProcedureNet7
{
    partial class FormVerifica
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
            circularButton1 = new CircularButton();
            label2 = new Label();
            verificaAAText = new TextBox();
            label3 = new Label();
            verificaCodiceFiscaleText = new TextBox();
            label4 = new Label();
            verificaQueryCodiciFiscaliText = new TextBox();
            label5 = new Label();
            label6 = new Label();
            verificaFaseElaborativaCombo = new ComboBox();
            verificaScriviDatabaseCheck = new CheckBox();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(119, 28);
            label1.TabIndex = 59;
            label1.Text = "LA VERIFICA";
            // 
            // circularButton1
            // 
            circularButton1.BackColor = Color.FromArgb(210, 40, 10);
            circularButton1.FlatAppearance.BorderSize = 0;
            circularButton1.FlatStyle = FlatStyle.Flat;
            circularButton1.Font = new Font("Impact", 27.75F);
            circularButton1.ForeColor = Color.White;
            circularButton1.Location = new Point(595, 145);
            circularButton1.Name = "circularButton1";
            circularButton1.Size = new Size(193, 193);
            circularButton1.TabIndex = 68;
            circularButton1.Text = "VERIFICA";
            circularButton1.UseVisualStyleBackColor = false;
            circularButton1.Click += circularButton1_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(12, 61);
            label2.Name = "label2";
            label2.Size = new Size(103, 15);
            label2.TabIndex = 61;
            label2.Text = "Anno accademico";
            // 
            // verificaAAText
            // 
            verificaAAText.Location = new Point(180, 53);
            verificaAAText.Name = "verificaAAText";
            verificaAAText.Size = new Size(100, 23);
            verificaAAText.TabIndex = 62;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(310, 61);
            label6.Name = "label6";
            label6.Size = new Size(92, 15);
            label6.TabIndex = 69;
            label6.Text = "Fase elaborativa";
            // 
            // verificaFaseElaborativaCombo
            // 
            verificaFaseElaborativaCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            verificaFaseElaborativaCombo.FormattingEnabled = true;
            verificaFaseElaborativaCombo.Items.AddRange(new object[] { "Provvisorie", "Definitive" });
            verificaFaseElaborativaCombo.Location = new Point(420, 53);
            verificaFaseElaborativaCombo.Name = "verificaFaseElaborativaCombo";
            verificaFaseElaborativaCombo.Size = new Size(137, 23);
            verificaFaseElaborativaCombo.TabIndex = 70;
            verificaFaseElaborativaCombo.SelectedIndex = 0;
            // 
            // verificaScriviDatabaseCheck
            // 
            verificaScriviDatabaseCheck.AutoSize = true;
            verificaScriviDatabaseCheck.Location = new Point(595, 105);
            verificaScriviDatabaseCheck.Name = "verificaScriviDatabaseCheck";
            verificaScriviDatabaseCheck.Size = new Size(184, 19);
            verificaScriviDatabaseCheck.TabIndex = 71;
            verificaScriviDatabaseCheck.Text = "Scrivi i risultati sul database";
            verificaScriviDatabaseCheck.UseVisualStyleBackColor = true;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(12, 96);
            label3.Name = "label3";
            label3.Size = new Size(143, 15);
            label3.TabIndex = 63;
            label3.Text = "Codice fiscale (opzionale)";
            // 
            // verificaCodiceFiscaleText
            // 
            verificaCodiceFiscaleText.CharacterCasing = CharacterCasing.Upper;
            verificaCodiceFiscaleText.Location = new Point(180, 88);
            verificaCodiceFiscaleText.MaxLength = 16;
            verificaCodiceFiscaleText.Name = "verificaCodiceFiscaleText";
            verificaCodiceFiscaleText.Size = new Size(180, 23);
            verificaCodiceFiscaleText.TabIndex = 64;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(12, 131);
            label4.Name = "label4";
            label4.Size = new Size(160, 15);
            label4.TabIndex = 65;
            label4.Text = "Query codici fiscali (opzionale)";
            // 
            // verificaQueryCodiciFiscaliText
            // 
            verificaQueryCodiciFiscaliText.AcceptsReturn = true;
            verificaQueryCodiciFiscaliText.AcceptsTab = true;
            verificaQueryCodiciFiscaliText.Font = new Font("Consolas", 9F);
            verificaQueryCodiciFiscaliText.Location = new Point(12, 151);
            verificaQueryCodiciFiscaliText.Multiline = true;
            verificaQueryCodiciFiscaliText.Name = "verificaQueryCodiciFiscaliText";
            verificaQueryCodiciFiscaliText.ScrollBars = ScrollBars.Both;
            verificaQueryCodiciFiscaliText.Size = new Size(545, 160);
            verificaQueryCodiciFiscaliText.TabIndex = 66;
            verificaQueryCodiciFiscaliText.Text = "select top(100) cod_fiscale from domanda where anno_accademico = '20262027'\r\norder by cod_fiscale";
            verificaQueryCodiciFiscaliText.WordWrap = false;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(12, 318);
            label5.Name = "label5";
            label5.Size = new Size(446, 15);
            label5.TabIndex = 67;
            label5.Text = "La query deve restituire una sola colonna e, se compilata, sostituisce il codice fiscale.";
            // 
            // FormVerifica
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(verificaScriviDatabaseCheck);
            Controls.Add(verificaFaseElaborativaCombo);
            Controls.Add(label6);
            Controls.Add(label5);
            Controls.Add(verificaQueryCodiciFiscaliText);
            Controls.Add(label4);
            Controls.Add(verificaCodiceFiscaleText);
            Controls.Add(label3);
            Controls.Add(verificaAAText);
            Controls.Add(label2);
            Controls.Add(circularButton1);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormVerifica";
            Text = "FormVerifica";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private CircularButton circularButton1;
        private Label label2;
        private TextBox verificaAAText;
        private Label label3;
        private TextBox verificaCodiceFiscaleText;
        private Label label4;
        private TextBox verificaQueryCodiciFiscaliText;
        private Label label5;
        private Label label6;
        private ComboBox verificaFaseElaborativaCombo;
        private CheckBox verificaScriviDatabaseCheck;
    }
}
