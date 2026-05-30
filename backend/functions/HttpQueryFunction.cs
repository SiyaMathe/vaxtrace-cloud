using System.Net;
using System.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace VaxTrace.Functions;

public class HttpQueryFunction
{
    private readonly ILogger<HttpQueryFunction> _log;

    public HttpQueryFunction(ILogger<HttpQueryFunction> log) => _log = log;

    [Function("HttpQueryVaccination")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "vaccination/{id}")]
        HttpRequestData req,
        string id,
        FunctionContext context)
    {
        _log.LogInformation("GET /api/vaccination/{ID} at {Time}", id, DateTimeOffset.UtcNow);

        if (string.IsNullOrWhiteSpace(id) || id.Length < 6)
        {
            // FIX: Pass status code directly to CreateResponse
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteAsJsonAsync(new { error = "Invalid ID number provided." });
            return badReq;
        }

        var sqlConn = Environment.GetEnvironmentVariable("SqlConnectionString")!;
        await using var connection = new SqlConnection(sqlConn);
        await connection.OpenAsync();

        await using var cmd = new SqlCommand("dbo.usp_GetVaccinationStatus", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 10
        };
        cmd.Parameters.AddWithValue("@IDNumber", id.Trim());

        PersonSummary? person = null;
        var doses = new List<DoseRecord>();

        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            string rawFullyVaccinated = reader.IsDBNull(8) ? "0" : reader.GetValue(8).ToString() ?? "0";
            bool isFullyVaccinatedParsed = rawFullyVaccinated == "1" || rawFullyVaccinated.Equals("true", StringComparison.OrdinalIgnoreCase);

            person = new PersonSummary(
                PersonID: reader.GetInt32(0),
                IDNumber: reader.GetString(1),
                IDType: reader.GetString(2),
                FirstName: reader.IsDBNull(3) ? null : reader.GetString(3),
                LastName: reader.IsDBNull(4) ? null : reader.GetString(4),
                TotalDoses: reader.GetInt32(5),
                FirstDoseDate: reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
                LatestDoseDate: reader.IsDBNull(7) ? null : DateOnly.FromDateTime(reader.GetDateTime(7)),
                IsFullyVaccinated: isFullyVaccinatedParsed, 
                DaysSinceLastDose: reader.IsDBNull(9) ? null : reader.GetInt32(9)
            );
        }
        
        if (person is null || person.PersonID == 0)
        {
            // FIX: Pass status code directly to CreateResponse
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { status = "NOT_FOUND", id = id, message = "No vaccination records found." });
            return notFound;
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            string rawIsVerified = reader.IsDBNull(7) ? "0" : reader.GetValue(7).ToString() ?? "0";
            bool isVerifiedParsed = rawIsVerified == "1" || rawIsVerified.Equals("true", StringComparison.OrdinalIgnoreCase);

            doses.Add(new DoseRecord(
                reader.GetInt32(0), reader.GetByte(1), DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6), isVerifiedParsed, reader.GetDateTime(8)
            ));
        }

        // FIX: Pass status code directly to CreateResponse
        var response = req.CreateResponse(HttpStatusCode.OK);
        
        await response.WriteAsJsonAsync(new
        {
            status = person.IsFullyVaccinated ? "FULLY_VACCINATED" : person.TotalDoses > 0 ? "PARTIALLY_VACCINATED" : "NOT_VACCINATED",
            idNumber = person.IDNumber,
            idType = person.IDType,
            name = person.FirstName is not null ? $"{person.FirstName} {person.LastName}".Trim() : null,
            vaccination = new { totalDoses = person.TotalDoses, isFullyVaccinated = person.IsFullyVaccinated, firstDoseDate = person.FirstDoseDate?.ToString("yyyy-MM-dd"), latestDoseDate = person.LatestDoseDate?.ToString("yyyy-MM-dd"), daysSinceLastDose = person.DaysSinceLastDose },
            doses = doses.Select(d => new { doseNumber = d.DoseNumber, vaccinationDate = d.VaccinationDate.ToString("yyyy-MM-dd"), vaccinationCenter = d.VaccinationCenter, serialNumber = d.SerialNumber, barcode = d.Barcode, providerFormat = d.ProviderFormat, isVerified = d.IsVerified, processedAt = d.ProcessedAt }),
            queriedAt = DateTimeOffset.UtcNow
        });

        return response;
    }

    private record PersonSummary(int PersonID, string IDNumber, string IDType, string? FirstName, string? LastName, int TotalDoses, DateOnly? FirstDoseDate, DateOnly? LatestDoseDate, bool IsFullyVaccinated, int? DaysSinceLastDose);
    private record DoseRecord(int RecordID, byte DoseNumber, DateOnly VaccinationDate, string? VaccinationCenter, string? SerialNumber, string? Barcode, string ProviderFormat, bool IsVerified, DateTime ProcessedAt);
}