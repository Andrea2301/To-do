using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Markdig;

namespace DoItNow.Services.Conversion
{
    /// <summary>Resultado de una conversión completada.</summary>
    public sealed record ConversionResult(string OutputPath, string Preview, int Characters);

    /// <summary>
    /// Motor de conversión local. Lee el documento original y escribe un archivo nuevo
    /// en la carpeta indicada. Nunca usa la red ni servicios externos.
    /// </summary>
    public static class DocumentConverter
    {
        private static readonly MarkdownPipeline MarkdownPipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        public static Task<ConversionResult> ConvertAsync(
            string inputPath,
            ConversionTarget target,
            string? outputFolder = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var kind = DocumentFormats.Detect(inputPath);

                if (!DocumentFormats.IsSupported(kind))
                    throw new NotSupportedException(DocumentFormats.UnsupportedReason(inputPath));

                string content;
                string preview;

                try
                {
                    (content, preview) = BuildContent(inputPath, kind, target.Key);
                }
                catch (NotSupportedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"No se pudo leer el archivo: {ex.Message}", ex);
                }

                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidOperationException(kind == DocumentKind.Pdf
                        ? "El PDF no tiene texto seleccionable. Si es un escaneo, necesita OCR y todavía no está soportado."
                        : "El archivo no contiene texto que se pueda convertir.");

                var folder = string.IsNullOrWhiteSpace(outputFolder)
                    ? Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory
                    : outputFolder;

                Directory.CreateDirectory(folder);

                var outputPath = GetUniquePath(
                    Path.Combine(folder, Path.GetFileNameWithoutExtension(inputPath) + "." + target.Extension));

                File.WriteAllText(outputPath, content, new UTF8Encoding(false));

                return new ConversionResult(outputPath, TrimPreview(preview), content.Length);
            }, cancellationToken);
        }

        private static (string Content, string Preview) BuildContent(string path, DocumentKind kind, string targetKey)
        {
            return (kind, targetKey) switch
            {
                (DocumentKind.Pdf, "txt") => Simple(PdfTextReader.Read(path, addPageMarkers: false)),
                (DocumentKind.Pdf, "md") => Simple(PdfTextReader.Read(path, addPageMarkers: true)),

                (DocumentKind.Word, "txt") => Simple(WordTextReader.Read(path).PlainText),
                (DocumentKind.Word, "md") => Simple(WordTextReader.Read(path).Markdown),

                (DocumentKind.Excel, "csv") => Simple(Tabular.ToCsv(ExcelSheetReader.Read(path))),
                (DocumentKind.Excel, "json") => Simple(Tabular.ToJson(ExcelSheetReader.Read(path))),

                (DocumentKind.Csv, "json") => Simple(Tabular.ToJson(
                    Tabular.ParseDelimited(TextFileReader.Read(path)))),
                (DocumentKind.Csv, "md") => Simple(Tabular.ToMarkdownTable(
                    Tabular.ParseDelimited(TextFileReader.Read(path)))),

                (DocumentKind.Json, "csv") => Simple(Tabular.ToCsv(
                    Tabular.FromJson(TextFileReader.Read(path)))),

                (DocumentKind.Markdown, "txt") => Simple(Markdown.ToPlainText(TextFileReader.Read(path), MarkdownPipeline)),
                (DocumentKind.Markdown, "html") => HtmlResult(TextFileReader.Read(path), asMarkdown: true),

                (DocumentKind.Text, "md") => Simple(TextToMarkdown(TextFileReader.Read(path))),
                (DocumentKind.Text, "html") => HtmlResult(TextFileReader.Read(path), asMarkdown: false),

                _ => throw new NotSupportedException(
                    $"La conversión {DocumentFormats.Label(kind)} → {targetKey.ToUpperInvariant()} no está disponible.")
            };
        }

        private static (string, string) Simple(string content) => (content, content);

        private static (string Content, string Preview) HtmlResult(string source, bool asMarkdown)
        {
            var body = asMarkdown
                ? Markdown.ToHtml(source, MarkdownPipeline)
                : TextToHtml(source);

            var title = "Documento convertido con DoItNow";

            var html = $$"""
                <!DOCTYPE html>
                <html lang="es">
                <head>
                  <meta charset="utf-8"/>
                  <meta name="viewport" content="width=device-width, initial-scale=1"/>
                  <title>{{WebUtility.HtmlEncode(title)}}</title>
                  <style>
                    :root { color-scheme: light dark; }
                    body { font-family: "Segoe UI", system-ui, sans-serif; line-height: 1.6;
                           max-width: 760px; margin: 48px auto; padding: 0 24px; color: #1c1d36; background: #fff; }
                    h1, h2, h3, h4 { line-height: 1.25; margin-top: 2em; }
                    h1 { border-bottom: 2px solid #e6e8fa; padding-bottom: .3em; }
                    code, pre { font-family: Consolas, monospace; background: #f4f5fb; border-radius: 8px; }
                    code { padding: .15em .35em; }
                    pre { padding: 14px 16px; overflow-x: auto; }
                    table { border-collapse: collapse; width: 100%; margin: 1.2em 0; }
                    th, td { border: 1px solid #e0e2f0; padding: 8px 10px; text-align: left; }
                    th { background: #f8f9fe; }
                    blockquote { margin: 1em 0; padding: .2em 1em; border-left: 4px solid #c9ccf0; color: #55587f; }
                    img { max-width: 100%; }
                    @media (prefers-color-scheme: dark) {
                      body { background: #0f1020; color: #e9eaf6; }
                      h1 { border-bottom-color: #2c2e4a; }
                      code, pre { background: #1b1d33; }
                      th, td { border-color: #2c2e4a; }
                      th { background: #1b1d33; }
                      blockquote { border-left-color: #3b3f66; color: #b9bcd8; }
                    }
                  </style>
                </head>
                <body>
                {{body}}
                </body>
                </html>
                """;

            return (html, asMarkdown ? Markdown.ToPlainText(source, MarkdownPipeline) : source);
        }

        private static string TextToMarkdown(string text)
        {
            var blocks = Regex.Split(text.Replace("\r\n", "\n"), @"\n{2,}")
                .Select(block => block.TrimEnd())
                .Where(block => block.Length > 0);

            return string.Join("\n\n", blocks) + "\n";
        }

        private static string TextToHtml(string text)
        {
            var paragraphs = Regex.Split(text.Replace("\r\n", "\n"), @"\n{2,}")
                .Select(block => block.TrimEnd())
                .Where(block => block.Length > 0)
                .Select(block => $"<p>{WebUtility.HtmlEncode(block).Replace("\n", "<br/>")}</p>");

            return string.Join(Environment.NewLine, paragraphs);
        }

        private static string TrimPreview(string content)
        {
            const int max = 6000;
            var preview = Regex.Replace(content, @"<!-- página \d+ -->\r?\n?", string.Empty);

            if (preview.Length <= max) return preview;

            return preview.Substring(0, max).TrimEnd() + "\n\n… (vista previa recortada)";
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            var folder = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);

            for (var i = 2; i < 1000; i++)
            {
                var candidate = Path.Combine(folder, $"{name} ({i}){extension}");
                if (!File.Exists(candidate)) return candidate;
            }

            return Path.Combine(folder, $"{name} ({Guid.NewGuid():N}){extension}");
        }
    }
}
