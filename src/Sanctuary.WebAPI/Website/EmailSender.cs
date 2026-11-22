using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sanctuary.WebAPI.Website;

public sealed class EmailOptions
{
    public const string Section = "Email";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string FromName { get; set; } = "Open Source Free Realms";
    public bool EnableSsl { get; set; } = true;
    public string? OutboxFolder { get; set; }
}

public sealed class EmailSender
{
    private readonly ILogger<EmailSender> _logger;
    private readonly IOptionsMonitor<EmailOptions> _options;

    public EmailSender(ILogger<EmailSender> logger, IOptionsMonitor<EmailOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public bool IsConfigured
    {
        get
        {
            var options = _options.CurrentValue;

            return !string.IsNullOrWhiteSpace(options.Host)
                && !string.IsNullOrWhiteSpace(options.From)
                && (string.IsNullOrWhiteSpace(options.Username) || !string.IsNullOrWhiteSpace(options.Password));
        }
    }

    public async Task<bool> SendAsync(string to, string subject, string text, string html, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        if (!IsConfigured)
            return await SaveToOutboxAsync(options, to, subject, text, cancellationToken);

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(options.From!, options.FromName),
                Subject = subject,
                Body = text,
                IsBodyHtml = false
            };

            message.To.Add(new MailAddress(to));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, null, MediaTypeNames.Text.Html));

            using var client = new SmtpClient(options.Host!, options.Port)
            {
                EnableSsl = options.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = string.IsNullOrWhiteSpace(options.Username) ? null : new NetworkCredential(options.Username, options.Password)
            };

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation("Sent e-mail \"{Subject}\".", subject);

            return true;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            _logger.LogError(ex, "Could not send e-mail \"{Subject}\".", subject);

            return false;
        }
    }

    private async Task<bool> SaveToOutboxAsync(EmailOptions options, string to, string subject, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.OutboxFolder))
        {
            _logger.LogWarning("E-mail is not set up, so \"{Subject}\" was not sent.", subject);
            return false;
        }

        var folder = Path.GetFullPath(options.OutboxFolder);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".txt");

        await File.WriteAllTextAsync(path, $"To: {to}{Environment.NewLine}Subject: {subject}{Environment.NewLine}{Environment.NewLine}{text}", cancellationToken);

        _logger.LogWarning("E-mail is not set up, so \"{Subject}\" was saved to {Path} instead of being sent.", subject, path);

        return true;
    }
}
