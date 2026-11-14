using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POSSystem.Application.Common.Interfaces;
using POSSystem.Application.Features.Auth;
using POSSystem.Application.Features.Categories;
using POSSystem.Application.Features.Customers;
using POSSystem.Application.Features.Products;
using POSSystem.Application.Features.Promotions;
using POSSystem.Application.Features.Purchases;
using POSSystem.Application.Features.Reports;
using POSSystem.Application.Features.Sales;
using POSSystem.Application.Features.Settings;
using POSSystem.Application.Features.Suppliers;
using POSSystem.Application.Features.Terminals;
using POSSystem.Application.Features.Cheques;
using POSSystem.Application.Features.Registers;
using POSSystem.Infrastructure.Identity;
using POSSystem.Infrastructure.Persistence;
using POSSystem.Infrastructure.Services;

namespace POSSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IDatabaseContextResolver, DatabaseContextResolver>();
        services.AddScoped<IApplicationDbContextFactory, ApplicationDbContextFactory>();
        services.AddScoped<ISalesShiftService, SalesShiftService>();
        services.AddScoped<DualDatabaseSyncInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<IDatabaseContextResolver>();
            var connStr = resolver.GetActiveConnectionString();
            options.UseNpgsql(connStr)
                   .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                   .AddInterceptors(sp.GetRequiredService<DualDatabaseSyncInterceptor>());
        }, ServiceLifetime.Scoped, ServiceLifetime.Scoped);

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireDigit = false;
                options.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddHttpClient();

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ITerminalService, TerminalService>();
        services.AddScoped<IRegisterService, RegisterService>();

        services.AddScoped<IWhatsAppNotificationService, WhatsAppNotificationService>();
        services.AddScoped<IChequeService, ChequeService>();

        services.AddHostedService<BackgroundServices.ChequeDueNotificationBackgroundService>();

        return services;
    }
}
