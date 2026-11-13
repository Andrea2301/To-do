using DoItNow.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DoItNow.Services
{
    /// <summary>
    /// Convierte variables leídas de un archivo <c>.env</c> en entradas cifradas de la
    /// bóveda. Las claves repetidas actualizan la entrada existente en lugar de duplicarla.
    /// </summary>
    public static class EnvVaultImporter
    {
        public sealed record Result(int Created, int Updated, VaultEntry? First);

        public static async Task<Result> ImportAsync(
            VaultService vault,
            IEnumerable<EnvFileImporter.EnvVariable> variables,
            string sourceName,
            DateTime now)
        {
            if (vault == null) throw new ArgumentNullException(nameof(vault));
            if (variables == null) throw new ArgumentNullException(nameof(variables));

            var existing = (await vault.GetEntriesAsync())
                .GroupBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            VaultEntry? first = null;
            var created = 0;
            var updated = 0;

            foreach (var variable in variables)
            {
                existing.TryGetValue(variable.Key, out var match);

                var entry = new VaultEntry
                {
                    Id = match?.Id ?? 0,
                    Title = variable.Key,
                    EntryType = "Variable",
                    Secret = variable.Value,
                    Notes = $"Importado desde {sourceName} el {now:yyyy-MM-dd}."
                };

                await vault.SaveEntryAsync(entry);
                first ??= entry;

                if (match == null) created++;
                else updated++;
            }

            return new Result(created, updated, first);
        }
    }
}
