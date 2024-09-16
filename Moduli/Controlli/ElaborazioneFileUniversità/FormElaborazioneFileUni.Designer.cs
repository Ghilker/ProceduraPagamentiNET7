namespace ProcedureNet7
{
    partial class FormElaborazioneFileUni
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
            elaborazioneFileUniExcelbtn = new Button();
            elaborazioneFileUniExcellbl = new Label();
            folderBrowserDialog1 = new FolderBrowserDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(421, 28);
            label1.TabIndex = 29;
            label1.Text = "PROCEDURA ELABORAZIONE FILE UNIVERSITÀ";
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
            // elaborazioneFileUniExcelbtn
            // 
            elaborazioneFileUniExcelbtn.Location = new Point(29, 55);
            elaborazioneFileUniExcelbtn.Name = "elaborazioneFileUniExcelbtn";
            elaborazioneFileUniExcelbtn.Size = new Size(267, 23);
            elaborazioneFileUniExcelbtn.TabIndex = 30;
            elaborazioneFileUniExcelbtn.Text = "Seleziona File Università";
            elaborazioneFileUniExcelbtn.UseVisualStyleBackColor = true;
            elaborazioneFileUniExcelbtn.Click += CaricamentoFileUnibtn_Click;
            // 
            // elaborazioneFileUniExcellbl
            // 
            elaborazioneFileUniExcellbl.AutoSize = true;
            elaborazioneFileUniExcellbl.Location = new Point(302, 63);
            elaborazioneFileUniExcellbl.Name = "elaborazioneFileUniExcellbl";
            elaborazioneFileUniExcellbl.Size = new Size(32, 15);
            elaborazioneFileUniExcellbl.TabIndex = 31;
            elaborazioneFileUniExcellbl.Text = "_____";
            // 
            // FormElaborazioneFileUni
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 350);
            ControlBox = false;
            Controls.Add(elaborazioneFileUniExcelbtn);
            Controls.Add(elaborazioneFileUniExcellbl);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormElaborazioneFileUni";
            Text = "FormElaborazioneFileUni";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button elaborazioneFileUniExcelbtn;
        private Label elaborazioneFileUniExcellbl;
        private FolderBrowserDialog folderBrowserDialog1;
    }
}