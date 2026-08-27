using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Ats.Web.TagHelpers;

// Renders a UTC instant as <time datetime="...Z" data-local="format"> with a UTC-labelled fallback.
// site.js rewrites the text to the viewer's own timezone (Intl.DateTimeFormat), so timestamps are
// correct per user regardless of the server's timezone. No-JS clients still see an unambiguous UTC
// value. Data stays UTC in the database; this is display-only.
//
// Usage: <local-time utc="@model.OccurredAt" format="short"></local-time>
// Always write an explicit end tag: a self-closing tag-helper element makes Razor swallow the
// markup that follows it.
// Formats: date | datetime | short | monthday | time | weekday (default datetime).
[HtmlTargetElement("local-time")]
public sealed class LocalTimeTagHelper : TagHelper
{
    public DateTimeOffset? Utc { get; set; }
    public string Format { get; set; } = "datetime";
    public string Empty { get; set; } = "—";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Utc is null)
        {
            output.TagName = "span";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Content.SetContent(Empty);
            return;
        }

        var utc = Utc.Value.ToUniversalTime();
        output.TagName = "time";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("datetime", utc.ToString("o"));
        output.Attributes.SetAttribute("data-local", Format);
        output.Content.SetContent(Fallback(utc, Format));
    }

    // UTC fallback for no-JS clients. Time-bearing formats carry a "UTC" label so they are unambiguous.
    private static string Fallback(DateTimeOffset utc, string format) => format switch
    {
        "weekday" => utc.ToString("dddd d MMMM"),
        "date" => utc.ToString("dd MMM yyyy"),
        "monthday" => utc.ToString("dd MMM"),
        "time" => utc.ToString("HH:mm") + " UTC",
        "short" => utc.ToString("dd/MM HH:mm") + " UTC",
        _ => utc.ToString("dd/MM/yyyy HH:mm") + " UTC",
    };
}
