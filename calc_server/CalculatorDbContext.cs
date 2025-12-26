using Microsoft.EntityFrameworkCore;
using calc_server.models;

namespace calc_server;

public class CalculatorDbContext : DbContext
{
    public CalculatorDbContext(DbContextOptions<CalculatorDbContext> options) : base(options) { }

    // This property represents the 'operations' table in Postgres
    public DbSet<OperationEntry> Operations { get; set; } = null!;
    
    // protected override void OnModelCreating(ModelBuilder modelBuilder)
    // {
    //     modelBuilder.Entity<OperationEntry>(b =>
    //     {
    //         b.ToTable("operations");
    //         b.HasKey(e => e.rawid);
    //         b.Property(e => e.rawid)
    //             .HasColumnName("rawid");
    //     });
    //
    //     base.OnModelCreating(modelBuilder);
    // }
}