using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace DoItNow.Services
{
    /// <summary>
    /// Resaltado de sintaxis para el editor de notas. Genera definiciones XSHD propias
    /// con una paleta legible en tema claro y oscuro (en lugar de depender de colores
    /// fijos pensados para fondos blancos).
    /// </summary>
    public static class SyntaxHighlightingService
    {
        public static readonly IReadOnlyList<string> Languages = new[]
        {
            "C#", "JSON", "SQL", "JavaScript", "Python", "PowerShell", "XML", "Markdown", "Texto plano"
        };

        private static readonly ConcurrentDictionary<string, IHighlightingDefinition?> Cache = new();

        public static IHighlightingDefinition? Get(string? language, bool dark)
        {
            var normalized = Normalize(language);
            if (normalized == null) return null;

            var key = normalized + (dark ? "|dark" : "|light");
            return Cache.GetOrAdd(key, _ => Load(normalized, dark));
        }

        private static string? Normalize(string? language)
        {
            if (string.IsNullOrWhiteSpace(language)) return null;

            return language.Trim().ToLowerInvariant() switch
            {
                "c#" or "csharp" or "cs" => "CSharp",
                "json" => "Json",
                "sql" => "Sql",
                "javascript" or "js" => "JavaScript",
                "python" or "py" => "Python",
                "powershell" or "ps" or "ps1" => "PowerShell",
                "xml" or "html" => "Xml",
                "markdown" or "md" => "Markdown",
                _ => null
            };
        }

        private static IHighlightingDefinition? Load(string language, bool dark)
        {
            var xshd = language switch
            {
                "CSharp" => BuildCSharp(dark),
                "Json" => BuildJson(dark),
                "Sql" => BuildSql(dark),
                "JavaScript" => BuildJavaScript(dark),
                "Python" => BuildPython(dark),
                "PowerShell" => BuildPowerShell(dark),
                "Xml" => BuildXml(dark),
                "Markdown" => BuildMarkdown(dark),
                _ => null
            };

            if (xshd == null) return null;

            try
            {
                using var reader = XmlReader.Create(new StringReader(xshd));
                return HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyntaxHighlightingService] {language}: {ex.Message}");
                return null;
            }
        }

        private readonly record struct Palette(
            string Comment, string String, string Keyword, string Number,
            string Type, string Preprocessor, string Tag, string Attribute,
            string Variable, string Property, string Heading, string Accent);

        private static Palette GetPalette(bool dark) => dark
            ? new Palette(
                Comment: "#8B90B8", String: "#F0B86E", Keyword: "#C4B5FD", Number: "#7DD3FC",
                Type: "#6EE7B7", Preprocessor: "#D8B4FE", Tag: "#F0A6C0", Attribute: "#FCD34D",
                Variable: "#5EEAD4", Property: "#93C5FD", Heading: "#C4B5FD", Accent: "#CCF658")
            : new Palette(
                Comment: "#6B7280", String: "#B45309", Keyword: "#7C3AED", Number: "#0E7490",
                Type: "#047857", Preprocessor: "#9333EA", Tag: "#BE123C", Attribute: "#B45309",
                Variable: "#0F766E", Property: "#1D4ED8", Heading: "#6D28D9", Accent: "#3F6212");

        private static string BuildCSharp(bool dark)
        {
            var p = GetPalette(dark);
            return $$"""
<SyntaxDefinition name="CSharp" extensions=".cs" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="{{p.Comment}}" fontStyle="italic" />
  <Color name="String" foreground="{{p.String}}" />
  <Color name="Keyword" foreground="{{p.Keyword}}" fontWeight="bold" />
  <Color name="Number" foreground="{{p.Number}}" />
  <Color name="Type" foreground="{{p.Type}}" />
  <Color name="Preprocessor" foreground="{{p.Preprocessor}}" />
  <RuleSet>
    <Span color="Comment" begin="//" />
    <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
    <Span color="String" begin="@&quot;" end="&quot;" multiline="true" />
    <Span color="String" begin="&quot;&quot;&quot;" end="&quot;&quot;&quot;" multiline="true" />
    <Span color="String" begin="&quot;" end="&quot;" escapeCharacter="\" />
    <Span color="String" begin="'" end="'" escapeCharacter="\" />
    <Rule color="Preprocessor">^\s*#\w+</Rule>
    <Rule color="Number">\b(0x[0-9a-fA-F]+|\d+(\.\d+)?([eE][+-]?\d+)?)[fFdDmMlLuU]?\b</Rule>
    <Rule color="Type">\b(bool|byte|char|decimal|double|float|int|long|object|sbyte|short|string|uint|ulong|ushort|var|void|dynamic|Task|List|Dictionary|IEnumerable|Guid|DateTime|Exception)\b</Rule>
    <Rule color="Keyword">\b(abstract|as|async|await|base|break|case|catch|checked|class|const|continue|default|delegate|do|else|enum|event|explicit|extern|false|finally|fixed|for|foreach|get|goto|if|implicit|in|interface|internal|is|lock|namespace|new|null|operator|out|override|params|partial|private|protected|public|readonly|record|ref|return|sealed|set|sizeof|stackalloc|static|struct|switch|this|throw|true|try|typeof|unchecked|unsafe|using|virtual|volatile|while|yield|where|when|init|required|nameof)\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        }

        private static string BuildJson(bool dark)
        {
            var p = GetPalette(dark);
            return $$"""
<SyntaxDefinition name="Json" extensions=".json" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Property" foreground="{{p.Property}}" fontWeight="bold" />
  <Color name="String" foreground="{{p.String}}" />
  <Color name="Keyword" foreground="{{p.Keyword}}" fontWeight="bold" />
  <Color name="Number" foreground="{{p.Number}}" />
  <RuleSet>
    <Rule color="Property">&quot;[^&quot;]*&quot;(?=\s*:)</Rule>
    <Span color="String" begin="&quot;" end="&quot;" escapeCharacter="\" />
    <Rule color="Keyword">\b(true|false|null)\b</Rule>
    <Rule color="Number">-?\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        }

        private static string BuildSql(bool dark)
        {
            var p = GetPalette(dark);
            return $$"""
<SyntaxDefinition name="Sql" extensions=".sql" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="{{p.Comment}}" fontStyle="italic" />
  <Color name="String" foreground="{{p.String}}" />
  <Color name="Keyword" foreground="{{p.Keyword}}" fontWeight="bold" />
  <Color name="Number" foreground="{{p.Number}}" />
  <Color name="Function" foreground="{{p.Type}}" />
  <RuleSet>
    <Span color="Comment" begin="--" />
    <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
    <Span color="String" begin="'" end="'" />
    <Rule color="Number">\b\d+(\.\d+)?\b</Rule>
    <Rule color="Function">\b(COUNT|SUM|AVG|MIN|MAX|COALESCE|IFNULL|DATETIME|DATE|STRFTIME|ROUND|LENGTH|LOWER|UPPER)\b</Rule>
    <Rule color="Keyword">(?i)\b(SELECT|FROM|WHERE|GROUP\s+BY|ORDER\s+BY|HAVING|LIMIT|OFFSET|INSERT|INTO|VALUES|UPDATE|SET|DELETE|CREATE|TABLE|INDEX|VIEW|ALTER|DROP|JOIN|LEFT|RIGHT|INNER|OUTER|ON|AS|AND|OR|NOT|NULL|IS|IN|EXISTS|BETWEEN|LIKE|CASE|WHEN|THEN|ELSE|END|DISTINCT|UNION|ALL|PRIMARY|KEY|FOREIGN|REFERENCES|DEFAULT|UNIQUE|AUTOINCREMENT|INTEGER|TEXT|REAL|BLOB|BOOLEAN|CASCADE|WITH)\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        }

        private static string BuildJavaScript(bool dark)
        {
            var p = GetPalette(dark);
            return $$"""
<SyntaxDefinition name="JavaScript" extensions=".js" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="{{p.Comment}}" fontStyle="italic" />
  <Color name="String" foreground="{{p.String}}" />
  <Color name="Keyword" foreground="{{p.Keyword}}" fontWeight="bold" />
  <Color name="Number" foreground="{{p.Number}}" />
  <Color name="Type" foreground="{{p.Type}}" />
  <RuleSet>
    <Span color="Comment" begin="//" />
    <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
    <Span color="String" begin="`" end="`" multiline="true" escapeCharacter="\" />
    <Span color="String" begin="&quot;" end="&quot;" escapeCharacter="\" />
    <Span color="String" begin="'" end="'" escapeCharacter="\" />
    <Rule color="Number">\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
    <Rule color="Type">\b(true|false|null|undefined|NaN|Infinity)\b</Rule>
    <Rule color="Keyword">\b(async|await|break|case|catch|class|const|continue|debugger|default|delete|do|else|export|extends|finally|for|from|function|get|if|import|in|instanceof|let|new|of|return|set|static|super|switch|this|throw|try|typeof|var|void|while|with|yield)\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        }

        private static string BuildPython(bool dark)
        {
            var p = GetPalette(dark);
            return $$"""
<SyntaxDefinition name="Python" extensions=".py" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="{{p.Comment}}" fontStyle="italic" />
  <Color name="String" foreground="{{p.String}}" />
  <Color name="Keyword" foreground="{{p.Keyword}}" fontWeight="bold" />
  <Color name="Number" foreground="{{p.Number}}" />
  <Color name="Type" foreground="{{p.Type}}" />
  <Color name="Decorator" foreground="{{p.Preprocessor}}" />
  <RuleSet>
    <Span color="Comment" begin="#" />
    <Span color="String" begin="&quot;&quot;&quot;" end="&quot;&quot;&quot;" multiline="true" />
    <Span color="String" begin="'''" end="'''" multiline="true" />
    <Span color="String" begin="&quot;" end="&quot;" escapeCharacter="\" />
    <Span color="String" begin="'" end="'" escapeCharacter="\" />
    <Rule color="Decorator">^\s*@\w+</Rule>
    <Rule color="Number">\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
    <Rule color="Type">\b(True|False|None|self|cls)\b</Rule>
    <Rule color="Keyword">\b(and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield|match|case)\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        }

        private static string BuildPowerShell(bool dark)
        {
            var p = GetPalette(dark);
            return $$"""
<SyntaxDefinition name="PowerShell" extensions=".ps1" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="{{p.Comment}}" fontStyle="italic" />
  <Color name="String" foreground="{{p.String}}" />
  <Color name="Keyword" foreground="{{p.Keyword}}" fontWeight="bold" />
  <Color name="Number" foreground="{{p.Number}}" />
  <Color name="Variable" foreground="{{p.Variable}}" />
  <Color name="Cmdlet" foreground="{{p.Type}}" />
  <RuleSet>
    <Span color="Comment" begin="#" />
    <Span color="Comment" multiline="true" begin="&lt;#" end="#&gt;" />
    <Span color="String" begin="&quot;" end="&quot;" escapeCharacter="`" />
    <Span color="String" begin="'" end="'" />
    <Rule color="Variable">\$[A-Za-z_][\w:]*</Rule>
    <Rule color="Number">\b\d+(\.\d+)?\b</Rule>
    <Rule color="Cmdlet">\b[A-Z][A-Za-z]+-[A-Z][A-Za-z]+\b</Rule>
    <Rule color="Keyword">(?i)\b(if|else|elseif|foreach|for|while|do|switch|function|param|return|try|catch|finally|throw|break|continue|in|not|and|or|xor|begin|process|end|class|enum|using|hidden|static)\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        }

        private static string BuildXml(bool dark)
        {
            var p = GetPalette(dark);
            return $$"""
<SyntaxDefinition name="Xml" extensions=".xml;.xaml;.html" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="{{p.Comment}}" fontStyle="italic" />
  <Color name="String" foreground="{{p.String}}" />
  <Color name="Tag" foreground="{{p.Tag}}" />
  <Color name="Attribute" foreground="{{p.Attribute}}" />
  <Color name="CData" foreground="{{p.Type}}" />
  <RuleSet>
    <Span color="Comment" multiline="true" begin="&lt;!--" end="--&gt;" />
    <Span color="CData" multiline="true" begin="&lt;!\[CDATA\[" end="\]\]&gt;" />
    <Span color="Tag" multiline="true" begin="&lt;/?[A-Za-z_:][-A-Za-z0-9_:.]*" end="/?&gt;">
      <RuleSet>
        <Span color="String" begin="&quot;" end="&quot;" />
        <Rule color="Attribute">\b[A-Za-z_:][-A-Za-z0-9_:.]*(?=\s*=)</Rule>
      </RuleSet>
    </Span>
  </RuleSet>
</SyntaxDefinition>
""";
        }

        private static string BuildMarkdown(bool dark)
        {
            var p = GetPalette(dark);
            return $$"""
<SyntaxDefinition name="Markdown" extensions=".md" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Heading" foreground="{{p.Heading}}" fontWeight="bold" />
  <Color name="Bold" foreground="{{p.Keyword}}" fontWeight="bold" />
  <Color name="Code" foreground="{{p.String}}" />
  <Color name="Link" foreground="{{p.Property}}" />
  <Color name="Quote" foreground="{{p.Comment}}" fontStyle="italic" />
  <RuleSet>
    <Rule color="Heading">^#{1,6}\s.*$</Rule>
    <Rule color="Quote">^&gt;.*$</Rule>
    <Rule color="Bold">\*\*[^*]+\*\*|__[^_]+__</Rule>
    <Rule color="Code">`[^`]+`</Rule>
    <Rule color="Link">\[[^\]]+\]\([^)]+\)</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        }
    }
}
