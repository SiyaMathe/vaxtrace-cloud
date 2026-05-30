using System.Net;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace VaxTrace.Functions;

public class HttpIngestFunction
{
    private readonly ILogger<HttpIngestFunction> _log;

    public HttpIngestFunction(ILogger<HttpIngestFunction> log) => _log = log;

    [Function("HttpIngestVaccination")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "vaccination")] HttpRequestData req,
        FunctionContext context)
    {
        _log.LogInformation("POST /api/vaccination received at {Time}", DateTimeOffset.UtcNow);

        var body = await req.ReadAsStringAsync() ?? string.Empty;
        
        // 1. Empty body check
        if (string.IsNullOrWhiteSpace(body))
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new { error = "Request body cannot be empty." });
            return response;
        }

        string queueMessage;

        // ── Parsing Logic ──────────────────────────────────────────────────
        if (body.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                string? format = root.TryGetProperty("format", out var fProp) ? fProp.GetString() : null;

                if (string.Equals(format, "A", StringComparison.OrdinalIgnoreCase))
                {
                    queueMessage = $"{GetRequired(root, "id")}:{GetRequired(root, "vaccinationCenter")}:{GetRequired(root, "vaccinationDate")}:{GetRequired(root, "vaccineSerialNumber")}";
                }
                else if (string.Equals(format, "B", StringComparison.OrdinalIgnoreCase))
                {
                    queueMessage = $"{GetRequired(root, "vaccineBarcode")}:{GetRequired(root, "vaccinationDate")}:{GetRequired(root, "vaccinationCenter")}:{GetRequired(root, "id")}";
                }
                else
                {
                    queueMessage = root.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? body : body;
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteAsJsonAsync(new { error = "Malformed JSON or missing fields.", detail = ex.Message });
                return response;
            }
        }
        else
        {
            queueMessage = body.Trim();
        }

        // ── Validation ──────────────────────────────────────────────────────
        var parsed = MessageParser.Parse(queueMessage);
        if (parsed is null || !parsed.IsValid)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new { error = "Invalid format", detail = parsed?.ParseError });
            return response;
        }

        // ── Queueing ────────────────────────────────────────────────────────
        var storageConn = Environment.GetEnvironmentVariable("VaxTraceStorage") ?? "UseDevelopmentStorage=true";
        var queueClient = new QueueClient(storageConn, Environment.GetEnvironmentVariable("VaccinationQueueName") ?? "vaccination-queue", 
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

        await queueClient.CreateIfNotExistsAsync();
        var sendReceipt = await queueClient.SendMessageAsync(queueMessage);

        // ── HARDENED SUCCESS RESPONSE ───────────────────────────────────────
        var successResponse = req.CreateResponse(HttpStatusCode.Accepted); // Temporary change
        await successResponse.WriteAsJsonAsync(new
        {
            status = "queued",
            messageId = sendReceipt.Value.MessageId,
            detectedFormat = parsed.Format.ToString(),
            idNumber = parsed.IDNumber,
            center = parsed.VaccinationCenter,
            date = parsed.VaccinationDate?.ToString("yyyy-MM-dd"),
            message = "Record queued for processing."
        });

        return successResponse;
    }

    private static string GetRequired(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var prop) && prop.GetString() is { } val) return val;
        throw new ArgumentException($"Required field '{key}' is missing.");
    }
}