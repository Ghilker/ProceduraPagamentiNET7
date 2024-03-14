namespace ProcedureNet7
{
    partial class SelectPagamentoSettings
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
            promptTipoStudenteOkBtn = new Button();
            promptTipoStudenteNokBtn = new Button();
            label2 = new Label();
            promptTipoStudenteCombo = new ComboBox();
            label1 = new Label();
            promptImpegnoCombo = new ComboBox();
            label3 = new Label();
            promptCodEnteCombo = new ComboBox();
            SuspendLayout();
            // 
            // promptTipoStudenteOkBtn
            // 
            promptTipoStudenteOkBtn.Location = new Point(322, 144);
            promptTipoStudenteOkBtn.Name = "promptTipoStudenteOkBtn";
            promptTipoStudenteOkBtn.Size = new Size(75, 23);
            promptTipoStudenteOkBtn.TabIndex = 9;
            promptTipoStudenteOkBtn.Text = "Conferma";
            promptTipoStudenteOkBtn.UseVisualStyleBackColor = true;
            promptTipoStudenteOkBtn.Click += promptTipoStudenteOkBtn_Click;
            // 
            // promptTipoStudenteNokBtn
            // 
            promptTipoStudenteNokBtn.Location = new Point(3, 144);
            promptTipoStudenteNokBtn.Name = "promptTipoStudenteNokBtn";
            promptTipoStudenteNokBtn.Size = new Size(75, 23);
            promptTipoStudenteNokBtn.TabIndex = 8;
            promptTipoStudenteNokBtn.Text = "Annulla";
            promptTipoStudenteNokBtn.UseVisualStyleBackColor = true;
            promptTipoStudenteNokBtn.Click += promptTipoStudenteNokBtn_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(3, 97);
            label2.Name = "label2";
            label2.Size = new Size(122, 15);
            label2.TabIndex = 7;
            label2.Text = "Indicare tipo studente";
            // 
            // promptTipoStudenteCombo
            // 
            promptTipoStudenteCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            promptTipoStudenteCombo.FormattingEnabled = true;
            promptTipoStudenteCombo.Location = new Point(3, 115);
            promptTipoStudenteCombo.Name = "promptTipoStudenteCombo";
            promptTipoStudenteCombo.Size = new Size(394, 23);
            promptTipoStudenteCombo.TabIndex = 6;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(3, 9);
            label1.Name = "label1";
            label1.Size = new Size(106, 15);
            label1.TabIndex = 11;
            label1.Text = "Indicare l'impegno";
            // 
            // promptImpegnoCombo
            // 
            promptImpegnoCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            promptImpegnoCombo.FormattingEnabled = true;
            promptImpegnoCombo.Location = new Point(3, 27);
            promptImpegnoCombo.Name = "promptImpegnoCombo";
            promptImpegnoCombo.Size = new Size(394, 23);
            promptImpegnoCombo.TabIndex = 10;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(3, 53);
            label3.Name = "label3";
            label3.Size = new Size(168, 15);
            label3.TabIndex = 13;
            label3.Text = "Indicare l'ente di appartenenza";
            // 
            // promptCodEnteCombo
            // 
            promptCodEnteCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            promptCodEnteCombo.FormattingEnabled = true;
            promptCodEnteCombo.Location = new Point(3, 71);
            promptCodEnteCombo.Name = "promptCodEnteCombo";
            promptCodEnteCombo.Size = new Size(394, 23);
            promptCodEnteCombo.TabIndex = 12;
            // 
            // SelectPagamentoSettings
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(400, 176);
            ControlBox = false;
            Controls.Add(label3);
            Controls.Add(promptCodEnteCombo);
            Controls.Add(label1);
            Controls.Add(promptImpegnoCombo);
            Controls.Add(promptTipoStudenteOkBtn);
            Controls.Add(promptTipoStudenteNokBtn);
            Controls.Add(label2);
            Controls.Add(promptTipoStudenteCombo);
            Name = "SelectPagamentoSettings";
            Text = "Seleziona i parametri";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button promptTipoStudenteOkBtn;
        private Button promptTipoStudenteNokBtn;
        private Label label2;
        private ComboBox promptTipoStudenteCombo;
        private Label label1;
        private ComboBox promptImpegnoCombo;
        private Label label3;
        private ComboBox promptCodEnteCombo;
    }
}