using System.Net;
using System.Net.Mail;
namespace ClearParentLite.Services;
public interface IEmailService { Task Send(string to, string subject, string text); }
public class EmailService(IConfiguration config, IWebHostEnvironment environment) : IEmailService
{
    public async Task Send(string to, string subject, string text) {
        using var message = new MailMessage(config["Email:From"] ?? "no-reply@clearparent.local", to, subject, text);
        using var smtp = new SmtpClient();
        if (string.IsNullOrWhiteSpace(config["Email:Host"])) {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Configure Email:Host and Email:From before enabling accounts.");
            var root = Path.Combine(environment.ContentRootPath, "App_Data", "mail"); Directory.CreateDirectory(root);
            smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory; smtp.PickupDirectoryLocation = root;
        } else {
            smtp.Host = config["Email:Host"]!; smtp.Port = config.GetValue("Email:Port", 587); smtp.EnableSsl = true;
            if (!string.IsNullOrEmpty(config["Email:Username"])) smtp.Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]);
        }
        message.BodyEncoding = System.Text.Encoding.UTF8; await smtp.SendMailAsync(message);
    }
}
