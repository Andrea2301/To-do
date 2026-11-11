using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DoItNow.Services.Conversion
{
    /// <summary>Tabla simple (cabeceras + filas) usada para CSV, Excel y JSON.</summary>
    public sealed class TableData
    {
        public List<string> Headers { get; } = new();
        public List<string[]> Rows { get; } = new();
        public bool IsEmpty => Rows.Count == 0 && Headers.Count == 0;
    }

    /// <summary>
    /// Lectura y escritura de datos tabulares: CSV/TSV, JSON y tablas Markdown.
    /// Implementación propia para no depender de la red ni de licencias externas.
    /// </summary>
    public static class Tabular
    {
        #region CSV / TSV

        private static readonly char[] CandidateDelimiters = { ',', ';', '\t', '|' };

        /// <summary>Lee un CSV/TSV respetando comillas, comillas escapadas y saltos de línea.</summary>
        public static TableData ParseDelimited(string content, char? forcedDelimiter = null)
        {
            var table = new TableData();
            if (string.IsNullOrEmpty(content)) return table;

            var delimiter = forcedDelimiter ?? DetectDelimiter(content);
            var rows = ParseRows(content, delimiter);

            if (rows.Count == 0) return table;

            table.Headers.AddRange(rows[0]);
            for (var i = 1; i < rows.Count; i++)
                table.Rows.Add(rows[i].ToArray());

            return table;
        }

        private static List<List<string>> ParseRows(string content, char delimiter)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < content.Length; i++)
            {
                var c = content[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;

                    row.Add(field.ToString());
                    field.Clear();

                    if (row.Count > 1 || row[0].Length > 0)
                        rows.Add(row);

                    row = new List<string>();
                }
                else
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                if (row.Count > 1 || row[0].Length > 0)
                    rows.Add(row);
            }

            return rows;
        }

        /// <summary>Cuenta separadores fuera de comillas en la primera línea útil.</summary>
        public static char DetectDelimiter(string content)
        {
            var counts = new Dictionary<char, int>();
            foreach (var candidate in CandidateDelimiters) counts[candidate] = 0;

            var inQuotes = false;
            foreach (var c in content)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (!inQuotes && (c == '\r' || c == '\n')) break;
                if (!inQuotes && counts.ContainsKey(c)) counts[c]++;
            }

            var best = CandidateDelimiters
                .OrderByDescending(c => counts[c])
                .First();

            return counts[best] > 0 ? best : ',';
        }

        /// <summary>Serializa una tabla a CSV con comillas cuando hace falta.</summary>
        public static string ToCsv(TableData table)
        {
            var sb = new StringBuilder();

            if (table.Headers.Count > 0)
                sb.AppendLine(JoinCsvLine(table.Headers));

            foreach (var row in table.Rows)
                sb.AppendLine(JoinCsvLine(row));

            return sb.ToString();
        }

        private static string JoinCsvLine(IEnumerable<string> values)
            => string.Join(",", values.Select(EscapeCsv));

        private static string EscapeCsv(string? value)
        {
            value ??= string.Empty;
            var needsQuotes = value.Contains(',') || value.Contains('"') ||
                              value.Contains('\n') || value.Contains('\r');
            return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }

        #endregion

        #region JSON

        /// <summary>Convierte un CSV/Excel a un array JSON de objetos.</summary>
        public static string ToJson(TableData table)
        {
            var headers = BuildUniqueHeaders(table);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                foreach (var row in table.Rows)
                {
                    writer.WriteStartObject();
                    for (var i = 0; i < headers.Count; i++)
                    {
                        writer.WriteString(headers[i], i < row.Length ? row[i] : string.Empty);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>Lee JSON y lo aplana en columnas (objetos anidados con notación de punto).</summary>
        public static TableData FromJson(string json)
        {
            var table = new TableData();

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = document.RootElement;

            // Patrón habitual: { "data": [ {...}, {...} ] }
            if (root.ValueKind == JsonValueKind.Object)
            {
                var properties = root.EnumerateObject().ToList();
                if (properties.Count == 1 && properties[0].Value.ValueKind == JsonValueKind.Array)
                    root = properties[0].Value;
            }

            var flattenedRows = new List<Dictionary<string, string>>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var row = new Dictionary<string, string>();
                    Flatten(item, string.Empty, row);
                    flattenedRows.Add(row);
                }
            }
            else
            {
                var row = new Dictionary<string, string>();
                Flatten(root, string.Empty, row);
                flattenedRows.Add(row);
            }

            // Unión de claves conservando el orden de aparición.
            var headers = new List<string>();
            foreach (var row in flattenedRows)
                foreach (var key in row.Keys)
                    if (!headers.Contains(key)) headers.Add(key);

            table.Headers.AddRange(headers);
            foreach (var row in flattenedRows)
                table.Rows.Add(headers.Select(h => row.TryGetValue(h, out var v) ? v : string.Empty).ToArray());

            return table;
        }

        private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> row)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                        Flatten(property.Value, key, row);
                    }
                    break;

                case JsonValueKind.Array:
                    row[prefix] = element.GetRawText();
                    break;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    row[prefix] = string.Empty;
                    break;

                default:
                    row[prefix] = element.ToString();
                    break;
            }
        }

        private static List<string> BuildUniqueHeaders(TableData table)
        {
            var headers = new List<string>();
            for (var i = 0; i < table.Headers.Count; i++)
            {
                var header = table.Headers[i];
                if (string.IsNullOrWhiteSpace(header)) header = $"col_{i + 1}";

                var unique = header;
                var suffix = 2;
                while (headers.Contains(unique)) unique = $"{header}_{suffix++}";

                headers.Add(unique);
            }
            return headers;
        }

        #endregion

        #region Markdown

        public static string ToMarkdownTable(TableData table)
        {
            if (table.Headers.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.Append("| ").Append(string.Join(" | ", table.Headers.Select(Cell))).AppendLine(" |");
            sb.Append('|').Append(string.Join("|", table.Headers.Select(_ => " --- "))).AppendLine("|");

            foreach (var row in table.Rows)
            {
                var cells = table.Headers.Select((_, i) => Cell(i < row.Length ? row[i] : string.Empty));
                sb.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");
            }

            return sb.ToString();

            static string Cell(string? value)
                => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        #endregion
    }
}
