using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnippetMgr.Models
{
    // --- MODELE DANYCH ---

    public class RootConfig
    {
        [JsonPropertyName("configName")] public string? ConfigName { get; set; } = "Default";
        [JsonPropertyName("config")] public ConfigBlock Config { get; set; } = new();
        [JsonPropertyName("sqlSnippets")] public List<EntryConfig> SqlSnippets { get; set; } = new();
        [JsonPropertyName("apps")] public List<EntryConfig> Apps { get; set; } = new();
        [JsonPropertyName("folders")] public List<EntryConfig> Folders { get; set; } = new();
        [JsonPropertyName("notes")] public List<EntryConfig> Notes { get; set; } = new();
        [JsonPropertyName("tabs")] public List<TabConfig> Tabs { get; set; } = new();
        [JsonPropertyName("queryHistory")] public List<string> QueryHistory { get; set; } = new();
    }

    public class ConfigBlock
    {
        [JsonPropertyName("sql")] public SqlConfig Sql { get; set; } = new();
        [JsonPropertyName("ui")] public UiConfig Ui { get; set; } = new();
        [JsonPropertyName("behavior")] public BehaviorConfig Behavior { get; set; } = new();
    }

    public class SqlConfig
    {
        [JsonPropertyName("server")] public string? Server { get; set; }
        [JsonPropertyName("useWindowsAuth")] public bool UseWindowsAuth { get; set; } = true;
        [JsonPropertyName("user")] public string? User { get; set; }
        [JsonPropertyName("password")] public string? Password { get; set; }
        [JsonPropertyName("useTempTables")] public bool UseTempTables { get; set; }
        [JsonPropertyName("defaultDatabase")] public string? DefaultDatabase { get; set; }
    }

    public class UiConfig
    {
        [JsonPropertyName("startMinimized")] public bool StartMinimized { get; set; }
        [JsonPropertyName("alwaysOnTop")] public bool AlwaysOnTop { get; set; }
        [JsonPropertyName("hotkey")] public string? Hotkey { get; set; }
        [JsonPropertyName("darkMode")] public bool DarkMode { get; set; }
        [JsonPropertyName("runAtStartup")] public bool RunAtStartup { get; set; }
    }

    public class BehaviorConfig
    {
        [JsonPropertyName("autoPasteOnSnippetSelect")] public bool AutoPasteOnSnippetSelect { get; set; }
        [JsonPropertyName("rememberLastTab")] public bool RememberLastTab { get; set; }
        [JsonPropertyName("clearSearchOnTabChange")] public bool ClearSearchOnTabChange { get; set; }
        [JsonPropertyName("focusSearchOnTabChange")] public bool FocusSearchOnTabChange { get; set; }
    }

    public class TabConfig
    {
        [JsonPropertyName("name")] public string? Name { get; set; }

        // Stare JSON-y mogły mieć "tab_name" – mapujemy to na Name
        [JsonPropertyName("tab_name")]
        public string? TabNameLegacy
        {
            get => Name;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                    Name = value;
            }
        }

        [JsonPropertyName("entries")] public List<EntryConfig> Entries { get; set; } = new();
    }

    public class EntryConfig
    {
        [JsonPropertyName("shortcut")] public string? Shortcut { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("paste_text")] public string? PasteText { get; set; }

        [JsonPropertyName("app_name")] public string? AppName { get; set; }
        [JsonPropertyName("app_path")] public string? AppPath { get; set; }
        [JsonPropertyName("app_args")] public string? AppArgs { get; set; }
        [JsonPropertyName("working_dir")] public string? WorkingDir { get; set; }
        [JsonPropertyName("run_as_admin")] public bool? RunAsAdmin { get; set; }

        [JsonPropertyName("folder_path")] public string? FolderPath { get; set; }

        [JsonPropertyName("note_text")] public string? NoteText { get; set; }

        [JsonPropertyName("pinned")] public bool? Pinned { get; set; }
        [JsonPropertyName("color")] public string? Color { get; set; }
        [JsonPropertyName("tags")] public object? TagsObject { get; set; }

        public string GetTagsString()
        {
            if (TagsObject == null) return string.Empty;

            try
            {
                if (TagsObject is JsonElement el && el.ValueKind == JsonValueKind.Array)
                    return string.Join(", ", el.EnumerateArray().Select(x => x.ToString()));

                if (TagsObject is string[] arr) return string.Join(", ", arr);

                return TagsObject.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public string GetContentText() =>
            NoteText ?? PasteText ?? AppPath ?? FolderPath ?? string.Empty;

        public string GetDisplayName() =>
            Title ?? AppName ?? Shortcut ?? Description ?? "Bez nazwy";
    }

    public class QueryResult
    {
        /// <summary>Główny wynik – dla zgodności wstecznej.</summary>
        public DataTable? Data { get; set; }

        /// <summary>Wszystkie wyniki (SELECT 1, SELECT 2, ...).</summary>
        public List<DataTable> AllData { get; set; } = new();

        public List<string> Messages { get; set; } = new();
        public string? FixedWidthPreview { get; set; }
    }

    // --- INTERFEJSY ---

    public interface IConfigService
    {
        RootConfig LoadConfig();
        void SaveConfig(RootConfig config);
        string GetConfigPath();
    }

    public interface ISqlService
    {
        void Configure(string? server, bool winAuth, string? user, string? pass);

        Task<List<string>> GetDatabasesAsync();
        Task<List<string>> GetTablesAsync(string db);
        Task<List<string>> GetColumnsAsync(string dbName, string schema, string table);

        Task<QueryResult> ExecuteQueryAsync(string db, string query);

        Task<string> ExportFixedLineAsync(string db, string query);
        Task<int> ExportFixedLineToFileAsync(string db, string query, string filePath);

        Task<string> GenerateCreateTableAsync(string db, string schema, string table, bool useTemp);
        Task<string> GenerateInsertAsync(string db, string schema, string table, string where, bool useTemp);
        Task<string> GenerateScriptFromQueryAsync(string db, string query, bool useTemp, bool includeData);
    }

    public interface ICommandService
    {
        void ExecuteEntry(EntryConfig entry);
        void SaveLastWindowHandle();
        void SetStartup(bool enable);
    }
}
