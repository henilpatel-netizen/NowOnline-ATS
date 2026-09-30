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

// SubtitleUtc, when set, is rendered as a viewer-timezone timestamp instead of the literal Subtitle.
public sealed record TimelineItem(string Title, string? Subtitle, bool IsCurrent = false, DateTimeOffset? SubtitleUtc = null);

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

    // Text-colour class for the tone. Bootstrap's .text-* are mapped to the NowOnline inks.
    public static string Ink(PillTone tone) => tone switch
    {
        PillTone.Success => "text-success",
        PillTone.Warning => "text-warning",
        PillTone.Danger => "text-danger",
        PillTone.Info => "text-info",
        _ => "ats-ink-muted"
    };
}
