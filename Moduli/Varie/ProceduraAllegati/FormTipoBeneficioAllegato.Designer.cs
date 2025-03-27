namespace ProcedureNet7
{
    partial class FormTipoBeneficioAllegato
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
            beneficiBox = new ComboBox();
            BeneficiSelezionatiPanel = new FlowLayoutPanel();
            buttonMain_OK = new Button();
            buttonMain_Cancel = new Button();
            SuspendLayout();
            // 
            // beneficiBox
            // 
            beneficiBox.DropDownStyle = ComboBoxStyle.DropDownList;
            beneficiBox.FormattingEnabled = true;
            beneficiBox.Location = new Point(12, 21);
            beneficiBox.Name = "beneficiBox";
            beneficiBox.Size = new Size(239, 23);
            beneficiBox.TabIndex = 0;
            beneficiBox.SelectedIndexChanged += beneficiBox_SelectedIndexChanged;
            // 
            // BeneficiSelezionatiPanel
            // 
            BeneficiSelezionatiPanel.Location = new Point(12, 50);
            BeneficiSelezionatiPanel.Name = "BeneficiSelezionatiPanel";
            BeneficiSelezionatiPanel.Size = new Size(369, 228);
            BeneficiSelezionatiPanel.TabIndex = 1;
            // 
            // buttonMain_OK
            // 
            buttonMain_OK.Location = new Point(306, 284);
            buttonMain_OK.Name = "buttonMain_OK";
            buttonMain_OK.Size = new Size(75, 23);
            buttonMain_OK.TabIndex = 2;
            buttonMain_OK.Text = "Ok";
            buttonMain_OK.UseVisualStyleBackColor = true;
            buttonMain_OK.Click += buttonMain_OK_Click;
            // 
            // buttonMain_Cancel
            // 
            buttonMain_Cancel.ImageAlign = ContentAlignment.TopCenter;
            buttonMain_Cancel.Location = new Point(225, 284);
            buttonMain_Cancel.Name = "buttonMain_Cancel";
            buttonMain_Cancel.Size = new Size(75, 23);
            buttonMain_Cancel.TabIndex = 3;
            buttonMain_Cancel.Text = "Cancel";
            buttonMain_Cancel.UseVisualStyleBackColor = true;
            buttonMain_Cancel.Click += buttonMain_Cancel_Click;
            // 
            // FormTipoBeneficioAllegato
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(393, 314);
            Controls.Add(buttonMain_Cancel);
            Controls.Add(buttonMain_OK);
            Controls.Add(BeneficiSelezionatiPanel);
            Controls.Add(beneficiBox);
            Name = "FormTipoBeneficioAllegato";
            Text = "FormTipoBeneficioAllegato";
            ResumeLayout(false);
        }

        #endregion

        private ComboBox beneficiBox;
        private FlowLayoutPanel BeneficiSelezionatiPanel;
        private Button buttonMain_OK;
        private Button buttonMain_Cancel;
    }
}