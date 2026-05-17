using Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.PostgreSql;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<LogEntry> LogEntries { get; set; }
    public DbSet<ErrorGroup> ErrorGroups { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LogEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.ServiceName)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Message)
                .IsRequired();

            entity.Property(e => e.StackTrace)
                .IsRequired(false);

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired();

            entity.Property(e => e.ErrorGroupId)
                .IsRequired(false);

            entity.HasIndex(e => e.ServiceName);
            entity.HasIndex(e => e.Level);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.ErrorGroupId);

            entity.HasOne(e => e.ErrorGroup)
                .WithMany(eg => eg.LogEntries)
                .HasForeignKey(e => e.ErrorGroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ErrorGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.ErrorClass)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Summary)
                .IsRequired();

            entity.Property(e => e.FirstSeenUtc)
                .IsRequired();

            entity.Property(e => e.LastSeenUtc)
                .IsRequired();

            entity.Property(e => e.Count)
                .IsRequired();
        });
    }
}
