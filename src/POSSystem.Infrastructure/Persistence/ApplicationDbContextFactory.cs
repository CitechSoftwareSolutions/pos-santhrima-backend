using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Common.Interfaces;

namespace POSSystem.Infrastructure.Persistence;

public interface IApplicationDbContextFactory
{
    ApplicationDbContext CreateNormalDbContext();
    ApplicationDbContext CreateAdminDbContext();
    ApplicationDbContext CreateDbContext(string connectionString);
}

public class ApplicationDbContextFactory : IApplicationDbContextFactory
{
    private readonly IDatabaseContextResolver _resolver;

    public ApplicationDbContextFactory(IDatabaseContextResolver resolver)
    {
        _resolver = resolver;
    }

    public ApplicationDbContext CreateNormalDbContext()
    {
        return CreateDbContext(_resolver.GetNormalConnectionString());
    }

    public ApplicationDbContext CreateAdminDbContext()
    {
        return CreateDbContext(_resolver.GetAdminConnectionString());
    }

    public ApplicationDbContext CreateDbContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
