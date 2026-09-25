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
        var result = await PostAsync(settings, "checkvacancyexists",
            new { CustomerId = settings.CustomerId, ExternalVacancyId = externalVacancyId }, ct);
        var exists = result.Reached && result.HttpStatus is >= 200 and < 300
            ? ReferralToolRules.ReadVacancyExists(result.Body)
            : null;
        return (result, exists);
    }

    public Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest r, CancellationToken ct = default) =>
        PostAsync(settings, "candidatestatusupdate",
            new { r.CustomerId, r.Code, r.ExternalVacancyId, r.ExternalCandidateId, r.CandidateStatus }, ct);

    private async Task<ReferralCallResult> PostAsync(
        ReferralToolSettings settings, string action, object payload, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return new ReferralCallResult(false, 0, "Cancelled before sending.", MaybeSent: false);
        try
        {
            using var request = Build(settings, action, payload);
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
