using System;
using System.Collections.Generic;
using System.Text;

namespace DoItNow.Services
{
    /// <summary>
    /// Lee archivos <c>.env</c> (dotenv) sin ejecutar nada ni acceder a la red.
    /// Admite comentarios con <c>#</c>, el prefijo <c>export</c>, comillas simples,
    /// comillas dobles con escapes y valores con <c>=</c> dentro.
    /// </summary>
    public static class EnvFileImporter
    {
        public sealed record EnvVariable(string Key, string Value);

        public static List<EnvVariable> Parse(string content)
        {
            var variables = new List<EnvVariable>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(content))
                return variables;

            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line.StartsWith("export ", StringComparison.Ordinal))
                    line = line[7..].TrimStart();

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                var key = line[..separator].Trim();
                if (!IsValidKey(key))
                    continue;

                var value = ParseValue(line[(separator + 1)..].Trim());

                // dotenv: si una clave se repite, gana la última aparición.
                if (index.TryGetValue(key, out var existing))
                    variables[existing] = new EnvVariable(key, value);
                else
                {
                    index[key] = variables.Count;
                    variables.Add(new EnvVariable(key, value));
                }
            }

            return variables;
        }

        private static bool IsValidKey(string key)
        {
            if (key.Length == 0)
                return false;

            var first = key[0];
            if (!char.IsLetter(first) && first != '_')
                return false;

            foreach (var c in key)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                    return false;
            }

            return true;
        }

        private static string ParseValue(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                return Unescape(value[1..^1]);

            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                return value[1..^1];

            // Comentario en línea: solo si el # va precedido de un espacio.
            var comment = value.IndexOf(" #", StringComparison.Ordinal);
            if (comment < 0)
                comment = value.IndexOf("\t#", StringComparison.Ordinal);
            if (comment >= 0)
                value = value[..comment];

            return value.TrimEnd();
        }

        private static string Unescape(string value)
        {
            var builder = new StringBuilder(value.Length);

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(c);
                    continue;
                }

                var next = value[++i];
                builder.Append(next switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ => next
                });
            }

            return builder.ToString();
        }
    }
}
