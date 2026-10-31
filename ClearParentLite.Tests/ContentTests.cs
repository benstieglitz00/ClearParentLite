using ClearParentLite.Services;
namespace ClearParentLite.Tests;
public class ContentTests
{
    [Fact] public void SanitizerRemovesActiveContentAndTracking() {
        var service=new ContentService();
        var clean=service.Sanitize("<p onclick='alert(1)'>Hello <strong>parent</strong></p><script>alert(1)</script><img src='https://tracker.example/pixel'><svg onload='alert(2)'></svg><a href='javascript:alert(1)'>bad</a>");
        Assert.DoesNotContain("onclick",clean); Assert.DoesNotContain("<script",clean); Assert.DoesNotContain("https:",clean); Assert.DoesNotContain("<svg",clean); Assert.DoesNotContain("javascript:",clean); Assert.Contains("<strong>parent</strong>",clean);
    }
    [Fact] public void OnlyOwnedAttachmentImagesSurvive() {
        var id=Guid.NewGuid(); var other=Guid.NewGuid();
        var clean=new ContentService().Sanitize($"<img src='/attachments/{id}'><img src='/attachments/{other}'><img src='data:image/png;base64,aaa'>",[id]);
        Assert.Contains(id.ToString(),clean); Assert.DoesNotContain(other.ToString(),clean); Assert.DoesNotContain("data:",clean);
    }
    [Theory] [InlineData("attack.svg","<svg/>")] [InlineData("fake.pdf","MZ executable")] [InlineData("a.html","<html>")] [InlineData("evil.pdf","%PDF-1.4 /JavaScript")]
    public void UnsafeFilesAreRejected(string name,string bytes) => Assert.Throws<InvalidDataException>(()=>PrivateStorage.Validate(name,System.Text.Encoding.UTF8.GetBytes(bytes)));
    [Fact] public void TokensAreRandomAndHashed() { var token=AccessService.NewToken(); Assert.Equal(64,token.Length); Assert.NotEqual(token,AccessService.NewToken()); Assert.NotEqual(token,AccessService.Hash(token)); }
}
