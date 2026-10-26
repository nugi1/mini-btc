using Microsoft.EntityFrameworkCore;
using Patterns.DDD.Application;
using Patterns.DDD.MassTransit;

namespace MiniBtc.Infrastructure.Persistence;

/// <summary>
/// The node's database. It is also the unit of work: one SaveChangesAsync is one PostgreSQL
/// transaction, holding a block's rows, the UTXO change, undo data, the new tip and the outbox
/// messages for its domain events.
/// </summary>
public sealed class MiniBtcDbContext(DbContextOptions<MiniBtcDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<ChainStateRecord> ChainStates => Set<ChainStateRecord>();
    public DbSet<BlockRecord> Blocks => Set<BlockRecord>();
    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();
    public DbSet<CoinRecord> Coins => Set<CoinRecord>();
    public DbSet<UndoCoinRecord> UndoCoins => Set<UndoCoinRecord>();
    public DbSet<UnverifiedScriptRecord> UnverifiedScripts => Set<UnverifiedScriptRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChainStateRecord>(entity =>
        {
            entity.ToTable("chain_state");
            entity.HasKey(s => s.Network);
            entity.Property(s => s.Network).HasColumnName("network");
            entity.Property(s => s.TipHash).HasColumnName("tip_hash");
            entity.Property(s => s.TipHeight).HasColumnName("tip_height");
        });
        

        modelBuilder.Entity<BlockRecord>(entity =>
        {
            entity.ToTable("blocks");
            entity.HasKey(b => b.Hash);
            entity.HasIndex(b => b.Height).IsUnique();
            entity.Property(b => b.Hash).HasColumnName("hash");
            entity.Property(b => b.Height).HasColumnName("height");
            entity.Property(b => b.PreviousHash).HasColumnName("previous_hash");
            entity.Property(b => b.Header).HasColumnName("header");
            entity.Property(b => b.TransactionCount).HasColumnName("transaction_count");
            entity.Property(b => b.Size).HasColumnName("size");
            entity.Property(b => b.Fees).HasColumnName("fees");
        });

        modelBuilder.Entity<TransactionRecord>(entity =>
        {
            entity.ToTable("transactions");
            entity.HasKey(t => new { t.BlockHash, t.Position });
            entity.HasIndex(t => t.Txid);
            entity.Property(t => t.BlockHash).HasColumnName("block_hash");
            entity.Property(t => t.Position).HasColumnName("position");
            entity.Property(t => t.Txid).HasColumnName("txid");
            entity.Property(t => t.Wtxid).HasColumnName("wtxid");
            entity.Property(t => t.IsCoinbase).HasColumnName("is_coinbase");
            entity.Property(t => t.Raw).HasColumnName("raw");
        });

        modelBuilder.Entity<CoinRecord>(entity =>
        {
            entity.ToTable("utxos");
            entity.HasKey(c => new { c.Txid, c.Vout });
            entity.HasIndex(c => c.ScriptPubKey); // address lookups (GET /addresses/{address}/utxos)
            entity.Property(c => c.Txid).HasColumnName("txid");
            entity.Property(c => c.Vout).HasColumnName("vout");
            entity.Property(c => c.Value).HasColumnName("value");
            entity.Property(c => c.ScriptPubKey).HasColumnName("script_pubkey");
            entity.Property(c => c.Height).HasColumnName("height");
            entity.Property(c => c.IsCoinbase).HasColumnName("is_coinbase");
        });

        modelBuilder.Entity<UndoCoinRecord>(entity =>
        {
            entity.ToTable("undo_coins");
            entity.HasKey(u => new { u.BlockHash, u.TxPosition, u.InputIndex });
            entity.Property(u => u.BlockHash).HasColumnName("block_hash");
            entity.Property(u => u.TxPosition).HasColumnName("tx_position");
            entity.Property(u => u.InputIndex).HasColumnName("input_index");
            entity.Property(u => u.Txid).HasColumnName("txid");
            entity.Property(u => u.Vout).HasColumnName("vout");
            entity.Property(u => u.Value).HasColumnName("value");
            entity.Property(u => u.ScriptPubKey).HasColumnName("script_pubkey");
            entity.Property(u => u.Height).HasColumnName("height");
            entity.Property(u => u.IsCoinbase).HasColumnName("is_coinbase");
        });

        modelBuilder.Entity<UnverifiedScriptRecord>(entity =>
        {
            entity.ToTable("unverified_scripts");
            entity.HasKey(u => new { u.BlockHash, u.Txid, u.InputIndex });
            entity.Property(u => u.BlockHash).HasColumnName("block_hash");
            entity.Property(u => u.Txid).HasColumnName("txid");
            entity.Property(u => u.InputIndex).HasColumnName("input_index");
            entity.Property(u => u.Reason).HasColumnName("reason");
        });

        // MassTransit's inbox and outbox tables; keeps EF from mapping DomainEvents. Must come last.
        modelBuilder.AddDomainEventOutbox();
    }
}
