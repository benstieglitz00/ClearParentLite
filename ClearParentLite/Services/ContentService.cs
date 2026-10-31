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
        s.AllowedAttributes.Clear(); s.AllowedAttributes.Add("src"); s.AllowedAttributes.Add("alt"); s.AllowedAttributes.Add("data-list"); s.AllowedCssProperties.Clear();
        var document = new HtmlParser().ParseDocument(s.Sanitize(html ?? ""));
        // Quill 2 represents both bullet and numbered lists as <ol> plus data-list.
        // Normalize to semantic HTML so read-only pages and exports retain the distinction.
        foreach(var list in document.QuerySelectorAll("ol").Reverse().ToArray()) {
            AngleSharp.Dom.IElement? group=null;
            foreach(var item in list.Children.ToArray()) {
                var tag=item.GetAttribute("data-list")=="bullet"?"ul":"ol";
                if(group?.LocalName!=tag) { group=document.CreateElement(tag);list.Parent!.InsertBefore(group,list); }
                group.AppendChild(item);
            }
            list.Remove();
        }
        foreach(var node in document.QuerySelectorAll("[data-list]"))node.RemoveAttribute("data-list");
        var allowed = (attachmentIds ?? []).ToHashSet();
        foreach (var img in document.QuerySelectorAll("img")) {
            var src = img.GetAttribute("src") ?? "";
            if (!src.StartsWith("/attachments/", StringComparison.Ordinal) || !Guid.TryParse(src[13..], out var id) || !allowed.Contains(id)) img.Remove();
        }
        return document.Body!.InnerHtml;
    }
    public string Text(string html) {
        var document=new HtmlParser().ParseDocument(html ?? "");
        foreach(var block in document.QuerySelectorAll("p,li,h2,h3,br,blockquote,pre"))block.AppendChild(document.CreateTextNode(" "));
        return System.Text.RegularExpressions.Regex.Replace(document.Body?.TextContent ?? "",@"\s+"," ").Trim();
    }
    public bool HasContent(string html) => !string.IsNullOrWhiteSpace(Text(html)) || html.Contains("<img ", StringComparison.Ordinal);
}
