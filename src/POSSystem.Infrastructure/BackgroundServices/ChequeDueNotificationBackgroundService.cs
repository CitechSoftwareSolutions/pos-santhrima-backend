using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POSSystem.Application.Features.Cheques;

namespace POSSystem.Infrastructure.BackgroundServices;

public class ChequeDueNotificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChequeDueNotificationBackgroundService> _logger;

    public ChequeDueNotificationBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ChequeDueNotificationBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private static (DateTime LocalNow, TimeZoneInfo TimeZone) GetLocalTimeAndZone(string timeZoneId)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById("Sri Lanka Standard Time");
            }
            catch
            {
                try
                {
                    tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
                }
                catch
                {
                    tz = TimeZoneInfo.CreateCustomTimeZone("LK_TZ", TimeSpan.FromHours(5.5), "Sri Lanka Time", "Sri Lanka Time");
                }
            }
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return (localNow, tz);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChequeDueNotificationBackgroundService started.");

        // Wait 20 seconds for API and database startup/seeding to complete
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int targetHour = 8;
                int targetMinute = 0;
                string timeZoneId = "Asia/Colombo";

                using (var scope = _serviceProvider.CreateScope())
                {
                    var chequeService = scope.ServiceProvider.GetRequiredService<IChequeService>();
                    try
                    {
                        var settings = await chequeService.GetWhatsAppSettingsAsync(stoppingToken);
                        if (settings != null)
                        {
                            targetHour = settings.ScheduledHour;
                            targetMinute = settings.ScheduledMinute;
                            if (!string.IsNullOrWhiteSpace(settings.TimeZoneId))
                            {
                                timeZoneId = settings.TimeZoneId;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not fetch custom WhatsApp settings for scheduler; using default 08:00 AM Colombo time.");
                    }
                }

                var (localNow, tz) = GetLocalTimeAndZone(timeZoneId);
                var targetToday = new DateTime(localNow.Year, localNow.Month, localNow.Day, targetHour, targetMinute, 0);

                DateTime nextRunLocal = localNow < targetToday ? targetToday : targetToday.AddDays(1);
                TimeSpan delay = nextRunLocal - localNow;

                _logger.LogInformation(
                    "Next Cheque WhatsApp Notification scheduled for: {NextRun} {TzName} (in {Hours:N1} hours / {Minutes:N0} minutes).",
                    nextRunLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                    tz.DisplayName,
                    delay.TotalHours,
                    delay.TotalMinutes);

                await Task.Delay(delay, stoppingToken);

                // Reached target schedule time! Run due cheque reminder
                _logger.LogInformation("Executing morning due cheques notification run at {Time}...", DateTime.UtcNow);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var chequeService = scope.ServiceProvider.GetRequiredService<IChequeService>();
                    var result = await chequeService.SendDueChequeRemindersAsync(forceSendAllDueToday: false, stoppingToken);
                    _logger.LogInformation("Due cheques reminder execution finished. Result: Success={Success}, Recipients={Sent}, Cheques={Cheques}, Message={Message}",
                        result.Success, result.RecipientsSentCount, result.ChequesCount, result.Message);
                }

                // Add small pause before next cycle to prevent re-firing in the same minute
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ChequeDueNotificationBackgroundService loop. Will retry in 5 minutes.");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("ChequeDueNotificationBackgroundService is stopping.");
    }
}
