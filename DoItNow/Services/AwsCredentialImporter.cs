using System;
using System.Collections.Generic;
using System.Linq;

namespace DoItNow.Services
{
    /// <summary>
    /// Lee archivos de credenciales de AWS en los dos formatos habituales:
    ///  • CSV descargado desde IAM ("Access key ID,Secret access key,...").
    ///  • INI de <c>~/.aws/credentials</c> o <c>~/.aws/config</c>.
    /// No accede a la red ni a AWS: solo interpreta el texto.
    /// </summary>
    public static class AwsCredentialImporter
    {
        public sealed record AwsCredential(
            string Profile,
            string AccessKeyId,
            string SecretAccessKey,
            string SessionToken,
            string Region);

        public static List<AwsCredential> Parse(string content, string fileName = "")
        {
            if (string.IsNullOrWhiteSpace(content))
                return new List<AwsCredential>();

            var credentials = LooksLikeIni(content)
                ? ParseIni(content)
                : ParseCsv(content);

            return credentials
                .Where(c => !string.IsNullOrWhiteSpace(c.AccessKeyId) &&
                            !string.IsNullOrWhiteSpace(c.SecretAccessKey))
                .GroupBy(c => c.AccessKeyId, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();
        }

        private static bool LooksLikeIni(string content)
        {
            var hasSection = content.IndexOf('[', StringComparison.Ordinal) >= 0;
            var hasAwsKeys = content.IndexOf("aws_access_key_id", StringComparison.OrdinalIgnoreCase) >= 0;
            return hasSection && (hasAwsKeys || content.IndexOf('=') >= 0);
        }

        private static List<AwsCredential> ParseIni(string content)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            string? current = null;

            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                    continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    current = line[1..^1].Trim();
                    if (!sections.ContainsKey(current))
                    {
                        sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        order.Add(current);
                    }
                    continue;
                }

                if (current == null) continue;

                var separator = line.IndexOf('=');
                if (separator <= 0) continue;

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (key.Length > 0)
                    sections[current][key] = value;
            }

            var result = new List<AwsCredential>();
            foreach (var section in order)
            {
                var values = sections[section];
                if (!values.TryGetValue("aws_access_key_id", out var accessKeyId) ||
                    !values.TryGetValue("aws_secret_access_key", out var secret))
                    continue;

                values.TryGetValue("aws_session_token", out var token);
                values.TryGetValue("region", out var region);

                result.Add(new AwsCredential(
                    ProfileLabel(section),
                    accessKeyId.Trim(),
                    secret.Trim(),
                    token?.Trim() ?? string.Empty,
                    region?.Trim() ?? string.Empty));
            }

            return result;
        }

        private static List<AwsCredential> ParseCsv(string content)
        {
            var lines = content
                .Split('\n')
                .Select(l => l.Trim('\r', ' ', '\t'))
                .Where(l => l.Length > 0)
                .ToList();

            if (lines.Count == 0)
                return new List<AwsCredential>();

            var rows = lines.Select(SplitCsvLine).ToList();
            var header = rows[0];
            var first = rows.Skip(1).ToList();

            var keyIndex = IndexOfHeader(header, "access key id");
            var secretIndex = IndexOfHeader(header, "secret access key");
            var nameIndex = IndexOfHeader(header, "user name");
            if (nameIndex < 0) nameIndex = IndexOfHeader(header, "key name");
            var tokenIndex = IndexOfHeader(header, "session token");

            var hasHeader = keyIndex >= 0 && secretIndex >= 0;
            if (!hasHeader)
            {
                // Sin cabecera reconocible: asumimos el orden clásico de AWS.
                keyIndex = 0;
                secretIndex = 1;
                nameIndex = 2;
                first = rows;
            }

            var result = new List<AwsCredential>();
            foreach (var row in first)
            {
                if (row.Count <= Math.Max(keyIndex, secretIndex)) continue;

                var accessKeyId = row[keyIndex].Trim();
                var secret = row[secretIndex].Trim();
                if (accessKeyId.Length < 8 || secret.Length < 8) continue;

                var name = nameIndex >= 0 && row.Count > nameIndex ? row[nameIndex].Trim() : string.Empty;
                var token = tokenIndex >= 0 && row.Count > tokenIndex ? row[tokenIndex].Trim() : string.Empty;
                var profile = name.Length > 0 ? name : $"key-{accessKeyId[^4..]}";

                result.Add(new AwsCredential(profile, accessKeyId, secret, token, string.Empty));
            }

            return result;
        }

        private static int IndexOfHeader(List<string> header, string name)
        {
            for (var i = 0; i < header.Count; i++)
            {
                if (header[i].Trim().Trim('"').Contains(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>Parte una línea CSV respetando comillas dobles.</summary>
        private static List<string> SplitCsvLine(string line)
        {
            var values = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            values.Add(current.ToString());
            return values;
        }

        private static string ProfileLabel(string section)
        {
            var label = section.Trim();
            if (label.StartsWith("profile ", StringComparison.OrdinalIgnoreCase))
                label = label[8..].Trim();

            return label.Length == 0 ? "default" : label;
        }
    }
}
