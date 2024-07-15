
namespace ProcedureNet7
{
    partial class FormProceduraPagamenti
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
            proceduraPagamentiFiltroCheck = new CheckBox();
            label30 = new Label();
            pagamentiTipoProceduraCombo = new ComboBox();
            label26 = new Label();
            pagamentiNuovoMandatoTxt = new TextBox();
            label25 = new Label();
            pagamentiOldMandatoTxt = new TextBox();
            label24 = new Label();
            pagamentiDataRiftxt = new TextBox();
            label23 = new Label();
            pagamentiAATxt = new TextBox();
            pagamentiSalvataggiolbl = new Label();
            pagamentiSalvataggioBTN = new Button();
            label1 = new Label();
            button1 = new Button();
            folderBrowserDialog = new FolderBrowserDialog();
            SuspendLayout();
            // 
            // proceduraPagamentiFiltroCheck
            // 
            proceduraPagamentiFiltroCheck.AutoSize = true;
            proceduraPagamentiFiltroCheck.Location = new Point(460, 76);
            proceduraPagamentiFiltroCheck.Name = "proceduraPagamentiFiltroCheck";
            proceduraPagamentiFiltroCheck.Size = new Size(102, 19);
            proceduraPagamentiFiltroCheck.TabIndex = 38;
            proceduraPagamentiFiltroCheck.Text = "Filtro Manuale";
            proceduraPagamentiFiltroCheck.UseVisualStyleBackColor = true;
            // 
            // label30
            // 
            label30.AutoSize = true;
            label30.Location = new Point(29, 75);
            label30.Name = "label30";
            label30.Size = new Size(87, 15);
            label30.TabIndex = 37;
            label30.Text = "Tipo Procedura";
            // 
            // pagamentiTipoProceduraCombo
            // 
            pagamentiTipoProceduraCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            pagamentiTipoProceduraCombo.FormattingEnabled = true;
            pagamentiTipoProceduraCombo.Location = new Point(235, 72);
            pagamentiTipoProceduraCombo.Name = "pagamentiTipoProceduraCombo";
            pagamentiTipoProceduraCombo.Size = new Size(186, 23);
            pagamentiTipoProceduraCombo.TabIndex = 36;
            // 
            // label26
            // 
            label26.AutoSize = true;
            label26.Location = new Point(29, 161);
            label26.Name = "label26";
            label26.Size = new Size(164, 15);
            label26.TabIndex = 35;
            label26.Text = "Numero mandato (opzionale)";
            // 
            // pagamentiNuovoMandatoTxt
            // 
            pagamentiNuovoMandatoTxt.Location = new Point(235, 158);
            pagamentiNuovoMandatoTxt.Name = "pagamentiNuovoMandatoTxt";
            pagamentiNuovoMandatoTxt.Size = new Size(186, 23);
            pagamentiNuovoMandatoTxt.TabIndex = 34;
            // 
            // label25
            // 
            label25.AutoSize = true;
            label25.Location = new Point(29, 190);
            label25.Name = "label25";
            label25.Size = new Size(193, 15);
            label25.TabIndex = 33;
            label25.Text = "Mandato da aggiornare (opzionale)";
            // 
            // pagamentiOldMandatoTxt
            // 
            pagamentiOldMandatoTxt.Location = new Point(235, 187);
            pagamentiOldMandatoTxt.Name = "pagamentiOldMandatoTxt";
            pagamentiOldMandatoTxt.Size = new Size(186, 23);
            pagamentiOldMandatoTxt.TabIndex = 32;
            // 
            // label24
            // 
            label24.AutoSize = true;
            label24.Location = new Point(29, 132);
            label24.Name = "label24";
            label24.Size = new Size(93, 15);
            label24.TabIndex = 31;
            label24.Text = "Data riferimento";
            // 
            // pagamentiDataRiftxt
            // 
            pagamentiDataRiftxt.Location = new Point(235, 129);
            pagamentiDataRiftxt.Name = "pagamentiDataRiftxt";
            pagamentiDataRiftxt.Size = new Size(186, 23);
            pagamentiDataRiftxt.TabIndex = 30;
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Location = new Point(29, 104);
            label23.Name = "label23";
            label23.Size = new Size(105, 15);
            label23.TabIndex = 29;
            label23.Text = "Anno Accademico";
            // 
            // pagamentiAATxt
            // 
            pagamentiAATxt.Location = new Point(235, 101);
            pagamentiAATxt.Name = "pagamentiAATxt";
            pagamentiAATxt.Size = new Size(186, 23);
            pagamentiAATxt.TabIndex = 28;
            // 
            // pagamentiSalvataggiolbl
            // 
            pagamentiSalvataggiolbl.AutoSize = true;
            pagamentiSalvataggiolbl.Location = new Point(235, 46);
            pagamentiSalvataggiolbl.Name = "pagamentiSalvataggiolbl";
            pagamentiSalvataggiolbl.Size = new Size(32, 15);
            pagamentiSalvataggiolbl.TabIndex = 27;
            pagamentiSalvataggiolbl.Text = "_____";
            // 
            // pagamentiSalvataggioBTN
            // 
            pagamentiSalvataggioBTN.Location = new Point(29, 42);
            pagamentiSalvataggioBTN.Name = "pagamentiSalvataggioBTN";
            pagamentiSalvataggioBTN.Size = new Size(179, 23);
            pagamentiSalvataggioBTN.TabIndex = 26;
            pagamentiSalvataggioBTN.Text = "Selezionare cartella salvataggio";
            pagamentiSalvataggioBTN.UseVisualStyleBackColor = true;
            pagamentiSalvataggioBTN.Click += PagamentiSalvataggioBTN_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(238, 28);
            label1.TabIndex = 53;
            label1.Text = "PROCEDURA PAGAMENTI";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 52;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // FormProceduraPagamenti
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            ControlBox = false;
            Controls.Add(label1);
            Controls.Add(button1);
            Controls.Add(proceduraPagamentiFiltroCheck);
            Controls.Add(label30);
            Controls.Add(pagamentiTipoProceduraCombo);
            Controls.Add(label26);
            Controls.Add(pagamentiNuovoMandatoTxt);
            Controls.Add(label25);
            Controls.Add(pagamentiOldMandatoTxt);
            Controls.Add(label24);
            Controls.Add(pagamentiDataRiftxt);
            Controls.Add(label23);
            Controls.Add(pagamentiAATxt);
            Controls.Add(pagamentiSalvataggiolbl);
            Controls.Add(pagamentiSalvataggioBTN);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraPagamenti";
            Text = "FormProceduraPagamenti";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private CheckBox proceduraPagamentiFiltroCheck;
        private Label label30;
        private ComboBox pagamentiTipoProceduraCombo;
        private Label label26;
        private TextBox pagamentiNuovoMandatoTxt;
        private Label label25;
        private TextBox pagamentiOldMandatoTxt;
        private Label label24;
        private TextBox pagamentiDataRiftxt;
        private Label label23;
        private TextBox pagamentiAATxt;
        private Label pagamentiSalvataggiolbl;
        private Button pagamentiSalvataggioBTN;
        private Label label1;
        private Button button1;
        private FolderBrowserDialog folderBrowserDialog;
    }
}