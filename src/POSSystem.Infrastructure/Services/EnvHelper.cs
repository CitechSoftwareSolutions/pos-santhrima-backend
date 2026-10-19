using System;
using System.IO;

namespace POSSystem.Infrastructure.Services;

public static class EnvHelper
{
    public static string? GetValue(string key)
    {
        var val = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(val)) return val;

        var potentialEnvPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env"),
            Path.Combine(AppContext.BaseDirectory, ".env"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env")
        };

        foreach (var path in potentialEnvPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    foreach (var rawLine in File.ReadAllLines(path))
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var eqIdx = line.IndexOf('=');
                        if (eqIdx > 0 && line[..eqIdx].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            var foundVal = line[(eqIdx + 1)..].Trim().Trim('"', '\'');
                            if (!string.IsNullOrWhiteSpace(foundVal))
                            {
                                return foundVal;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore transient file lock / read exceptions
                }
            }
        }

        return null;
    }
}
