namespace ProcedureNet7
{
    partial class FormProceduraTicket
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
            ticketSendMailCheck = new CheckBox();
            ticketMaillbl = new Label();
            ticketMailFilebtn = new Button();
            ticketDeleteRedCheck = new CheckBox();
            ticketDeleteChiusiCheck = new CheckBox();
            ticketFilelbl = new Label();
            ticketFileBtn = new Button();
            label1 = new Label();
            button1 = new Button();
            openFileDialog = new OpenFileDialog();
            SuspendLayout();
            // 
            // ticketSendMailCheck
            // 
            ticketSendMailCheck.AutoSize = true;
            ticketSendMailCheck.Location = new Point(51, 133);
            ticketSendMailCheck.Margin = new Padding(4, 3, 4, 3);
            ticketSendMailCheck.Name = "ticketSendMailCheck";
            ticketSendMailCheck.Size = new Size(439, 19);
            ticketSendMailCheck.TabIndex = 17;
            ticketSendMailCheck.Text = "Manda e-mail (Seleziona file e-mail contenente sia il mittente che i destinatari)";
            ticketSendMailCheck.UseVisualStyleBackColor = true;
            // 
            // ticketMaillbl
            // 
            ticketMaillbl.AutoSize = true;
            ticketMaillbl.Location = new Point(226, 170);
            ticketMaillbl.Margin = new Padding(4, 0, 4, 0);
            ticketMaillbl.Name = "ticketMaillbl";
            ticketMaillbl.Size = new Size(37, 15);
            ticketMaillbl.TabIndex = 16;
            ticketMaillbl.Text = "______";
            // 
            // ticketMailFilebtn
            // 
            ticketMailFilebtn.Location = new Point(51, 158);
            ticketMailFilebtn.Margin = new Padding(4, 3, 4, 3);
            ticketMailFilebtn.Name = "ticketMailFilebtn";
            ticketMailFilebtn.Size = new Size(167, 27);
            ticketMailFilebtn.TabIndex = 15;
            ticketMailFilebtn.Text = "Seleziona file e-mail";
            ticketMailFilebtn.UseVisualStyleBackColor = true;
            ticketMailFilebtn.Click += TicketMailFilebtn_Click;
            // 
            // ticketDeleteRedCheck
            // 
            ticketDeleteRedCheck.AutoSize = true;
            ticketDeleteRedCheck.Location = new Point(51, 108);
            ticketDeleteRedCheck.Margin = new Padding(4, 3, 4, 3);
            ticketDeleteRedCheck.Name = "ticketDeleteRedCheck";
            ticketDeleteRedCheck.Size = new Size(156, 19);
            ticketDeleteRedCheck.TabIndex = 14;
            ticketDeleteRedCheck.Text = "Cancella anni precedenti";
            ticketDeleteRedCheck.UseVisualStyleBackColor = true;
            // 
            // ticketDeleteChiusiCheck
            // 
            ticketDeleteChiusiCheck.AutoSize = true;
            ticketDeleteChiusiCheck.Location = new Point(51, 83);
            ticketDeleteChiusiCheck.Margin = new Padding(4, 3, 4, 3);
            ticketDeleteChiusiCheck.Name = "ticketDeleteChiusiCheck";
            ticketDeleteChiusiCheck.Size = new Size(137, 19);
            ticketDeleteChiusiCheck.TabIndex = 13;
            ticketDeleteChiusiCheck.Text = "Cancella ticket chiusi";
            ticketDeleteChiusiCheck.UseVisualStyleBackColor = true;
            // 
            // ticketFilelbl
            // 
            ticketFilelbl.AutoSize = true;
            ticketFilelbl.Location = new Point(342, 62);
            ticketFilelbl.Margin = new Padding(4, 0, 4, 0);
            ticketFilelbl.Name = "ticketFilelbl";
            ticketFilelbl.Size = new Size(37, 15);
            ticketFilelbl.TabIndex = 12;
            ticketFilelbl.Text = "______";
            // 
            // ticketFileBtn
            // 
            ticketFileBtn.Location = new Point(51, 50);
            ticketFileBtn.Margin = new Padding(4, 3, 4, 3);
            ticketFileBtn.Name = "ticketFileBtn";
            ticketFileBtn.Size = new Size(283, 27);
            ticketFileBtn.TabIndex = 11;
            ticketFileBtn.Text = "Seleziona file ticket";
            ticketFileBtn.UseVisualStyleBackColor = true;
            ticketFileBtn.Click += TicketFileBtn_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(51, 9);
            label1.Name = "label1";
            label1.Size = new Size(246, 28);
            label1.TabIndex = 28;
            label1.Text = "PROCEDURA INVIO TICKET";
            // 
            // button1
            // 
            button1.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            button1.Location = new Point(476, 289);
            button1.Name = "button1";
            button1.Size = new Size(312, 49);
            button1.TabIndex = 29;
            button1.Text = "AVVIA LA PROCEDURA";
            button1.UseVisualStyleBackColor = true;
            button1.Click += RunProcedureBTNClick;
            // 
            // openFileDialog
            // 
            openFileDialog.FileName = "openFileDialog1";
            // 
            // FormProceduraTicket
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(800, 350);
            ControlBox = false;
            Controls.Add(button1);
            Controls.Add(label1);
            Controls.Add(ticketSendMailCheck);
            Controls.Add(ticketMaillbl);
            Controls.Add(ticketMailFilebtn);
            Controls.Add(ticketDeleteRedCheck);
            Controls.Add(ticketDeleteChiusiCheck);
            Controls.Add(ticketFilelbl);
            Controls.Add(ticketFileBtn);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormProceduraTicket";
            Text = "FormProceduraTicket";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private CheckBox ticketSendMailCheck;
        private Label ticketMaillbl;
        private Button ticketMailFilebtn;
        private CheckBox ticketDeleteRedCheck;
        private CheckBox ticketDeleteChiusiCheck;
        private Label ticketFilelbl;
        private Button ticketFileBtn;
        private Label label1;
        private Button button1;
        private OpenFileDialog openFileDialog;
    }
}