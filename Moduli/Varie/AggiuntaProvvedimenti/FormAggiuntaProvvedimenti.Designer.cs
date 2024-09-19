namespace ProcedureNet7
{
    partial class FormAggiuntaProvvedimenti
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
            label11 = new Label();
            provvedimentiNotaText = new TextBox();
            label10 = new Label();
            provvedimentiBox = new ComboBox();
            label9 = new Label();
            provvedimentiDataText = new TextBox();
            label8 = new Label();
            provvedimentiAAText = new TextBox();
            label7 = new Label();
            provvedimentiNumeroText = new TextBox();
            provvedimentiFolderlbl = new Label();
            ProvvedimentiFolderbtn = new Button();
            label1 = new Label();
            button1 = new Button();
            folderBrowserDialog = new FolderBrowserDialog();
            label2 = new Label();
            provvedimentiBeneficioBox = new ExtendedComboBox();
            specificheImpPRBox = new TextBox();
            label30 = new Label();
            label36 = new Label();
            specificheImpSABox = new TextBox();
            specificheEseSABox = new TextBox();
            label31 = new Label();
            label37 = new Label();
            specificheEsePRBox = new TextBox();
            specificheTipoFondoBox = new TextBox();
            label33 = new Label();
            specificheCapitoloBox = new TextBox();
            label34 = new Label();
            panelInserimentoImpegni = new Panel();
            provvedimentiRequiredSpecificheImpegni = new CheckBox();
            panelInserimentoImpegni.SuspendLayout();
            SuspendLayout();
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(29, 229);
            label11.Margin = new Padding(4, 0, 4, 0);
            label11.Name = "label11";
            label11.Size = new Size(117, 15);
            label11.TabIndex = 37;
            label11.Text = "Nota provvedimento";
            // 
            // provvedimentiNotaText
            // 
            provvedimentiNotaText.Location = new Point(154, 227);
            provvedimentiNotaText.Margin = new Padding(4, 3, 4, 3);
            provvedimentiNotaText.Name = "provvedimentiNotaText";
            provvedimentiNotaText.Size = new Size(361, 23);
            provvedimentiNotaText.TabIndex = 36;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(29, 84);
            label10.Margin = new Padding(4, 0, 4, 0);
            label10.Name = "label10";
            label10.Size = new Size(138, 15);
            label10.TabIndex = 35;
            label10.Text = "tipologia provvedimento";
            // 
            // provvedimentiBox
            // 
            provvedimentiBox.DropDownStyle = ComboBoxStyle.DropDownList;
            provvedimentiBox.FormattingEnabled = true;
            provvedimentiBox.Location = new Point(287, 81);
            provvedimentiBox.Margin = new Padding(4, 3, 4, 3);
            provvedimentiBox.Name = "provvedimentiBox";
            provvedimentiBox.Size = new Size(228, 23);
            provvedimentiBox.Sorted = true;
            provvedimentiBox.TabIndex = 34;
            provvedimentiBox.SelectedIndexChanged += ProvvedimentiBox_SelectedIndexChanged;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(29, 201);
            label9.Margin = new Padding(4, 0, 4, 0);
            label9.Name = "label9";
            label9.Size = new Size(242, 15);
            label9.TabIndex = 33;
            label9.Text = "Data provvedimento - formato dd/mm/yyyy";
            // 
            // provvedimentiDataText
            // 
            provvedimentiDataText.Location = new Point(287, 198);
            provvedimentiDataText.Margin = new Padding(4, 3, 4, 3);
            provvedimentiDataText.Name = "provvedimentiDataText";
            provvedimentiDataText.Size = new Size(228, 23);
            provvedimentiDataText.TabIndex = 32;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(29, 172);
            label8.Margin = new Padding(4, 0, 4, 0);
            label8.Name = "label8";
            label8.Size = new Size(218, 15);
            label8.TabIndex = 31;
            label8.Text = "A.A. provvedimento - formato xxxxyyyy";
            // 
            // provvedimentiAAText
            // 
            provvedimentiAAText.Location = new Point(287, 169);
            provvedimentiAAText.Margin = new Padding(4, 3, 4, 3);
            provvedimentiAAText.Name = "provvedimentiAAText";
            provvedimentiAAText.Size = new Size(228, 23);
            provvedimentiAAText.TabIndex = 30;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(29, 144);
            label7.Margin = new Padding(4, 0, 4, 0);
            label7.Name = "label7";
            label7.Size = new Size(133, 15);
            label7.TabIndex = 29;
            label7.Text = "numero provvedimento";
            // 
            // provvedimentiNumeroText
            // 
            provvedimentiNumeroText.Location = new Point(287, 140);
            provvedimentiNumeroText.Margin = new Padding(4, 3, 4, 3);
            provvedimentiNumeroText.Name = "provvedimentiNumeroText";
            provvedimentiNumeroText.Size = new Size(228, 23);
            provvedimentiNumeroText.TabIndex = 28;
            // 
            // provvedimentiFolderlbl
            // 
            provvedimentiFolderlbl.AutoSize = true;
            provvedimentiFolderlbl.Location = new Point(287, 57);
            provvedimentiFolderlbl.Margin = new Padding(4, 0, 4, 0);
            provvedimentiFolderlbl.Name = "provvedimentiFolderlbl";
            provvedimentiFolderlbl.Size = new Size(32, 15);
            provvedimentiFolderlbl.TabIndex = 27;
            provvedimentiFolderlbl.Text = "_____";
            // 
            // ProvvedimentiFolderbtn
            // 
            ProvvedimentiFolderbtn.Location = new Point(29, 51);
            ProvvedimentiFolderbtn.Margin = new Padding(4, 3, 4, 3);
            ProvvedimentiFolderbtn.Name = "ProvvedimentiFolderbtn";
            ProvvedimentiFolderbtn.Size = new Size(204, 27);
            ProvvedimentiFolderbtn.TabIndex = 26;
            ProvvedimentiFolderbtn.Text = "Seleziona Cartella Provvedimenti";
            ProvvedimentiFolderbtn.UseVisualStyleBackColor = true;
            ProvvedimentiFolderbtn.Click += ProvvedimentiFolderbtn_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(381, 28);
            label1.TabIndex = 39;
            label1.Text = "PROCEDURA AGGIUNTA PROVVEDIMENTI";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 38;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(29, 114);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(141, 15);
            label2.TabIndex = 41;
            label2.Text = "benefici/o da considerare";
            // 
            // provvedimentiBeneficioBox
            // 
            provvedimentiBeneficioBox.DropDownStyle = ComboBoxStyle.DropDownList;
            provvedimentiBeneficioBox.FormattingEnabled = true;
            provvedimentiBeneficioBox.Location = new Point(287, 111);
            provvedimentiBeneficioBox.Margin = new Padding(4, 3, 4, 3);
            provvedimentiBeneficioBox.Name = "provvedimentiBeneficioBox";
            provvedimentiBeneficioBox.Size = new Size(228, 23);
            provvedimentiBeneficioBox.Sorted = true;
            provvedimentiBeneficioBox.TabIndex = 40;
            // 
            // specificheImpPRBox
            // 
            specificheImpPRBox.Location = new Point(121, 3);
            specificheImpPRBox.Name = "specificheImpPRBox";
            specificheImpPRBox.Size = new Size(131, 23);
            specificheImpPRBox.TabIndex = 50;
            // 
            // label30
            // 
            label30.AutoSize = true;
            label30.Location = new Point(3, 6);
            label30.Name = "label30";
            label30.Size = new Size(112, 15);
            label30.TabIndex = 51;
            label30.Text = "Impegno prima rata";
            // 
            // label36
            // 
            label36.AutoSize = true;
            label36.Location = new Point(3, 123);
            label36.Name = "label36";
            label36.Size = new Size(55, 15);
            label36.TabIndex = 61;
            label36.Text = "Ese saldo";
            // 
            // specificheImpSABox
            // 
            specificheImpSABox.Location = new Point(121, 32);
            specificheImpSABox.Name = "specificheImpSABox";
            specificheImpSABox.Size = new Size(131, 23);
            specificheImpSABox.TabIndex = 52;
            // 
            // specificheEseSABox
            // 
            specificheEseSABox.Location = new Point(121, 120);
            specificheEseSABox.Name = "specificheEseSABox";
            specificheEseSABox.Size = new Size(131, 23);
            specificheEseSABox.TabIndex = 60;
            // 
            // label31
            // 
            label31.AutoSize = true;
            label31.Location = new Point(3, 36);
            label31.Name = "label31";
            label31.Size = new Size(86, 15);
            label31.TabIndex = 53;
            label31.Text = "Impegno saldo";
            // 
            // label37
            // 
            label37.AutoSize = true;
            label37.Location = new Point(3, 94);
            label37.Name = "label37";
            label37.Size = new Size(81, 15);
            label37.TabIndex = 59;
            label37.Text = "Ese prima rata";
            // 
            // specificheEsePRBox
            // 
            specificheEsePRBox.Location = new Point(121, 91);
            specificheEsePRBox.Name = "specificheEsePRBox";
            specificheEsePRBox.Size = new Size(131, 23);
            specificheEsePRBox.TabIndex = 58;
            // 
            // specificheTipoFondoBox
            // 
            specificheTipoFondoBox.Location = new Point(121, 62);
            specificheTipoFondoBox.Name = "specificheTipoFondoBox";
            specificheTipoFondoBox.Size = new Size(131, 23);
            specificheTipoFondoBox.TabIndex = 54;
            // 
            // label33
            // 
            label33.AutoSize = true;
            label33.Location = new Point(3, 65);
            label33.Name = "label33";
            label33.Size = new Size(65, 15);
            label33.TabIndex = 55;
            label33.Text = "Tipo fondo";
            // 
            // specificheCapitoloBox
            // 
            specificheCapitoloBox.Location = new Point(121, 149);
            specificheCapitoloBox.Name = "specificheCapitoloBox";
            specificheCapitoloBox.Size = new Size(131, 23);
            specificheCapitoloBox.TabIndex = 56;
            // 
            // label34
            // 
            label34.AutoSize = true;
            label34.Location = new Point(3, 151);
            label34.Name = "label34";
            label34.Size = new Size(52, 15);
            label34.TabIndex = 57;
            label34.Text = "Capitolo";
            // 
            // panelInserimentoImpegni
            // 
            panelInserimentoImpegni.BackColor = Color.Silver;
            panelInserimentoImpegni.BorderStyle = BorderStyle.Fixed3D;
            panelInserimentoImpegni.Controls.Add(label30);
            panelInserimentoImpegni.Controls.Add(specificheImpPRBox);
            panelInserimentoImpegni.Controls.Add(label34);
            panelInserimentoImpegni.Controls.Add(specificheCapitoloBox);
            panelInserimentoImpegni.Controls.Add(label36);
            panelInserimentoImpegni.Controls.Add(label33);
            panelInserimentoImpegni.Controls.Add(specificheImpSABox);
            panelInserimentoImpegni.Controls.Add(specificheTipoFondoBox);
            panelInserimentoImpegni.Controls.Add(specificheEseSABox);
            panelInserimentoImpegni.Controls.Add(specificheEsePRBox);
            panelInserimentoImpegni.Controls.Add(label31);
            panelInserimentoImpegni.Controls.Add(label37);
            panelInserimentoImpegni.Location = new Point(522, 76);
            panelInserimentoImpegni.Margin = new Padding(0);
            panelInserimentoImpegni.Name = "panelInserimentoImpegni";
            panelInserimentoImpegni.Size = new Size(266, 179);
            panelInserimentoImpegni.TabIndex = 62;
            // 
            // provvedimentiRequiredSpecificheImpegni
            // 
            provvedimentiRequiredSpecificheImpegni.AutoSize = true;
            provvedimentiRequiredSpecificheImpegni.Location = new Point(29, 256);
            provvedimentiRequiredSpecificheImpegni.Name = "provvedimentiRequiredSpecificheImpegni";
            provvedimentiRequiredSpecificheImpegni.Size = new Size(340, 19);
            provvedimentiRequiredSpecificheImpegni.TabIndex = 63;
            provvedimentiRequiredSpecificheImpegni.Text = "Necessaria chiusura/eventuale apertura specifiche impegno";
            provvedimentiRequiredSpecificheImpegni.UseVisualStyleBackColor = true;
            provvedimentiRequiredSpecificheImpegni.CheckedChanged += ProvvedimentiRequiredSpecificheImpegni_CheckedChanged;
            // 
            // FormAggiuntaProvvedimenti
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            ControlBox = false;
            Controls.Add(provvedimentiRequiredSpecificheImpegni);
            Controls.Add(panelInserimentoImpegni);
            Controls.Add(label2);
            Controls.Add(provvedimentiBeneficioBox);
            Controls.Add(label1);
            Controls.Add(button1);
            Controls.Add(label11);
            Controls.Add(provvedimentiNotaText);
            Controls.Add(label10);
            Controls.Add(provvedimentiBox);
            Controls.Add(label9);
            Controls.Add(provvedimentiDataText);
            Controls.Add(label8);
            Controls.Add(provvedimentiAAText);
            Controls.Add(label7);
            Controls.Add(provvedimentiNumeroText);
            Controls.Add(provvedimentiFolderlbl);
            Controls.Add(ProvvedimentiFolderbtn);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormAggiuntaProvvedimenti";
            Text = "FormAggiuntaProvvedimenti";
            panelInserimentoImpegni.ResumeLayout(false);
            panelInserimentoImpegni.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label11;
        private TextBox provvedimentiNotaText;
        private Label label10;
        private ComboBox provvedimentiBox;
        private Label label9;
        private TextBox provvedimentiDataText;
        private Label label8;
        private TextBox provvedimentiAAText;
        private Label label7;
        private TextBox provvedimentiNumeroText;
        private Label provvedimentiFolderlbl;
        private Button ProvvedimentiFolderbtn;
        private Label label1;
        private Button button1;
        private FolderBrowserDialog folderBrowserDialog;
        private Label label2;
        private ExtendedComboBox provvedimentiBeneficioBox;
        private TextBox specificheImpPRBox;
        private Label label30;
        private Label label36;
        private TextBox specificheImpSABox;
        private TextBox specificheEseSABox;
        private Label label31;
        private Label label37;
        private TextBox specificheEsePRBox;
        private TextBox specificheTipoFondoBox;
        private Label label33;
        private TextBox specificheCapitoloBox;
        private Label label34;
        private Panel panelInserimentoImpegni;
        private CheckBox provvedimentiRequiredSpecificheImpegni;
    }
}