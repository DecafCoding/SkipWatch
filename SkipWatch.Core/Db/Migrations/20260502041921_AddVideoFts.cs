using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkipWatch.Core.Db.Migrations;

/// <summary>
/// Placeholder for the FTS5 virtual table over Videos.Title + SummaryMd + TranscriptText
/// plus its three sync triggers (insert / update / delete). The body is intentionally empty
/// in H3 — fill it in via migrationBuilder.Sql(...) when Phase 6 (library-wide search) lands
/// and the Videos table has real content to index. See prd.md §5 for the SQL.
/// </summary>
public partial class AddVideoFts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
