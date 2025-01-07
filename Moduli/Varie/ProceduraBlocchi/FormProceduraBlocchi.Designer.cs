namespace ProcedureNet7
{
    partial class FormProceduraBlocchi
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
            label6 = new Label();
            blocksUsername = new TextBox();
            label5 = new Label();
            blocksYear = new TextBox();
            blockFilePath = new Label();
            blockFileChooseBtn = new Button();
            label1 = new Label();
            button1 = new Button();
            openFileDialog = new OpenFileDialog();
            templateBlocchiDownload = new Button();
            blocksGiaRimossi = new CheckBox();
            blocksInsertMessaggioCheck = new CheckBox();
            SuspendLayout();
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(263, 112);
            label6.Margin = new Padding(4, 0, 4, 0);
            label6.Name = "label6";
            label6.Size = new Size(182, 15);
            label6.TabIndex = 11;
            label6.Text = "Inserisci nome utente per blocchi";
            // 
            // blocksUsername
            // 
            blocksUsername.Location = new Point(29, 104);
            blocksUsername.Margin = new Padding(4, 3, 4, 3);
            blocksUsername.Name = "blocksUsername";
            blocksUsername.Size = new Size(226, 23);
            blocksUsername.TabIndex = 10;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(263, 83);
            label5.Margin = new Padding(4, 0, 4, 0);
            label5.Name = "label5";
            label5.Size = new Size(251, 15);
            label5.TabIndex = 9;
            label5.Text = "Inserisci anno accademico (formato xxxxyyyy)";
            // 
            // blocksYear
            // 
            blocksYear.Location = new Point(29, 75);
            blocksYear.Margin = new Padding(4, 3, 4, 3);
            blocksYear.Name = "blocksYear";
            blocksYear.Size = new Size(226, 23);
            blocksYear.TabIndex = 8;
            // 
            // blockFilePath
            // 
            blockFilePath.AutoSize = true;
            blockFilePath.Location = new Point(263, 54);
            blockFilePath.Margin = new Padding(4, 0, 4, 0);
            blockFilePath.Name = "blockFilePath";
            blockFilePath.Size = new Size(32, 15);
            blockFilePath.TabIndex = 7;
            blockFilePath.Text = "_____";
            // 
            // blockFileChooseBtn
            // 
            blockFileChooseBtn.Location = new Point(29, 42);
            blockFileChooseBtn.Margin = new Padding(4, 3, 4, 3);
            blockFileChooseBtn.Name = "blockFileChooseBtn";
            blockFileChooseBtn.Size = new Size(226, 27);
            blockFileChooseBtn.TabIndex = 6;
            blockFileChooseBtn.Text = "Scegli File Excel";
            blockFileChooseBtn.UseVisualStyleBackColor = true;
            blockFileChooseBtn.Click += BlockFileChooseBtn_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(422, 28);
            label1.TabIndex = 29;
            label1.Text = "PROCEDURA AGGIUNTA/RIMOZIONE BLOCCHI";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 28;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // openFileDialog
            // 
            openFileDialog.FileName = "openFileDialog1";
            // 
            // templateBlocchiDownload
            // 
            templateBlocchiDownload.Location = new Point(561, 11);
            templateBlocchiDownload.Margin = new Padding(4, 3, 4, 3);
            templateBlocchiDownload.Name = "templateBlocchiDownload";
            templateBlocchiDownload.Size = new Size(226, 27);
            templateBlocchiDownload.TabIndex = 30;
            templateBlocchiDownload.Text = "Scarica il modello";
            templateBlocchiDownload.UseVisualStyleBackColor = true;
            templateBlocchiDownload.Click += TemplateBlocchiDownload_Click;
            // 
            // blocksGiaRimossi
            // 
            blocksGiaRimossi.AutoSize = true;
            blocksGiaRimossi.Location = new Point(29, 133);
            blocksGiaRimossi.Name = "blocksGiaRimossi";
            blocksGiaRimossi.Size = new Size(170, 19);
            blocksGiaRimossi.TabIndex = 31;
            blocksGiaRimossi.Text = "Inserisci blocchi già rimossi";
            blocksGiaRimossi.UseVisualStyleBackColor = true;
            // 
            // blocksInsertMessaggioCheck
            // 
            blocksInsertMessaggioCheck.AutoSize = true;
            blocksInsertMessaggioCheck.Location = new Point(29, 158);
            blocksInsertMessaggioCheck.Name = "blocksInsertMessaggioCheck";
            blocksInsertMessaggioCheck.Size = new Size(301, 19);
            blocksInsertMessaggioCheck.TabIndex = 32;
            blocksInsertMessaggioCheck.Text = "Inserisci messaggio motivazione inserimento blocco";
            blocksInsertMessaggioCheck.UseVisualStyleBackColor = true;
            // 
            // FormProceduraBlocchi
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            ControlBox = false;
            Controls.Add(blocksInsertMessaggioCheck);
            Controls.Add(blocksGiaRimossi);
            Controls.Add(templateBlocchiDownload);
            Controls.Add(label1);
            Controls.Add(button1);
            Controls.Add(label6);
            Controls.Add(blocksUsername);
            Controls.Add(label5);
            Controls.Add(blocksYear);
            Controls.Add(blockFilePath);
            Controls.Add(blockFileChooseBtn);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraBlocchi";
            Text = "FormProceduraBlocchi";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label6;
        private TextBox blocksUsername;
        private Label label5;
        private TextBox blocksYear;
        private Label blockFilePath;
        private Button blockFileChooseBtn;
        private Label label1;
        private Button button1;
        private OpenFileDialog openFileDialog;
        private Button templateBlocchiDownload;
        private CheckBox blocksGiaRimossi;
        private CheckBox blocksInsertMessaggioCheck;
    }
}