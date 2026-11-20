using Game.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Game.Api.Infrastructure;

public class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<Round> Rounds => Set<Round>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Round>(e =>
        {
            e.ToTable("rounds");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(r => r.PlayerId).HasColumnName("player_id").IsRequired();
            e.Property(r => r.BetCents).HasColumnName("bet_cents");
            e.Property(r => r.WinCents).HasColumnName("win_cents");
            e.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16)
                .IsRequired().IsConcurrencyToken();
            e.Property(r => r.FailureReason).HasColumnName("failure_reason");
            e.Property(r => r.BalanceAfterCents).HasColumnName("balance_after_cents");
            e.Property(r => r.RecoveryAttempts).HasColumnName("recovery_attempts");
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.Property(r => r.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(r => new { r.Status, r.UpdatedAt }).HasDatabaseName("ix_rounds_status_updated_at");
            e.HasIndex(r => r.PlayerId).HasDatabaseName("ix_rounds_player_id");
        });
    }
}
