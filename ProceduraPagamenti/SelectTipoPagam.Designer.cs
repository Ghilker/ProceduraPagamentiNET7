namespace ProcedureNet7
{
    partial class SelectTipoPagam
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
            promptTipoPagamNokBtn = new Button();
            promptTipoPagamOkBtn = new Button();
            promptBeneficioCombo = new ComboBox();
            label2 = new Label();
            label1 = new Label();
            promptTipoPagamCombo = new ComboBox();
            label3 = new Label();
            promptCategoriaPagamCombo = new ComboBox();
            label4 = new Label();
            promptEffettuarePagamCombo = new ComboBox();
            SuspendLayout();
            // 
            // promptTipoPagamNokBtn
            // 
            promptTipoPagamNokBtn.Location = new Point(12, 223);
            promptTipoPagamNokBtn.Name = "promptTipoPagamNokBtn";
            promptTipoPagamNokBtn.Size = new Size(75, 23);
            promptTipoPagamNokBtn.TabIndex = 4;
            promptTipoPagamNokBtn.Text = "Annulla";
            promptTipoPagamNokBtn.UseVisualStyleBackColor = true;
            promptTipoPagamNokBtn.Click += promptTipoPagamNokBtn_Click;
            // 
            // promptTipoPagamOkBtn
            // 
            promptTipoPagamOkBtn.Location = new Point(351, 223);
            promptTipoPagamOkBtn.Name = "promptTipoPagamOkBtn";
            promptTipoPagamOkBtn.Size = new Size(75, 23);
            promptTipoPagamOkBtn.TabIndex = 5;
            promptTipoPagamOkBtn.Text = "Conferma";
            promptTipoPagamOkBtn.UseVisualStyleBackColor = true;
            promptTipoPagamOkBtn.Click += promptTipoPagamOkBtn_Click;
            // 
            // promptBeneficioCombo
            // 
            promptBeneficioCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            promptBeneficioCombo.FormattingEnabled = true;
            promptBeneficioCombo.Location = new Point(12, 27);
            promptBeneficioCombo.Name = "promptBeneficioCombo";
            promptBeneficioCombo.Size = new Size(394, 23);
            promptBeneficioCombo.TabIndex = 2;
            promptBeneficioCombo.SelectedIndexChanged += promptBeneficioCombo_SelectedIndexChanged;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(12, 9);
            label2.Name = "label2";
            label2.Size = new Size(101, 15);
            label2.TabIndex = 3;
            label2.Text = "Indicare Beneficio";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(12, 53);
            label1.Name = "label1";
            label1.Size = new Size(165, 15);
            label1.TabIndex = 6;
            label1.Text = "Indicare Tipologia Pagamento";
            // 
            // promptTipoPagamCombo
            // 
            promptTipoPagamCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            promptTipoPagamCombo.FormattingEnabled = true;
            promptTipoPagamCombo.Location = new Point(12, 71);
            promptTipoPagamCombo.Name = "promptTipoPagamCombo";
            promptTipoPagamCombo.Size = new Size(394, 23);
            promptTipoPagamCombo.TabIndex = 7;
            promptTipoPagamCombo.SelectedIndexChanged += promptTipoPagamCombo_SelectedIndexChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(12, 97);
            label3.Name = "label3";
            label3.Size = new Size(167, 15);
            label3.TabIndex = 8;
            label3.Text = "Indicare Categoria Pagamento";
            // 
            // promptCategoriaPagamCombo
            // 
            promptCategoriaPagamCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            promptCategoriaPagamCombo.FormattingEnabled = true;
            promptCategoriaPagamCombo.Location = new Point(12, 115);
            promptCategoriaPagamCombo.Name = "promptCategoriaPagamCombo";
            promptCategoriaPagamCombo.Size = new Size(394, 23);
            promptCategoriaPagamCombo.TabIndex = 9;
            promptCategoriaPagamCombo.SelectedIndexChanged += promptCategoriaPagamCombo_SelectedIndexChanged;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(12, 141);
            label4.Name = "label4";
            label4.Size = new Size(192, 15);
            label4.TabIndex = 10;
            label4.Text = "Indicare il pagamento da effettuare";
            // 
            // promptEffettuarePagamCombo
            // 
            promptEffettuarePagamCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            promptEffettuarePagamCombo.FormattingEnabled = true;
            promptEffettuarePagamCombo.Location = new Point(12, 159);
            promptEffettuarePagamCombo.Name = "promptEffettuarePagamCombo";
            promptEffettuarePagamCombo.Size = new Size(394, 23);
            promptEffettuarePagamCombo.TabIndex = 11;
            // 
            // SelectTipoPagam
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(438, 258);
            ControlBox = false;
            Controls.Add(promptEffettuarePagamCombo);
            Controls.Add(label4);
            Controls.Add(promptCategoriaPagamCombo);
            Controls.Add(label3);
            Controls.Add(promptTipoPagamCombo);
            Controls.Add(label1);
            Controls.Add(promptTipoPagamOkBtn);
            Controls.Add(promptTipoPagamNokBtn);
            Controls.Add(label2);
            Controls.Add(promptBeneficioCombo);
            Name = "SelectTipoPagam";
            Text = "Selezionare tipo pagamento";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Button promptTipoPagamNokBtn;
        private Button promptTipoPagamOkBtn;
        private ComboBox promptBeneficioCombo;
        private Label label2;
        private Label label1;
        private ComboBox promptTipoPagamCombo;
        private Label label3;
        private ComboBox promptCategoriaPagamCombo;
        private Label label4;
        private ComboBox promptEffettuarePagamCombo;
    }
}