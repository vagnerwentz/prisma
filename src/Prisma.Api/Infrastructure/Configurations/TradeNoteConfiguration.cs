using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Configurations;

// A nota de corretagem digitada e as linhas dela (docs/investimentos.md, "I.7b1 API"). As linhas vivem dentro da
// nota: gravadas, trocadas e lidas com ela, e o filtro de dono da nota vale para elas.
public sealed class TradeNoteConfiguration : IEntityTypeConfiguration<TradeNote>
{
    public void Configure(EntityTypeBuilder<TradeNote> builder)
    {
        builder.ToTable("trade_notes", table =>
        {
            table.HasCheckConstraint("ck_trade_notes_costs", "fees_cents >= 0 AND brokerage_fee_cents >= 0");
            table.HasCheckConstraint("ck_trade_notes_broker", "length(btrim(broker)) > 0");
            // Os nomes dos tipos, sem Unit (conta como Stock): I.7b3.
            var brokerageKinds = string.Join(", ", TradeNote.AllKinds.Select(k => $"'{k}'"));
            table.HasCheckConstraint("ck_trade_notes_brokerage_fee_kinds", $"brokerage_fee_kinds <@ ARRAY[{brokerageKinds}]::text[]");
        });

        // Os tipos em que a corretagem foi cobrada, pelo nome (como o tipo da linha): text[].
        builder.PrimitiveCollection(n => n.BrokerageFeeKinds).ElementType(e => e.HasConversion<string>());

        builder.Property(n => n.Broker).HasMaxLength(TradeNote.BrokerMaxLength);
        builder.Property(n => n.Date).HasColumnName("trade_date");

        builder.HasOne<AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(n => new { n.UserId, n.Date });

        builder.OwnsMany(n => n.Trades, trades =>
        {
            var kinds = string.Join(", ", Enum.GetNames<AssetKind>().Select(k => $"'{k}'"));
            var sides = string.Join(", ", Enum.GetNames<TradeSide>().Select(s => $"'{s}'"));
            trades.ToTable("trades", table =>
            {
                table.HasCheckConstraint("ck_trades_quantity", "quantity > 0");
                table.HasCheckConstraint("ck_trades_unit_price", "unit_price_cents > 0");
                table.HasCheckConstraint("ck_trades_asset_kind", $"asset_kind IN ({kinds})");
                table.HasCheckConstraint("ck_trades_side", $"side IN ({sides})");
            });

            trades.WithOwner().HasForeignKey("TradeNoteId");
            trades.Property<long>("Id");
            trades.HasKey("Id");
            trades.HasIndex("TradeNoteId", nameof(Trade.LineNumber)).IsUnique();

            trades.Property(t => t.AssetKind).HasConversion<string>().HasMaxLength(20);
            trades.Property(t => t.Side).HasConversion<string>().HasMaxLength(10);
            trades.HasOne<Asset>().WithMany().HasForeignKey(t => t.AssetId).OnDelete(DeleteBehavior.Restrict);
            trades.HasIndex(t => t.AssetId);
        });

        builder.Navigation(n => n.Trades).HasField("_trades").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
