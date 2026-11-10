using System;

namespace DoItNow.Models
{
    /// <summary>
    /// Nota con contenido de código o texto. El lenguaje controla el resaltado de sintaxis.
    /// </summary>
    public class CodeNote
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Language { get; set; } = "C#";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string LanguageIcon => Language switch
        {
            "C#" => "C#",
            "JSON" => "{}",
            "SQL" => "SQL",
            "JavaScript" => "JS",
            "Python" => "PY",
            "PowerShell" => "PS",
            "XML" => "</>",
            "Markdown" => "MD",
            _ => "TXT"
        };
    }
}
