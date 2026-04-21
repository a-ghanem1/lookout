using Microsoft.EntityFrameworkCore;

namespace Lookout.EntityFrameworkCore.IntegrationTests.Helpers;

public sealed class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    public DbSet<Widget> Widgets => Set<Widget>();
}
