using Microsoft.EntityFrameworkCore;
using Wallet.Api.Domain;

namespace Wallet.Api.Infrastructure;

public class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("accounts", t => t.HasCheckConstraint("ck_accounts_balance_non_negative", "balance_cents >= 0"));
            e.HasKey(a => a.PlayerId);
            e.Property(a => a.PlayerId).HasColumnName("player_id");
            e.Property(a => a.BalanceCents).HasColumnName("balance_cents");
            e.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<LedgerTransaction>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.PlayerId).HasColumnName("player_id");
            e.Property(t => t.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16);
            e.Property(t => t.AmountCents).HasColumnName("amount_cents");
            e.Property(t => t.BalanceAfterCents).HasColumnName("balance_after_cents");
            e.Property(t => t.RoundId).HasColumnName("round_id");
            e.Property(t => t.ReferenceTransactionId).HasColumnName("reference_transaction_id");
            e.Property(t => t.CreatedAt).HasColumnName("created_at");
            e.HasIndex(t => t.PlayerId).HasDatabaseName("ix_transactions_player_id");
            e.HasIndex(t => t.ReferenceTransactionId).IsUnique().HasFilter("type = 'Cancel'").HasDatabaseName("ux_transactions_cancel_reference");
        });
    }
}
