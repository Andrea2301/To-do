using DoItNow.Services;
using System.Linq;
using Xunit;

namespace DoItNow.Tests;

public class EnvFileImporterTests
{
    [Fact]
    public void Parse_BasicPairs_ReturnsKeysInOrder()
    {
        const string env =
            "GROQ_API_KEY=abc123\n" +
            "AWS_REGION=us-east-1\n" +
            "EMPTY_VALUE=\n";

        var variables = EnvFileImporter.Parse(env);

        Assert.Equal(3, variables.Count);
        Assert.Equal("GROQ_API_KEY", variables[0].Key);
        Assert.Equal("abc123", variables[0].Value);
        Assert.Equal("us-east-1", variables[1].Value);
        Assert.Equal(string.Empty, variables[2].Value);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines()
    {
        const string env =
            "# comentario\n" +
            "\n" +
            "   \n" +
            "   # comentario indentado\n" +
            "TOKEN=valor\n";

        var variable = Assert.Single(EnvFileImporter.Parse(env));

        Assert.Equal("TOKEN", variable.Key);
    }

    [Fact]
    public void Parse_AcceptsExportPrefix()
    {
        const string env = "export DATABASE_URL=postgres://localhost/dev\n";

        var variable = Assert.Single(EnvFileImporter.Parse(env));

        Assert.Equal("DATABASE_URL", variable.Key);
        Assert.Equal("postgres://localhost/dev", variable.Value);
    }

    [Fact]
    public void Parse_DoubleQuotes_UnescapesSpecialCharacters()
    {
        const string env = "MULTILINE=\"linea1\\nlinea2\\tfin\"\nQUOTED=\"dice \\\"hola\\\"\"\n";

        var variables = EnvFileImporter.Parse(env);

        Assert.Equal("linea1\nlinea2\tfin", variables[0].Value);
        Assert.Equal("dice \"hola\"", variables[1].Value);
    }

    [Fact]
    public void Parse_SingleQuotes_KeepContentLiteral()
    {
        const string env = "LITERAL='sin \\n escapes'\n";

        var variable = Assert.Single(EnvFileImporter.Parse(env));

        Assert.Equal("sin \\n escapes", variable.Value);
    }

    [Fact]
    public void Parse_InlineComments_OnlyOutsideQuotes()
    {
        const string env =
            "PORT=8080 # puerto local\n" +
            "HASH=\"valor # con hash\"\n";

        var variables = EnvFileImporter.Parse(env);

        Assert.Equal("8080", variables[0].Value);
        Assert.Equal("valor # con hash", variables[1].Value);
    }

    [Fact]
    public void Parse_DuplicateKeys_LastValueWins()
    {
        const string env =
            "API_KEY=primera\n" +
            "API_KEY=segunda\n";

        var variable = Assert.Single(EnvFileImporter.Parse(env));

        Assert.Equal("API_KEY", variable.Key);
        Assert.Equal("segunda", variable.Value);
    }

    [Fact]
    public void Parse_KeepsEqualsSignsInsideValues()
    {
        const string env = "CONNECTION=Server=local;User=dev;Password=s3cret\n";

        var variable = Assert.Single(EnvFileImporter.Parse(env));

        Assert.Equal("Server=local;User=dev;Password=s3cret", variable.Value);
    }

    [Fact]
    public void Parse_SkipsInvalidLines()
    {
        const string env =
            "SIN_SEPARADOR\n" +
            "=valor-sin-clave\n" +
            "1INVALIDA=valor\n" +
            "CLAVE-VALIDA=valor\n" +
            "OK_KEY=valor\n";

        var variable = Assert.Single(EnvFileImporter.Parse(env));

        Assert.Equal("OK_KEY", variable.Key);
    }

    [Fact]
    public void Parse_HandlesCrLfAndEmptyContent()
    {
        var variables = EnvFileImporter.Parse("A=1\r\nB=2\r\n");

        Assert.Equal(2, variables.Count);
        Assert.Equal("2", variables[1].Value);

        Assert.Empty(EnvFileImporter.Parse(string.Empty));
        Assert.Empty(EnvFileImporter.Parse("   \r\n\t"));
    }
}
