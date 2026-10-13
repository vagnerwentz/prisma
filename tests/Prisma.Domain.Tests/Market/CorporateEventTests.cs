using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Market;

// docs/investimentos.md, "I.7c1": o evento é a regra da empresa (proporção, data), igual para todos.
public sealed class CorporateEventTests
{
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly Guid B = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2024, 12, 16);

    private static Result<CorporateEvent> Create(CorporateEventKind kind, Guid? target, int from, int to, string key = "k") =>
        CorporateEvent.Create(key, kind, A, target, Day, from, to, "fonte", Now);

    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 0)]
    [InlineData(-1, 8)]
    public void Proportion_must_be_positive(int from, int to) =>
        Create(CorporateEventKind.Split, null, from, to).IsSuccess.ShouldBeFalse();

    [Fact]
    public void Split_has_no_target_and_conversion_needs_another_asset()
    {
        Create(CorporateEventKind.Split, B, 1, 8).IsSuccess.ShouldBeFalse();
        Create(CorporateEventKind.Conversion, null, 100, 80).IsSuccess.ShouldBeFalse();
        Create(CorporateEventKind.Conversion, A, 100, 80).IsSuccess.ShouldBeFalse();
        Create(CorporateEventKind.Conversion, B, 100, 80).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Key_is_required() =>
        Create(CorporateEventKind.Split, null, 1, 8, key: " ").IsSuccess.ShouldBeFalse();

    [Theory]
    [InlineData(1, 8, 110, 880)]
    [InlineData(100, 80, 880, 704)]
    [InlineData(10, 1, 105, 10)]   // grupamento: a fração some
    [InlineData(100, 80, 333, 266)] // 266,4
    public void New_quantity_rounds_down(int from, int to, long held, long expected) =>
        Create(CorporateEventKind.Split, null, from, to).Value.Convert(held).ShouldBe(expected);

    // A sincronização: entra o que é novo, muda o que mudou, sai o que saiu do arquivo, e o resto fica como está.
    [Fact]
    public void Reconciliation_adds_updates_removes_and_keeps()
    {
        var kept = CorporateEvent.Create("kept", CorporateEventKind.Split, A, null, Day, 1, 8, "f", Now).Value;
        var changed = CorporateEvent.Create("changed", CorporateEventKind.Conversion, A, B, Day, 100, 80, "f", Now).Value;
        var gone = CorporateEvent.Create("gone", CorporateEventKind.Split, B, null, Day, 1, 2, "f", Now).Value;
        CorporateEventDefinition[] file =
        [
            new("kept", CorporateEventKind.Split, A, null, Day, 1, 8, "f"),
            new("changed", CorporateEventKind.Conversion, A, B, Day, 100, 79, "f, corrigido"),
            new("new", CorporateEventKind.Split, B, null, Day.AddDays(1), 10, 1, "f"),
        ];

        var changes = CorporateEventReconciliation.Apply([kept, changed, gone], file, Now.AddDays(1)).Value;

        changes.Added.Select(e => e.Key).ShouldBe(["new"]);
        changes.Updated.ShouldBe(1);
        changes.Removed.ShouldBe([gone]);
        (changed.ToQuantity, changed.Source, changed.UpdatedAt).ShouldBe((79, "f, corrigido", Now.AddDays(1)));
        kept.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Reconciliation_twice_changes_nothing()
    {
        CorporateEventDefinition[] file = [new("a", CorporateEventKind.Split, A, null, Day, 1, 8, "f")];
        var first = CorporateEventReconciliation.Apply([], file, Now).Value;

        var second = CorporateEventReconciliation.Apply(first.Added, file, Now.AddDays(1)).Value;

        (second.Added.Count, second.Updated, second.Removed.Count).ShouldBe((0, 0, 0));
    }

    [Fact]
    public void Reconciliation_refuses_an_invalid_file()
    {
        CorporateEventDefinition[] file =
        [
            new("dup", CorporateEventKind.Split, A, null, Day, 1, 8, "f"),
            new("dup", CorporateEventKind.Split, B, null, Day, 1, 8, "f"),
        ];

        CorporateEventReconciliation.Apply([], file, Now).IsSuccess.ShouldBeFalse();
        CorporateEventReconciliation.Apply([], [new("x", CorporateEventKind.Split, A, null, Day, 0, 8, "f")], Now).IsSuccess.ShouldBeFalse();
    }

    // O ativo que saiu da bolsa antes do catálogo existir: entra já inativo, desde o dia seguinte ao último pregão.
    [Fact]
    public void Historical_asset_enters_inactive()
    {
        ListedAsset.TryCreate("BCFF11", "BTG Pactual Fundo de Fundos", null, AssetKind.Fii, out var listed).ShouldBeTrue();

        var asset = Asset.FromHistory(listed, new DateOnly(2024, 10, 18), Now);

        asset.IsActive.ShouldBeFalse();
        SaoPauloTime.DateOf(asset.InactiveSince!.Value).ShouldBe(new DateOnly(2024, 10, 19));
        asset.Symbol.ShouldBe("BCFF11");
    }
}

// docs/investimentos.md, "I.7c4": a liquidação paga em cotas tem destino, preço de referência e, às vezes, caixa.
public sealed class LiquidationEventTests
{
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly Guid B = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2024, 12, 16);

    private static Result<CorporateEvent> Create(CorporateEventKind kind, Guid? target, long? price, long? cash) =>
        CorporateEvent.Create("k", kind, A, target, Day, 100, 80, "fonte", Now, price, cash);

    [Fact]
    public void Liquidation_needs_another_asset_and_a_positive_price()
    {
        Create(CorporateEventKind.Liquidation, B, 1_025_000_000, 56_000_000).IsSuccess.ShouldBeTrue();
        Create(CorporateEventKind.Liquidation, B, 1_025_000_000, null).IsSuccess.ShouldBeTrue();
        Create(CorporateEventKind.Liquidation, B, 1_025_000_000, 0).IsSuccess.ShouldBeTrue();
        Create(CorporateEventKind.Liquidation, null, 1_025_000_000, null).IsSuccess.ShouldBeFalse();
        Create(CorporateEventKind.Liquidation, A, 1_025_000_000, null).IsSuccess.ShouldBeFalse();
        Create(CorporateEventKind.Liquidation, B, null, null).Error!.Message.ShouldBe("Evento k: a liquidação precisa do preço de referência do destino, maior que zero.");
        Create(CorporateEventKind.Liquidation, B, 0, null).IsSuccess.ShouldBeFalse();
        Create(CorporateEventKind.Liquidation, B, 1_025_000_000, -1).Error!.Message.ShouldBe("Evento k: o caixa por cota não pode ser negativo.");
    }

    [Fact]
    public void Price_and_cash_are_only_for_liquidation()
    {
        Create(CorporateEventKind.Conversion, B, 1_025_000_000, null).Error!.Message.ShouldBe("Evento k: preço e caixa por cota são só da liquidação.");
        Create(CorporateEventKind.Conversion, B, null, 56_000_000).IsSuccess.ShouldBeFalse();
        Create(CorporateEventKind.Split, null, 1_025_000_000, null).IsSuccess.ShouldBeFalse();
    }

    // A curadoria corrige um evento existente: troca o tipo e os valores.
    [Fact]
    public void Reconciliation_turns_a_conversion_into_a_liquidation()
    {
        var existing = CorporateEvent.Create("bcff", CorporateEventKind.Conversion, A, B, Day, 100, 80, "f", Now).Value;

        var changes = CorporateEventReconciliation.Apply([existing],
            [new("bcff", CorporateEventKind.Liquidation, A, B, Day, 100, 80, "f", 1_025_000_000, 56_000_000)], Now).Value;

        changes.Updated.ShouldBe(1);
        (existing.Kind, existing.TargetUnitPrice, existing.CashPerUnit).ShouldBe((CorporateEventKind.Liquidation, (long?)1_025_000_000, (long?)56_000_000));
        CorporateEventReconciliation.Apply([existing],
            [new("bcff", CorporateEventKind.Liquidation, A, B, Day, 100, 80, "f", 1_025_000_000, 56_000_000)], Now).Value.Updated.ShouldBe(0);
    }

    // Corrigir só o preço ou o caixa também é mudança.
    [Theory]
    [InlineData(1_030_000_000L, 56_000_000L)]
    [InlineData(1_025_000_000L, 57_000_000L)]
    [InlineData(1_025_000_000L, null)]
    public void Correcting_only_the_values_updates_the_event(long price, long? cash)
    {
        var existing = CorporateEvent.Create("bcff", CorporateEventKind.Liquidation, A, B, Day, 100, 80, "f", Now, 1_025_000_000, 56_000_000).Value;

        CorporateEventReconciliation.Apply([existing], [new("bcff", CorporateEventKind.Liquidation, A, B, Day, 100, 80, "f", price, cash)], Now)
            .Value.Updated.ShouldBe(1);
        (existing.TargetUnitPrice, existing.CashPerUnit).ShouldBe(((long?)price, cash));
    }

    [Theory]
    [InlineData("10.25", 1_025_000_000L)]
    [InlineData("0.56", 56_000_000L)]
    [InlineData("83.3946076", 8_339_460_760L)]
    [InlineData("4.30605192", 430_605_192L)]
    [InlineData("10", 1_000_000_000L)]
    [InlineData("0", 0L)]
    public void Unit_value_is_read_with_up_to_eight_decimals(string text, long expected)
    {
        CorporateEvent.TryParseUnitValue(text, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("4.306051918")] // 9 casas
    [InlineData("10,25")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData(" 10.25")]
    [InlineData("1e3")]
    [InlineData("99999999999")] // passa de R$ 1 bilhão por cota
    public void Unit_value_refuses_what_is_not_a_plain_decimal(string text) =>
        CorporateEvent.TryParseUnitValue(text, out _).ShouldBeFalse();
}
