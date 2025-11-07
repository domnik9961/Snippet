using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;   // używamy Microsoft.Data.SqlClient
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace SnippetMgr
{
    public partial class SqlScriptForm : Form
    {
        private string _baseConnectionString;   // bez nazwy bazy (tylko serwer + auth)
        private AppConfig _config;
        private readonly string _configPath;

        public SqlScriptForm()
        {
            InitializeComponent();

            // plik konfiguracyjny (opcjonalny)
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sqltool.config.json");

            // Ustawienia startowe
            rbWindowsAuth.Checked = true;
            UpdateAuthControls();
            lblStatus.Text = "Gotowy.";

            // RĘCZNE podpięcie zdarzeń – żeby na pewno działały
            btnConnect.Click += btnConnect_Click;
            rbWindowsAuth.CheckedChanged += rbWindowsAuth_CheckedChanged;
            rbSqlAuth.CheckedChanged += rbSqlAuth_CheckedChanged;
            cmbDatabases.SelectedIndexChanged += cmbDatabases_SelectedIndexChanged;
            btnGenerate.Click += btnGenerate_Click;
            btnCopy.Click += btnCopy_Click;
            btnSave.Click += btnSave_Click;

            // nowe – #temp + INSERT z SELECT
            chkUseTempTable.CheckedChanged += chkUseTempTable_CheckedChanged;
            btnGenerateFromQuery.Click += btnGenerateFromQuery_Click;

            // Próba wczytania configu i automatyczne połączenie
            LoadConfigAndAutoConnect();
        }

        private void LoadConfigAndAutoConnect()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    lblStatus.Text = "Brak pliku konfiguracyjnego – użyj ustawień ręcznych.";
                    return;
                }

                string json = File.ReadAllText(_configPath, Encoding.UTF8);
                _config = JsonSerializer.Deserialize<AppConfig>(json);

                if (_config == null)
                {
                    lblStatus.Text = "Nie udało się zdeserializować pliku konfiguracyjnego.";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_config.Server))
                    txtServer.Text = _config.Server;

                if (_config.UseWindowsAuth)
                {
                    rbWindowsAuth.Checked = true;
                }
                else
                {
                    rbSqlAuth.Checked = true;
                    txtUser.Text = _config.User ?? string.Empty;
                    txtPassword.Text = _config.Password ?? string.Empty;
                }

                // Automatyczne połączenie
                btnConnect_Click(this, EventArgs.Empty);
            }
            catch
            {
                lblStatus.Text = "Błąd wczytywania configu – pomijam plik.";
            }
        }

        private void rbWindowsAuth_CheckedChanged(object sender, EventArgs e)
        {
            UpdateAuthControls();
        }

        private void rbSqlAuth_CheckedChanged(object sender, EventArgs e)
        {
            UpdateAuthControls();
        }

        private void UpdateAuthControls()
        {
            bool sqlAuth = rbSqlAuth.Checked;
            txtUser.Enabled = sqlAuth;
            txtPassword.Enabled = sqlAuth;
        }

        private void chkUseTempTable_CheckedChanged(object sender, EventArgs e)
        {
            if (chkUseTempTable.Checked)
                lblStatus.Text = "Tworzenie skryptu z tabelą tymczasową #temp.";
            else
                lblStatus.Text = "Tworzenie skryptu dla tabeli docelowej.";
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                lblStatus.Text = "Łączenie z serwerem...";
                Cursor = Cursors.WaitCursor;

                if (string.IsNullOrWhiteSpace(txtServer.Text))
                {
                    MessageBox.Show("Podaj nazwę serwera (np. NAZWA-SERWERA lub NAZWA-SERWERA\\INSTANCJA).",
                        "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    lblStatus.Text = "Nie podano nazwy serwera.";
                    return;
                }

                if (rbSqlAuth.Checked)
                {
                    if (string.IsNullOrWhiteSpace(txtUser.Text) || string.IsNullOrWhiteSpace(txtPassword.Text))
                    {
                        MessageBox.Show("Podaj login i hasło dla uwierzytelniania SQL Server.",
                            "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        lblStatus.Text = "Brak loginu/hasła.";
                        return;
                    }
                }

                _baseConnectionString = BuildBaseConnectionString();
                string connStr = _baseConnectionString + "Database=master;";

                using (var conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    if (conn.State != ConnectionState.Open)
                    {
                        MessageBox.Show("Nie udało się połączyć z serwerem SQL.", "Błąd",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblStatus.Text = "Brak połączenia z serwerem.";
                        return;
                    }

                    var dbs = GetDatabases(conn);

                    cmbDatabases.Items.Clear();
                    foreach (var db in dbs)
                        cmbDatabases.Items.Add(db);

                    if (cmbDatabases.Items.Count > 0)
                        cmbDatabases.SelectedIndex = 0;

                    lblStatus.Text = $"Połączono z serwerem. Znaleziono baz: {dbs.Count}.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd połączenia: " + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Błąd połączenia z serwerem.";
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private string BuildBaseConnectionString()
        {
            var sb = new StringBuilder();
            sb.Append("Server=").Append(txtServer.Text.Trim()).Append(";");

            if (rbWindowsAuth.Checked)
            {
                sb.Append("Integrated Security=True;");
            }
            else
            {
                sb.Append("User Id=").Append(txtUser.Text.Trim()).Append(";");
                sb.Append("Password=").Append(txtPassword.Text).Append(";");
            }

            // obsługa problemów z certyfikatem
            sb.Append("Encrypt=True;");
            sb.Append("TrustServerCertificate=True;");

            return sb.ToString();
        }

        private List<string> GetDatabases(SqlConnection conn)
        {
            var list = new List<string>();

            using (var cmd = new SqlCommand(@"
                SELECT name 
                FROM sys.databases 
                WHERE database_id > 4  -- pomijamy systemowe
                ORDER BY name;", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    list.Add(r.GetString(0));
                }
            }

            return list;
        }

        private void cmbDatabases_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (cmbDatabases.SelectedItem == null) return;

                string dbName = cmbDatabases.SelectedItem.ToString();
                lblStatus.Text = $"Łączenie z bazą {dbName}...";
                Cursor = Cursors.WaitCursor;

                string connStr = _baseConnectionString + $"Database={dbName};";

                using (var conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    if (conn.State != ConnectionState.Open)
                    {
                        MessageBox.Show("Nie udało się połączyć z wybraną bazą.", "Błąd",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblStatus.Text = "Brak połączenia z bazą.";
                        return;
                    }

                    var tables = GetTables(conn);

                    cmbTables.Items.Clear();
                    foreach (var t in tables)
                        cmbTables.Items.Add(t);

                    if (cmbTables.Items.Count > 0)
                        cmbTables.SelectedIndex = 0;

                    lblStatus.Text = $"Baza {dbName}: tabel {tables.Count}.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd pobierania tabel: " + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Błąd pobierania tabel.";
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private List<string> GetTables(SqlConnection conn)
        {
            var list = new List<string>();

            using (var cmd = new SqlCommand(@"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME;", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string schema = r.GetString(0);
                    string name = r.GetString(1);
                    list.Add($"{schema}.{name}");
                }
            }

            return list;
        }

        // =====================================
        //  GENEROWANIE SKRYPTU DLA TABELI
        // =====================================
        private void btnGenerate_Click(object sender, EventArgs e)
        {
            try
            {
                txtScript.Clear();

                if (cmbDatabases.SelectedItem == null ||
                    cmbTables.SelectedItem == null)
                {
                    MessageBox.Show("Wybierz bazę danych i tabelę.", "Informacja",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (string.IsNullOrEmpty(_baseConnectionString))
                {
                    MessageBox.Show("Najpierw połącz się z serwerem.", "Informacja",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string dbName = cmbDatabases.SelectedItem.ToString();
                string tableFull = cmbTables.SelectedItem.ToString(); // np. dbo.Customers

                var parts = tableFull.Split('.');
                if (parts.Length != 2)
                {
                    MessageBox.Show("Nieprawidłowa nazwa tabeli.", "Błąd",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string schema = parts[0];
                string table = parts[1];

                // warunek WHERE z pola tekstowego (opcjonalny)
                string whereClause = txtWhere.Text.Trim();
                bool useTemp = chkUseTempTable.Checked;

                string connStr = _baseConnectionString + $"Database={dbName};";

                var sb = new StringBuilder();
                lblStatus.Text = "Generowanie skryptu...";
                Cursor = Cursors.WaitCursor;

                using (var conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    if (conn.State != ConnectionState.Open)
                    {
                        MessageBox.Show("Nie udało się połączyć z bazą podczas generowania skryptu.", "Błąd",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblStatus.Text = "Brak połączenia z bazą.";
                        return;
                    }

                    // 1. CREATE TABLE (lub CREATE TABLE #temp)
                    string createScript = GenerateCreateTableScript(conn, schema, table, useTemp);
                    sb.AppendLine($"-- Skrypt dla tabeli {schema}.{table} w bazie {dbName}");
                    sb.AppendLine("SET ANSI_NULLS ON;");
                    sb.AppendLine("SET QUOTED_IDENTIFIER ON;");
                    sb.AppendLine();
                    sb.AppendLine(createScript);
                    sb.AppendLine("GO");
                    sb.AppendLine();

                    // 2. INSERT (SELECT * FROM tabela, opcjonalnie z WHERE)
                    if (chkIncludeData.Checked)
                    {
                        string insertScript = GenerateInsertForTable(conn, schema, table, whereClause, useTemp, out int rowCount);
                        sb.AppendLine(insertScript);
                        lblStatus.Text = $"Wygenerowano CREATE TABLE + {rowCount} wierszy INSERT.";
                    }
                    else
                    {
                        lblStatus.Text = "Wygenerowano tylko CREATE TABLE.";
                    }
                }

                txtScript.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd generowania skryptu: " + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Błąd generowania skryptu.";
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        // =====================================
        //  GENEROWANIE INSERT Z DOWOLNEGO SELECT
        // =====================================
        private void btnGenerateFromQuery_Click(object sender, EventArgs e)
        {
            try
            {
                txtScript.Clear();

                string fullSelect = txtFreeQuery.Text;
                if (string.IsNullOrWhiteSpace(fullSelect))
                {
                    MessageBox.Show("Wpisz pełne zapytanie SELECT.", "Informacja",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (cmbDatabases.SelectedItem == null)
                {
                    MessageBox.Show("Wybierz bazę danych, dla której ma zostać wykonany SELECT.",
                        "Informacja", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (string.IsNullOrEmpty(_baseConnectionString))
                {
                    MessageBox.Show("Najpierw połącz się z serwerem.", "Informacja",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string dbName = cmbDatabases.SelectedItem.ToString();
                string connStr = _baseConnectionString + $"Database={dbName};";

                var sb = new StringBuilder();
                lblStatus.Text = "Generowanie INSERT z SELECT...";
                Cursor = Cursors.WaitCursor;

                using (var conn = new SqlConnection(connStr))
                {
                    conn.Open();
                    string insertScript = GenerateInsertFromQuery(conn, fullSelect, out int rowCount);
                    sb.AppendLine(insertScript);
                    lblStatus.Text = $"INSERT z SELECT – wygenerowano {rowCount} wierszy.";
                }

                txtScript.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd INSERT z SELECT: " + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Błąd INSERT z SELECT.";
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private string GenerateInsertFromQuery(SqlConnection conn, string selectSql, out int rowCount)
        {
            var dt = new DataTable();
            using (var da = new SqlDataAdapter(selectSql, conn))
            {
                da.Fill(dt);
            }

            if (dt.Rows.Count == 0)
            {
                rowCount = 0;
                return "-- Zapytanie SELECT nie zwróciło żadnych wierszy.\n";
            }

            rowCount = dt.Rows.Count;

            string targetTable = GuessTargetTableName(selectSql);
            if (string.IsNullOrWhiteSpace(targetTable))
                targetTable = "[TWOJA_TABELA]";

            var sb = new StringBuilder();
            sb.AppendLine("-- INSERT wygenerowany z zapytania:");
            sb.AppendLine("-- " + selectSql.Replace(Environment.NewLine, " "));
            sb.AppendLine();

            string[] columnNames = new string[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
                columnNames[i] = "[" + dt.Columns[i].ColumnName + "]";
            string columnList = string.Join(", ", columnNames);

            for (int r = 0; r < dt.Rows.Count; r++)
            {
                var row = dt.Rows[r];
                string[] values = new string[dt.Columns.Count];

                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    object val = row[c];
                    Type type = dt.Columns[c].DataType;
                    values[c] = ToSqlLiteral(val, type);
                }

                string valuesList = string.Join(", ", values);
                sb.AppendLine($"INSERT INTO {targetTable} ({columnList}) VALUES ({valuesList});");
            }

            return sb.ToString();
        }

        private string GuessTargetTableName(string selectSql)
        {
            if (string.IsNullOrWhiteSpace(selectSql))
                return null;

            string upper = selectSql.ToUpperInvariant();
            int fromIdx = upper.IndexOf(" FROM ", StringComparison.Ordinal);
            if (fromIdx < 0)
                return null;

            int start = fromIdx + " FROM ".Length;
            int len = selectSql.Length;

            // pomijamy spacje, nowe linie etc.
            while (start < len && char.IsWhiteSpace(selectSql[start]))
                start++;

            if (start >= len)
                return null;

            int end = start;
            while (end < len && !char.IsWhiteSpace(selectSql[end]) && selectSql[end] != ';')
                end++;

            string token = selectSql.Substring(start, end - start).Trim();
            token = token.TrimEnd(',', ')');

            return token;
        }

        // =====================================
        //  GENEROWANIE DDL + INSERT DLA TABELI
        // =====================================
        private string GenerateCreateTableScript(SqlConnection conn, string schema, string table, bool useTemp)
        {
            var columns = new List<ColumnInfo>();

            using (var cmd = new SqlCommand(@"
                SELECT 
                    c.COLUMN_NAME,
                    c.DATA_TYPE,
                    c.IS_NULLABLE,
                    c.CHARACTER_MAXIMUM_LENGTH,
                    c.COLUMN_DEFAULT,
                    c.NUMERIC_PRECISION,
                    c.NUMERIC_SCALE
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @schema
                  AND c.TABLE_NAME = @table
                ORDER BY c.ORDINAL_POSITION;", conn))
            {
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@table", table);

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var col = new ColumnInfo
                        {
                            Name = r.GetString(0),
                            DataType = r.GetString(1),
                            IsNullable = r.GetString(2) == "YES",
                            MaxLength = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                            Default = r.IsDBNull(4) ? null : r.GetString(4),
                            NumericPrecision = r.IsDBNull(5) ? (byte?)null : r.GetByte(5),
                            NumericScale = r.IsDBNull(6) ? (int?)null : Convert.ToInt32(r.GetValue(6))
                        };
                        columns.Add(col);
                    }
                }
            }

            // PK
            var pkColumns = new List<string>();
            string pkName = null;

            using (var cmd = new SqlCommand(@"
                SELECT k.COLUMN_NAME, k.ORDINAL_POSITION, t.CONSTRAINT_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
                  ON t.CONSTRAINT_NAME = k.CONSTRAINT_NAME
                 AND t.TABLE_SCHEMA = k.TABLE_SCHEMA
                 AND t.TABLE_NAME = k.TABLE_NAME
                WHERE t.TABLE_SCHEMA = @schema
                  AND t.TABLE_NAME = @table
                  AND t.CONSTRAINT_TYPE = 'PRIMARY KEY'
                ORDER BY k.ORDINAL_POSITION;", conn))
            {
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@table", table);

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        pkColumns.Add(r.GetString(0));
                        if (pkName == null)
                            pkName = r.GetString(2);
                    }
                }
            }

            var sb = new StringBuilder();

            if (useTemp)
                sb.AppendLine($"CREATE TABLE #{table}");
            else
                sb.AppendLine($"CREATE TABLE [{schema}].[{table}]");

            sb.AppendLine("(");

            var lines = new List<string>();

            foreach (var col in columns)
            {
                string typeDefinition = BuildSqlType(col);
                string nullStr = col.IsNullable ? "NULL" : "NOT NULL";

                string defaultStr = "";
                if (!string.IsNullOrEmpty(col.Default))
                {
                    defaultStr = " DEFAULT " + col.Default;
                }

                string line = $"    [{col.Name}] {typeDefinition} {nullStr}{defaultStr}";
                lines.Add(line);
            }

            if (pkColumns.Count > 0)
            {
                string pkCols = string.Join(", ", pkColumns.ConvertAll(c => $"[{c}]"));
                if (string.IsNullOrEmpty(pkName))
                    pkName = $"PK_{table}";

                string pkLine = $"    CONSTRAINT [{pkName}] PRIMARY KEY CLUSTERED ({pkCols})";
                lines.Add(pkLine);
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.Append(")");

            return sb.ToString();
        }

        private string BuildSqlType(ColumnInfo col)
        {
            string t = col.DataType.ToLowerInvariant();

            switch (t)
            {
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "binary":
                case "varbinary":
                    if (col.MaxLength == null || col.MaxLength <= 0)
                        return t + "(max)";

                    return t + "(" + (col.MaxLength == -1 ? "max" : col.MaxLength.ToString()) + ")";

                case "decimal":
                case "numeric":
                    if (col.NumericPrecision != null && col.NumericScale != null)
                        return $"{t}({col.NumericPrecision},{col.NumericScale})";
                    return t;

                default:
                    return t;
            }
        }

        private string GenerateInsertForTable(SqlConnection conn, string schema, string table, string whereClause, bool useTemp, out int rowCount)
        {
            // Budujemy SELECT z opcjonalnym WHERE – zawsze z tabeli w bazie
            string sql = $"SELECT * FROM [{schema}].[{table}]";
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sql += $" WHERE {whereClause}";
            }

            var dt = new DataTable();
            using (var da = new SqlDataAdapter(sql, conn))
            {
                da.Fill(dt);
            }

            if (dt.Rows.Count == 0)
            {
                rowCount = 0;
                if (!string.IsNullOrWhiteSpace(whereClause))
                    return $"-- Tabela [{schema}].[{table}] – brak danych dla WHERE {whereClause}.\n";
                return $"-- Tabela [{schema}].[{table}] jest pusta.\n";
            }

            rowCount = dt.Rows.Count;

            var sb = new StringBuilder();

            string[] columnNames = new string[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
                columnNames[i] = "[" + dt.Columns[i].ColumnName + "]";

            string columnList = string.Join(", ", columnNames);

            string targetName = useTemp ? "#" + table : $"[{schema}].[{table}]";

            if (!string.IsNullOrWhiteSpace(whereClause))
                sb.AppendLine($"-- Dane tabeli [{schema}].[{table}] (WHERE {whereClause})");
            else
                sb.AppendLine($"-- Dane tabeli [{schema}].[{table}]");

            for (int r = 0; r < dt.Rows.Count; r++)
            {
                var row = dt.Rows[r];
                string[] values = new string[dt.Columns.Count];

                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    object val = row[c];
                    Type type = dt.Columns[c].DataType;
                    values[c] = ToSqlLiteral(val, type);
                }

                string valuesList = string.Join(", ", values);
                sb.AppendLine($"INSERT INTO {targetName} ({columnList}) VALUES ({valuesList});");
            }

            return sb.ToString();
        }

        private string ToSqlLiteral(object value, Type type)
        {
            if (value == null || value == DBNull.Value)
                return "NULL";

            if (type == typeof(string) || type == typeof(char))
            {
                string s = value.ToString().Replace("'", "''");
                return $"N'{s}'";
            }

            if (type == typeof(DateTime))
            {
                var dt = (DateTime)value;
                return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
            }

            if (type == typeof(bool))
            {
                return ((bool)value) ? "1" : "0";
            }

            if (type == typeof(byte[]))
            {
                var bytes = (byte[])value;
                if (bytes.Length == 0) return "0x";
                var sb = new StringBuilder("0x");
                foreach (var b in bytes)
                    sb.Append(b.ToString("X2"));
                return sb.ToString();
            }

            if (type.IsPrimitive || type == typeof(decimal))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            string fallback = value.ToString().Replace("'", "''");
            return $"N'{fallback}'";
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtScript.Text))
            {
                Clipboard.SetText(txtScript.Text);
                lblStatus.Text = "Skrypt skopiowany do schowka.";
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtScript.Text))
            {
                MessageBox.Show("Brak skryptu do zapisania.", "Informacja",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Pliki SQL (*.sql)|*.sql|Wszystkie pliki (*.*)|*.*";
                sfd.FileName = $"{cmbDatabases.SelectedItem}_{cmbTables.SelectedItem}.sql";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(sfd.FileName, txtScript.Text, Encoding.UTF8);
                    lblStatus.Text = $"Zapisano do {sfd.FileName}.";
                }
            }
        }

        private class ColumnInfo
        {
            public string Name { get; set; }
            public string DataType { get; set; }
            public bool IsNullable { get; set; }
            public int? MaxLength { get; set; }
            public string Default { get; set; }
            public byte? NumericPrecision { get; set; }
            public int? NumericScale { get; set; }
        }

        private class AppConfig
        {
            public string Server { get; set; }
            public bool UseWindowsAuth { get; set; } = true;
            public string User { get; set; }
            public string Password { get; set; }
        }
    }
}
