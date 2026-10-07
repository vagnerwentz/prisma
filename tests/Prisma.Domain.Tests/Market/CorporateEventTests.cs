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
