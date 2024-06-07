namespace ProcedureNet7
{
    partial class FormProceduraStorni
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
            label31 = new Label();
            storniSelectedEseFinanziarioTxt = new TextBox();
            storniFilelbl = new Label();
            storniFileBtn = new Button();
            label1 = new Label();
            button1 = new Button();
            openFileDialog = new OpenFileDialog();
            SuspendLayout();
            // 
            // label31
            // 
            label31.AutoSize = true;
            label31.Location = new Point(29, 83);
            label31.Name = "label31";
            label31.Size = new Size(161, 15);
            label31.TabIndex = 7;
            label31.Text = "Indicare l'esercizio finanziario";
            // 
            // storniSelectedEseFinanziarioTxt
            // 
            storniSelectedEseFinanziarioTxt.Location = new Point(221, 80);
            storniSelectedEseFinanziarioTxt.Name = "storniSelectedEseFinanziarioTxt";
            storniSelectedEseFinanziarioTxt.Size = new Size(100, 23);
            storniSelectedEseFinanziarioTxt.TabIndex = 6;
            // 
            // storniFilelbl
            // 
            storniFilelbl.AutoSize = true;
            storniFilelbl.Location = new Point(221, 56);
            storniFilelbl.Name = "storniFilelbl";
            storniFilelbl.Size = new Size(32, 15);
            storniFilelbl.TabIndex = 5;
            storniFilelbl.Text = "_____";
            // 
            // storniFileBtn
            // 
            storniFileBtn.Location = new Point(29, 52);
            storniFileBtn.Name = "storniFileBtn";
            storniFileBtn.Size = new Size(137, 23);
            storniFileBtn.TabIndex = 4;
            storniFileBtn.Text = "Seleziona file storni";
            storniFileBtn.UseVisualStyleBackColor = true;
            storniFileBtn.Click += StorniFileBtn_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(197, 28);
            label1.TabIndex = 57;
            label1.Text = "PROCEDURA STORNI";
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
            button1.Click += RunProcedureBtnClick;
            // 
            // openFileDialog
            // 
            openFileDialog.FileName = "openFileDialog1";
            // 
            // FormProceduraStorni
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            Controls.Add(label1);
            Controls.Add(button1);
            Controls.Add(label31);
            Controls.Add(storniSelectedEseFinanziarioTxt);
            Controls.Add(storniFilelbl);
            Controls.Add(storniFileBtn);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraStorni";
            Text = "FormProceduraStorni";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label31;
        private TextBox storniSelectedEseFinanziarioTxt;
        private Label storniFilelbl;
        private Button storniFileBtn;
        private Label label1;
        private Button button1;
        private OpenFileDialog openFileDialog;
    }
}