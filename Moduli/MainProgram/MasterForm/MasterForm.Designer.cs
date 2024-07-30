namespace ProcedureNet7
{
    partial class MasterForm
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
            proceduresPanel = new Panel();
            connectionPanel = new Panel();
            masterLogbox = new RichTextBox();
            label1 = new Label();
            procedureSelect = new ExtendedComboBox();
            backgroundWorker = new System.ComponentModel.BackgroundWorker();
            progressBar = new ProgressBar();
            button1 = new Button();
            SuspendLayout();
            // 
            // proceduresPanel
            // 
            proceduresPanel.BorderStyle = BorderStyle.FixedSingle;
            proceduresPanel.Location = new Point(12, 12);
            proceduresPanel.Name = "proceduresPanel";
            proceduresPanel.Size = new Size(800, 350);
            proceduresPanel.TabIndex = 0;
            // 
            // connectionPanel
            // 
            connectionPanel.BorderStyle = BorderStyle.FixedSingle;
            connectionPanel.Location = new Point(561, 368);
            connectionPanel.Name = "connectionPanel";
            connectionPanel.Size = new Size(250, 300);
            connectionPanel.TabIndex = 1;
            // 
            // masterLogbox
            // 
            masterLogbox.Location = new Point(12, 424);
            masterLogbox.Name = "masterLogbox";
            masterLogbox.ReadOnly = true;
            masterLogbox.RightToLeft = RightToLeft.No;
            masterLogbox.ScrollBars = RichTextBoxScrollBars.ForcedVertical;
            masterLogbox.Size = new Size(543, 244);
            masterLogbox.TabIndex = 2;
            masterLogbox.Text = "";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(202, 368);
            label1.Name = "label1";
            label1.Size = new Size(135, 15);
            label1.TabIndex = 4;
            label1.Text = "Selezionare la procedura";
            // 
            // procedureSelect
            // 
            procedureSelect.DropDownStyle = ComboBoxStyle.DropDownList;
            procedureSelect.FormattingEnabled = true;
            procedureSelect.Location = new Point(343, 368);
            procedureSelect.Name = "procedureSelect";
            procedureSelect.Size = new Size(212, 23);
            procedureSelect.TabIndex = 3;
            procedureSelect.SelectedIndexChanged += ProcedureSelect_SelectedIndexChanged;
            // 
            // backgroundWorker
            // 
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
            // 
            // progressBar
            // 
            progressBar.Location = new Point(12, 397);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(543, 23);
            progressBar.TabIndex = 5;
            // 
            // button1
            // 
            button1.Location = new Point(12, 368);
            button1.Name = "button1";
            button1.Size = new Size(184, 23);
            button1.TabIndex = 6;
            button1.Text = "Cambia utente";
            button1.UseVisualStyleBackColor = true;
            button1.Click += ChangeUserButton_Click;
            // 
            // MasterForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(823, 677);
            Controls.Add(button1);
            Controls.Add(progressBar);
            Controls.Add(label1);
            Controls.Add(procedureSelect);
            Controls.Add(masterLogbox);
            Controls.Add(connectionPanel);
            Controls.Add(proceduresPanel);
            Name = "MasterForm";
            Text = "ProcedureNET7";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Panel proceduresPanel;
        private Panel connectionPanel;
        private RichTextBox masterLogbox;
        private Label label1;
        private ExtendedComboBox procedureSelect;
        private System.ComponentModel.BackgroundWorker backgroundWorker;
        private ProgressBar progressBar;
        private Button button1;
    }
}