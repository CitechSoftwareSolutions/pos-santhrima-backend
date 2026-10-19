using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Cheques;
using POSSystem.Domain.Entities;
using POSSystem.Domain.Enums;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class ChequeService : IChequeService
{
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppNotificationService _whatsAppService;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly ILogger<ChequeService> _logger;
    private static bool _initialized = false;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public ChequeService(
        ApplicationDbContext db,
        IWhatsAppNotificationService whatsAppService,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger<ChequeService> logger)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _configuration = configuration;
        _logger = logger;
    }

    private static DateTime GetLocalNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Sri Lanka Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(5.5);
            }
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            // Ensure table creation fallback
            var createTablesSql = @"
                CREATE TABLE IF NOT EXISTS ""Cheques"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""ChequeNumber"" character varying(100) NOT NULL,
                    ""Payee"" character varying(255) NOT NULL,
                    ""Amount"" numeric(18,2) NOT NULL,
                    ""IssueDate"" timestamp with time zone NOT NULL,
                    ""DueDate"" timestamp with time zone NOT NULL,
                    ""BankName"" character varying(150) NULL,
                    ""Status"" integer NOT NULL DEFAULT 0,
                    ""Remarks"" character varying(1000) NULL,
                    ""IsReminderSent"" boolean NOT NULL DEFAULT false,
                    ""ReminderSentAt"" timestamp with time zone NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false
                );

                CREATE TABLE IF NOT EXISTS ""ChequeNotificationRecipients"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""Name"" character varying(150) NOT NULL,
                    ""PhoneNumber"" character varying(50) NOT NULL,
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    ""Notes"" character varying(255) NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false
                );

                CREATE TABLE IF NOT EXISTS ""WhatsAppSettings"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""IsEnabled"" boolean NOT NULL DEFAULT true,
                    ""Provider"" character varying(50) NOT NULL DEFAULT 'Twilio',
                    ""TwilioAccountSid"" character varying(100) NULL,
                    ""TwilioAuthToken"" character varying(100) NULL,
                    ""TwilioFromNumber"" character varying(50) NULL,
                    ""UltraMsgInstanceId"" character varying(100) NULL,
                    ""UltraMsgToken"" character varying(100) NULL,
                    ""ScheduledHour"" integer NOT NULL DEFAULT 8,
                    ""ScheduledMinute"" integer NOT NULL DEFAULT 0,
                    ""TimeZoneId"" character varying(100) NOT NULL DEFAULT 'Asia/Colombo',
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false
                );
            ";

            try
            {
                await _db.Database.ExecuteSqlRawAsync(createTablesSql, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fallback table creation skipped or already exists.");
            }

            // Ensure default WhatsAppSetting row
            var setting = await _db.WhatsAppSettings.IgnoreQueryFilters().FirstOrDefaultAsync(cancellationToken);
            if (setting is null)
            {
                _db.WhatsAppSettings.Add(new WhatsAppSetting
                {
                    IsEnabled = true,
                    Provider = "Twilio",
                    TwilioFromNumber = "whatsapp:+14155238886",
                    ScheduledHour = 8,
                    ScheduledMinute = 0,
                    TimeZoneId = "Asia/Colombo",
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<PagedResult<ChequeDto>> GetAllAsync(ChequeQueryParams query, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var queryable = _db.Cheques.AsNoTracking().AsQueryable();

        // Search text
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            queryable = queryable.Where(c =>
                c.ChequeNumber.ToLower().Contains(term) ||
                c.Payee.ToLower().Contains(term) ||
                (c.BankName != null && c.BankName.ToLower().Contains(term)) ||
                (c.Remarks != null && c.Remarks.ToLower().Contains(term)));
        }

        // Status filter
        if (query.Status.HasValue)
        {
            queryable = queryable.Where(c => c.Status == query.Status.Value);
        }

        var localToday = GetLocalNow().Date;
        var todayUtcStart = DateTime.SpecifyKind(localToday, DateTimeKind.Utc);
        var todayUtcEnd = DateTime.SpecifyKind(localToday.AddDays(1), DateTimeKind.Utc);

        // Due Filter
        if (!string.IsNullOrWhiteSpace(query.DueFilter))
        {
            var filter = query.DueFilter.Trim().ToLower();
            if (filter == "due_today")
            {
                queryable = queryable.Where(c => c.DueDate >= todayUtcStart && c.DueDate < todayUtcEnd);
            }
            else if (filter == "due_week")
            {
                var nextWeekUtc = DateTime.SpecifyKind(localToday.AddDays(7), DateTimeKind.Utc);
                queryable = queryable.Where(c => c.DueDate >= todayUtcStart && c.DueDate <= nextWeekUtc);
            }
            else if (filter == "overdue")
            {
                queryable = queryable.Where(c => c.DueDate < todayUtcStart && c.Status == ChequeStatus.Pending);
            }
        }

        if (query.StartDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(query.StartDate.Value.Date, DateTimeKind.Utc);
            queryable = queryable.Where(c => c.DueDate >= startUtc);
        }

        if (query.EndDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(query.EndDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            queryable = queryable.Where(c => c.DueDate < endUtc);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderBy(c => c.DueDate)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => ToDto(c))
            .ToListAsync(cancellationToken);

        return new PagedResult<ChequeDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<ChequeSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var localToday = GetLocalNow().Date;
        var todayUtcStart = DateTime.SpecifyKind(localToday, DateTimeKind.Utc);
        var todayUtcEnd = DateTime.SpecifyKind(localToday.AddDays(1), DateTimeKind.Utc);

        var cheques = await _db.Cheques.AsNoTracking().ToListAsync(cancellationToken);

        var pending = cheques.Where(c => c.Status == ChequeStatus.Pending).ToList();
        var dueToday = pending.Where(c => c.DueDate >= todayUtcStart && c.DueDate < todayUtcEnd).ToList();
        var overdue = pending.Where(c => c.DueDate < todayUtcStart).ToList();
        var cleared = cheques.Where(c => c.Status == ChequeStatus.Cleared).ToList();

        return new ChequeSummaryDto(
            TotalPendingCount: pending.Count,
            TotalPendingAmount: pending.Sum(c => c.Amount),
            DueTodayCount: dueToday.Count,
            DueTodayAmount: dueToday.Sum(c => c.Amount),
            OverdueCount: overdue.Count,
            OverdueAmount: overdue.Sum(c => c.Amount),
            ClearedCount: cleared.Count,
            ClearedAmount: cleared.Sum(c => c.Amount));
    }

    public async Task<ChequeDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var cheque = await _db.Cheques.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Cheque), id);

        return ToDto(cheque);
    }

    public async Task<ChequeDto> CreateAsync(CreateChequeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var dueUtc = DateTime.SpecifyKind(request.DueDate.Date, DateTimeKind.Utc);
        var issueUtc = DateTime.SpecifyKind((request.IssueDate ?? DateTime.UtcNow).Date, DateTimeKind.Utc);

        var cheque = new Cheque
        {
            ChequeNumber = request.ChequeNumber.Trim(),
            Payee = request.Payee.Trim(),
            Amount = request.Amount,
            DueDate = dueUtc,
            IssueDate = issueUtc,
            BankName = request.BankName?.Trim(),
            Remarks = request.Remarks?.Trim(),
            Status = ChequeStatus.Pending,
            IsReminderSent = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Cheques.Add(cheque);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created Cheque #{ChequeNumber} to {Payee} for Rs. {Amount}", cheque.ChequeNumber, cheque.Payee, cheque.Amount);
        return ToDto(cheque);
    }

    public async Task<ChequeDto> UpdateAsync(Guid id, UpdateChequeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var cheque = await _db.Cheques.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Cheque), id);

        cheque.ChequeNumber = request.ChequeNumber.Trim();
        cheque.Payee = request.Payee.Trim();
        cheque.Amount = request.Amount;
        cheque.DueDate = DateTime.SpecifyKind(request.DueDate.Date, DateTimeKind.Utc);
        if (request.IssueDate.HasValue)
        {
            cheque.IssueDate = DateTime.SpecifyKind(request.IssueDate.Value.Date, DateTimeKind.Utc);
        }
        cheque.BankName = request.BankName?.Trim();
        cheque.Remarks = request.Remarks?.Trim();
        cheque.Status = request.Status;
        cheque.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(cheque);
    }

    public async Task<ChequeDto> UpdateStatusAsync(Guid id, UpdateChequeStatusRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var cheque = await _db.Cheques.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Cheque), id);

        cheque.Status = request.Status;
        if (!string.IsNullOrWhiteSpace(request.Remarks))
        {
            cheque.Remarks = request.Remarks.Trim();
        }
        cheque.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(cheque);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var cheque = await _db.Cheques.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Cheque), id);

        cheque.IsDeleted = true;
        cheque.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    // Recipients
    public async Task<List<ChequeNotificationRecipientDto>> GetRecipientsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        return await _db.ChequeNotificationRecipients
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new ChequeNotificationRecipientDto(
                r.Id,
                r.Name,
                r.PhoneNumber,
                r.IsActive,
                r.Notes,
                r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ChequeNotificationRecipientDto> CreateRecipientAsync(CreateRecipientRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var recipient = new ChequeNotificationRecipient
        {
            Name = request.Name.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            IsActive = request.IsActive,
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.ChequeNotificationRecipients.Add(recipient);
        await _db.SaveChangesAsync(cancellationToken);

        return new ChequeNotificationRecipientDto(
            recipient.Id,
            recipient.Name,
            recipient.PhoneNumber,
            recipient.IsActive,
            recipient.Notes,
            recipient.CreatedAt);
    }

    public async Task<ChequeNotificationRecipientDto> UpdateRecipientAsync(Guid id, UpdateRecipientRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var recipient = await _db.ChequeNotificationRecipients.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ChequeNotificationRecipient), id);

        recipient.Name = request.Name.Trim();
        recipient.PhoneNumber = request.PhoneNumber.Trim();
        recipient.IsActive = request.IsActive;
        recipient.Notes = request.Notes?.Trim();
        recipient.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new ChequeNotificationRecipientDto(
            recipient.Id,
            recipient.Name,
            recipient.PhoneNumber,
            recipient.IsActive,
            recipient.Notes,
            recipient.CreatedAt);
    }

    public async Task DeleteRecipientAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var recipient = await _db.ChequeNotificationRecipients.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ChequeNotificationRecipient), id);

        recipient.IsDeleted = true;
        recipient.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    // Settings
    public async Task<WhatsAppSettingDto> GetWhatsAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var setting = await _db.WhatsAppSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            setting = new WhatsAppSetting
            {
                IsEnabled = true,
                Provider = "Twilio",
                TwilioFromNumber = "whatsapp:+14155238886",
                ScheduledHour = 8,
                ScheduledMinute = 0,
                TimeZoneId = "Asia/Colombo",
                CreatedAt = DateTime.UtcNow
            };
            _db.WhatsAppSettings.Add(setting);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ToSettingDto(setting);
    }

    public async Task<WhatsAppSettingDto> UpdateWhatsAppSettingsAsync(UpdateWhatsAppSettingRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var setting = await _db.WhatsAppSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            setting = new WhatsAppSetting();
            _db.WhatsAppSettings.Add(setting);
        }

        setting.IsEnabled = request.IsEnabled;
        setting.Provider = request.Provider.Trim();
        setting.TwilioAccountSid = request.TwilioAccountSid?.Trim();
        setting.TwilioAuthToken = request.TwilioAuthToken?.Trim();
        setting.TwilioFromNumber = request.TwilioFromNumber?.Trim();
        setting.UltraMsgInstanceId = request.UltraMsgInstanceId?.Trim();
        setting.UltraMsgToken = request.UltraMsgToken?.Trim();
        setting.ScheduledHour = Math.Clamp(request.ScheduledHour, 0, 23);
        setting.ScheduledMinute = Math.Clamp(request.ScheduledMinute, 0, 59);
        setting.TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "Asia/Colombo" : request.TimeZoneId.Trim();
        setting.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ToSettingDto(setting);
    }

    public async Task<WhatsAppSendResultDto> SendDueChequeRemindersAsync(bool forceSendAllDueToday = false, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var localToday = GetLocalNow().Date;
        var todayUtcStart = DateTime.SpecifyKind(localToday, DateTimeKind.Utc);
        var todayUtcEnd = DateTime.SpecifyKind(localToday.AddDays(1), DateTimeKind.Utc);

        var chequesQuery = _db.Cheques
            .Where(c => c.Status == ChequeStatus.Pending && c.DueDate >= todayUtcStart && c.DueDate < todayUtcEnd);

        if (!forceSendAllDueToday)
        {
            chequesQuery = chequesQuery.Where(c => !c.IsReminderSent);
        }

        var dueChequesEntities = await chequesQuery.ToListAsync(cancellationToken);

        if (dueChequesEntities.Count == 0)
        {
            return new WhatsAppSendResultDto(true, "No pending due cheques found for today requiring reminders.", 0, 0);
        }

        var recipients = await GetRecipientsAsync(cancellationToken);
        var dueChequeDtos = dueChequesEntities.Select(ToDto).ToList();

        var result = await _whatsAppService.DispatchDueChequesAsync(dueChequeDtos, recipients, cancellationToken);

        if (result.Success && result.RecipientsSentCount > 0)
        {
            var nowUtc = DateTime.UtcNow;
            foreach (var chq in dueChequesEntities)
            {
                chq.IsReminderSent = true;
                chq.ReminderSentAt = nowUtc;
                chq.UpdatedAt = nowUtc;
            }
            await _db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<WhatsAppSendResultDto> SendTestMessageAsync(TestWhatsAppRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var storeSetting = await _db.StoreSettings.FirstOrDefaultAsync(cancellationToken);
        var storeName = storeSetting?.StoreName ?? "SANTHRIMA STORES";

        var testBody = request.Message;
        if (string.IsNullOrWhiteSpace(testBody))
        {
            testBody = $"*{storeName.ToUpperInvariant()} – TEST NOTIFICATION*\n\n✅ Test WhatsApp message from POS Cheque System.\n*Time*: {GetLocalNow():dd MMMM yyyy, hh:mm tt}.";
        }

        var (success, error) = await _whatsAppService.SendTextMessageAsync(request.PhoneNumber, testBody, cancellationToken);

        if (success)
        {
            return new WhatsAppSendResultDto(true, $"Test WhatsApp message successfully dispatched to {request.PhoneNumber}!", 1, 0);
        }
        else
        {
            return new WhatsAppSendResultDto(false, $"Failed to send test message: {error}", 0, 0, new List<string> { error ?? "Unknown error" });
        }
    }

    private static ChequeDto ToDto(Cheque c) => new(
        c.Id,
        c.ChequeNumber,
        c.Payee,
        c.Amount,
        c.IssueDate,
        c.DueDate,
        c.BankName,
        c.Status,
        c.Remarks,
        c.IsReminderSent,
        c.ReminderSentAt,
        c.CreatedAt,
        c.UpdatedAt);

    private WhatsAppSettingDto ToSettingDto(WhatsAppSetting s)
    {
        var envProvider = EnvHelper.GetValue("WHATSAPP_PROVIDER") ?? _configuration["WhatsApp:Provider"];
        var envUltraInst = EnvHelper.GetValue("WHATSAPP_ULTRAMSG_INSTANCE_ID") ?? _configuration["WhatsApp:UltraMsgInstanceId"];
        var envUltraToken = EnvHelper.GetValue("WHATSAPP_ULTRAMSG_TOKEN") ?? _configuration["WhatsApp:UltraMsgToken"];
        var envTwilioSid = EnvHelper.GetValue("WHATSAPP_TWILIO_ACCOUNT_SID") ?? _configuration["WhatsApp:TwilioAccountSid"];
        var envTwilioToken = EnvHelper.GetValue("WHATSAPP_TWILIO_AUTH_TOKEN") ?? _configuration["WhatsApp:TwilioAuthToken"];
        var envTwilioFrom = EnvHelper.GetValue("WHATSAPP_TWILIO_FROM_NUMBER") ?? _configuration["WhatsApp:TwilioFromNumber"];

        bool isEnvConfigured = !string.IsNullOrWhiteSpace(envUltraInst) || !string.IsNullOrWhiteSpace(envTwilioSid) || !string.IsNullOrWhiteSpace(envUltraToken);

        var provider = (!string.IsNullOrWhiteSpace(envProvider) ? envProvider : s.Provider ?? "UltraMsg").Trim();
        var ultraInst = !string.IsNullOrWhiteSpace(envUltraInst) ? envUltraInst : s.UltraMsgInstanceId;
        var twilioSid = !string.IsNullOrWhiteSpace(envTwilioSid) ? envTwilioSid : s.TwilioAccountSid;
        var twilioFrom = !string.IsNullOrWhiteSpace(envTwilioFrom) ? envTwilioFrom : (s.TwilioFromNumber ?? "whatsapp:+14155238886");

        bool hasToken = (provider.Equals("UltraMsg", StringComparison.OrdinalIgnoreCase) && (!string.IsNullOrWhiteSpace(envUltraToken) || !string.IsNullOrWhiteSpace(s.UltraMsgToken)))
                     || (provider.Equals("Twilio", StringComparison.OrdinalIgnoreCase) && (!string.IsNullOrWhiteSpace(envTwilioToken) || !string.IsNullOrWhiteSpace(s.TwilioAuthToken)));

        return new WhatsAppSettingDto(
            s.Id,
            s.IsEnabled,
            provider,
            twilioSid,
            null, // Protected: do not send raw secret token to browser
            twilioFrom,
            ultraInst,
            null, // Protected: do not send raw secret token to browser
            s.ScheduledHour,
            s.ScheduledMinute,
            s.TimeZoneId,
            IsConfiguredViaEnv: isEnvConfigured,
            HasConfiguredToken: hasToken);
    }
}
