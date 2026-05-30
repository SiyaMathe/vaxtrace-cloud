using FluentAssertions;
using VaxTrace.Functions;
using Xunit;

namespace VaxTrace.Tests;

public class MessageParserTests
{
    // ── Format A Tests ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_FormatA_StandardSaId_ReturnsValidMessage()
    {
        const string msg = "8001015009087:Charlotte Maxeke Hospital:2024-01-20:JNJ-2024-002-A";
        var result = MessageParser.Parse(msg);

        result.Should().NotBeNull();
        result!.Format.Should().Be('A');
        result.IDNumber.Should().Be("8001015009087");
        result.VaccinationCenter.Should().Be("Charlotte Maxeke Hospital");
        result.VaccinationDate.Should().Be(new DateOnly(2024, 1, 20));
        result.VaccineSerialNumber.Should().Be("JNJ-2024-002-A");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Parse_FormatA_CenterWithColonInName_ParsesCorrectly()
    {
        const string msg = "9203224800088:St. Mary's: Durban Clinic:2024-03-01:PFZ-001";
        var result = MessageParser.Parse(msg);

        result.Should().NotBeNull();
        result!.Format.Should().Be('A');
        result.IDNumber.Should().Be("9203224800088");
        result.VaccineSerialNumber.Should().Be("PFZ-001");
        result.VaccinationDate.Should().Be(new DateOnly(2024, 3, 1));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Parse_FormatA_PassportNumber_ReturnsValidMessage()
    {
        const string msg = "P12345678:Groote Schuur Hospital:2024-02-15:AZ-2024-005";
        var result = MessageParser.Parse(msg);

        result.Should().NotBeNull();
        result!.Format.Should().Be('A');
        result.IDNumber.Should().Be("P12345678");
        result.IsValid.Should().BeTrue();
    }

    // ── Format B Tests ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_FormatB_StandardMessage_ReturnsValidMessage()
    {
        const string msg = "BAR-00123:2024-01-15:Groote Schuur Hospital:0105215359081";
        var result = MessageParser.Parse(msg);

        result.Should().NotBeNull();
        result!.Format.Should().Be('B');
        result.IDNumber.Should().Be("0105215359081");
        result.VaccinationCenter.Should().Be("Groote Schuur Hospital");
        result.VaccinationDate.Should().Be(new DateOnly(2024, 1, 15));
        result.VaccineBarcode.Should().Be("BAR-00123");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Parse_FormatB_BarcodeWithHyphens_ParsesCorrectly()
    {
        const string msg = "PFZ-2024-001-B:2024-02-12:Tygerberg Hospital:7512086150082";
        var result = MessageParser.Parse(msg);

        result.Should().NotBeNull();
        result!.Format.Should().Be('B');
        result.VaccineBarcode.Should().Be("PFZ-2024-001-B");
        result.IDNumber.Should().Be("7512086150082");
        result.IsValid.Should().BeTrue();
    }

    // ── Edge Cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NullMessage_ReturnsNull()
    {
        var result = MessageParser.Parse(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        var result = MessageParser.Parse(string.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public void Parse_TooFewParts_ReturnsNull()
    {
        var result = MessageParser.Parse("8001015009087:SomeHospital");
        result.Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidDate_ReturnsInvalidMessage()
    {
        const string msg = "8001015009087:Hospital Name:NOT-A-DATE:PFZ-001";
        var result = MessageParser.Parse(msg);

        result.Should().NotBeNull();
        result!.IsValid.Should().BeFalse();
        result.ParseError.Should().Contain("date");
    }

    [Fact]
    public void Parse_FormatA_WhitespaceAroundParts_TrimsCorrectly()
    {
        const string msg = " 8001015009087 : Charlotte Maxeke Hospital : 2024-01-20 : JNJ-2024 ";
        var result = MessageParser.Parse(msg);

        result.Should().NotBeNull();
        result!.IDNumber!.Trim().Should().Be("8001015009087");
    }

    // ── Message Builder Tests ─────────────────────────────────────────────────

    [Fact]
    public void BuildFormatA_ThenParse_RoundTripsCorrectly()
    {
        var original = MessageParser.BuildFormatA("8001015009087", "Charlotte Maxeke Hospital", new DateOnly(2024, 1, 20), "JNJ-2024-002-A");
        var parsed = MessageParser.Parse(original);

        parsed.Should().NotBeNull();
        parsed!.Format.Should().Be('A');
        parsed.IDNumber.Should().Be("8001015009087");
        parsed.VaccinationCenter.Should().Be("Charlotte Maxeke Hospital");
        parsed.VaccinationDate.Should().Be(new DateOnly(2024, 1, 20));
        parsed.VaccineSerialNumber.Should().Be("JNJ-2024-002-A");
        parsed.IsValid.Should().BeTrue();
    }

    [Fact]
    public void BuildFormatB_ThenParse_RoundTripsCorrectly()
    {
        var original = MessageParser.BuildFormatB("BAR-99999", new DateOnly(2024, 6, 1), "Inkosi Albert Luthuli Hospital", "9203224800088");
        var parsed = MessageParser.Parse(original);

        parsed.Should().NotBeNull();
        parsed!.Format.Should().Be('B');
        parsed.VaccineBarcode.Should().Be("BAR-99999");
        parsed.IDNumber.Should().Be("9203224800088");
        parsed.VaccinationCenter.Should().Be("Inkosi Albert Luthuli Hospital");
        parsed.IsValid.Should().BeTrue();
    }

    // ── Regression: five real-world examples from the seed data ──────────────

    [Theory]
    [InlineData("0105215359081:Groote Schuur Hospital:2024-01-15:PFZ-2024-001-A", 'A', "0105215359081")]
    [InlineData("BAR-00001:2024-02-12:Groote Schuur Hospital:0105215359081",       'B', "0105215359081")]
    [InlineData("8001015009087:Charlotte Maxeke Hospital:2024-01-20:JNJ-2024-002-A", 'A', "8001015009087")]
    [InlineData("9203224800088:Inkosi Albert Luthuli Hospital:2024-01-10:AZ-2024-003-A", 'A', "9203224800088")]
    [InlineData("BAR-00003:2024-02-07:Inkosi Albert Luthuli Hospital:9203224800088", 'B', "9203224800088")]
    public void Parse_SeedDataMessages_AllValid(string message, char expectedFormat, string expectedId)
    {
        var result = MessageParser.Parse(message);

        result.Should().NotBeNull();
        result!.IsValid.Should().BeTrue(because: $"'{message}' is a known-valid seed message");
        result.Format.Should().Be(expectedFormat);
        result.IDNumber.Should().Be(expectedId);
    }
}