namespace ProcedureNet7
{
    partial class FormProceduraRendiconto
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
            button1 = new Button();
            procedureFolderSelectBtn = new Button();
            procedureFolderSelectLbl = new Label();
            label3 = new Label();
            label4 = new Label();
            procedureAAstartText = new TextBox();
            procedureAAendText = new TextBox();
            folderBrowserDialog1 = new FolderBrowserDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(251, 28);
            label1.TabIndex = 55;
            label1.Text = "PROCEDURA RENDICONTO";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 54;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // procedureFolderSelectBtn
            // 
            procedureFolderSelectBtn.Location = new Point(29, 60);
            procedureFolderSelectBtn.Name = "procedureFolderSelectBtn";
            procedureFolderSelectBtn.Size = new Size(200, 23);
            procedureFolderSelectBtn.TabIndex = 56;
            procedureFolderSelectBtn.Text = "Seleziona cartella salvataggio";
            procedureFolderSelectBtn.UseVisualStyleBackColor = true;
            procedureFolderSelectBtn.Click += ProcedureFolderSelectBtn_Click;
            // 
            // procedureFolderSelectLbl
            // 
            procedureFolderSelectLbl.AutoSize = true;
            procedureFolderSelectLbl.Location = new Point(235, 68);
            procedureFolderSelectLbl.Name = "procedureFolderSelectLbl";
            procedureFolderSelectLbl.Size = new Size(32, 15);
            procedureFolderSelectLbl.TabIndex = 57;
            procedureFolderSelectLbl.Text = "_____";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(29, 93);
            label3.Name = "label3";
            label3.Size = new Size(134, 15);
            label3.TabIndex = 58;
            label3.Text = "Anno accademico inizio";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(29, 121);
            label4.Name = "label4";
            label4.Size = new Size(126, 15);
            label4.TabIndex = 59;
            label4.Text = "Anno accademico fine";
            // 
            // procedureAAstartText
            // 
            procedureAAstartText.Location = new Point(167, 89);
            procedureAAstartText.Name = "procedureAAstartText";
            procedureAAstartText.Size = new Size(100, 23);
            procedureAAstartText.TabIndex = 60;
            // 
            // procedureAAendText
            // 
            procedureAAendText.Location = new Point(167, 118);
            procedureAAendText.Name = "procedureAAendText";
            procedureAAendText.Size = new Size(100, 23);
            procedureAAendText.TabIndex = 61;
            // 
            // FormProceduraRendiconto
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            ControlBox = false;
            Controls.Add(procedureAAendText);
            Controls.Add(procedureAAstartText);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(procedureFolderSelectLbl);
            Controls.Add(procedureFolderSelectBtn);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraRendiconto";
            Text = "FormProceduraRendiconto";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button procedureFolderSelectBtn;
        private Label procedureFolderSelectLbl;
        private Label label3;
        private Label label4;
        private TextBox procedureAAstartText;
        private TextBox procedureAAendText;
        private FolderBrowserDialog folderBrowserDialog1;
    }
}