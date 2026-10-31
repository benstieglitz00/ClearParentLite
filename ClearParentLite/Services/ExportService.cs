using ClearParentLite.Data;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using AngleSharp.Html.Parser;
using AngleSharp.Dom;
namespace ClearParentLite.Services;
public interface IExportService { byte[] Pdf(IReadOnlyList<TimelineEntry> entries,DateTime exportedUtc,CancellationToken ct); }
public class ExportService : IExportService
{
    public byte[] Pdf(IReadOnlyList<TimelineEntry> entries,DateTime exportedUtc,CancellationToken ct) {
        var doc=new MigraDoc.DocumentObjectModel.Document();doc.Info.Title="ClearParent Lite - Parenting timeline";doc.Info.Author="ClearParent Lite";
        var normal=doc.Styles[StyleNames.Normal]!;normal.Font.Name="Arial";normal.Font.Size=10;normal.ParagraphFormat.SpaceAfter=7;
        doc.Styles[StyleNames.Heading1]!.Font.Size=22;doc.Styles[StyleNames.Heading1]!.Font.Color=Colors.DarkSlateBlue;
        doc.Styles[StyleNames.Heading2]!.Font.Size=15;doc.Styles[StyleNames.Heading2]!.ParagraphFormat.SpaceBefore=15;
        var section=doc.AddSection();section.PageSetup.TopMargin=Unit.FromCentimeter(2);section.PageSetup.BottomMargin=Unit.FromCentimeter(2);section.PageSetup.LeftMargin=Unit.FromCentimeter(2);section.PageSetup.RightMargin=Unit.FromCentimeter(2);
        var footer=section.Footers.Primary.AddParagraph();footer.Format.Font.Size=8;footer.Format.Font.Color=Colors.Gray;footer.AddText("Private - ClearParent Lite | Page ");footer.AddPageField();footer.AddText(" of ");footer.AddNumPagesField();
        section.AddParagraph("Parenting timeline",StyleNames.Heading1);
        section.AddParagraph($"Exported {exportedUtc:yyyy-MM-dd HH:mm:ss} UTC | {entries.Count} entries");
        section.AddParagraph("Personal documentation. This report does not certify events or provide legal advice.").Format.Font.Size=8;
        foreach(var entry in entries) {
            ct.ThrowIfCancellationRequested();
            section.AddParagraph(entry.Title,StyleNames.Heading2);
            section.AddParagraph($"Occurred on {entry.EventDate:yyyy-MM-dd}").Format.Font.Bold=true;
            var audit=section.AddParagraph($"Recorded {entry.CreatedUtc:yyyy-MM-dd HH:mm:ss} UTC | Edited {entry.UpdatedUtc:yyyy-MM-dd HH:mm:ss} UTC\nEntry ID: {entry.Id}");audit.Format.Font.Size=8;audit.Format.Font.Color=Colors.Gray;
            AddHtml(section,entry.DescriptionHtml);
            foreach(var update in entry.Updates.Where(u=>!u.IsDeleted).OrderBy(u=>u.CreatedUtc).ThenBy(u=>u.Id)) {
                section.AddParagraph($"Update - {update.CreatedUtc:yyyy-MM-dd HH:mm:ss} UTC").Format.Font.Bold=true;AddHtml(section,update.BodyHtml);
            }
            if(entry.Attachments.Any(a=>!a.IsDeleted)) {
                section.AddParagraph("Attachments").Format.Font.Bold=true;
                foreach(var a in entry.Attachments.Where(a=>!a.IsDeleted)) section.AddParagraph($"{a.OriginalFileName} ({a.ByteLength:N0} bytes)\nSHA-256: {a.Sha256}").Format.Font.Size=8;
            }
            if(entry.Revisions.Count>0) {
                section.AddParagraph("Revision history").Format.Font.Bold=true;
                foreach(var r in entry.Revisions.OrderBy(r=>r.RevisionNumber)) {
                    section.AddParagraph($"Version {r.RevisionNumber} - {r.EditedUtc:yyyy-MM-dd HH:mm:ss} UTC | {r.Title} | Occurred {r.EventDate:yyyy-MM-dd}").Format.Font.Size=8;
                    if(!string.IsNullOrEmpty(r.ChangeReason)) section.AddParagraph(r.ChangeReason).Format.Font.Size=8;
                    AddHtml(section,r.DescriptionHtml);
                }
            }
        }
        var renderer=new PdfDocumentRenderer { Document=doc };renderer.RenderDocument();
        using var stream=new MemoryStream();renderer.PdfDocument.Save(stream,false);return stream.ToArray();
    }
    static void AddHtml(Section section,string html) {
        var body=new HtmlParser().ParseDocument(html).Body!;
        var buffer=new System.Text.StringBuilder();
        void Walk(INode node) {
            if(node is IText t) {buffer.Append(t.Data);return;}
            if(node is IElement e && e.LocalName=="img") {buffer.Append(" [Screenshot: see attachments] ");return;}
            foreach(var child in node.ChildNodes)Walk(child);
            if(node is IElement element && new[]{"p","li","h2","h3","br","blockquote","pre"}.Contains(element.LocalName))buffer.AppendLine();
        }
        Walk(body);
        foreach(var line in buffer.ToString().Split('\n'))if(!string.IsNullOrWhiteSpace(line))section.AddParagraph(line.Trim());
    }
}
