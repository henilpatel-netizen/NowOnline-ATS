using System.Linq.Expressions;
using System.Text.Json;
using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public static class ReferralToolRules
{
    // The contract documents ReferralTool's duplicate guard but not its response, so this decides on
    // the status code alone. The payload is a frozen snapshot, so a validation 4xx is deterministic and
    // shows on the first send ReferralTool processes; any other 4xx after an attempt it may have
    // recorded (see MayHaveBeenProcessed) is the guard rejecting a re-send. Auth, timeout and
    // rate-limit answers say nothing about the event, so they are retried whatever happened before.
    public static OutboxOutcome ClassifyStatusUpdate(bool reached, int httpStatus, bool hadPossiblyProcessedAttempt)
    {
        if (!reached || httpStatus >= 500) return OutboxOutcome.Transient;
        if (httpStatus is >= 200 and < 300) return OutboxOutcome.Delivered;
        if (IsCredentialRejection(httpStatus)) return OutboxOutcome.Postpone;
        if (httpStatus is 408 or 429) return OutboxOutcome.Transient;
        if (httpStatus is >= 400 and < 500 && hadPossiblyProcessedAttempt) return OutboxOutcome.Delivered;
        return OutboxOutcome.Failed;
    }

    // ReferralTool refused the X-Api-Key / X-Auth-Token: a settings problem the owner fixes, so the
    // message waits without spending attempts (as with SettingsProblem) instead of dead-lettering.
    public static bool IsCredentialRejection(int httpStatus) => httpStatus is 401 or 403;

    // An earlier status-update attempt ReferralTool may have recorded: accepted (2xx), failed on its
    // side (5xx), or no reply on file (null: may have been sent, or the worker stopped before recording
    // it). HttpStatus 0 means definitely not sent and does not count. Translated to SQL by EF.
    public static Expression<Func<WebhookDelivery, bool>> MayHaveBeenProcessed { get; } =
        d => d.HttpStatus == null || (d.HttpStatus >= 200 && d.HttpStatus < 300) || d.HttpStatus >= 500;

    // Failures that happen before any request byte reaches ReferralTool.
    public static bool IsDefinitelyNotSent(Exception ex) =>
        ex is UriFormatException
        || ex is HttpRequestException
        {
            HttpRequestError: HttpRequestError.ConnectionError
                or HttpRequestError.NameResolutionError
                or HttpRequestError.SecureConnectionError
        };

    // Failures the client turns into an unreached, possibly-sent result instead of throwing; the client
    // checks IsDefinitelyNotSent first. IOException covers a reply cut off mid-read (HttpIOException),
    // which is not an HttpRequestException.
    public static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or IOException;

    // The WebhookDelivery.HttpStatus to store: the reply's status, null when the request may have been
    // sent without a reply, 0 when it definitely was not sent (so MayHaveBeenProcessed excludes it).
    public static int? RecordedStatus(ReferralCallResult result) =>
        result.Reached ? result.HttpStatus : result.MaybeSent ? null : 0;

    // Why these settings cannot reach ReferralTool, ignoring the enabled switch so an owner can test
    // before switching the integration on. Null when they can.
    public static string? ConnectionProblem(TenantSettings s)
    {
        if (s.ReferralToolCustomerId is null
            || string.IsNullOrWhiteSpace(s.ReferralToolBaseUrl)
            || string.IsNullOrWhiteSpace(s.ReferralToolApiKey)
            || string.IsNullOrWhiteSpace(s.ReferralToolAuthToken))
            return "Integration settings are incomplete: fill in the base URL, customer id, X-Api-Key and X-Auth-Token.";
        return ReferralToolBaseUrl.Validate(s.ReferralToolBaseUrl);
    }

    // Why the worker cannot deliver with these settings, or null when it can.
    public static string? SettingsProblem(TenantSettings? s) =>
        s is null || !s.IntegrationEnabled ? "Integration is disabled." : ConnectionProblem(s);

    // Why an enabled integration postpones every message and so never produces a Failed one: unusable
    // settings, or ReferralTool refused the credentials on the tenant's latest delivery attempt (any
    // kind; a later accepted attempt clears it). Null when it can deliver or is switched off on purpose.
    public static string? BlockedReason(TenantSettings? s, int? latestDeliveryStatus)
    {
        if (s is null || !s.IntegrationEnabled) return null;
        if (ConnectionProblem(s) is { } problem) return problem;
        return latestDeliveryStatus is { } status && IsCredentialRejection(status)
            ? "ReferralTool rejected the X-Api-Key or X-Auth-Token; check the credentials in the integration settings."
            : null;
    }

    // Reads the checkvacancyexists reply ({ "exists": bool }). Null means the body is not that shape.
    public static bool? ReadVacancyExists(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("exists", out var e))
                return null;
            return e.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
