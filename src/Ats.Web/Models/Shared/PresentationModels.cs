using Ats.Domain.Enums;

namespace Ats.Web.Models.Shared;

public enum PillTone { Neutral, Success, Warning, Danger, Info }

public sealed record AvatarModel(string? Name, double SizeRem = 2.0, bool Ring = false);

public sealed record StatusPillModel(string Label, PillTone Tone, bool ShowDot = true);

public sealed record SourceChipModel(ApplicationOrigin Origin);

public sealed record StatTileModel(
    string Eyebrow,
    string Value,
    string? Unit = null,
    string? DeltaText = null,
    string? DeltaIcon = null,
    PillTone DeltaTone = PillTone.Neutral);

public sealed record PipelineSegment(string Label, int Count);

public sealed record PipelineBarModel(IReadOnlyList<PipelineSegment> Segments, bool ShowLabels = false);

public sealed record EmptyStateModel(string Icon, string Headline, string? Body = null);

public sealed record TimelineItem(string Title, string? Subtitle, bool IsCurrent = false);

public sealed record TimelineModel(IReadOnlyList<TimelineItem> Items);

public static class PillToneCss
{
    public static string Pill(PillTone tone) => tone switch
    {
        PillTone.Success => "ats-pill--success",
        PillTone.Warning => "ats-pill--warning",
        PillTone.Danger => "ats-pill--danger",
        PillTone.Info => "ats-pill--info",
        _ => "ats-pill--neutral"
    };

    public static string Ink(PillTone tone) => tone switch
    {
        PillTone.Success => "var(--no-success-ink)",
        PillTone.Warning => "var(--no-warning-ink)",
        PillTone.Danger => "var(--no-danger-ink)",
        PillTone.Info => "var(--no-info-ink)",
        _ => "var(--ats-ink-muted)"
    };
}
