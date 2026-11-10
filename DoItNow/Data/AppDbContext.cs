using DoItNow.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

namespace DoItNow.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<TaskItem> Tasks => Set<TaskItem>();
        public DbSet<SubTaskItem> SubTasks => Set<SubTaskItem>();
        public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<AppSetting> Settings => Set<AppSetting>();
        public DbSet<VaultEntry> VaultEntries => Set<VaultEntry>();
        public DbSet<CodeNote> CodeNotes => Set<CodeNote>();

        public static string DatabasePath
        {
            get
            {
                var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(folder, "DoItNow");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "doitnow.db");
            }
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 1. Tareas (Tasks)
            modelBuilder.Entity<TaskItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Priority).HasConversion<string>().HasMaxLength(20);
                entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(30);
                entity.Property(e => e.State).HasConversion<string>().HasMaxLength(20);

                // Índices estratégicos para búsquedas y filtros en tiempo real
                entity.HasIndex(e => e.DueDate);
                entity.HasIndex(e => e.State);
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => new { e.DueDate, e.State });

                // Relación 1:N con Subtareas en cascada
                entity.HasMany(e => e.SubTasks)
                      .WithOne(s => s.Task)
                      .HasForeignKey(s => s.TaskId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // 2. Subtareas (SubTasks)
            modelBuilder.Entity<SubTaskItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
                entity.HasIndex(e => e.TaskId);
            });

            // 3. Conversaciones de IA (ChatConversations)
            modelBuilder.Entity<ChatConversation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);

                entity.HasMany(e => e.Messages)
                      .WithOne(m => m.Conversation)
                      .HasForeignKey(m => m.ConversationId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // 4. Mensajes del Chat con Ollama (ChatMessages)
            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.ModelUsed).HasMaxLength(100);
                entity.HasIndex(e => e.ConversationId);
            });

            // 5. Configuración General y de Ollama (Settings)
            modelBuilder.Entity<AppSetting>(entity =>
            {
                entity.HasKey(e => e.Key);
                entity.Property(e => e.Key).HasMaxLength(100);
                entity.Property(e => e.Value).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(250);
            });

            // 6. Bóveda de claves sensibles (VaultEntries)
            modelBuilder.Entity<VaultEntry>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(150);
                entity.Property(e => e.EntryType).HasMaxLength(40);
                entity.Property(e => e.Username).HasMaxLength(200);
                entity.Property(e => e.Url).HasMaxLength(500);
                entity.Property(e => e.EncryptedSecret).IsRequired();
                entity.HasIndex(e => e.Title);
            });

            // 7. Notas con código (CodeNotes)
            modelBuilder.Entity<CodeNote>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Language).HasMaxLength(40);
                entity.HasIndex(e => e.Title);
                entity.HasIndex(e => e.UpdatedAt);
            });
        }

        /// <summary>
        /// Inicializa la base de datos y aplica migraciones automáticas seguras de columnas/tablas
        /// garantizando compatibilidad sin pérdida de datos si la BD ya existía.
        /// </summary>
        public void InitializeDatabase()
        {
            Database.EnsureCreated();

            // Script de compatibilidad rápida por si la BD preexistente de SQLite carecía de columnas nuevas
            try
            {
                // Nuevas columnas en Tasks
                Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN State TEXT DEFAULT 'Pending';");
            }
            catch { /* Ya existe, ignorar */ }

            try
            {
                Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN UpdatedAt TEXT NULL;");
            }
            catch { }

            try
            {
                Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN EstimatedMinutes INTEGER NULL;");
            }
            catch { }

            try
            {
                Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN ActualMinutes INTEGER NULL;");
            }
            catch { }

            try
            {
                Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN AiSummary TEXT NULL;");
            }
            catch { }

            // Tablas adicionales si no se crearon
            Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS SubTasks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TaskId INTEGER NOT NULL,
                    Title TEXT NOT NULL,
                    IsCompleted INTEGER NOT NULL DEFAULT 0,
                    OrderIndex INTEGER NOT NULL DEFAULT 0,
                    IsAiGenerated INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    CompletedAt TEXT NULL,
                    FOREIGN KEY(TaskId) REFERENCES Tasks(Id) ON DELETE CASCADE
                );
            ");

            Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ChatConversations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
            ");

            Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ChatMessages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConversationId INTEGER NOT NULL,
                    Role TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    ModelUsed TEXT NULL,
                    Timestamp TEXT NOT NULL,
                    FOREIGN KEY(ConversationId) REFERENCES ChatConversations(Id) ON DELETE CASCADE
                );
            ");

            Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    Description TEXT NULL,
                    UpdatedAt TEXT NOT NULL
                );
            ");

            // Bóveda cifrada. El secreto y las notas se almacenan siempre cifrados.
            Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS VaultEntries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    EntryType TEXT NOT NULL DEFAULT 'API Key',
                    Username TEXT NOT NULL DEFAULT '',
                    Url TEXT NOT NULL DEFAULT '',
                    EncryptedSecret TEXT NOT NULL,
                    EncryptedNotes TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
            ");

            Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS CodeNotes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Content TEXT NOT NULL DEFAULT '',
                    Language TEXT NOT NULL DEFAULT 'C#',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
            ");

            try
            {
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_VaultEntries_Title ON VaultEntries(Title);");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_CodeNotes_Title ON CodeNotes(Title);");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_CodeNotes_UpdatedAt ON CodeNotes(UpdatedAt);");
            }
            catch { }
        }
    }
}
