using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SnippetMgr.Models;

namespace SnippetMgr.Services
{
    // =========================
    //  JSON CONFIG SERVICE
    // =========================
    public class JsonConfigService : IConfigService
    {
        private readonly string _path;
        private readonly string _backupDir;
        private readonly ILogger<JsonConfigService> _logger;

        public JsonConfigService(ILogger<JsonConfigService> logger)
        {
            _logger = logger;
            _path = Path.Combine(AppContext.BaseDirectory, "templates.json");
            _backupDir = Path.Combine(AppContext.BaseDirectory, "Backups");
        }

        public string GetConfigPath() => _path;

        public RootConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(_path))
                    return new RootConfig();

                var json = File.ReadAllText(_path, Encoding.UTF8);

                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    PropertyNameCaseInsensitive = true
                };

                var cfg = JsonSerializer.Deserialize<RootConfig>(json, options);
                return cfg ?? new RootConfig();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wczytywania konfiguracji z {Path}", _path);
                return new RootConfig();
            }
        }

        public void SaveConfig(RootConfig config)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                // Backup starej wersji
                if (File.Exists(_path))
                {
                    Directory.CreateDirectory(_backupDir);
                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var backupFile = Path.Combine(_backupDir, $"templates_{stamp}.json");
                    File.Copy(_path, backupFile, overwrite: false);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(_path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas zapisu konfiguracji do {Path}", _path);
                MessageBox.Show("Nie udało się zapisać konfiguracji: " + ex.Message,
                    "Błąd zapisu configu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    // =========================
    //  SQL SERVICE
    // =========================
    public class SqlService : ISqlService
    {
        private string? _baseConnectionString;

        public void Configure(string? server, bool winAuth, string? user, string? pass)
        {
            if (string.IsNullOrWhiteSpace(server))
                throw new ArgumentException("Nie podano serwera SQL.", nameof(server));

            var csb = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = "master",
                TrustServerCertificate = true,
                ConnectTimeout = 15,
                MultipleActiveResultSets = true
            };

            if (winAuth)
            {
                csb.IntegratedSecurity = true;
                if (csb.ContainsKey("User ID")) csb.Remove("User ID");
                if (csb.ContainsKey("Password")) csb.Remove("Password");
            }
            else
            {
                csb.IntegratedSecurity = false;

                if (string.IsNullOrWhiteSpace(user))
                    throw new InvalidOperationException("Dla logowania SQL należy podać login (user).");

                csb.UserID = user;
                csb.Password = pass ?? string.Empty;
            }

            _baseConnectionString = csb.ConnectionString;
        }

        private SqlConnection GetConn(string db = "master")
        {
            if (string.IsNullOrEmpty(_baseConnectionString))
                throw new InvalidOperationException("SqlService nie został skonfigurowany. Wywołaj Configure().");

            var b = new SqlConnectionStringBuilder(_baseConnectionString)
            {
                InitialCatalog = db
            };
            return new SqlConnection(b.ConnectionString);
        }

        // --- BAZY / TABELE / KOLUMNY ---

        public async Task<List<string>> GetDatabasesAsync()
        {
            var list = new List<string>();

            using var conn = GetConn("master");
            await conn.OpenAsync();

            const string sql = "SELECT name FROM sys.databases ORDER BY name;";

            using var cmd = new SqlCommand(sql, conn);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(r.GetString(0));

            return list;
        }

        public async Task<List<string>> GetTablesAsync(string db)
        {
            var list = new List<string>();

            using var conn = GetConn(db);
            await conn.OpenAsync();

            const string sql = @"
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;";

            using var cmd = new SqlCommand(sql, conn);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var schema = r.GetString(0);
                var table = r.GetString(1);
                list.Add($"{schema}.{table}");
            }

            return list;
        }

        public async Task<List<string>> GetColumnsAsync(string dbName, string schema, string table)
        {
            var list = new List<string>();

            using var conn = GetConn(dbName);
            await conn.OpenAsync();

            const string sql = @"
SELECT c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
       c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
ORDER BY c.ORDINAL_POSITION;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var name = r.GetString(0);
                var type = r.GetString(1);
                var nullable = r.GetString(3).Equals("YES", StringComparison.OrdinalIgnoreCase);

                var sb = new StringBuilder();
                sb.Append(name).Append(" ").Append(type);

                if (!r.IsDBNull(2) && r.GetValue(2) is int len && len > 0)
                {
                    if (len == -1) sb.Append("(MAX)");
                    else sb.Append("(").Append(len).Append(")");
                }

                sb.Append(nullable ? " NULL" : " NOT NULL");
                list.Add(sb.ToString());
            }

            return list;
        }

        // --- WYKONYWANIE ZAPYTAŃ ---

        public async Task<QueryResult> ExecuteQueryAsync(string db, string query)
        {
            var result = new QueryResult();
            if (string.IsNullOrWhiteSpace(query))
                return result;

            using var conn = GetConn(db);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(query, conn);

            try
            {
                using var reader = await cmd.ExecuteReaderAsync();
                var dt = new DataTable();
                dt.Load(reader);
                result.Data = dt;

                if (dt.Rows.Count > 0)
                    result.FixedWidthPreview = GenerateFixedWidthString(dt);
            }
            catch (SqlException ex)
            {
                result.Messages.Add(ex.Message);
                // nie rzucamy dalej – pozwalamy UI to pokazać
            }

            return result;
        }

        // --- EKSPORT LINIOWY ---

        public async Task<string> ExportFixedLineAsync(string db, string query)
        {
            var res = await ExecuteQueryAsync(db, query);
            return res.FixedWidthPreview ?? string.Empty;
        }

        public async Task<int> ExportFixedLineToFileAsync(string db, string query, string filePath)
        {
            var res = await ExecuteQueryAsync(db, query);
            if (res.Data == null)
            {
                await File.WriteAllTextAsync(filePath, string.Empty, Encoding.UTF8);
                return 0;
            }

            var dt = res.Data;
            int count = 0;
            var widths = GetColumnWidths(dt);

            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            foreach (DataRow row in dt.Rows)
            {
                var line = BuildFixedWidthLine(row, widths);
                await sw.WriteLineAsync(line);
                count++;
            }

            return count;
        }

        // --- GENEROWANIE CREATE / INSERT / SCRIPT ---

        public async Task<string> GenerateCreateTableAsync(string db, string schema, string table, bool useTemp)
        {
            using var conn = GetConn(db);
            await conn.OpenAsync();

            const string sql = @"
SELECT c.name          AS ColumnName,
       t.name          AS TypeName,
       c.max_length,
       c.precision,
       c.scale,
       c.is_nullable,
       c.is_identity
FROM sys.columns c
JOIN sys.types t       ON c.user_type_id = t.user_type_id
JOIN sys.tables tb     ON c.object_id   = tb.object_id
JOIN sys.schemas s     ON tb.schema_id  = s.schema_id
WHERE s.name = @schema AND tb.name = @table
ORDER BY c.column_id;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var sb = new StringBuilder();
            string targetName = useTemp ? $"#{table}" : $"[{schema}].[{table}]";

            sb.AppendLine($"CREATE TABLE {targetName}");
            sb.AppendLine("(");

            using var reader = await cmd.ExecuteReaderAsync();
            bool first = true;
            while (await reader.ReadAsync())
            {
                if (!first) sb.AppendLine(",");
                first = false;

                var colName = reader.GetString(0);
                var typeName = reader.GetString(1);
                var maxLen = reader.GetInt16(2);
                var precision = reader.GetByte(3);
                var scale = reader.GetByte(4);
                var isNullable = reader.GetBoolean(5);
                var isIdentity = reader.GetBoolean(6);

                sb.Append("    [").Append(colName).Append("] ");
                sb.Append(BuildSqlType(typeName, maxLen, precision, scale));
                sb.Append(isNullable ? " NULL" : " NOT NULL");

                if (isIdentity)
                    sb.Append(" IDENTITY(1,1)");
            }

            sb.AppendLine();
            sb.AppendLine(");");

            return sb.ToString();
        }

        public async Task<string> GenerateInsertAsync(string db, string schema, string table, string where, bool useTemp)
        {
            using var conn = GetConn(db);
            await conn.OpenAsync();

            const string sql = @"
SELECT c.name          AS ColumnName
FROM sys.columns c
JOIN sys.tables tb     ON c.object_id   = tb.object_id
JOIN sys.schemas s     ON tb.schema_id  = s.schema_id
WHERE s.name = @schema AND tb.name = @table
ORDER BY c.column_id;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var cols = new List<string>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    cols.Add(reader.GetString(0));
            }

            if (cols.Count == 0)
                return string.Empty;

            string sourceName = $"[{schema}].[{table}]";
            string targetName = useTemp ? $"#{table}" : sourceName;

            var sb = new StringBuilder();
            sb.Append("INSERT INTO ").Append(targetName).AppendLine();
            sb.Append("    (").Append(string.Join(", ", cols.Select(c => "[" + c + "]"))).AppendLine(")");
            sb.Append("SELECT").AppendLine();
            sb.Append("    ").Append(string.Join(", ", cols.Select(c => "[" + c + "]"))).AppendLine();
            sb.Append("FROM ").Append(sourceName);

            where = (where ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(where))
            {
                if (where.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine().Append(where);
                else
                    sb.AppendLine().Append("WHERE ").Append(where);
            }
            sb.AppendLine(";");

            return sb.ToString();
        }

        public Task<string> GenerateScriptFromQueryAsync(string db, string query, bool useTemp, bool includeData)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Task.FromResult(string.Empty);

            query = query.Trim();

            if (!useTemp)
            {
                // Bez tymczasowych tabel – zwracamy zapytanie 1:1
                return Task.FromResult(query);
            }

            // Dla trybu temp: SELECT * INTO #TmpResult FROM (query) AS src
            var sb = new StringBuilder();
            sb.AppendLine("SELECT *");
            sb.AppendLine("INTO #TmpResult");
            sb.AppendLine("FROM (");
            sb.AppendLine(query);
            sb.AppendLine(") AS src;");

            return Task.FromResult(sb.ToString());
        }

        // --- Helpery fixed-width ---

        private static string GenerateFixedWidthString(DataTable table)
        {
            var widths = GetColumnWidths(table);
            var sb = new StringBuilder();

            // nagłówki
            var headerCells = new List<string>();
            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                headerCells.Add(col.ColumnName.PadRight(widths[i]));
            }
            sb.AppendLine(string.Join(" ", headerCells));

            // separator
            sb.AppendLine(new string('-', widths.Sum() + table.Columns.Count - 1));

            // wiersze
            foreach (DataRow row in table.Rows)
            {
                sb.AppendLine(BuildFixedWidthLine(row, widths));
            }

            return sb.ToString();
        }

        private static int[] GetColumnWidths(DataTable table)
        {
            var widths = new int[table.Columns.Count];

            for (int i = 0; i < table.Columns.Count; i++)
            {
                widths[i] = table.Columns[i].ColumnName.Length;
            }

            foreach (DataRow row in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var text = row[i]?.ToString() ?? string.Empty;
                    if (text.Length > widths[i])
                        widths[i] = text.Length;
                }
            }

            for (int i = 0; i < widths.Length; i++)
                widths[i] += 1;

            return widths;
        }

        private static string BuildFixedWidthLine(DataRow row, int[] widths)
        {
            var cells = new List<string>();

            for (int i = 0; i < row.Table.Columns.Count; i++)
            {
                var text = row[i]?.ToString() ?? string.Empty;
                if (text.Length > widths[i])
                    text = text.Substring(0, widths[i]);

                cells.Add(text.PadRight(widths[i]));
            }

            return string.Join(" ", cells);
        }

        private static string BuildSqlType(string typeName, short maxLen, byte precision, byte scale)
        {
            typeName = typeName.ToLowerInvariant();

            switch (typeName)
            {
                case "char":
                case "nchar":
                case "varchar":
                case "nvarchar":
                case "binary":
                case "varbinary":
                    if (maxLen == -1)
                        return typeName.ToUpperInvariant() + "(MAX)";

                    int len = maxLen;
                    if (typeName == "nchar" || typeName == "nvarchar")
                        len = maxLen / 2;

                    return $"{typeName.ToUpperInvariant()}({len})";

                case "decimal":
                case "numeric":
                    return $"{typeName.ToUpperInvariant()}({precision},{scale})";

                default:
                    return typeName.ToUpperInvariant();
            }
        }
    }

    // =========================
    //  COMMAND SERVICE
    // =========================
    public class CommandService : ICommandService
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly ILogger<CommandService> _logger;
        private IntPtr _lastWindowHandle;

        public CommandService(ILogger<CommandService> logger)
        {
            _logger = logger;
        }

        public void SaveLastWindowHandle()
        {
            try
            {
                _lastWindowHandle = GetForegroundWindow();
            }
            catch
            {
                _lastWindowHandle = IntPtr.Zero;
            }
        }

        public void ExecuteEntry(EntryConfig entry)
        {
            try
            {
                // 1) Folder
                if (!string.IsNullOrEmpty(entry.FolderPath))
                {
                    RunProcess(entry.FolderPath, null, null, false);
                    return;
                }

                // 2) Aplikacja
                if (!string.IsNullOrEmpty(entry.AppPath))
                {
                    var workDir =
                        !string.IsNullOrEmpty(entry.WorkingDir)
                            ? entry.WorkingDir
                            : Path.GetDirectoryName(entry.AppPath) ?? "";

                    RunProcess(entry.AppPath, entry.AppArgs, workDir, entry.RunAsAdmin == true);
                    return;
                }

                // 3) Tekst do wklejenia
                if (!string.IsNullOrEmpty(entry.PasteText))
                {
                    if (_lastWindowHandle != IntPtr.Zero)
                        SetForegroundWindow(_lastWindowHandle);

                    Clipboard.SetText(entry.PasteText);
                    SendKeys.SendWait("^v");
                    return;
                }

                // 4) Notatka – skopiuj do schowka
                if (!string.IsNullOrEmpty(entry.NoteText))
                {
                    Clipboard.SetText(entry.NoteText);
                    MessageBox.Show("Notatka skopiowana do schowka.",
                        "Notatka",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wykonywania wpisu");
                MessageBox.Show("Błąd uruchamiania wpisu: " + ex.Message,
                    "Błąd",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void RunProcess(string path, string? args, string? workDir, bool admin)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args ?? "",
                    WorkingDirectory = !string.IsNullOrWhiteSpace(workDir)
                        ? workDir
                        : Environment.CurrentDirectory,
                    UseShellExecute = true,
                    Verb = admin ? "runas" : ""
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd uruchamiania procesu {Path}", path);
                MessageBox.Show("Nie można uruchomić: " + path + Environment.NewLine + ex.Message,
                    "Błąd uruchamiania",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public void SetStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    writable: true);

                if (key == null)
                    return;

                var exePath = Application.ExecutablePath;

                if (enable)
                {
                    key.SetValue("SnippetMgr", exePath);
                }
                else
                {
                    key.DeleteValue("SnippetMgr", throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd ustawiania autostartu");
            }
        }
    }
}
