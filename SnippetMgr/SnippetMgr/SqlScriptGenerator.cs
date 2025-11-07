using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace SnippetMgr
{
    /// <summary>
    /// Klasa odpowiedzialna za generowanie skryptów T-SQL:
    /// - CREATE TABLE na podstawie INFORMATION_SCHEMA
    /// - INSERT na podstawie SELECT * FROM ...
    /// Dzięki temu logika jest odseparowana od formularza.
    /// </summary>
    public class SqlScriptGenerator
    {
        #region Struktura opisująca kolumnę

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

        #endregion

        #region CREATE TABLE

        /// <summary>
        /// Generuje skrypt CREATE TABLE dla wskazanej tabeli.
        /// </summary>
        public string GenerateCreateTableScript(SqlConnection conn, string schema, string table)
        {
            // pobranie metadanych kolumn
            var columns = GetColumns(conn, schema, table);

            // pobranie definicji klucza głównego
            var pkColumns = new List<string>();
            string pkName = null;
            GetPrimaryKeyInfo(conn, schema, table, pkColumns, ref pkName);

            // składanie CREATE TABLE
            var sb = new StringBuilder();
            sb.AppendLine($"CREATE TABLE [{schema}].[{table}]");
            sb.AppendLine("(");

            var lines = new List<string>();

            foreach (var col in columns)
            {
                string typeDef = BuildSqlType(col);
                string nullStr = col.IsNullable ? "NULL" : "NOT NULL";

                string defaultStr = "";
                if (!string.IsNullOrEmpty(col.Default))
                {
                    defaultStr = " DEFAULT " + col.Default;
                }

                string line = $"    [{col.Name}] {typeDef} {nullStr}{defaultStr}";
                lines.Add(line);
            }

            // PRIMARY KEY
            if (pkColumns.Count > 0)
            {
                if (string.IsNullOrEmpty(pkName))
                    pkName = $"PK_{table}";

                string pkCols = string.Join(", ", pkColumns.ConvertAll(c => $"[{c}]"));
                string pkLine = $"    CONSTRAINT [{pkName}] PRIMARY KEY CLUSTERED ({pkCols})";
                lines.Add(pkLine);
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.Append(")");

            return sb.ToString();
        }

        private List<ColumnInfo> GetColumns(SqlConnection conn, string schema, string table)
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

            return columns;
        }

        private void GetPrimaryKeyInfo(SqlConnection conn, string schema, string table,
                                       List<string> pkColumns, ref string pkName)
        {
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
        }

        /// <summary>
        /// Buduje definicję typu danych (np. varchar(50), decimal(18,2), nvarchar(max)).
        /// </summary>
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

        #endregion

        #region INSERT

        /// <summary>
        /// Generuje INSERT INTO ... SELECT ... dla całej tabeli
        /// (z opcjonalnym WHERE).
        /// </summary>
        public string GenerateInsertForTable(SqlConnection conn,
                                             string schema,
                                             string table,
                                             string whereClause,
                                             out int rowCount)
        {
            // prosty SELECT * FROM [schema].[table] [WHERE ...]
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

            // lista kolumn: [Col1], [Col2], ...
            string[] columnNames = new string[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
                columnNames[i] = "[" + dt.Columns[i].ColumnName + "]";
            string columnList = string.Join(", ", columnNames);

            if (!string.IsNullOrWhiteSpace(whereClause))
                sb.AppendLine($"-- Dane tabeli [{schema}].[{table}] (WHERE {whereClause})");
            else
                sb.AppendLine($"-- Dane tabeli [{schema}].[{table}]");

            // każdy wiersz -> INSERT INTO ...
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
                sb.AppendLine($"INSERT INTO [{schema}].[{table}] ({columnList}) VALUES ({valuesList});");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Konwertuje wartość .NET na literał T-SQL (NULL, N'...', liczby, daty, binaria).
        /// </summary>
        private string ToSqlLiteral(object value, Type type)
        {
            if (value == null || value == DBNull.Value)
                return "NULL";

            if (type == typeof(string) || type == typeof(char))
            {
                string s = value.ToString().Replace("'", "''");
                return "N'" + s + "'";
            }

            if (type == typeof(DateTime))
            {
                var dt = (DateTime)value;
                return "'" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'";
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
                foreach (byte b in bytes)
                    sb.Append(b.ToString("X2"));
                return sb.ToString();
            }

            if (type.IsPrimitive || type == typeof(decimal))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            // fallback: traktujemy jako string NVARCHAR
            string fallback = value.ToString().Replace("'", "''");
            return "N'" + fallback + "'";
        }

        #endregion
    }
}
