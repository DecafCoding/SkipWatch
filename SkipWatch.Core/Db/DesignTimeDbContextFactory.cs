using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SkipWatch.Core.Db;

/// <summary>
/// Used only by the EF Core tools (dotnet ef migrations / database update). The runtime path
/// in Program.cs registers SkipWatchDbContext via DI against the real ~/.skipwatch/skipwatch.db.
/// This factory points at the same file so generated migrations target the same schema.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SkipWatchDbContext>
{
    public SkipWatchDbContext CreateDbContext(string[] args)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".skipwatch");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "skipwatch.db");

        var options = new DbContextOptionsBuilder<SkipWatchDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new SkipWatchDbContext(options);
    }
}
