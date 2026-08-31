using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Ats.Web.TagHelpers;

// Material Symbols render as a text ligature (<span class="ms">arrow_back</span>), so a screen
// reader reads the icon's NAME: "Submit application arrow_forward". Decorative icons must therefore
// be hidden from assistive tech (WCAG 1.1.1 / 4.1.2).
//
// This applies aria-hidden="true" automatically to every .ms span, so it cannot be forgotten on new
// markup. An icon that carries standalone meaning opts out simply by labelling itself — set role or
// aria-label (or aria-hidden="false") and this leaves the element alone.
[HtmlTargetElement("span", Attributes = "class")]
public sealed class MaterialIconTagHelper : TagHelper
{
    // Run late so the class attribute reflects anything other helpers added.
    public override int Order => 1000;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!HasIconClass(output)) return;

        // Already described, or deliberately exposed: leave the author's intent intact.
        if (output.Attributes.ContainsName("aria-hidden")
            || output.Attributes.ContainsName("role")
            || output.Attributes.ContainsName("aria-label")
            || output.Attributes.ContainsName("aria-labelledby"))
        {
            return;
        }

        output.Attributes.SetAttribute("aria-hidden", "true");
    }

    private static bool HasIconClass(TagHelperOutput output)
    {
        var value = ClassValue(output);
        if (string.IsNullOrEmpty(value)) return false;

        // Token match, so "ms" and "ms-sm" match but "dismiss" or "ms-auto" alone do not.
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "ms") return true;
        }
        return false;
    }

    // On a validation-error postback MVC replaces the class attribute value with an IHtmlContent
    // (to add input-validation-error). Calling ToString() on that returns the TYPE NAME, not the
    // class text, so the value has to be rendered properly.
    private static string? ClassValue(TagHelperOutput output)
    {
        if (!output.Attributes.TryGetAttribute("class", out var attr)) return null;

        return attr.Value switch
        {
            string s => s,
            IHtmlContent html => Render(html),
            null => null,
            var other => other.ToString()
        };
    }

    private static string Render(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
