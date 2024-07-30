
namespace ProcedureNet7
{
    partial class FormControlloPuntiBonus
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
            controlloBonusSaveFolder = new Button();
            controlloBonusFolderLbl = new Label();
            folderBrowserDialog1 = new FolderBrowserDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(371, 28);
            label1.TabIndex = 57;
            label1.Text = "PROCEDURA CONTROLLO PUNTI BONUS";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 56;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += this.RunProcedureBtnClick;
            // 
            // controlloBonusSaveFolder
            // 
            controlloBonusSaveFolder.Location = new Point(29, 66);
            controlloBonusSaveFolder.Name = "controlloBonusSaveFolder";
            controlloBonusSaveFolder.Size = new Size(182, 23);
            controlloBonusSaveFolder.TabIndex = 58;
            controlloBonusSaveFolder.Text = "Seleziona cartella salvataggio";
            controlloBonusSaveFolder.UseVisualStyleBackColor = true;
            controlloBonusSaveFolder.Click += controlloBonusSaveFolder_Click_1;
            // 
            // controlloBonusFolderLbl
            // 
            controlloBonusFolderLbl.AutoSize = true;
            controlloBonusFolderLbl.Location = new Point(217, 70);
            controlloBonusFolderLbl.Name = "controlloBonusFolderLbl";
            controlloBonusFolderLbl.Size = new Size(32, 15);
            controlloBonusFolderLbl.TabIndex = 59;
            controlloBonusFolderLbl.Text = "_____";
            // 
            // FormControlloPuntiBonus
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(controlloBonusFolderLbl);
            Controls.Add(controlloBonusSaveFolder);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormControlloPuntiBonus";
            Text = "FormControlloPuntiBonus";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button controlloBonusSaveFolder;
        private Label controlloBonusFolderLbl;
        private FolderBrowserDialog folderBrowserDialog1;
    }
}