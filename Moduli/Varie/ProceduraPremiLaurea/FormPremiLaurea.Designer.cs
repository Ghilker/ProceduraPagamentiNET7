namespace ProcedureNet7
{ 
    partial class FormPremiLaurea
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
            PremiLaureaSavelbl = new Label();
            PremiLaureaSavebtn = new Button();
            PremiLaureaFilelbl = new Label();
            PremiLaureaFilebtn = new Button();
            openFileDialog = new OpenFileDialog();
            saveFolderDialog = new FolderBrowserDialog();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(263, 28);
            label1.TabIndex = 33;
            label1.Text = "PROCEDURA PREMI LAUREA";
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
            // PremiLaureaSavelbl
            // 
            PremiLaureaSavelbl.AutoSize = true;
            PremiLaureaSavelbl.Location = new Point(304, 100);
            PremiLaureaSavelbl.Name = "PremiLaureaSavelbl";
            PremiLaureaSavelbl.Size = new Size(32, 15);
            PremiLaureaSavelbl.TabIndex = 43;
            PremiLaureaSavelbl.Text = "_____";
            // 
            // PremiLaureaSavebtn
            // 
            PremiLaureaSavebtn.Location = new Point(29, 92);
            PremiLaureaSavebtn.Name = "PremiLaureaSavebtn";
            PremiLaureaSavebtn.Size = new Size(267, 23);
            PremiLaureaSavebtn.TabIndex = 42;
            PremiLaureaSavebtn.Text = "Seleziona Cartella salvataggio";
            PremiLaureaSavebtn.UseVisualStyleBackColor = true;
            PremiLaureaSavebtn.Click += PremiLaureaSavebtn_Click;
            // 
            // PremiLaureaFilelbl
            // 
            PremiLaureaFilelbl.AutoSize = true;
            PremiLaureaFilelbl.Location = new Point(304, 71);
            PremiLaureaFilelbl.Margin = new Padding(4, 0, 4, 0);
            PremiLaureaFilelbl.Name = "PremiLaureaFilelbl";
            PremiLaureaFilelbl.Size = new Size(32, 15);
            PremiLaureaFilelbl.TabIndex = 41;
            PremiLaureaFilelbl.Text = "_____";
            // 
            // PremiLaureaFilebtn
            // 
            PremiLaureaFilebtn.Location = new Point(29, 59);
            PremiLaureaFilebtn.Margin = new Padding(4, 3, 4, 3);
            PremiLaureaFilebtn.Name = "PremiLaureaFilebtn";
            PremiLaureaFilebtn.Size = new Size(267, 27);
            PremiLaureaFilebtn.TabIndex = 40;
            PremiLaureaFilebtn.Text = "Seleziona file excel";
            PremiLaureaFilebtn.UseVisualStyleBackColor = true;
            PremiLaureaFilebtn.Click += PremiLaureaFilebtn_Click;
            // 
            // openFileDialog
            // 
            openFileDialog.FileName = "openFileDialog";
            // 
            // FormPremiLaurea
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(PremiLaureaSavelbl);
            Controls.Add(PremiLaureaSavebtn);
            Controls.Add(PremiLaureaFilelbl);
            Controls.Add(PremiLaureaFilebtn);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormPremiLaurea";
            Text = "FormPremiLaurea";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button button1;
        private Label PremiLaureaSavelbl;
        private Button PremiLaureaSavebtn;
        private Label PremiLaureaFilelbl;
        private Button PremiLaureaFilebtn;
        private OpenFileDialog openFileDialog;
        private FolderBrowserDialog saveFolderDialog;
    }
}