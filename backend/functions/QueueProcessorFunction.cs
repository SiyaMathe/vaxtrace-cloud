using System.Data;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace VaxTrace.Functions;

/// <summary>
/// Queue-triggered Azure Function — POE Part 2 core requirement.
///
/// Fires automatically when a message is placed on "vaccination-queue".
/// Pipeline:
///   1. Parse the message (Format A or B)
///   2. Log to QueueMessageLog (SQL)
///   3. Archive raw JSON to Blob Storage
///   4. Upsert VaccinationRecord to SQL via stored procedure
///   5. Update QueueMessageLog with result
///
/// On failure: Azure retries up to maxDequeueCount (5) times,
/// then moves to the dead-letter queue automatically.
/// </summary>
public class QueueProcessorFunction
{
    private readonly ILogger<QueueProcessorFunction> _log;

    public QueueProcessorFunction(ILogger<QueueProcessorFunction> log) => _log = log;

    [Function("QueueProcessorFunction")]
    public async Task Run(
        [QueueTrigger("vaccination-queue", Connection = "VaxTraceStorage")]
        string rawMessage,
        FunctionContext context)
    {
        _log.LogInformation("Queue trigger fired — processing message at {Time}", DateTimeOffset.UtcNow);

        var sqlConn    = Environment.GetEnvironmentVariable("SqlConnectionString")!;
        var storageConn = Environment.GetEnvironmentVariable("VaxTraceStorage")
            ?? "UseDevelopmentStorage=true";
        var blobContainer = Environment.GetEnvironmentVariable("VaccinationBlobContainer")
            ?? "vaccination-raw-archive";

        int logId   = 0;
        int recordId = 0;

        await using var connection = new SqlConnection(sqlConn);
        await connection.OpenAsync();

        // ── Step 1: Parse the message ─────────────────────────────────────────
        var parsed = MessageParser.Parse(rawMessage);

        // ── Step 2: Log the incoming message to SQL ───────────────────────────
        logId = await LogMessageAsync(connection, rawMessage, parsed);

        if (parsed is null || !parsed.IsValid)
        {
            _log.LogWarning("Failed to parse message: {Error}", parsed?.ParseError ?? "null parse result");
            await UpdateLogAsync(connection, logId, "FAILED", null,
                parsed?.ParseError ?? "Parse returned null");
            return;   // Don't throw — let Azure move to dead-letter after max retries
        }

        // ── Step 3: Archive raw message to Blob Storage ───────────────────────
        string blobPath = string.Empty;
        try
        {
            blobPath = await ArchiveToBlobAsync(storageConn, blobContainer, parsed, rawMessage);
            _log.LogInformation("Archived to blob: {BlobPath}", blobPath);
        }
        catch (Exception ex)
        {
            // Blob failure is non-fatal — continue to SQL insert
            _log.LogWarning("Blob archive failed (non-fatal): {Error}", ex.Message);
        }

        // ── Step 4: Upsert to SQL via stored procedure ────────────────────────
        try
        {
            bool isNew;
            (recordId, isNew) = await UpsertVaccinationRecordAsync(
                connection, parsed, blobPath);

            string outcome = isNew ? "SUCCESS" : "DUPLICATE";
            await UpdateLogAsync(connection, logId, outcome, recordId, null);

            _log.LogInformation(
                "{Outcome} — ID: {IDNumber}, RecordID: {RecordID}, Format: {Format}",
                outcome, parsed.IDNumber, recordId, parsed.Format);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SQL upsert failed for IDNumber: {ID}", parsed.IDNumber);
            await UpdateLogAsync(connection, logId, "FAILED", null, ex.Message);
            throw;  // Re-throw so Azure retries the queue message
        }
    }

