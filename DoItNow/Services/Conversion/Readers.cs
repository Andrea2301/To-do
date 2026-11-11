using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DoItNow.Services.Conversion
{
    /// <summary>Lectura de texto plano con detección de codificación (UTF-8, UTF-16 o ANSI).</summary>
    public static class TextFileReader
    {
        public static string Read(string path)
        {
            var bytes = File.ReadAllBytes(path);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Archivos guardados con la página de códigos de Windows (habitual en Excel/Notepad en español).
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }

    /// <summary>Extrae texto de PDF con PdfPig (100% local).</summary>
    public static class PdfTextReader
    {
        public static string Read(string path, bool addPageMarkers)
        {
            using var document = PdfDocument.Open(path);
            var sb = new StringBuilder();
            var pageNumber = 0;

            foreach (var page in document.GetPages())
            {
                pageNumber++;

                if (sb.Length > 0) sb.AppendLine().AppendLine();

                if (addPageMarkers)
                {
                    sb.AppendLine($"<!-- página {pageNumber} -->");
                }

                var text = ContentOrderTextExtractor.GetText(page);
                sb.Append(text.TrimEnd());
            }

            return sb.ToString().Trim() + Environment.NewLine;
        }

        public static int CountPages(string path)
        {
            using var document = PdfDocument.Open(path);
            return document.NumberOfPages;
        }
    }

    /// <summary>Lee DOCX conservando títulos, listas y tablas.</summary>
    public static class WordTextReader
    {
        public sealed record WordContent(string PlainText, string Markdown);

        public static WordContent Read(string path)
        {
            using var document = WordprocessingDocument.Open(path, false);
            var body = document.MainDocumentPart?.Document?.Body;

            var plain = new StringBuilder();
            var markdown = new StringBuilder();

            if (body is null)
                return new WordContent(string.Empty, string.Empty);

            foreach (var element in body.ChildElements)
            {
                if (element is W.Paragraph paragraph)
                {
                    AppendParagraph(paragraph, plain, markdown);
                }
                else if (element is W.Table table)
                {
                    AppendTable(table, plain, markdown);
                }
            }

            return new WordContent(
                plain.ToString().Trim() + Environment.NewLine,
                markdown.ToString().Trim() + Environment.NewLine);
        }

        private static void AppendParagraph(W.Paragraph paragraph, StringBuilder plain, StringBuilder markdown)
        {
            var text = CleanText(paragraph.InnerText);
            if (string.IsNullOrWhiteSpace(text)) return;

            var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
            var headingLevel = GetHeadingLevel(styleId);
            var isListItem = paragraph.ParagraphProperties?.NumberingProperties is not null;

            if (headingLevel > 0)
            {
                plain.AppendLine(text.ToUpperInvariant());
                plain.AppendLine();

                markdown.Append('#', headingLevel).Append(' ').AppendLine(text);
                markdown.AppendLine();
                return;
            }

            if (isListItem)
            {
                plain.Append("• ").AppendLine(text);
                markdown.Append("- ").AppendLine(text);
                return;
            }

            plain.AppendLine(text);
            plain.AppendLine();

            markdown.AppendLine(text);
            markdown.AppendLine();
        }

        private static void AppendTable(W.Table table, StringBuilder plain, StringBuilder markdown)
        {
            var rows = table.Elements<W.TableRow>().ToList();
            if (rows.Count == 0) return;

            var parsedRows = rows
                .Select(row => row.Elements<W.TableCell>()
                    .Select(cell => CleanText(cell.InnerText).Replace("\r", " ").Replace("\n", " "))
                    .ToArray())
                .ToList();

            foreach (var row in parsedRows)
                plain.AppendLine(string.Join(" | ", row));

            plain.AppendLine();

            var header = parsedRows[0];
            markdown.Append("| ").Append(string.Join(" | ", header)).AppendLine(" |");
            markdown.Append('|').Append(string.Join("|", header.Select(_ => " --- "))).AppendLine("|");

            foreach (var row in parsedRows.Skip(1))
                markdown.Append("| ").Append(string.Join(" | ", row)).AppendLine(" |");

            markdown.AppendLine();
        }

        private static int GetHeadingLevel(string styleId)
        {
            if (string.IsNullOrWhiteSpace(styleId)) return 0;

            var match = Regex.Match(styleId, @"(?:heading|t[ií]tulo|ttulo)\s*(\d)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var level))
                return Math.Clamp(level, 1, 6);

            if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
                styleId.Equals("Titulo", StringComparison.OrdinalIgnoreCase))
                return 1;

            return 0;
        }

        private static string CleanText(string value)
            => value.Replace("\u000B", " ").Replace("\u0007", " ").Trim();
    }

    /// <summary>Lee la primera hoja de un XLSX (OpenXml, sin dependencias nativas).</summary>
    public static class ExcelSheetReader
    {
        public static TableData Read(string path, string? sheetName = null)
        {
            var table = new TableData();

            using var document = SpreadsheetDocument.Open(path, false);
            var workbookPart = document.WorkbookPart;
            var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList();

            if (workbookPart is null || sheets is null || sheets.Count == 0) return table;

            var sheet = sheetName is null
                ? sheets[0]
                : sheets.FirstOrDefault(s => s.Name?.Value == sheetName) ?? sheets[0];

            if (sheet.Id?.Value is null) return table;

            var worksheetPart = workbookPart.GetPartById(sheet.Id.Value) as WorksheetPart;
            var sheetData = worksheetPart?.Worksheet?.GetFirstChild<SheetData>();
            if (sheetData is null) return table;

            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            var rows = new List<string[]>();

            foreach (var row in sheetData.Elements<Row>())
            {
                var values = new List<string>();
                var nextColumn = 0;

                foreach (var cell in row.Elements<Cell>())
                {
                    var column = ColumnIndex(cell.CellReference?.Value);
                    if (column < 0) column = nextColumn;

                    while (values.Count < column) values.Add(string.Empty);
                    values.Add(ReadCell(cell, sharedStrings));

                    nextColumn = column + 1;
                }

                if (values.Any(v => !string.IsNullOrWhiteSpace(v)))
                    rows.Add(values.ToArray());
            }

            if (rows.Count == 0) return table;

            table.Headers.AddRange(rows[0]);
            foreach (var row in rows.Skip(1))
                table.Rows.Add(row);

            return table;
        }

        private static string ReadCell(Cell cell, SharedStringTable? sharedStrings)
        {
            var dataType = cell.DataType?.Value;

            if (dataType == CellValues.SharedString &&
                int.TryParse(cell.CellValue?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                return sharedStrings?.ElementAtOrDefault(index)?.InnerText ?? string.Empty;
            }

            if (dataType == CellValues.InlineString)
                return cell.InlineString?.InnerText ?? string.Empty;

            if (dataType == CellValues.Boolean)
                return cell.CellValue?.Text == "1" ? "TRUE" : "FALSE";

            return cell.CellValue?.Text ?? string.Empty;
        }

        private static int ColumnIndex(string? reference)
        {
            if (string.IsNullOrEmpty(reference)) return -1;

            var index = 0;
            foreach (var ch in reference)
            {
                if (!char.IsLetter(ch)) break;
                index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }

            return index - 1;
        }
    }
}
