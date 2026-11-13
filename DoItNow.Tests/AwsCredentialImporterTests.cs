using DoItNow.Services;
using System.Linq;
using Xunit;

namespace DoItNow.Tests;

public class AwsCredentialImporterTests
{
    [Fact]
    public void Parse_IamCsvWithHeader_ReadsAccessKeySecretAndProfile()
    {
        const string csv =
            "\"User name\",\"Password\",\"Access key ID\",\"Secret access key\",\"Console login link\"\r\n" +
            "\"deploy\",\"\",\"AKIAIOSFODNN7EXAMPLE\",\"wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY\",\"https://signin.aws.amazon.com\"\r\n";

        var credentials = AwsCredentialImporter.Parse(csv, "credentials.csv");

        var credential = Assert.Single(credentials);
        Assert.Equal("deploy", credential.Profile);
        Assert.Equal("AKIAIOSFODNN7EXAMPLE", credential.AccessKeyId);
        Assert.Equal("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", credential.SecretAccessKey);
    }

    [Fact]
    public void Parse_IamCsv_RespectsQuotedCommasInProfileName()
    {
        const string csv =
            "\"User name\",\"Access key ID\",\"Secret access key\"\r\n" +
            "\"Ops, team\",\"AKIAQUOTEDEXAMPLE\",\"secretoConComa12345\"\r\n";

        var credential = Assert.Single(AwsCredentialImporter.Parse(csv, "keys.csv"));

        Assert.Equal("Ops, team", credential.Profile);
        Assert.Equal("AKIAQUOTEDEXAMPLE", credential.AccessKeyId);
    }

    [Fact]
    public void Parse_CsvWithoutHeader_FallsBackToAwsColumnOrder()
    {
        const string csv = "AKIANOHEADEREXAMPLE,secretoSinCabecera12345,perfil-sin-cabecera\n";

        var credential = Assert.Single(AwsCredentialImporter.Parse(csv, "keys.csv"));

        Assert.Equal("AKIANOHEADEREXAMPLE", credential.AccessKeyId);
        Assert.Equal("secretoSinCabecera12345", credential.SecretAccessKey);
        Assert.Equal("perfil-sin-cabecera", credential.Profile);
    }

    [Fact]
    public void Parse_CsvWithDifferentColumnOrder_UsesHeaderNames()
    {
        const string csv =
            "Secret access key,Access key ID,User name\n" +
            "secretoReordenado12345,AKIAREORDEREXAMPLE,reordenado\n";

        var credential = Assert.Single(AwsCredentialImporter.Parse(csv, "keys.csv"));

        Assert.Equal("AKIAREORDEREXAMPLE", credential.AccessKeyId);
        Assert.Equal("secretoReordenado12345", credential.SecretAccessKey);
        Assert.Equal("reordenado", credential.Profile);
    }

    [Fact]
    public void Parse_IniWithProfiles_ReadsRegionAndSessionToken()
    {
        const string ini =
            "[default]\n" +
            "aws_access_key_id = AKIADEFAULTEXAMPLE\n" +
            "aws_secret_access_key = secretoDefault12345\n" +
            "region = us-east-1\n" +
            "\n" +
            "[profile dev]\n" +
            "aws_access_key_id = AKIADEVEXAMPLE\n" +
            "aws_secret_access_key = secretoDev12345\n" +
            "aws_session_token = tokenTemporal\n";

        var credentials = AwsCredentialImporter.Parse(ini, "credentials");

        Assert.Equal(2, credentials.Count);

        var defaults = credentials.Single(c => c.Profile == "default");
        Assert.Equal("us-east-1", defaults.Region);
        Assert.Equal(string.Empty, defaults.SessionToken);

        var dev = credentials.Single(c => c.Profile == "dev");
        Assert.Equal("tokenTemporal", dev.SessionToken);
    }

    [Fact]
    public void Parse_DuplicateAccessKeys_ReturnsOneCredential()
    {
        const string csv =
            "Access key ID,Secret access key,User name\n" +
            "AKIADUPLICATEDEXAMPLE,secretoDuplicado12345,primero\n" +
            "AKIADUPLICATEDEXAMPLE,secretoDuplicado12345,segundo\n";

        var credential = Assert.Single(AwsCredentialImporter.Parse(csv, "keys.csv"));

        Assert.Equal("primero", credential.Profile);
    }

    [Fact]
    public void Parse_IncompleteRows_AreIgnored()
    {
        const string csv =
            "Access key ID,Secret access key,User name\n" +
            "AKIAINCOMPLETEEXAMPLE,,sin-secreto\n" +
            ",secretoSinAccessKey12345,sin-access-key\n" +
            "AKIAVALIDEXAMPLE,secretoValido12345,valido\n";

        var credential = Assert.Single(AwsCredentialImporter.Parse(csv, "keys.csv"));

        Assert.Equal("AKIAVALIDEXAMPLE", credential.AccessKeyId);
    }

    [Fact]
    public void Parse_IniWithoutAwsKeys_ReturnsEmpty()
    {
        const string ini = "[default]\nregion = eu-west-1\noutput = json\n";

        Assert.Empty(AwsCredentialImporter.Parse(ini, "config"));
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmpty()
    {
        Assert.Empty(AwsCredentialImporter.Parse(string.Empty));
        Assert.Empty(AwsCredentialImporter.Parse("   \r\n\t"));
    }
}
