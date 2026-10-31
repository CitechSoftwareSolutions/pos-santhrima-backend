namespace POSSystem.Application.Common.Interfaces;

public interface IDatabaseContextResolver
{
    /// <summary>
    /// Returns the active connection string based on the incoming request header (X-Database-Context: admin vs normal)
    /// or explicit override.
    /// </summary>
    string GetActiveConnectionString();

    /// <summary>
    /// Returns the connection string for the Normal database (pos_system).
    /// </summary>
    string GetNormalConnectionString();

    /// <summary>
    /// Returns the connection string for the Admin database (pos_system_admin).
    /// </summary>
    string GetAdminConnectionString();

    /// <summary>
    /// Returns true if currently operating in Admin (Dark Mode) database context.
    /// </summary>
    bool IsAdminMode();

    /// <summary>
    /// Allows programmatically overriding the mode for the current scope.
    /// </summary>
    void SetExplicitMode(bool isAdmin);
}
