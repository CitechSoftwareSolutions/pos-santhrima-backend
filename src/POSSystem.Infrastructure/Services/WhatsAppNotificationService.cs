using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using POSSystem.Application.Features.Cheques;
using POSSystem.Domain.Entities;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppNotificationService> _logger;

    public WhatsAppNotificationService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WhatsAppNotificationService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private static string NormalizePhoneNumber(string phone)
    {
        var cleaned = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (cleaned.StartsWith("0") && cleaned.Length == 10)
        {
            // Convert Sri Lankan local 07XXXXXXXX to +947XXXXXXXX
            cleaned = "+94" + cleaned[1..];
        }
        else if (!cleaned.StartsWith("+"))
        {
            cleaned = "+" + cleaned;
        }
        return cleaned;
    }

    public async Task<(bool Success, string? Error)> SendTextMessageAsync(
        string toPhoneNumber,
        string messageBody,
        CancellationToken cancellationToken = default)
    {
        var settings = await _db.WhatsAppSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings != null && !settings.IsEnabled)
        {
            const string msg = "WhatsApp notifications are currently disabled in settings.";
            _logger.LogWarning(msg);
            return (false, msg);
        }

        var recipientPhone = NormalizePhoneNumber(toPhoneNumber);

        // Resolve credentials from Environment Variables / .env first, then fall back to database
        var envProvider = EnvHelper.GetValue("WHATSAPP_PROVIDER") ?? _configuration["WhatsApp:Provider"];
        var envUltraInst = EnvHelper.GetValue("WHATSAPP_ULTRAMSG_INSTANCE_ID") ?? _configuration["WhatsApp:UltraMsgInstanceId"];
        var envUltraToken = EnvHelper.GetValue("WHATSAPP_ULTRAMSG_TOKEN") ?? _configuration["WhatsApp:UltraMsgToken"];
        var envTwilioSid = EnvHelper.GetValue("WHATSAPP_TWILIO_ACCOUNT_SID") ?? _configuration["WhatsApp:TwilioAccountSid"];
        var envTwilioToken = EnvHelper.GetValue("WHATSAPP_TWILIO_AUTH_TOKEN") ?? _configuration["WhatsApp:TwilioAuthToken"];
        var envTwilioFrom = EnvHelper.GetValue("WHATSAPP_TWILIO_FROM_NUMBER") ?? _configuration["WhatsApp:TwilioFromNumber"];

        var provider = (!string.IsNullOrWhiteSpace(envProvider) ? envProvider : settings?.Provider ?? "UltraMsg").Trim();
        var twilioSid = !string.IsNullOrWhiteSpace(envTwilioSid) ? envTwilioSid : settings?.TwilioAccountSid;
        var twilioToken = !string.IsNullOrWhiteSpace(envTwilioToken) ? envTwilioToken : settings?.TwilioAuthToken;
        var twilioFrom = !string.IsNullOrWhiteSpace(envTwilioFrom) ? envTwilioFrom : (settings?.TwilioFromNumber ?? "whatsapp:+14155238886");
        var ultraInst = !string.IsNullOrWhiteSpace(envUltraInst) ? envUltraInst : settings?.UltraMsgInstanceId;
        var ultraToken = !string.IsNullOrWhiteSpace(envUltraToken) ? envUltraToken : settings?.UltraMsgToken;

        try
        {
            if (provider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(twilioSid) || string.IsNullOrWhiteSpace(twilioToken))
                {
                    return (false, "Twilio Account SID or Auth Token is not configured in .env (WHATSAPP_TWILIO_ACCOUNT_SID / WHATSAPP_TWILIO_AUTH_TOKEN).");
                }

                var client = _httpClientFactory.CreateClient();
                var authBytes = Encoding.ASCII.GetBytes($"{twilioSid}:{twilioToken}");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                var fromNumber = twilioFrom?.Trim() ?? "whatsapp:+14155238886";
                if (!fromNumber.StartsWith("whatsapp:"))
                {
                    fromNumber = "whatsapp:" + fromNumber;
                }

                var toTwilio = "whatsapp:" + recipientPhone;

                var postData = new Dictionary<string, string>
                {
                    { "To", toTwilio },
                    { "From", fromNumber },
                    { "Body", messageBody }
                };

                var url = $"https://api.twilio.com/2010-04-01/Accounts/{twilioSid}/Messages.json";
                var response = await client.PostAsync(url, new FormUrlEncodedContent(postData), cancellationToken);

                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("WhatsApp message successfully sent via Twilio to {Phone}", recipientPhone);
                    return (true, null);
                }
                else
                {
                    _logger.LogError("Twilio WhatsApp sending failed: Status={Status}, Content={Content}", response.StatusCode, responseContent);
                    return (false, $"Twilio Error ({response.StatusCode}): {responseContent}");
                }
            }
            else if (provider.Equals("UltraMsg", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(ultraInst) || string.IsNullOrWhiteSpace(ultraToken))
                {
                    return (false, "UltraMsg Instance ID or Token is not configured in .env (WHATSAPP_ULTRAMSG_INSTANCE_ID / WHATSAPP_ULTRAMSG_TOKEN).");
                }

                var client = _httpClientFactory.CreateClient();
                var url = $"https://api.ultramsg.com/{ultraInst.Trim()}/messages/chat";

                var postData = new Dictionary<string, string>
                {
                    { "token", ultraToken.Trim() },
                    { "to", recipientPhone },
                    { "body", messageBody }
                };

                var response = await client.PostAsync(url, new FormUrlEncodedContent(postData), cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("WhatsApp message successfully sent via UltraMsg to {Phone}", recipientPhone);
                    return (true, null);
                }
                else
                {
                    _logger.LogError("UltraMsg WhatsApp sending failed: Status={Status}, Content={Content}", response.StatusCode, responseContent);
                    return (false, $"UltraMsg Error ({response.StatusCode}): {responseContent}");
                }
            }
            else
            {
                return (false, $"Unsupported WhatsApp provider: '{provider}'. Please choose Twilio or UltraMsg in .env.");
            }
        }

        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception occurred while sending WhatsApp message to {Phone}", recipientPhone);
            return (false, $"Exception sending message: {ex.Message}");
        }
    }

    private static DateTime GetLocalDate(DateTime utcDate)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Sri Lanka Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDate, DateTimeKind.Utc), tz);
        }
        catch
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDate, DateTimeKind.Utc), tz);
            }
            catch
            {
                return utcDate.AddHours(5.5);
            }
        }
    }

    public async Task<WhatsAppSendResultDto> DispatchDueChequesAsync(
        IEnumerable<ChequeDto> dueCheques,
        IEnumerable<ChequeNotificationRecipientDto> recipients,
        CancellationToken cancellationToken = default)
    {
        var chequesList = dueCheques.ToList();
        var recipientsList = recipients.Where(r => r.IsActive).ToList();

        if (chequesList.Count == 0)
        {
            return new WhatsAppSendResultDto(true, "No cheques are due today.", 0, 0);
        }

        if (recipientsList.Count == 0)
        {
            return new WhatsAppSendResultDto(false, "No active WhatsApp recipient phone numbers are registered.", 0, chequesList.Count);
        }

        var storeSetting = await _db.StoreSettings.FirstOrDefaultAsync(cancellationToken);
        var storeName = storeSetting?.StoreName ?? "SANTHRIMA STORE";

        // Determine Due Date in Sri Lanka local time
        var displayDueDate = chequesList.Count > 0 ? GetLocalDate(chequesList[0].DueDate) : GetLocalDate(DateTime.UtcNow);
        var totalAmount = chequesList.Sum(c => c.Amount);

        var sb = new StringBuilder();
        sb.AppendLine($"*{storeName.ToUpperInvariant()} – CHEQUE DUE ALERT*");
        sb.AppendLine($"*Due Date*: {displayDueDate:dd MMMM yyyy}");
        sb.AppendLine();

        if (chequesList.Count == 1)
        {
            sb.AppendLine("*1 cheque is due for payment today.*");
        }
        else
        {
            sb.AppendLine($"*{chequesList.Count} cheques are due for payment today.*");
        }

        sb.AppendLine("────────────────────");
        sb.AppendLine("Cheque Details");

        for (int i = 0; i < chequesList.Count; i++)
        {
            var chq = chequesList[i];
            if (chequesList.Count > 1)
            {
                sb.AppendLine($"*Cheque #{i + 1}*");
            }
            else
            {
                sb.AppendLine();
            }

            var bank = !string.IsNullOrWhiteSpace(chq.BankName) ? chq.BankName.Trim() : "—";
            var purpose = !string.IsNullOrWhiteSpace(chq.Remarks) ? chq.Remarks.Trim() : "—";

            sb.AppendLine($"- *Cheque No.*: {chq.ChequeNumber}");
            sb.AppendLine($"- *Issued To*: {chq.Payee}");
            sb.AppendLine($"- *Bank*: {bank}");
            sb.AppendLine($"- *Amount*: Rs. {chq.Amount:N2}");
            sb.AppendLine($"- *Purpose*: {purpose}");

            if (i < chequesList.Count - 1)
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine("────────────────────");
        sb.AppendLine();
        sb.Append($"*Total Amount Due*: Rs. {totalAmount:N2}");

        var messageText = sb.ToString();

        int sentSuccessCount = 0;
        var errors = new List<string>();

        foreach (var recipient in recipientsList)
        {
            var (success, error) = await SendTextMessageAsync(recipient.PhoneNumber, messageText, cancellationToken);
            if (success)
            {
                sentSuccessCount++;
            }
            else
            {
                errors.Add($"{recipient.Name} ({recipient.PhoneNumber}): {error}");
            }
        }

        bool overallSuccess = sentSuccessCount > 0;
        var summaryMsg = sentSuccessCount == recipientsList.Count
            ? $"Successfully sent due cheque notification for {chequesList.Count} cheque(s) to all {sentSuccessCount} recipient(s)."
            : $"Sent notification to {sentSuccessCount} of {recipientsList.Count} recipient(s).";

        return new WhatsAppSendResultDto(
            overallSuccess,
            summaryMsg,
            sentSuccessCount,
            chequesList.Count,
            errors.Count > 0 ? errors : null);
    }
}
