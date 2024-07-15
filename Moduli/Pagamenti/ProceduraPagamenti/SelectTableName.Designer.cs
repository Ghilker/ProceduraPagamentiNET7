namespace ProcedureNet7
{
    partial class SelectTableName
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
            promptTableNameNoBtn = new Button();
            promptTableNameYesBtn = new Button();
            promptTableNameTxt = new TextBox();
            label1 = new Label();
            SuspendLayout();
            // 
            // promptTableNameNoBtn
            // 
            promptTableNameNoBtn.Location = new Point(222, 81);
            promptTableNameNoBtn.Name = "promptTableNameNoBtn";
            promptTableNameNoBtn.Size = new Size(75, 23);
            promptTableNameNoBtn.TabIndex = 0;
            promptTableNameNoBtn.Text = "Annulla";
            promptTableNameNoBtn.UseVisualStyleBackColor = true;
            promptTableNameNoBtn.Click += promptTableNameNoBtn_Click;
            // 
            // promptTableNameYesBtn
            // 
            promptTableNameYesBtn.Location = new Point(303, 81);
            promptTableNameYesBtn.Name = "promptTableNameYesBtn";
            promptTableNameYesBtn.Size = new Size(75, 23);
            promptTableNameYesBtn.TabIndex = 1;
            promptTableNameYesBtn.Text = "Conferma";
            promptTableNameYesBtn.UseVisualStyleBackColor = true;
            promptTableNameYesBtn.Click += promptTableNameYesBtn_Click;
            // 
            // promptTableNameTxt
            // 
            promptTableNameTxt.Location = new Point(18, 39);
            promptTableNameTxt.Name = "promptTableNameTxt";
            promptTableNameTxt.Size = new Size(290, 23);
            promptTableNameTxt.TabIndex = 2;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(18, 21);
            label1.Name = "label1";
            label1.Size = new Size(360, 15);
            label1.TabIndex = 3;
            label1.Text = "Indicare il nome della tabella di riferimento/nuova tabella da creare";
            // 
            // SelectTableName
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(390, 116);
            ControlBox = false;
            Controls.Add(label1);
            Controls.Add(promptTableNameTxt);
            Controls.Add(promptTableNameYesBtn);
            Controls.Add(promptTableNameNoBtn);
            Name = "SelectTableName";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Selezionare Nome Tabella";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button promptTableNameNoBtn;
        private Button promptTableNameYesBtn;
        private TextBox promptTableNameTxt;
        private Label label1;
    }
}