using AngleSharp.Html.Parser;
using Ganss.Xss;
namespace ClearParentLite.Services;
public class ContentService
{
    public string Sanitize(string html, IEnumerable<Guid>? attachmentIds = null)
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "blockquote", "ol", "ul", "li", "h2", "h3", "pre", "code", "img" }) s.AllowedTags.Add(tag);
        s.AllowedAttributes.Clear(); s.AllowedAttributes.Add("src"); s.AllowedAttributes.Add("alt"); s.AllowedCssProperties.Clear();
        var document = new HtmlParser().ParseDocument(s.Sanitize(html));
        var allowed = (attachmentIds ?? []).ToHashSet();
        foreach (var img in document.QuerySelectorAll("img")) {
            var src = img.GetAttribute("src") ?? "";
            if (!src.StartsWith("/attachments/", StringComparison.Ordinal) || !Guid.TryParse(src[13..], out var id) || !allowed.Contains(id)) img.Remove();
        }
        return document.Body!.InnerHtml;
    }
    public string Text(string html) => new HtmlParser().ParseDocument(html).Body?.TextContent.Trim() ?? "";
    public bool HasContent(string html) => !string.IsNullOrWhiteSpace(Text(html)) || html.Contains("<img ", StringComparison.Ordinal);
}