    // ── Helper: archive raw JSON payload to Blob Storage ─────────────────────
    private static async Task<string> ArchiveToBlobAsync(
        string storageConn, string containerName,
        ParsedVaccinationMessage parsed, string rawMessage)
    {
        var blobServiceClient   = new BlobServiceClient(storageConn);
        var containerClient     = blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync();

        // Blob path: year/month/day/format/idnumber_timestamp.json
        var now      = DateTimeOffset.UtcNow;
        var blobName = $"{now:yyyy/MM/dd}/format{parsed.Format}/" +
                       $"{parsed.IDNumber}_{now:HHmmss_fff}.json";

        var payload = JsonSerializer.Serialize(new
        {
            rawMessage       = rawMessage,
            parsedFormat     = parsed.Format.ToString(),
            idNumber         = parsed.IDNumber,
            vaccinationCenter = parsed.VaccinationCenter,
            vaccinationDate  = parsed.VaccinationDate?.ToString("yyyy-MM-dd"),
            serialNumber     = parsed.VaccineSerialNumber,
            barcode          = parsed.VaccineBarcode,
            archivedAt       = now
        }, new JsonSerializerOptions { WriteIndented = true });

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(payload)),
            overwrite: true);

        return blobName;
    }

    // ── Helper: call usp_UpsertVaccinationRecord ──────────────────────────────
    private static async Task<(int RecordId, bool IsNew)> UpsertVaccinationRecordAsync(
        SqlConnection conn, ParsedVaccinationMessage parsed, string blobPath)
    {
        await using var cmd = new SqlCommand("dbo.usp_UpsertVaccinationRecord", conn)
        {
            CommandType    = CommandType.StoredProcedure,
            CommandTimeout = 30
        };

        cmd.Parameters.AddWithValue("@IDNumber",            parsed.IDNumber!);
        cmd.Parameters.AddWithValue("@IDType",              parsed.IDType ?? "SA_ID");
        cmd.Parameters.AddWithValue("@CenterNameRaw",       parsed.VaccinationCenter!);
        cmd.Parameters.AddWithValue("@VaccineSerialNumber", (object?)parsed.VaccineSerialNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VaccineBarcode",      (object?)parsed.VaccineBarcode      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VaccinationDate",     parsed.VaccinationDate!.Value.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@MessageFormat",       parsed.Format.ToString());
        cmd.Parameters.AddWithValue("@RawMessage",          parsed.RawMessage);
        cmd.Parameters.AddWithValue("@BlobPath",            string.IsNullOrEmpty(blobPath) ? DBNull.Value : blobPath);

        var recordIdParam = cmd.Parameters.Add("@RecordID", SqlDbType.Int);
        var isNewParam    = cmd.Parameters.Add("@IsNew",    SqlDbType.Bit);
        recordIdParam.Direction = ParameterDirection.Output;
        isNewParam.Direction    = ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();

        return ((int)recordIdParam.Value, (bool)isNewParam.Value);
    }

    // ── Helper: log message to QueueMessageLog ────────────────────────────────
    private static async Task<int> LogMessageAsync(
        SqlConnection conn, string rawMessage, ParsedVaccinationMessage? parsed)
    {
        await using var cmd = new SqlCommand("dbo.usp_LogQueueMessage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@RawMessage",     rawMessage.Length > 1000
            ? rawMessage[..1000] : rawMessage);
        cmd.Parameters.AddWithValue("@MessageFormat",  parsed is not null
            ? parsed.Format.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("@ParsedIDNumber", (object?)parsed?.IDNumber ?? DBNull.Value);

        var logIdParam      = cmd.Parameters.Add("@LogID", SqlDbType.Int);
        logIdParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (int)logIdParam.Value;
    }

    // ── Helper: update log status ─────────────────────────────────────────────
    private static async Task UpdateLogAsync(
        SqlConnection conn, int logId, string status, int? recordId, string? error)
    {
        await using var cmd = new SqlCommand("dbo.usp_UpdateQueueMessageLog", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@LogID",        logId);
        cmd.Parameters.AddWithValue("@Status",       status);
        cmd.Parameters.AddWithValue("@RecordID",     (object?)recordId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ErrorMessage", (object?)error    ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
