namespace ProcedureNet7
{
    partial class MainUI
    {
        /// <summary>
        /// Variabile di progettazione necessaria.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Pulire le risorse in uso.
        /// </summary>
        /// <param name="disposing">ha valore true se le risorse gestite devono essere eliminate, false in caso contrario.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Codice generato da Progettazione Windows Form

        /// <summary>
        /// Metodo necessario per il supporto della finestra di progettazione. Non modificare
        /// il contenuto del metodo con l'editor di codice.
        /// </summary>
        private void InitializeComponent()
        {
            StartProcedureBtn = new Button();
            folderBrowserDialog = new FolderBrowserDialog();
            backgroundWorker = new System.ComponentModel.BackgroundWorker();
            progressBar = new ProgressBar();
            serverIP = new TextBox();
            databaseName = new TextBox();
            userID = new TextBox();
            password = new TextBox();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            memorizeConnectionCheckBox = new CheckBox();
            progressReport = new TextBox();
            chooseProcedure = new ComboBox();
            DatabaseInformations = new Panel();
            label27 = new Label();
            credentialDropdownCombo = new ComboBox();
            openFileDialog = new OpenFileDialog();
            openFileDialogSecondary = new OpenFileDialog();
            panelProceduraPagamenti = new Panel();
            proceduraPagamentiFiltroCheck = new CheckBox();
            label30 = new Label();
            pagamentiTipoProceduraCombo = new ComboBox();
            label26 = new Label();
            pagamentiNuovoMandatoTxt = new TextBox();
            label25 = new Label();
            pagamentiOldMandatoTxt = new TextBox();
            label24 = new Label();
            pagamentiDataRiftxt = new TextBox();
            label23 = new Label();
            pagamentiAATxt = new TextBox();
            pagamentiSalvataggiolbl = new Label();
            pagamentiSalvataggioBTN = new Button();
            panelProceduraFlussoDiRitorno = new Panel();
            label29 = new Label();
            proceduraFlussoRitornoTipoBandoTxt = new TextBox();
            label28 = new Label();
            proceduraFlussoRitornoNumMandatoTxt = new TextBox();
            proceduraFlussoRitornoFileLbl = new Label();
            proceduraFlussoRitornoFileBtn = new Button();
            panelStorni = new Panel();
            label31 = new Label();
            storniSelectedEseFinanziarioTxt = new TextBox();
            storniFilelbl = new Label();
            storniFileBtn = new Button();
            DatabaseInformations.SuspendLayout();
            panelProceduraPagamenti.SuspendLayout();
            panelProceduraFlussoDiRitorno.SuspendLayout();
            panelStorni.SuspendLayout();
            SuspendLayout();
            // 
            // StartProcedureBtn
            // 
            StartProcedureBtn.Location = new Point(10, 419);
            StartProcedureBtn.Margin = new Padding(4, 3, 4, 3);
            StartProcedureBtn.Name = "StartProcedureBtn";
            StartProcedureBtn.Size = new Size(651, 27);
            StartProcedureBtn.TabIndex = 1;
            StartProcedureBtn.Text = "Start procedure";
            StartProcedureBtn.UseVisualStyleBackColor = true;
            StartProcedureBtn.Click += StartProcedureBtn_Click;
            // 
            // progressBar
            // 
            progressBar.Location = new Point(10, 194);
            progressBar.Margin = new Padding(4, 3, 4, 3);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(488, 27);
            progressBar.TabIndex = 3;
            // 
            // serverIP
            // 
            serverIP.Location = new Point(7, 46);
            serverIP.Margin = new Padding(4, 3, 4, 3);
            serverIP.Name = "serverIP";
            serverIP.Size = new Size(116, 23);
            serverIP.TabIndex = 5;
            // 
            // databaseName
            // 
            databaseName.Location = new Point(130, 46);
            databaseName.Margin = new Padding(4, 3, 4, 3);
            databaseName.Name = "databaseName";
            databaseName.Size = new Size(116, 23);
            databaseName.TabIndex = 6;
            // 
            // userID
            // 
            userID.Location = new Point(254, 46);
            userID.Margin = new Padding(4, 3, 4, 3);
            userID.Name = "userID";
            userID.Size = new Size(116, 23);
            userID.TabIndex = 7;
            // 
            // password
            // 
            password.Location = new Point(377, 46);
            password.Margin = new Padding(4, 3, 4, 3);
            password.Name = "password";
            password.Size = new Size(116, 23);
            password.TabIndex = 8;
            password.UseSystemPasswordChar = true;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(7, 28);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(52, 15);
            label1.TabIndex = 9;
            label1.Text = "Server IP";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(127, 28);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(88, 15);
            label2.TabIndex = 10;
            label2.Text = "Database name";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(250, 28);
            label3.Margin = new Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new Size(44, 15);
            label3.TabIndex = 11;
            label3.Text = "User ID";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(374, 28);
            label4.Margin = new Padding(4, 0, 4, 0);
            label4.Name = "label4";
            label4.Size = new Size(57, 15);
            label4.TabIndex = 12;
            label4.Text = "Password";
            // 
            // memorizeConnectionCheckBox
            // 
            memorizeConnectionCheckBox.AutoSize = true;
            memorizeConnectionCheckBox.Location = new Point(502, 48);
            memorizeConnectionCheckBox.Margin = new Padding(4, 3, 4, 3);
            memorizeConnectionCheckBox.Name = "memorizeConnectionCheckBox";
            memorizeConnectionCheckBox.Size = new Size(142, 19);
            memorizeConnectionCheckBox.TabIndex = 13;
            memorizeConnectionCheckBox.Text = "Memorize connection";
            memorizeConnectionCheckBox.UseVisualStyleBackColor = true;
            // 
            // progressReport
            // 
            progressReport.Location = new Point(10, 227);
            progressReport.Margin = new Padding(4, 3, 4, 3);
            progressReport.Multiline = true;
            progressReport.Name = "progressReport";
            progressReport.ReadOnly = true;
            progressReport.ScrollBars = ScrollBars.Both;
            progressReport.Size = new Size(487, 175);
            progressReport.TabIndex = 14;
            // 
            // chooseProcedure
            // 
            chooseProcedure.DropDownStyle = ComboBoxStyle.DropDownList;
            chooseProcedure.FormattingEnabled = true;
            chooseProcedure.Location = new Point(517, 194);
            chooseProcedure.Margin = new Padding(4, 3, 4, 3);
            chooseProcedure.Name = "chooseProcedure";
            chooseProcedure.Size = new Size(140, 23);
            chooseProcedure.Sorted = true;
            chooseProcedure.TabIndex = 15;
            chooseProcedure.SelectedIndexChanged += ChooseProcedure_SelectedIndexChanged;
            // 
            // DatabaseInformations
            // 
            DatabaseInformations.Controls.Add(label27);
            DatabaseInformations.Controls.Add(credentialDropdownCombo);
            DatabaseInformations.Controls.Add(memorizeConnectionCheckBox);
            DatabaseInformations.Controls.Add(serverIP);
            DatabaseInformations.Controls.Add(databaseName);
            DatabaseInformations.Controls.Add(userID);
            DatabaseInformations.Controls.Add(label4);
            DatabaseInformations.Controls.Add(password);
            DatabaseInformations.Controls.Add(label3);
            DatabaseInformations.Controls.Add(label1);
            DatabaseInformations.Controls.Add(label2);
            DatabaseInformations.Location = new Point(10, 446);
            DatabaseInformations.Margin = new Padding(4, 3, 4, 3);
            DatabaseInformations.Name = "DatabaseInformations";
            DatabaseInformations.Size = new Size(656, 82);
            DatabaseInformations.TabIndex = 18;
            // 
            // label27
            // 
            label27.AutoSize = true;
            label27.Location = new Point(395, 11);
            label27.Margin = new Padding(4, 0, 4, 0);
            label27.Name = "label27";
            label27.Size = new Size(102, 15);
            label27.TabIndex = 27;
            label27.Text = "Seleziona account";
            // 
            // credentialDropdownCombo
            // 
            credentialDropdownCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            credentialDropdownCombo.FormattingEnabled = true;
            credentialDropdownCombo.Location = new Point(504, 3);
            credentialDropdownCombo.Margin = new Padding(4, 3, 4, 3);
            credentialDropdownCombo.Name = "credentialDropdownCombo";
            credentialDropdownCombo.Size = new Size(140, 23);
            credentialDropdownCombo.Sorted = true;
            credentialDropdownCombo.TabIndex = 26;
            credentialDropdownCombo.SelectedIndexChanged += credentialDropdownCombo_SelectedIndexChanged;
            // 
            // openFileDialog
            // 
            openFileDialog.FileName = "openFileDialog1";
            // 
            // openFileDialogSecondary
            // 
            openFileDialogSecondary.FileName = "openFileDialog1";
            // 
            // panelProceduraPagamenti
            // 
            panelProceduraPagamenti.Controls.Add(proceduraPagamentiFiltroCheck);
            panelProceduraPagamenti.Controls.Add(label30);
            panelProceduraPagamenti.Controls.Add(pagamentiTipoProceduraCombo);
            panelProceduraPagamenti.Controls.Add(label26);
            panelProceduraPagamenti.Controls.Add(pagamentiNuovoMandatoTxt);
            panelProceduraPagamenti.Controls.Add(label25);
            panelProceduraPagamenti.Controls.Add(pagamentiOldMandatoTxt);
            panelProceduraPagamenti.Controls.Add(label24);
            panelProceduraPagamenti.Controls.Add(pagamentiDataRiftxt);
            panelProceduraPagamenti.Controls.Add(label23);
            panelProceduraPagamenti.Controls.Add(pagamentiAATxt);
            panelProceduraPagamenti.Controls.Add(pagamentiSalvataggiolbl);
            panelProceduraPagamenti.Controls.Add(pagamentiSalvataggioBTN);
            panelProceduraPagamenti.Location = new Point(10, 6);
            panelProceduraPagamenti.Name = "panelProceduraPagamenti";
            panelProceduraPagamenti.Size = new Size(658, 181);
            panelProceduraPagamenti.TabIndex = 24;
            // 
            // proceduraPagamentiFiltroCheck
            // 
            proceduraPagamentiFiltroCheck.AutoSize = true;
            proceduraPagamentiFiltroCheck.Location = new Point(9, 119);
            proceduraPagamentiFiltroCheck.Name = "proceduraPagamentiFiltroCheck";
            proceduraPagamentiFiltroCheck.Size = new Size(102, 19);
            proceduraPagamentiFiltroCheck.TabIndex = 25;
            proceduraPagamentiFiltroCheck.Text = "Filtro Manuale";
            proceduraPagamentiFiltroCheck.UseVisualStyleBackColor = true;
            // 
            // label30
            // 
            label30.AutoSize = true;
            label30.Location = new Point(4, 36);
            label30.Name = "label30";
            label30.Size = new Size(87, 15);
            label30.TabIndex = 24;
            label30.Text = "Tipo Procedura";
            // 
            // pagamentiTipoProceduraCombo
            // 
            pagamentiTipoProceduraCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            pagamentiTipoProceduraCombo.FormattingEnabled = true;
            pagamentiTipoProceduraCombo.Location = new Point(95, 32);
            pagamentiTipoProceduraCombo.Name = "pagamentiTipoProceduraCombo";
            pagamentiTipoProceduraCombo.Size = new Size(186, 23);
            pagamentiTipoProceduraCombo.TabIndex = 23;
            // 
            // label26
            // 
            label26.AutoSize = true;
            label26.Location = new Point(347, 71);
            label26.Name = "label26";
            label26.Size = new Size(102, 15);
            label26.TabIndex = 12;
            label26.Text = "Numero mandato";
            // 
            // pagamentiNuovoMandatoTxt
            // 
            pagamentiNuovoMandatoTxt.Location = new Point(241, 64);
            pagamentiNuovoMandatoTxt.Name = "pagamentiNuovoMandatoTxt";
            pagamentiNuovoMandatoTxt.Size = new Size(100, 23);
            pagamentiNuovoMandatoTxt.TabIndex = 11;
            // 
            // label25
            // 
            label25.AutoSize = true;
            label25.Location = new Point(350, 95);
            label25.Name = "label25";
            label25.Size = new Size(193, 15);
            label25.TabIndex = 10;
            label25.Text = "Mandato da aggiornare (opzionale)";
            // 
            // pagamentiOldMandatoTxt
            // 
            pagamentiOldMandatoTxt.Location = new Point(241, 88);
            pagamentiOldMandatoTxt.Name = "pagamentiOldMandatoTxt";
            pagamentiOldMandatoTxt.Size = new Size(100, 23);
            pagamentiOldMandatoTxt.TabIndex = 9;
            // 
            // label24
            // 
            label24.AutoSize = true;
            label24.Location = new Point(109, 95);
            label24.Name = "label24";
            label24.Size = new Size(93, 15);
            label24.TabIndex = 5;
            label24.Text = "Data riferimento";
            // 
            // pagamentiDataRiftxt
            // 
            pagamentiDataRiftxt.Location = new Point(3, 87);
            pagamentiDataRiftxt.Name = "pagamentiDataRiftxt";
            pagamentiDataRiftxt.Size = new Size(100, 23);
            pagamentiDataRiftxt.TabIndex = 4;
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Location = new Point(109, 73);
            label23.Name = "label23";
            label23.Size = new Size(105, 15);
            label23.TabIndex = 3;
            label23.Text = "Anno Accademico";
            // 
            // pagamentiAATxt
            // 
            pagamentiAATxt.Location = new Point(3, 63);
            pagamentiAATxt.Name = "pagamentiAATxt";
            pagamentiAATxt.Size = new Size(100, 23);
            pagamentiAATxt.TabIndex = 2;
            // 
            // pagamentiSalvataggiolbl
            // 
            pagamentiSalvataggiolbl.AutoSize = true;
            pagamentiSalvataggiolbl.Location = new Point(191, 11);
            pagamentiSalvataggiolbl.Name = "pagamentiSalvataggiolbl";
            pagamentiSalvataggiolbl.Size = new Size(32, 15);
            pagamentiSalvataggiolbl.TabIndex = 1;
            pagamentiSalvataggiolbl.Text = "_____";
            // 
            // pagamentiSalvataggioBTN
            // 
            pagamentiSalvataggioBTN.Location = new Point(4, 4);
            pagamentiSalvataggioBTN.Name = "pagamentiSalvataggioBTN";
            pagamentiSalvataggioBTN.Size = new Size(179, 23);
            pagamentiSalvataggioBTN.TabIndex = 0;
            pagamentiSalvataggioBTN.Text = "Selezionare cartella salvataggio";
            pagamentiSalvataggioBTN.UseVisualStyleBackColor = true;
            pagamentiSalvataggioBTN.Click += pagamentiSalvataggioBTN_Click;
            // 
            // panelProceduraFlussoDiRitorno
            // 
            panelProceduraFlussoDiRitorno.Controls.Add(label29);
            panelProceduraFlussoDiRitorno.Controls.Add(proceduraFlussoRitornoTipoBandoTxt);
            panelProceduraFlussoDiRitorno.Controls.Add(label28);
            panelProceduraFlussoDiRitorno.Controls.Add(proceduraFlussoRitornoNumMandatoTxt);
            panelProceduraFlussoDiRitorno.Controls.Add(proceduraFlussoRitornoFileLbl);
            panelProceduraFlussoDiRitorno.Controls.Add(proceduraFlussoRitornoFileBtn);
            panelProceduraFlussoDiRitorno.Location = new Point(10, 6);
            panelProceduraFlussoDiRitorno.Name = "panelProceduraFlussoDiRitorno";
            panelProceduraFlussoDiRitorno.Size = new Size(658, 182);
            panelProceduraFlussoDiRitorno.TabIndex = 26;
            // 
            // label29
            // 
            label29.AutoSize = true;
            label29.Location = new Point(19, 74);
            label29.Name = "label29";
            label29.Size = new Size(67, 15);
            label29.TabIndex = 5;
            label29.Text = "Tipo bando";
            // 
            // proceduraFlussoRitornoTipoBandoTxt
            // 
            proceduraFlussoRitornoTipoBandoTxt.Location = new Point(188, 67);
            proceduraFlussoRitornoTipoBandoTxt.Name = "proceduraFlussoRitornoTipoBandoTxt";
            proceduraFlussoRitornoTipoBandoTxt.Size = new Size(100, 23);
            proceduraFlussoRitornoTipoBandoTxt.TabIndex = 4;
            // 
            // label28
            // 
            label28.AutoSize = true;
            label28.Location = new Point(20, 46);
            label28.Name = "label28";
            label28.Size = new Size(164, 15);
            label28.TabIndex = 3;
            label28.Text = "Numero mandato provvisorio";
            // 
            // proceduraFlussoRitornoNumMandatoTxt
            // 
            proceduraFlussoRitornoNumMandatoTxt.Location = new Point(189, 39);
            proceduraFlussoRitornoNumMandatoTxt.Name = "proceduraFlussoRitornoNumMandatoTxt";
            proceduraFlussoRitornoNumMandatoTxt.Size = new Size(100, 23);
            proceduraFlussoRitornoNumMandatoTxt.TabIndex = 2;
            // 
            // proceduraFlussoRitornoFileLbl
            // 
            proceduraFlussoRitornoFileLbl.AutoSize = true;
            proceduraFlussoRitornoFileLbl.Location = new Point(207, 16);
            proceduraFlussoRitornoFileLbl.Name = "proceduraFlussoRitornoFileLbl";
            proceduraFlussoRitornoFileLbl.Size = new Size(32, 15);
            proceduraFlussoRitornoFileLbl.TabIndex = 1;
            proceduraFlussoRitornoFileLbl.Text = "_____";
            // 
            // proceduraFlussoRitornoFileBtn
            // 
            proceduraFlussoRitornoFileBtn.Location = new Point(20, 12);
            proceduraFlussoRitornoFileBtn.Name = "proceduraFlussoRitornoFileBtn";
            proceduraFlussoRitornoFileBtn.Size = new Size(178, 23);
            proceduraFlussoRitornoFileBtn.TabIndex = 0;
            proceduraFlussoRitornoFileBtn.Text = "Selezionare il flusso di ritorno";
            proceduraFlussoRitornoFileBtn.UseVisualStyleBackColor = true;
            proceduraFlussoRitornoFileBtn.Click += proceduraFlussoRitornoFileBtn_Click;
            // 
            // panelStorni
            // 
            panelStorni.Controls.Add(label31);
            panelStorni.Controls.Add(storniSelectedEseFinanziarioTxt);
            panelStorni.Controls.Add(storniFilelbl);
            panelStorni.Controls.Add(storniFileBtn);
            panelStorni.Location = new Point(10, 6);
            panelStorni.Name = "panelStorni";
            panelStorni.Size = new Size(658, 182);
            panelStorni.TabIndex = 27;
            // 
            // label31
            // 
            label31.AutoSize = true;
            label31.Location = new Point(134, 46);
            label31.Name = "label31";
            label31.Size = new Size(161, 15);
            label31.TabIndex = 3;
            label31.Text = "Indicare l'esercizio finanziario";
            // 
            // storniSelectedEseFinanziarioTxt
            // 
            storniSelectedEseFinanziarioTxt.Location = new Point(26, 37);
            storniSelectedEseFinanziarioTxt.Name = "storniSelectedEseFinanziarioTxt";
            storniSelectedEseFinanziarioTxt.Size = new Size(100, 23);
            storniSelectedEseFinanziarioTxt.TabIndex = 2;
            // 
            // storniFilelbl
            // 
            storniFilelbl.AutoSize = true;
            storniFilelbl.Location = new Point(171, 18);
            storniFilelbl.Name = "storniFilelbl";
            storniFilelbl.Size = new Size(32, 15);
            storniFilelbl.TabIndex = 1;
            storniFilelbl.Text = "_____";
            // 
            // storniFileBtn
            // 
            storniFileBtn.Location = new Point(25, 13);
            storniFileBtn.Name = "storniFileBtn";
            storniFileBtn.Size = new Size(137, 23);
            storniFileBtn.TabIndex = 0;
            storniFileBtn.Text = "Seleziona file storni";
            storniFileBtn.UseVisualStyleBackColor = true;
            storniFileBtn.Click += storniFileBtn_Click;
            // 
            // MainUI
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(680, 527);
            Controls.Add(panelProceduraPagamenti);
            Controls.Add(panelStorni);
            Controls.Add(panelProceduraFlussoDiRitorno);
            Controls.Add(DatabaseInformations);
            Controls.Add(progressReport);
            Controls.Add(StartProcedureBtn);
            Controls.Add(progressBar);
            Controls.Add(chooseProcedure);
            Margin = new Padding(4, 3, 4, 3);
            Name = "MainUI";
            Text = "MainUI";
            DatabaseInformations.ResumeLayout(false);
            DatabaseInformations.PerformLayout();
            panelProceduraPagamenti.ResumeLayout(false);
            panelProceduraPagamenti.PerformLayout();
            panelProceduraFlussoDiRitorno.ResumeLayout(false);
            panelProceduraFlussoDiRitorno.PerformLayout();
            panelStorni.ResumeLayout(false);
            panelStorni.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Button StartProcedureBtn;
        private FolderBrowserDialog folderBrowserDialog;
        private System.ComponentModel.BackgroundWorker backgroundWorker;
        private ProgressBar progressBar;
        private TextBox serverIP;
        private TextBox databaseName;
        private TextBox userID;
        private TextBox password;
        private Label label1;
        private Label label2;
        private Label label3;
        private Label label4;
        private CheckBox memorizeConnectionCheckBox;
        private TextBox progressReport;
        private ComboBox chooseProcedure;
        private Panel DatabaseInformations;
        private OpenFileDialog openFileDialog;
        private OpenFileDialog openFileDialogSecondary;
        private Panel panelProceduraPagamenti;
        private Button pagamentiSalvataggioBTN;
        private Label pagamentiSalvataggiolbl;
        private Label label23;
        private TextBox pagamentiAATxt;
        private Label label24;
        private TextBox pagamentiDataRiftxt;
        private Label label26;
        private TextBox pagamentiNuovoMandatoTxt;
        private Label label25;
        private TextBox pagamentiOldMandatoTxt;
        private Label label30;
        private ComboBox pagamentiTipoProceduraCombo;
        private ComboBox credentialDropdownCombo;
        private Label label27;
        private Panel panelProceduraFlussoDiRitorno;
        private Label proceduraFlussoRitornoFileLbl;
        private Button proceduraFlussoRitornoFileBtn;
        private Label label28;
        private TextBox proceduraFlussoRitornoNumMandatoTxt;
        private Label label29;
        private TextBox proceduraFlussoRitornoTipoBandoTxt;
        private Panel panelStorni;
        private Label storniFilelbl;
        private Button storniFileBtn;
        private Label label31;
        private TextBox storniSelectedEseFinanziarioTxt;
        private CheckBox proceduraPagamentiFiltroCheck;
    }
}

