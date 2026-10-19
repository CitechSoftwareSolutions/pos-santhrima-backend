using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POSSystem.Domain.Entities;

namespace POSSystem.Infrastructure.Persistence.Configurations;

public class ChequeConfiguration : IEntityTypeConfiguration<Cheque>
{
    public void Configure(EntityTypeBuilder<Cheque> builder)
    {
        builder.ToTable("Cheques");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChequeNumber).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Payee).IsRequired().HasMaxLength(255);
        builder.Property(c => c.Amount).HasColumnType("numeric(18,2)");
        builder.Property(c => c.BankName).HasMaxLength(150);
        builder.Property(c => c.Remarks).HasMaxLength(1000);

        builder.HasIndex(c => c.ChequeNumber);
        builder.HasIndex(c => c.DueDate);
        builder.HasIndex(c => c.Status);
    }
}

public class ChequeNotificationRecipientConfiguration : IEntityTypeConfiguration<ChequeNotificationRecipient>
{
    public void Configure(EntityTypeBuilder<ChequeNotificationRecipient> builder)
    {
        builder.ToTable("ChequeNotificationRecipients");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(150);
        builder.Property(r => r.PhoneNumber).IsRequired().HasMaxLength(50);
        builder.Property(r => r.Notes).HasMaxLength(255);

        builder.HasIndex(r => r.PhoneNumber);
    }
}

public class WhatsAppSettingConfiguration : IEntityTypeConfiguration<WhatsAppSetting>
{
    public void Configure(EntityTypeBuilder<WhatsAppSetting> builder)
    {
        builder.ToTable("WhatsAppSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Provider).IsRequired().HasMaxLength(50);
        builder.Property(s => s.TwilioAccountSid).HasMaxLength(100);
        builder.Property(s => s.TwilioAuthToken).HasMaxLength(100);
        builder.Property(s => s.TwilioFromNumber).HasMaxLength(50);
        builder.Property(s => s.UltraMsgInstanceId).HasMaxLength(100);
        builder.Property(s => s.UltraMsgToken).HasMaxLength(100);
        builder.Property(s => s.TimeZoneId).HasMaxLength(100);
    }
}
