namespace VaxTrace.Functions;

/// <summary>
/// Parses vaccination messages from two different provider formats:
///
/// Format A: Id:VaccinationCenter:VaccinationDate:VaccineSerialNumber
///   Example: 8001015009087:Groote Schuur Hospital:2024-01-15:PFZ-2024-001-A
///
/// Format B: VaccineBarcode:VaccinationDate:VaccinationCenter:Id
///   Example: BAR-00123:2024-01-15:Groote Schuur Hospital:8001015009087
/// </summary>
public static class MessageParser
{
    public static ParsedVaccinationMessage? Parse(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return null;

        // Messages use colon as separator but VaccinationCenter can contain colons
        // So we split on the first and last occurrence to handle center names safely
        var parts = rawMessage.Split(':');
        if (parts.Length < 4)
            return null;

        // ── Detect format by checking first field ─────────────────────────────
        // Format A: first field looks like a SA ID (13 digits) or passport
        // Format B: first field looks like a barcode (alphanumeric with hyphens)

        if (LooksLikeIdNumber(parts[0]))
        {
            return ParseFormatA(parts, rawMessage);
        }
        else if (LooksLikeIdNumber(parts[^1]))   // last field is the ID
        {
            return ParseFormatB(parts, rawMessage);
        }

        return new ParsedVaccinationMessage
        {
            Format         = 'U',
            RawMessage     = rawMessage,
            ParseError     = $"Cannot determine format — first part: '{parts[0]}', last part: '{parts[^1]}'"
        };
    }

    /// <summary>Format A: Id:Center:Date:SerialNumber</summary>
    private static ParsedVaccinationMessage ParseFormatA(string[] parts, string raw)
    {
        // Center can have colons in it, so everything between index 1 and the last
        // two parts (date and serial) is the center name
        if (parts.Length < 4)
            return new ParsedVaccinationMessage { Format = 'A', RawMessage = raw, ParseError = "Too few parts for Format A" };

        string serialNumber  = parts[^1];
        string dateStr       = parts[^2];
        string centerName    = string.Join(":", parts[1..^2]);
        string idNumber      = parts[0].Trim();

        if (!DateOnly.TryParse(dateStr, out var vacDate))
            return new ParsedVaccinationMessage { Format = 'A', RawMessage = raw, ParseError = $"Invalid date: {dateStr}" };

        return new ParsedVaccinationMessage
        {
            Format               = 'A',
            IDNumber             = idNumber,
            VaccinationCenter    = centerName.Trim(),
            VaccinationDate      = vacDate,
            VaccineSerialNumber  = serialNumber.Trim(),
            RawMessage           = raw
        };
    }

    /// <summary>Format B: Barcode:Date:Center:Id</summary>
    private static ParsedVaccinationMessage ParseFormatB(string[] parts, string raw)
    {
        if (parts.Length < 4)
            return new ParsedVaccinationMessage { Format = 'B', RawMessage = raw, ParseError = "Too few parts for Format B" };

        string idNumber    = parts[^1].Trim();
        string centerName  = string.Join(":", parts[2..^1]);
        string dateStr     = parts[1];
        string barcode     = parts[0].Trim();

        if (!DateOnly.TryParse(dateStr, out var vacDate))
            return new ParsedVaccinationMessage { Format = 'B', RawMessage = raw, ParseError = $"Invalid date: {dateStr}" };

        return new ParsedVaccinationMessage
        {
            Format             = 'B',
            IDNumber           = idNumber,
            VaccinationCenter  = centerName.Trim(),
            VaccinationDate    = vacDate,
            VaccineBarcode     = barcode,
            RawMessage         = raw
        };
    }

    private static bool LooksLikeIdNumber(string value)
    {
        var trimmed = value.Trim();
        // SA ID: exactly 13 digits
        if (trimmed.Length == 13 && trimmed.All(char.IsDigit))
            return true;
        // Passport: letter(s) followed by digits, 6-12 chars
        if (trimmed.Length is >= 6 and <= 12 &&
            trimmed.Length > 0 && char.IsLetter(trimmed[0]) &&
            trimmed[1..].All(char.IsLetterOrDigit))
            return true;
        return false;
    }
}

public class ParsedVaccinationMessage
{
    public char      Format               { get; init; } = 'U';
    public string?   IDNumber             { get; init; }
    public string?   IDType               { get; init; } = "SA_ID";
    public string?   VaccinationCenter    { get; init; }
    public DateOnly? VaccinationDate      { get; init; }
    public string?   VaccineSerialNumber  { get; init; }
    public string?   VaccineBarcode       { get; init; }
    public string    RawMessage           { get; init; } = string.Empty;
    public string?   ParseError           { get; init; }

    public bool IsValid =>
        ParseError is null &&
        !string.IsNullOrWhiteSpace(IDNumber) &&
        VaccinationDate.HasValue &&
        !string.IsNullOrWhiteSpace(VaccinationCenter);

    /// <summary>Compose the canonical queue message string for Format A</summary>
    public static string BuildFormatA(string id, string center, DateOnly date, string serial)
        => $"{id}:{center}:{date:yyyy-MM-dd}:{serial}";

    /// <summary>Compose the canonical queue message string for Format B</summary>
    public static string BuildFormatB(string barcode, DateOnly date, string center, string id)
        => $"{barcode}:{date:yyyy-MM-dd}:{center}:{id}";
}
