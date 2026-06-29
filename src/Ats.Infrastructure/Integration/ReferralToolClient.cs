using System.Text;
using System.Text.Json;
using Ats.Application.Integration;

namespace Ats.Infrastructure.Integration;

public sealed class ReferralToolClient : IReferralToolClient
{
    private readonly HttpClient _http;
    public ReferralToolClient(HttpClient http) => _http = http;

    public async Task<(ReferralCallResult Result, bool Exists)> CheckVacancyExistsAsync(
        ReferralToolSettings settings, string externalVacancyId, CancellationToken ct = default)
    {
        using var request = Build(settings, "checkvacancyexists",
            new { CustomerId = settings.CustomerId, ExternalVacancyId = externalVacancyId });
        try
        {
            using var resp = await _http.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var exists = false;
            if (resp.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("exists", out var e) && e.ValueKind == JsonValueKind.True)
                        exists = true;
                }
                catch (JsonException) { }
            }
            return (new ReferralCallResult(true, (int)resp.StatusCode, body), exists);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (new ReferralCallResult(false, 0, ex.Message), false);
        }
    }

    public async Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest r, CancellationToken ct = default)
    {
        using var request = Build(settings, "candidatestatusupdate",
            new { r.CustomerId, r.Code, r.ExternalVacancyId, r.ExternalCandidateId, r.CandidateStatus });
        try
        {
            using var resp = await _http.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return new ReferralCallResult(true, (int)resp.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ReferralCallResult(false, 0, ex.Message);
        }
    }

    private static HttpRequestMessage Build(ReferralToolSettings s, string action, object payload)
    {
        var url = $"{s.BaseUrl.TrimEnd('/')}/v1.0/kafka/{action}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("X-Api-Key", s.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Auth-Token", s.AuthToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }
}
