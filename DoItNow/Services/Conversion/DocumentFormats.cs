using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DoItNow.Services.Conversion
{
    /// <summary>Tipo de documento que el convertidor sabe leer.</summary>
    public enum DocumentKind
    {
        Pdf,
        Word,
        Excel,
        Csv,
        Json,
        Markdown,
        Html,
        Text,
        Unknown
    }

    /// <summary>Formato de salida disponible para un documento.</summary>
    public sealed record ConversionTarget(string Key, string Label, string Extension)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Catálogo de formatos soportados y de las conversiones disponibles para cada uno.
    /// Todo se resuelve en local: ninguna operación toca la red.
    /// </summary>
    public static class DocumentFormats
    {
        private static readonly ConversionTarget Txt = new("txt", "Texto (.txt)", "txt");
        private static readonly ConversionTarget Markdown = new("md", "Markdown (.md)", "md");
        private static readonly ConversionTarget Csv = new("csv", "CSV (.csv)", "csv");
        private static readonly ConversionTarget Json = new("json", "JSON (.json)", "json");
        private static readonly ConversionTarget Html = new("html", "HTML (.html)", "html");

        public const string FileFilter =
            "Documentos (*.pdf;*.docx;*.xlsx;*.csv;*.tsv;*.json;*.md;*.txt)|*.pdf;*.docx;*.xlsx;*.csv;*.tsv;*.json;*.md;*.txt|" +
            "PDF (*.pdf)|*.pdf|" +
            "Word (*.docx)|*.docx|" +
            "Excel (*.xlsx)|*.xlsx|" +
            "CSV (*.csv;*.tsv)|*.csv;*.tsv|" +
            "JSON (*.json)|*.json|" +
            "Markdown y texto (*.md;*.txt)|*.md;*.txt|" +
            "Todos los archivos (*.*)|*.*";

        public static DocumentKind Detect(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => DocumentKind.Pdf,
                ".docx" or ".docm" => DocumentKind.Word,
                ".xlsx" or ".xlsm" => DocumentKind.Excel,
                ".csv" or ".tsv" => DocumentKind.Csv,
                ".json" => DocumentKind.Json,
                ".md" or ".markdown" => DocumentKind.Markdown,
                ".html" or ".htm" => DocumentKind.Html,
                ".txt" or ".log" => DocumentKind.Text,
                _ => DocumentKind.Unknown
            };
        }

        public static string Label(DocumentKind kind) => kind switch
        {
            DocumentKind.Pdf => "PDF",
            DocumentKind.Word => "Word",
            DocumentKind.Excel => "Excel",
            DocumentKind.Csv => "CSV",
            DocumentKind.Json => "JSON",
            DocumentKind.Markdown => "Markdown",
            DocumentKind.Html => "HTML",
            DocumentKind.Text => "Texto",
            _ => "Desconocido"
        };

        /// <summary>Etiqueta corta para el chip del archivo (PDF, DOCX, XLSX...).</summary>
        public static string ExtensionLabel(string path)
            => Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

        public static IReadOnlyList<ConversionTarget> Targets(DocumentKind kind) => kind switch
        {
            DocumentKind.Pdf => new[] { Txt, Markdown },
            DocumentKind.Word => new[] { Txt, Markdown },
            DocumentKind.Excel => new[] { Csv, Json },
            DocumentKind.Csv => new[] { Json, Markdown },
            DocumentKind.Json => new[] { Csv },
            DocumentKind.Markdown => new[] { Html, Txt },
            DocumentKind.Text => new[] { Markdown, Html },
            _ => Array.Empty<ConversionTarget>()
        };

        public static bool IsSupported(DocumentKind kind) => Targets(kind).Count > 0;

        public static string UnsupportedReason(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".doc" => "Los .doc antiguos no se pueden leer. Ábrelo en Word y guárdalo como .docx.",
                ".xls" => "Los .xls antiguos no se pueden leer. Guárdalo como .xlsx.",
                ".ppt" or ".pptx" => "PowerPoint todavía no está soportado.",
                ".html" or ".htm" => "HTML está soportado como salida, no como entrada.",
                _ => $"Formato {extension} no soportado."
            };
        }

        public static string SupportedExtensionsText
            => "PDF · Word · Excel · CSV · JSON · Markdown · TXT";
    }
}
