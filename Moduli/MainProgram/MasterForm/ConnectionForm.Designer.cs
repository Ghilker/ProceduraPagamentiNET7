namespace ProcedureNet7
{
    partial class ConnectionForm
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
            label27 = new Label();
            credentialDropdownCombo = new ExtendedComboBox();
            memorizeConnectionCheckBox = new CheckBox();
            serverIP = new TextBox();
            databaseName = new TextBox();
            userID = new TextBox();
            label4 = new Label();
            password = new TextBox();
            label3 = new Label();
            label1 = new Label();
            label2 = new Label();
            connectionButton = new Button();
            connectionLabel = new Label();
            SuspendLayout();
            // 
            // label27
            // 
            label27.AutoSize = true;
            label27.Location = new Point(13, 9);
            label27.Margin = new Padding(4, 0, 4, 0);
            label27.Name = "label27";
            label27.Size = new Size(102, 15);
            label27.TabIndex = 27;
            label27.Text = "Seleziona account";
            // 
            // credentialDropdownCombo
            // 
            credentialDropdownCombo.BackColor = SystemColors.Window;
            credentialDropdownCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            credentialDropdownCombo.FormattingEnabled = true;
            credentialDropdownCombo.Location = new Point(13, 27);
            credentialDropdownCombo.Margin = new Padding(4, 3, 4, 3);
            credentialDropdownCombo.Name = "credentialDropdownCombo";
            credentialDropdownCombo.Size = new Size(140, 23);
            credentialDropdownCombo.Sorted = true;
            credentialDropdownCombo.TabIndex = 26;
            credentialDropdownCombo.SelectedIndexChanged += CredentialDropdownCombo_SelectedIndexChanged;
            // 
            // memorizeConnectionCheckBox
            // 
            memorizeConnectionCheckBox.AutoSize = true;
            memorizeConnectionCheckBox.Location = new Point(14, 172);
            memorizeConnectionCheckBox.Margin = new Padding(4, 3, 4, 3);
            memorizeConnectionCheckBox.Name = "memorizeConnectionCheckBox";
            memorizeConnectionCheckBox.Size = new Size(165, 19);
            memorizeConnectionCheckBox.TabIndex = 13;
            memorizeConnectionCheckBox.Text = "Memorizza la connessione";
            memorizeConnectionCheckBox.UseVisualStyleBackColor = true;
            // 
            // serverIP
            // 
            serverIP.Location = new Point(121, 56);
            serverIP.Margin = new Padding(4, 3, 4, 3);
            serverIP.Name = "serverIP";
            serverIP.Size = new Size(116, 23);
            serverIP.TabIndex = 5;
            // 
            // databaseName
            // 
            databaseName.Location = new Point(121, 85);
            databaseName.Margin = new Padding(4, 3, 4, 3);
            databaseName.Name = "databaseName";
            databaseName.Size = new Size(116, 23);
            databaseName.TabIndex = 6;
            // 
            // userID
            // 
            userID.Location = new Point(121, 114);
            userID.Margin = new Padding(4, 3, 4, 3);
            userID.Name = "userID";
            userID.Size = new Size(116, 23);
            userID.TabIndex = 7;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(11, 143);
            label4.Margin = new Padding(4, 0, 4, 0);
            label4.Name = "label4";
            label4.Size = new Size(57, 15);
            label4.TabIndex = 12;
            label4.Text = "Password";
            // 
            // password
            // 
            password.Location = new Point(121, 143);
            password.Margin = new Padding(4, 3, 4, 3);
            password.Name = "password";
            password.Size = new Size(116, 23);
            password.TabIndex = 8;
            password.UseSystemPasswordChar = true;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(11, 114);
            label3.Margin = new Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new Size(44, 15);
            label3.TabIndex = 11;
            label3.Text = "User ID";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(11, 56);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(52, 15);
            label1.TabIndex = 9;
            label1.Text = "Server IP";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(11, 85);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(88, 15);
            label2.TabIndex = 10;
            label2.Text = "Database name";
            // 
            // connectionButton
            // 
            connectionButton.BackColor = Color.LightSteelBlue;
            connectionButton.BackgroundImageLayout = ImageLayout.Center;
            connectionButton.FlatStyle = FlatStyle.Popup;
            connectionButton.Location = new Point(11, 257);
            connectionButton.Name = "connectionButton";
            connectionButton.Size = new Size(226, 31);
            connectionButton.TabIndex = 28;
            connectionButton.Text = "Connetti al database";
            connectionButton.UseVisualStyleBackColor = false;
            connectionButton.Click += ConnectionButton_Click;
            // 
            // connectionLabel
            // 
            connectionLabel.AutoSize = true;
            connectionLabel.ForeColor = Color.Red;
            connectionLabel.Location = new Point(11, 239);
            connectionLabel.Name = "connectionLabel";
            connectionLabel.Size = new Size(83, 15);
            connectionLabel.TabIndex = 29;
            connectionLabel.Text = "Non connesso";
            // 
            // ConnectionForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(224, 224, 224);
            ClientSize = new Size(250, 300);
            Controls.Add(connectionLabel);
            Controls.Add(connectionButton);
            Controls.Add(label27);
            Controls.Add(credentialDropdownCombo);
            Controls.Add(memorizeConnectionCheckBox);
            Controls.Add(label1);
            Controls.Add(label4);
            Controls.Add(serverIP);
            Controls.Add(password);
            Controls.Add(userID);
            Controls.Add(databaseName);
            Controls.Add(label2);
            Controls.Add(label3);
            FormBorderStyle = FormBorderStyle.None;
            Name = "ConnectionForm";
            Text = "ConnectionForm";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label label27;
        private ExtendedComboBox credentialDropdownCombo;
        private CheckBox memorizeConnectionCheckBox;
        private TextBox serverIP;
        private TextBox databaseName;
        private TextBox userID;
        private Label label4;
        private TextBox password;
        private Label label3;
        private Label label1;
        private Label label2;
        private Button connectionButton;
        private Label connectionLabel;
    }
}