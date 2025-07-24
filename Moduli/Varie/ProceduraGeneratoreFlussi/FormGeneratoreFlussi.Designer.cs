namespace ProcedureNet7
{ 
    partial class FormGeneratoreFlussi
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
            GenflussiFilebtn = new Button();
            GenflussiFilelbl = new Label();
            openFileDialog = new OpenFileDialog();
            GenflussiSavebtn = new Button();
            GenflussiSavelbl = new Label();
            saveFolderDialog = new FolderBrowserDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(130, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA ";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F);
            button1.Location = new Point(460, 291);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 32;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBtnClick;
            // 
            // GenflussiFilebtn
            // 
            GenflussiFilebtn.Location = new Point(29, 56);
            GenflussiFilebtn.Margin = new Padding(4, 3, 4, 3);
            GenflussiFilebtn.Name = "GenflussiFilebtn";
            GenflussiFilebtn.Size = new Size(267, 27);
            GenflussiFilebtn.TabIndex = 34;
            GenflussiFilebtn.Text = "Seleziona file excel";
            GenflussiFilebtn.UseVisualStyleBackColor = true;
            GenflussiFilebtn.Click += GenflussiFilebtn_Click;
            // 
            // GenflussiFilelbl
            // 
            GenflussiFilelbl.AutoSize = true;
            GenflussiFilelbl.Location = new Point(304, 68);
            GenflussiFilelbl.Margin = new Padding(4, 0, 4, 0);
            GenflussiFilelbl.Name = "GenflussiFilelbl";
            GenflussiFilelbl.Size = new Size(32, 15);
            GenflussiFilelbl.TabIndex = 35;
            GenflussiFilelbl.Text = "_____";
            // 
            // openFileDialog
            // 
            openFileDialog.FileName = "openFileDialog1";
            // 
            // GenflussiSavebtn
            // 
            GenflussiSavebtn.Location = new Point(29, 89);
            GenflussiSavebtn.Name = "GenflussiSavebtn";
            GenflussiSavebtn.Size = new Size(267, 23);
            GenflussiSavebtn.TabIndex = 38;
            GenflussiSavebtn.Text = "Seleziona Cartella salvataggio";
            GenflussiSavebtn.UseVisualStyleBackColor = true;
            GenflussiSavebtn.Click += GenflussiSavebtn_Click;
            // 
            // GenflussiSavelbl
            // 
            GenflussiSavelbl.AutoSize = true;
            GenflussiSavelbl.Location = new Point(304, 97);
            GenflussiSavelbl.Name = "GenflussiSavelbl";
            GenflussiSavelbl.Size = new Size(32, 15);
            GenflussiSavelbl.TabIndex = 39;
            GenflussiSavelbl.Text = "_____";
            // 
            // FormGeneratoreFlussi
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(GenflussiSavelbl);
            Controls.Add(GenflussiSavebtn);
            Controls.Add(GenflussiFilelbl);
            Controls.Add(GenflussiFilebtn);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormGeneratoreFlussi";
            Text = "Form_procedure_name_";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Button GenflussiFilebtn;
        private Label GenflussiFilelbl;
        private OpenFileDialog openFileDialog;
        private Button GenflussiSavebtn;
        private Label GenflussiSavelbl;
        private FolderBrowserDialog saveFolderDialog;
    }
}