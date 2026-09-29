using System.Text;
using System.Text.Json;
using Ats.Application.Integration;

namespace Ats.Infrastructure.Integration;

public sealed class ReferralToolClient : IReferralToolClient
{
    private readonly HttpClient _http;
    public ReferralToolClient(HttpClient http) => _http = http;

    public async Task<(ReferralCallResult Result, bool? Exists)> CheckVacancyExistsAsync(
        ReferralToolSettings settings, string externalVacancyId, CancellationToken ct = default)
    {
        var result = await SendAsync(settings, HttpMethod.Post, "kafka/checkvacancyexists",
            new { CustomerId = settings.CustomerId, ExternalVacancyId = externalVacancyId }, ct);
        var exists = result.Reached && result.HttpStatus is >= 200 and < 300
            ? ReferralToolRules.ReadVacancyExists(result.Body)
            : null;
        return (result, exists);
    }

    public Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest r, CancellationToken ct = default) =>
        SendAsync(settings, HttpMethod.Post, "kafka/candidatestatusupdate",
            new { r.CustomerId, r.Code, r.ExternalVacancyId, r.ExternalCandidateId, r.CandidateStatus }, ct);

    public Task<ReferralCallResult> CreateVacancyAsync(
        ReferralToolSettings settings, VacancyPayload v, CancellationToken ct = default) =>
        SendAsync(settings, HttpMethod.Post, "vacancy",
            new { v.Id, v.Title, v.Url, v.Location, v.EmploymentType, v.Categories }, ct);

    public Task<ReferralCallResult> UpdateVacancyAsync(
        ReferralToolSettings settings, VacancyPayload v, CancellationToken ct = default) =>
        SendAsync(settings, HttpMethod.Put, $"vacancy/{Uri.EscapeDataString(v.Id)}",
            new { v.Title, v.Url, v.Inactive, v.Location, v.EmploymentType, v.Categories }, ct);

    private async Task<ReferralCallResult> SendAsync(
        ReferralToolSettings settings, HttpMethod method, string path, object payload, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return new ReferralCallResult(false, 0, "Cancelled before sending.", MaybeSent: false);
        try
        {
            using var request = Build(settings, method, path, payload);
            using var resp = await _http.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return new ReferralCallResult(true, (int)resp.StatusCode, body);
        }
        catch (Exception ex) when (ReferralToolRules.IsDefinitelyNotSent(ex))
        {
            return new ReferralCallResult(false, 0, ex.Message, MaybeSent: false);
        }
        catch (Exception ex) when (ReferralToolRules.IsTransportFailure(ex))
        {
            return new ReferralCallResult(false, 0, ex.Message);
        }
    }

    private static HttpRequestMessage Build(ReferralToolSettings s, HttpMethod method, string path, object payload)
    {
        var request = new HttpRequestMessage(method, $"{s.BaseUrl.TrimEnd('/')}/v1.0/{path}");
        request.Headers.TryAddWithoutValidation("X-Api-Key", s.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Auth-Token", s.AuthToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }
}
