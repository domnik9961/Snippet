namespace SnippetMgr
{
    partial class SqlScriptForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.GroupBox groupBoxConn;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtServer;
        private System.Windows.Forms.RadioButton rbWindowsAuth;
        private System.Windows.Forms.RadioButton rbSqlAuth;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtUser;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.Button btnConnect;

        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.ComboBox cmbDatabases;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox cmbTables;

        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox txtWhere;
        private System.Windows.Forms.CheckBox chkIncludeData;
        private System.Windows.Forms.CheckBox chkUseTempTable;

        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox txtFreeQuery;
        private System.Windows.Forms.Button btnGenerateFromQuery;

        private System.Windows.Forms.Button btnGenerate;
        private System.Windows.Forms.Button btnCopy;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.TextBox txtScript;

        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            groupBoxConn = new GroupBox();
            label1 = new Label();
            txtServer = new TextBox();
            rbWindowsAuth = new RadioButton();
            rbSqlAuth = new RadioButton();
            label2 = new Label();
            txtUser = new TextBox();
            label3 = new Label();
            txtPassword = new TextBox();
            btnConnect = new Button();
            label4 = new Label();
            cmbDatabases = new ComboBox();
            label5 = new Label();
            cmbTables = new ComboBox();
            label6 = new Label();
            txtWhere = new TextBox();
            chkIncludeData = new CheckBox();
            chkUseTempTable = new CheckBox();
            label7 = new Label();
            txtFreeQuery = new TextBox();
            btnGenerateFromQuery = new Button();
            btnGenerate = new Button();
            btnCopy = new Button();
            btnSave = new Button();
            txtScript = new TextBox();
            statusStrip1 = new StatusStrip();
            lblStatus = new ToolStripStatusLabel();
            groupBoxConn.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // groupBoxConn
            // 
            groupBoxConn.Controls.Add(label1);
            groupBoxConn.Controls.Add(txtServer);
            groupBoxConn.Controls.Add(rbWindowsAuth);
            groupBoxConn.Controls.Add(rbSqlAuth);
            groupBoxConn.Controls.Add(label2);
            groupBoxConn.Controls.Add(txtUser);
            groupBoxConn.Controls.Add(label3);
            groupBoxConn.Controls.Add(txtPassword);
            groupBoxConn.Controls.Add(btnConnect);
            groupBoxConn.Location = new Point(12, 12);
            groupBoxConn.Name = "groupBoxConn";
            groupBoxConn.Size = new Size(528, 100);
            groupBoxConn.TabIndex = 0;
            groupBoxConn.TabStop = false;
            groupBoxConn.Text = "Połączenie z SQL Server";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(10, 22);
            label1.Name = "label1";
            label1.Size = new Size(45, 15);
            label1.TabIndex = 0;
            label1.Text = "Serwer:";
            // 
            // txtServer
            // 
            txtServer.Location = new Point(70, 19);
            txtServer.Name = "txtServer";
            txtServer.Size = new Size(300, 23);
            txtServer.TabIndex = 1;
            // 
            // rbWindowsAuth
            // 
            rbWindowsAuth.AutoSize = true;
            rbWindowsAuth.Location = new Point(13, 50);
            rbWindowsAuth.Name = "rbWindowsAuth";
            rbWindowsAuth.Size = new Size(74, 19);
            rbWindowsAuth.TabIndex = 2;
            rbWindowsAuth.TabStop = true;
            rbWindowsAuth.Text = "Windows";
            rbWindowsAuth.UseVisualStyleBackColor = true;
            // 
            // rbSqlAuth
            // 
            rbSqlAuth.AutoSize = true;
            rbSqlAuth.Location = new Point(13, 70);
            rbSqlAuth.Name = "rbSqlAuth";
            rbSqlAuth.Size = new Size(81, 19);
            rbSqlAuth.TabIndex = 3;
            rbSqlAuth.TabStop = true;
            rbSqlAuth.Text = "SQL Server";
            rbSqlAuth.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(117, 50);
            label2.Name = "label2";
            label2.Size = new Size(40, 15);
            label2.TabIndex = 4;
            label2.Text = "Login:";
            // 
            // txtUser
            // 
            txtUser.Location = new Point(117, 66);
            txtUser.Name = "txtUser";
            txtUser.Size = new Size(120, 23);
            txtUser.TabIndex = 5;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(247, 50);
            label3.Name = "label3";
            label3.Size = new Size(40, 15);
            label3.TabIndex = 6;
            label3.Text = "Hasło:";
            // 
            // txtPassword
            // 
            txtPassword.Location = new Point(250, 66);
            txtPassword.Name = "txtPassword";
            txtPassword.Size = new Size(120, 23);
            txtPassword.TabIndex = 7;
            txtPassword.UseSystemPasswordChar = true;
            // 
            // btnConnect
            // 
            btnConnect.Location = new Point(397, 39);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(100, 30);
            btnConnect.TabIndex = 1;
            btnConnect.Text = "Połącz";
            btnConnect.UseVisualStyleBackColor = true;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(12, 125);
            label4.Name = "label4";
            label4.Size = new Size(34, 15);
            label4.TabIndex = 2;
            label4.Text = "Baza:";
            // 
            // cmbDatabases
            // 
            cmbDatabases.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDatabases.FormattingEnabled = true;
            cmbDatabases.Location = new Point(70, 122);
            cmbDatabases.Name = "cmbDatabases";
            cmbDatabases.Size = new Size(200, 23);
            cmbDatabases.TabIndex = 3;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(290, 125);
            label5.Name = "label5";
            label5.Size = new Size(44, 15);
            label5.TabIndex = 4;
            label5.Text = "Tabela:";
            // 
            // cmbTables
            // 
            cmbTables.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTables.FormattingEnabled = true;
            cmbTables.Location = new Point(340, 122);
            cmbTables.Name = "cmbTables";
            cmbTables.Size = new Size(200, 23);
            cmbTables.TabIndex = 5;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(12, 155);
            label6.Name = "label6";
            label6.Size = new Size(119, 15);
            label6.TabIndex = 6;
            label6.Text = "WHERE (opcjonalne):";
            // 
            // txtWhere
            // 
            txtWhere.Location = new Point(136, 152);
            txtWhere.Name = "txtWhere";
            txtWhere.Size = new Size(298, 23);
            txtWhere.TabIndex = 7;
            // 
            // chkIncludeData
            // 
            chkIncludeData.AutoSize = true;
            chkIncludeData.Checked = true;
            chkIncludeData.CheckState = CheckState.Checked;
            chkIncludeData.Location = new Point(15, 180);
            chkIncludeData.Name = "chkIncludeData";
            chkIncludeData.Size = new Size(138, 19);
            chkIncludeData.TabIndex = 8;
            chkIncludeData.Text = "Dołącz dane (INSERT)";
            chkIncludeData.UseVisualStyleBackColor = true;
            // 
            // chkUseTempTable
            // 
            chkUseTempTable.AutoSize = true;
            chkUseTempTable.Location = new Point(170, 180);
            chkUseTempTable.Name = "chkUseTempTable";
            chkUseTempTable.Size = new Size(130, 19);
            chkUseTempTable.TabIndex = 9;
            chkUseTempTable.Text = "Generuj jako #temp";
            chkUseTempTable.UseVisualStyleBackColor = true;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(12, 210);
            label7.Name = "label7";
            label7.Size = new Size(146, 15);
            label7.TabIndex = 10;
            label7.Text = "Pełne SELECT (do INSERT):";
            // 
            // txtFreeQuery
            // 
            txtFreeQuery.Location = new Point(15, 226);
            txtFreeQuery.Multiline = true;
            txtFreeQuery.Name = "txtFreeQuery";
            txtFreeQuery.ScrollBars = ScrollBars.Vertical;
            txtFreeQuery.Size = new Size(650, 49);
            txtFreeQuery.TabIndex = 11;
            // 
            // btnGenerateFromQuery
            // 
            btnGenerateFromQuery.Location = new Point(412, 190);
            btnGenerateFromQuery.Name = "btnGenerateFromQuery";
            btnGenerateFromQuery.Size = new Size(106, 30);
            btnGenerateFromQuery.TabIndex = 12;
            btnGenerateFromQuery.Text = "INSERT z SELECT";
            btnGenerateFromQuery.UseVisualStyleBackColor = true;
            // 
            // btnGenerate
            // 
            btnGenerate.Location = new Point(524, 190);
            btnGenerate.Name = "btnGenerate";
            btnGenerate.Size = new Size(100, 30);
            btnGenerate.TabIndex = 13;
            btnGenerate.Text = "Generuj skrypt";
            btnGenerate.UseVisualStyleBackColor = true;
            // 
            // btnCopy
            // 
            btnCopy.Location = new Point(440, 147);
            btnCopy.Name = "btnCopy";
            btnCopy.Size = new Size(100, 30);
            btnCopy.TabIndex = 14;
            btnCopy.Text = "Kopiuj";
            btnCopy.UseVisualStyleBackColor = true;
            // 
            // btnSave
            // 
            btnSave.Location = new Point(306, 190);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(100, 30);
            btnSave.TabIndex = 15;
            btnSave.Text = "Zapisz do pliku";
            btnSave.UseVisualStyleBackColor = true;
            // 
            // txtScript
            // 
            txtScript.AcceptsReturn = true;
            txtScript.AcceptsTab = true;
            txtScript.Location = new Point(15, 317);
            txtScript.Multiline = true;
            txtScript.Name = "txtScript";
            txtScript.ScrollBars = ScrollBars.Both;
            txtScript.Size = new Size(650, 200);
            txtScript.TabIndex = 16;
            txtScript.WordWrap = false;
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { lblStatus });
            statusStrip1.Location = new Point(0, 535);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(684, 22);
            statusStrip1.TabIndex = 17;
            statusStrip1.Text = "statusStrip1";
            // 
            // lblStatus
            // 
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(51, 17);
            lblStatus.Text = "Gotowy.";
            // 
            // SqlScriptForm
            // 
            ClientSize = new Size(684, 557);
            Controls.Add(statusStrip1);
            Controls.Add(txtScript);
            Controls.Add(btnSave);
            Controls.Add(btnCopy);
            Controls.Add(btnGenerate);
            Controls.Add(btnGenerateFromQuery);
            Controls.Add(txtFreeQuery);
            Controls.Add(label7);
            Controls.Add(chkUseTempTable);
            Controls.Add(chkIncludeData);
            Controls.Add(txtWhere);
            Controls.Add(label6);
            Controls.Add(cmbTables);
            Controls.Add(label5);
            Controls.Add(cmbDatabases);
            Controls.Add(label4);
            Controls.Add(groupBoxConn);
            MinimumSize = new Size(700, 596);
            Name = "SqlScriptForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "SQL Script Tool";
            groupBoxConn.ResumeLayout(false);
            groupBoxConn.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
