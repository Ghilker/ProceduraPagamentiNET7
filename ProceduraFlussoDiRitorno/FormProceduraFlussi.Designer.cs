namespace ProcedureNet7
{
    partial class FormProceduraFlussi
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
            proceduraFlussoRitornoNomeFileCheck = new CheckBox();
            label28 = new Label();
            proceduraFlussoRitornoNumMandatoTxt = new TextBox();
            proceduraFlussoRitornoFileLbl = new Label();
            proceduraFlussoRitornoFileBtn = new Button();
            label1 = new Label();
            button1 = new Button();
            openFileDialog = new OpenFileDialog();
            SuspendLayout();
            // 
            // proceduraFlussoRitornoNomeFileCheck
            // 
            proceduraFlussoRitornoNomeFileCheck.AutoSize = true;
            proceduraFlussoRitornoNomeFileCheck.Location = new Point(29, 121);
            proceduraFlussoRitornoNomeFileCheck.Name = "proceduraFlussoRitornoNomeFileCheck";
            proceduraFlussoRitornoNomeFileCheck.Size = new Size(193, 19);
            proceduraFlussoRitornoNomeFileCheck.TabIndex = 11;
            proceduraFlussoRitornoNomeFileCheck.Text = "Numero mandato nel nome file";
            proceduraFlussoRitornoNomeFileCheck.UseVisualStyleBackColor = true;
            // 
            // label28
            // 
            label28.AutoSize = true;
            label28.Location = new Point(29, 91);
            label28.Name = "label28";
            label28.Size = new Size(164, 15);
            label28.TabIndex = 10;
            label28.Text = "Numero mandato provvisorio";
            // 
            // proceduraFlussoRitornoNumMandatoTxt
            // 
            proceduraFlussoRitornoNumMandatoTxt.Location = new Point(222, 88);
            proceduraFlussoRitornoNumMandatoTxt.Name = "proceduraFlussoRitornoNumMandatoTxt";
            proceduraFlussoRitornoNumMandatoTxt.Size = new Size(100, 23);
            proceduraFlussoRitornoNumMandatoTxt.TabIndex = 9;
            // 
            // proceduraFlussoRitornoFileLbl
            // 
            proceduraFlussoRitornoFileLbl.AutoSize = true;
            proceduraFlussoRitornoFileLbl.Location = new Point(222, 60);
            proceduraFlussoRitornoFileLbl.Name = "proceduraFlussoRitornoFileLbl";
            proceduraFlussoRitornoFileLbl.Size = new Size(32, 15);
            proceduraFlussoRitornoFileLbl.TabIndex = 8;
            proceduraFlussoRitornoFileLbl.Text = "_____";
            // 
            // proceduraFlussoRitornoFileBtn
            // 
            proceduraFlussoRitornoFileBtn.Location = new Point(29, 56);
            proceduraFlussoRitornoFileBtn.Name = "proceduraFlussoRitornoFileBtn";
            proceduraFlussoRitornoFileBtn.Size = new Size(178, 23);
            proceduraFlussoRitornoFileBtn.TabIndex = 7;
            proceduraFlussoRitornoFileBtn.Text = "Selezionare il flusso di ritorno";
            proceduraFlussoRitornoFileBtn.UseVisualStyleBackColor = true;
            proceduraFlussoRitornoFileBtn.Click += ProceduraFlussoRitornoFileBtn_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(29, 11);
            label1.Name = "label1";
            label1.Size = new Size(302, 28);
            label1.TabIndex = 55;
            label1.Text = "PROCEDURA FLUSSI DI RITORNO";
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
            // openFileDialog
            // 
            openFileDialog.FileName = "openFileDialog1";
            // 
            // FormProceduraFlussi
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            ControlBox = false;
            Controls.Add(label1);
            Controls.Add(button1);
            Controls.Add(proceduraFlussoRitornoNomeFileCheck);
            Controls.Add(label28);
            Controls.Add(proceduraFlussoRitornoNumMandatoTxt);
            Controls.Add(proceduraFlussoRitornoFileLbl);
            Controls.Add(proceduraFlussoRitornoFileBtn);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraFlussi";
            Text = "FormProceduraFlussi";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private CheckBox proceduraFlussoRitornoNomeFileCheck;
        private Label label28;
        private TextBox proceduraFlussoRitornoNumMandatoTxt;
        private Label proceduraFlussoRitornoFileLbl;
        private Button proceduraFlussoRitornoFileBtn;
        private Label label1;
        private Button button1;
        private OpenFileDialog openFileDialog;
    }
}