using DoItNow.Data;
using DoItNow.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DoItNow.Services
{
    /// <summary>CRUD local de notas con código. Sigue el patrón Repository del resto de servicios.</summary>
    public class NoteService
    {
        private readonly AppDbContext _context;

        public NoteService()
        {
            _context = new AppDbContext();
            _context.InitializeDatabase();
            SeedInitialDataIfEmpty();
        }

        private void SeedInitialDataIfEmpty()
        {
            if (_context.CodeNotes.Any()) return;

            _context.CodeNotes.AddRange(
                new CodeNote
                {
                    Title = "Llamada a la API de Groq",
                    Language = "C#",
                    Content = """
// DoItNow · ejemplo de cliente HTTP para Groq
using var client = new HttpClient();
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", apiKey);

var body = new
{
    model = "llama-3.3-70b-versatile",
    messages = new[] { new { role = "user", content = "Hola" } }
};

var response = await client.PostAsJsonAsync(
    "https://api.groq.com/openai/v1/chat/completions", body);
response.EnsureSuccessStatusCode();
""",
                    CreatedAt = DateTime.Now.AddDays(-2),
                    UpdatedAt = DateTime.Now.AddDays(-2)
                },
                new CodeNote
                {
                    Title = "Consulta SQL de tareas por día",
                    Language = "SQL",
                    Content = """
SELECT date(DueDate) AS day, COUNT(*) AS total
FROM Tasks
WHERE State <> 'Completed'
GROUP BY date(DueDate)
ORDER BY day DESC
LIMIT 14;
""",
                    CreatedAt = DateTime.Now.AddDays(-1),
                    UpdatedAt = DateTime.Now.AddDays(-1)
                },
                new CodeNote
                {
                    Title = "Payload de configuración",
                    Language = "JSON",
                    Content = """
{
  "aiSettings": {
    "provider": "Auto",
    "ollamaEndpoint": "http://localhost:11434",
    "ollamaModel": "llama3.2"
  },
  "appSettings": {
    "theme": "Claro"
  }
}
""",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });

            _context.SaveChanges();
        }

        public async Task<List<CodeNote>> GetAllAsync()
        {
            return await _context.CodeNotes
                .AsNoTracking()
                .OrderByDescending(n => n.UpdatedAt)
                .ToListAsync();
        }

        public async Task<CodeNote> AddAsync(CodeNote note)
        {
            note.CreatedAt = DateTime.Now;
            note.UpdatedAt = DateTime.Now;
            _context.CodeNotes.Add(note);
            await _context.SaveChangesAsync();
            return note;
        }

        public async Task UpdateAsync(CodeNote note)
        {
            note.UpdatedAt = DateTime.Now;
            _context.CodeNotes.Update(note);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int noteId)
        {
            var note = await _context.CodeNotes.FindAsync(noteId);
            if (note == null) return;

            _context.CodeNotes.Remove(note);
            await _context.SaveChangesAsync();
        }

        public async Task<int> CountAsync() => await _context.CodeNotes.CountAsync();
    }
}
