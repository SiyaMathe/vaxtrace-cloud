using System;
using System.Linq;

namespace VaxTrace.Functions;

// ── Data Model ────────────────────────────────────────────────────────────

public class ParsedVaccinationMessage
{
    public char        Format              { get; init; } = 'U';
    public string?     IDNumber            { get; init; }
    public string?     IDType              { get; init; } = "SA_ID";
    public string?     VaccinationCenter   { get; init; }
    public DateOnly?   VaccinationDate     { get; init; }
    public string?     VaccineSerialNumber { get; init; }
    public string?     VaccineBarcode      { get; init; }
    public string      RawMessage          { get; init; } = string.Empty;
    public string?     ParseError          { get; init; }

    public bool IsValid =>
        ParseError is null &&
        !string.IsNullOrWhiteSpace(IDNumber) &&
        VaccinationDate.HasValue &&
        !string.IsNullOrWhiteSpace(VaccinationCenter);
}

// ── Parser Logic ──────────────────────────────────────────────────────────

public static class MessageParser
{
    public static ParsedVaccinationMessage? Parse(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return null;

        var parts = rawMessage.Split(':');
        if (parts.Length < 4)
            return null;

        if (LooksLikeIdNumber(parts[0]))
        {
            return ParseFormatA(parts, rawMessage);
        }
        else if (LooksLikeIdNumber(parts[^1]))
        {
            return ParseFormatB(parts, rawMessage);
        }

        return new ParsedVaccinationMessage
        {
            Format = 'U',
            RawMessage = rawMessage,
            ParseError = $"Cannot determine format — first part: '{parts[0]}', last part: '{parts[^1]}'"
        };
    }

    // ── Builder Methods (called by Tests) ─────────────────────────────────────

    public static string BuildFormatA(string id, string center, DateOnly date, string serial)
        => $"{id}:{center}:{date:yyyy-MM-dd}:{serial}";

    public static string BuildFormatB(string barcode, DateOnly date, string center, string id)
        => $"{barcode}:{date:yyyy-MM-dd}:{center}:{id}";

    // ── Private Parsers ───────────────────────────────────────────────────────

    private static ParsedVaccinationMessage ParseFormatA(string[] parts, string raw)
    {
        if (parts.Length < 4)
            return new ParsedVaccinationMessage { Format = 'A', RawMessage = raw, ParseError = "Too few parts for Format A" };

        string serialNumber = parts[^1];
        string dateStr = parts[^2];
        string centerName = string.Join(":", parts[1..^2]);
        string idNumber = parts[0].Trim();

        if (!DateOnly.TryParse(dateStr, out var vacDate))
            return new ParsedVaccinationMessage { Format = 'A', RawMessage = raw, ParseError = $"Invalid date: {dateStr}" };

        return new ParsedVaccinationMessage
        {
            Format = 'A',
            IDNumber = idNumber,
            VaccinationCenter = centerName.Trim(),
            VaccinationDate = vacDate,
            VaccineSerialNumber = serialNumber.Trim(),
            RawMessage = raw
        };
    }

    private static ParsedVaccinationMessage ParseFormatB(string[] parts, string raw)
    {
        if (parts.Length < 4)
            return new ParsedVaccinationMessage { Format = 'B', RawMessage = raw, ParseError = "Too few parts for Format B" };

        string idNumber = parts[^1].Trim();
        string centerName = string.Join(":", parts[2..^1]);
        string dateStr = parts[1];
        string barcode = parts[0].Trim();

        if (!DateOnly.TryParse(dateStr, out var vacDate))
            return new ParsedVaccinationMessage { Format = 'B', RawMessage = raw, ParseError = $"Invalid date: {dateStr}" };

        return new ParsedVaccinationMessage
        {
            Format = 'B',
            IDNumber = idNumber,
            VaccinationCenter = centerName.Trim(),
            VaccinationDate = vacDate,
            VaccineBarcode = barcode,
            RawMessage = raw
        };
    }

    private static bool LooksLikeIdNumber(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 13 && trimmed.All(char.IsDigit))
            return true;
        if (trimmed.Length is >= 6 and <= 12 &&
            trimmed.Length > 0 && char.IsLetter(trimmed[0]) &&
            trimmed[1..].All(char.IsLetterOrDigit))
            return true;
        return false;
    }
}