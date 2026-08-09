namespace POSSystem.Infrastructure.Persistence;

internal static class DateTimeExtensions
{
    public static DateTime AsUtc(this DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
