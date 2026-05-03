using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SkipWatch.Core.Db;

namespace SkipWatch.Tests.Db;

public sealed class SkipWatchDbContextSmokeTests
{
    [Fact]
    public void Migrations_apply_against_in_memory_sqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SkipWatchDbContext>()
            .UseSqlite(connection)
            .Options;

        using var ctx = new SkipWatchDbContext(options);
        ctx.Database.Migrate();

        ctx.Channels.Should().BeEmpty();
        ctx.Videos.Should().BeEmpty();
        ctx.Topics.Should().BeEmpty();
        ctx.Projects.Should().BeEmpty();
    }
}
