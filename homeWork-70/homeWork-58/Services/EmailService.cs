using MailKit.Net.Smtp;
using MimeKit;

namespace InstagramApp.Services;

public class EmailService
{
    public async Task<bool> SendEmailAsync(string? email, string? subject, string? text)
    {
        try
        {
            var emailMessage = new MimeMessage();
            emailMessage.From.Add(new MailboxAddress("Instagram App", "lesson70nrs@mail.ru"));
            emailMessage.To.Add(new MailboxAddress("", email));
            emailMessage.Subject = subject;
            emailMessage.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = text };

            using (var client = new SmtpClient())
            {
                await client.ConnectAsync("smtp.mail.ru", 465, true);
                await client.AuthenticateAsync("lesson70nrs@mail.ru", "GUXn4x3VqEmm60alMesP");
                await client.SendAsync(emailMessage);
                await client.DisconnectAsync(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Не удалось отправить письмо на {email}: {ex.Message}");
            return false;
        }
    }
}