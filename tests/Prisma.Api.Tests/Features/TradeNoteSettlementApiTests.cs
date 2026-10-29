using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Infrastructure.Privacy;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/investimentos.md, "I.8 Caixa da corretora": a nota vinculada a uma conta debita (compra) ou credita (venda) o
// caixa dela, por uma liquidação líquida que a nota manda. Os valores esperados saem da tabela da especificação.
[Collection(ApiCollection.Name)]
public sealed class TradeNoteSettlementApiTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record NoteDto(Guid Id, string Date, Guid? SettlementAccountId, long NetCashCents);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents, long? ProjectedBalanceCents, long? AssetsCents);

    private sealed record SummaryDto(long IncomeCents, long ExpenseCents, long InvestedCents);

    private sealed record EntryDto(
        Guid Id, Guid AccountId, string Type, long AmountCents, string PurchaseDate, string SettlementDate,
        string? TransferDirection, string Description, Guid? TradeNoteId, Guid? TransferPairId);

    private sealed record ProblemDto(string? Detail);

    private static readonly DateTime Start = new(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Start);
    private readonly FakeAssetListSource _source = new();

    private sealed record Setup(PrismaApiFactory Factory, HttpClient Client, Guid Petr4, Guid Vale3, Guid Itau, Guid Ion);

    private static object Line(Guid assetId, string side, long quantity, long unitPriceCents) =>
        new { assetId, side, quantity, unitPriceCents };

    private static object Body(string date, object[] trades, Guid? account, long feesCents = 0, string broker = "Íon") =>
        new { broker, date, feesCents, brokerageFeeCents = 0, trades, settlementAccountId = account };

    // O caso do dono: R$ 5.749,70 do Itaú para a Íon e a compra de 100 PETR4 × R$ 57,00 com R$ 49,70 de custos.
    private static object OwnerBuy(Setup s, Guid? account) =>
        Body("2026-10-22", [Line(s.Petr4, "Buy", 100, 5700)], account, feesCents: 4970);

    [Fact]
    public async Task Linked_buy_takes_the_net_cash_out_of_the_account_and_leaves_it_at_zero()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574970);

        var note = await Create(s, OwnerBuy(s, s.Ion));

        note.SettlementAccountId.ShouldBe(s.Ion);
        note.NetCashCents.ShouldBe(574970);
        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(0);
        var entry = (await Settlements(s)).Single();
        (entry.AccountId, entry.Type, entry.AmountCents, entry.TransferDirection, entry.TradeNoteId)
            .ShouldBe((s.Ion, "TradeSettlement", 574970L, "Out", note.Id));
        (entry.PurchaseDate, entry.SettlementDate, entry.TransferPairId).ShouldBe(("2026-10-22", "2026-10-22", null));
        entry.Description.ShouldBe("Nota de corretagem · Íon");
    }

    [Fact]
    public async Task Note_without_an_account_keeps_everything_as_before()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574970);

        var note = await Create(s, OwnerBuy(s, null));

        (note.SettlementAccountId, note.NetCashCents).ShouldBe((null, 574970L));
        (await Settlements(s)).ShouldBeEmpty();
        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(574970);
    }

    // O pedido de uma tela aberta antes do deploy não manda o campo: é "sem vínculo".
    [Fact]
    public async Task Request_without_the_field_means_no_account()
    {
        var s = await Begin();
        await using var _ = s.Factory;

        var response = await s.Client.PostAsJsonAsync("/trade-notes", new
        {
            broker = "Íon", date = "2026-10-22", feesCents = 0, brokerageFeeCents = 0,
            trades = new[] { Line(s.Petr4, "Buy", 10, 5700) },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Settlements(s)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Linked_sell_credits_the_net_cash()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Create(s, Body("2026-10-01", [Line(s.Petr4, "Buy", 100, 5000)], null));

        var note = await Create(s, Body("2026-10-22", [Line(s.Petr4, "Sell", 100, 6000)], s.Ion, feesCents: 5000));

        note.NetCashCents.ShouldBe(-595000);
        var entry = (await Settlements(s)).Single();
        (entry.AmountCents, entry.TransferDirection).ShouldBe((595000L, "In"));
        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(595000);
    }

    [Fact]
    public async Task Buy_and_sell_in_one_note_settle_only_the_net()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Create(s, Body("2026-10-01", [Line(s.Vale3, "Buy", 10, 15000)], null));

        var note = await Create(s,
            Body("2026-10-22", [Line(s.Petr4, "Buy", 10, 10000), Line(s.Vale3, "Sell", 10, 15000)], s.Ion, feesCents: 1000));

        note.NetCashCents.ShouldBe(-49000);
        (await Settlements(s)).Single().AmountCents.ShouldBe(49000);
        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(49000);
    }

    [Fact]
    public async Task Net_zero_note_writes_no_settlement()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Create(s, Body("2026-10-01", [Line(s.Vale3, "Buy", 10, 10000)], null));

        var note = await Create(s, Body("2026-10-22", [Line(s.Petr4, "Buy", 10, 10000), Line(s.Vale3, "Sell", 10, 10000)], s.Ion));

        note.NetCashCents.ShouldBe(0);
        (await Settlements(s)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Changing_the_account_moves_the_settlement()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574970);
        var note = await Create(s, OwnerBuy(s, s.Ion));

        (await Update(s, note.Id, OwnerBuy(s, s.Itau))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(574970);
        // A Íon volta ao que a transferência pôs; o Itaú, que mandou R$ 5.749,70, agora também paga a compra.
        (await Balance(s, s.Itau)).BalanceCents.ShouldBe(-1149940);
        (await Settlements(s)).Single().AccountId.ShouldBe(s.Itau);
    }

    [Fact]
    public async Task Editing_price_or_date_makes_the_settlement_follow_the_note()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var note = await Create(s, OwnerBuy(s, s.Ion));

        (await Update(s, note.Id, Body("2026-10-23", [Line(s.Petr4, "Buy", 100, 5800)], s.Ion, feesCents: 4970)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = (await Settlements(s)).Single();
        (entry.AmountCents, entry.PurchaseDate, entry.SettlementDate).ShouldBe((584970L, "2026-10-23", "2026-10-23"));
    }

    [Fact]
    public async Task Removing_the_account_removes_the_settlement_and_linking_again_brings_one_back()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574970);
        var note = await Create(s, OwnerBuy(s, s.Ion));

        (await Update(s, note.Id, OwnerBuy(s, null))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Settlements(s)).ShouldBeEmpty();
        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(574970);

        (await Update(s, note.Id, OwnerBuy(s, s.Ion))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Settlements(s)).Single().AmountCents.ShouldBe(574970);
        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_the_note_takes_the_settlement_and_undo_brings_both_back()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574970);
        var note = await Create(s, OwnerBuy(s, s.Ion));

        (await s.Client.DeleteAsync($"/trade-notes/{note.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Settlements(s)).ShouldBeEmpty();
        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(574970);

        (await s.Client.PostAsync($"/trade-notes/{note.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Settlements(s)).Single().AmountCents.ShouldBe(574970);
        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(0);
    }

    [Fact]
    public async Task Month_summary_is_untouched_by_the_settlement()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574970);
        var before = await Summary(s);

        await Create(s, OwnerBuy(s, s.Ion));

        before.InvestedCents.ShouldBe(574970);
        (await Summary(s)).ShouldBe(before);
    }

    [Fact]
    public async Task Account_can_go_negative_the_api_only_records()
    {
        var s = await Begin();
        await using var _ = s.Factory;

        await Create(s, OwnerBuy(s, s.Ion));

        (await Balance(s, s.Ion)).BalanceCents.ShouldBe(-574970);
    }

    [Fact]
    public async Task Future_dated_note_only_moves_the_projected_balance()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574970);

        await Create(s, Body("2026-11-10", [Line(s.Petr4, "Buy", 100, 5700)], s.Ion, feesCents: 4970));

        var balance = await Balance(s, s.Ion);
        (balance.BalanceCents, balance.ProjectedBalanceCents).ShouldBe((574970L, 0L));
    }

    [Fact]
    public async Task Credit_card_cannot_settle_a_note()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var card = await PostId(s.Client, "/accounts",
            new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });

        var response = await s.Client.PostAsJsonAsync("/trade-notes", OwnerBuy(s, card));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(response)).ShouldBe("O cartão de crédito não liquida nota de corretagem.");
        (await s.Client.GetFromJsonAsync<List<NoteDto>>("/trade-notes"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_or_other_users_account_looks_like_a_missing_account()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var other = await s.Factory.CreateAuthenticatedClientAsync();
        var othersAccount = await PostId(other, "/accounts", new { name = "Outra", type = "Investment", initialBalanceCents = 0 });

        (await s.Client.PostAsJsonAsync("/trade-notes", OwnerBuy(s, othersAccount))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Client.PostAsJsonAsync("/trade-notes", OwnerBuy(s, Guid.NewGuid()))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Settlements(s)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Settlement_is_not_edited_or_deleted_by_hand()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var note = await Create(s, OwnerBuy(s, s.Ion));
        var entry = (await Settlements(s)).Single();

        var edit = await Patch(s.Client, $"/transactions/{entry.Id}", new
        {
            accountId = s.Ion, type = "Expense", amountCents = 100, purchaseDate = "2026-10-22", method = "Pix",
        });
        var delete = await s.Client.DeleteAsync($"/transactions/{entry.Id}");

        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Detail(edit)).ShouldBe("Altere ou exclua a nota de corretagem.");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Detail(delete)).ShouldBe("Altere ou exclua a nota de corretagem.");
        (await Settlements(s)).Single().Id.ShouldBe(entry.Id);
        note.NetCashCents.ShouldBe(574970);
    }

    [Fact]
    public async Task Other_users_never_see_the_settlement()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Create(s, OwnerBuy(s, s.Ion));
        var other = await s.Factory.CreateAuthenticatedClientAsync();

        (await other.GetFromJsonAsync<List<EntryDto>>("/transactions?type=TradeSettlement"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Account_with_a_linked_note_is_not_deleted()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Create(s, Body("2026-09-01", [Line(s.Vale3, "Buy", 10, 10000)], null));
        await Create(s, Body("2026-10-01", [Line(s.Petr4, "Buy", 10, 10000), Line(s.Vale3, "Sell", 10, 10000)], s.Ion));
        // Líquido zero: não há lançamento, mas a nota aponta para a conta.

        var response = await s.Client.DeleteAsync($"/accounts/{s.Ion}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deleted_note_and_its_settlement_leave_the_database_together()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var note = await Create(s, OwnerBuy(s, s.Ion));
        var entry = (await Settlements(s)).Single();
        (await s.Client.DeleteAsync($"/trade-notes/{note.Id}")).EnsureSuccessStatusCode();

        _clock.UtcNow = Start + TimeSpan.FromMinutes(31);
        await s.Factory.Services.GetRequiredService<ErasureRunner>().RunAsync(CancellationToken.None);

        (await Count("trade_notes", note.Id)).ShouldBe(0);
        (await Count("transactions", entry.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Unlinked_settlement_is_purged_while_the_note_stays()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var note = await Create(s, OwnerBuy(s, s.Ion));
        var entry = (await Settlements(s)).Single();
        (await Update(s, note.Id, OwnerBuy(s, null))).EnsureSuccessStatusCode();

        _clock.UtcNow = Start + TimeSpan.FromMinutes(31);
        await s.Factory.Services.GetRequiredService<ErasureRunner>().RunAsync(CancellationToken.None);

        (await Count("transactions", entry.Id)).ShouldBe(0);
        (await Count("trade_notes", note.Id)).ShouldBe(1);
    }

    // Rede de segurança da limpeza: uma liquidação que ficou viva com a nota excluída sai junto com ela.
    [Fact]
    public async Task Purge_takes_a_live_settlement_of_a_deleted_note()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var note = await Create(s, OwnerBuy(s, s.Ion));
        var entry = (await Settlements(s)).Single();
        (await s.Client.DeleteAsync($"/trade-notes/{note.Id}")).EnsureSuccessStatusCode();
        await Execute("UPDATE transactions SET deleted_at = NULL WHERE id = @id", entry.Id);

        _clock.UtcNow = Start + TimeSpan.FromMinutes(31);
        await s.Factory.Services.GetRequiredService<ErasureRunner>().RunAsync(CancellationToken.None);

        (await Count("trade_notes", note.Id)).ShouldBe(0);
        (await Count("transactions", entry.Id)).ShouldBe(0);
    }

    // Rede de segurança da limpeza: conta excluída que uma nota ainda aponta não sai do banco.
    [Fact]
    public async Task Purge_keeps_an_account_that_a_note_still_points_to()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var note = await Create(s, Body("2026-10-01", [Line(s.Petr4, "Buy", 10, 10000)], s.Ion));
        (await s.Client.DeleteAsync($"/transactions/{(await Settlements(s)).Single().Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await Execute("UPDATE transactions SET deleted_at = @past WHERE trade_note_id = @id", note.Id);
        await Execute("UPDATE accounts SET deleted_at = @past WHERE id = @id", s.Ion);

        _clock.UtcNow = Start + TimeSpan.FromMinutes(31);
        await s.Factory.Services.GetRequiredService<ErasureRunner>().RunAsync(CancellationToken.None);

        (await Count("accounts", s.Ion)).ShouldBe(1);
        (await Count("trade_notes", note.Id)).ShouldBe(1);
    }

    // --- Em ativos (I.8c): o que a conta pôs em ativos, para o "Investido" ---

    [Fact]
    public async Task Linked_buy_puts_the_money_in_assets_so_the_total_does_not_drop()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574970);
        var total = async () => (await Balance(s, s.Itau)).BalanceCents + (await Balance(s, s.Ion)).BalanceCents
            + (await Balance(s, s.Itau)).AssetsCents + (await Balance(s, s.Ion)).AssetsCents;
        var before = await total();

        await Create(s, OwnerBuy(s, s.Ion));

        (await Balance(s, s.Ion)).AssetsCents.ShouldBe(574970);
        (await Balance(s, s.Itau)).AssetsCents.ShouldBe(0);
        (await total()).ShouldBe(before);
    }

    [Fact]
    public async Task Sells_take_from_assets_never_below_zero_and_unlinked_notes_do_not_count()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Create(s, Body("2026-10-01", [Line(s.Petr4, "Buy", 100, 5000)], null));
        (await Balance(s, s.Ion)).AssetsCents.ShouldBe(0);

        await Create(s, Body("2026-10-02", [Line(s.Vale3, "Buy", 10, 10000)], s.Ion));
        (await Balance(s, s.Ion)).AssetsCents.ShouldBe(100000);

        await Create(s, Body("2026-10-22", [Line(s.Petr4, "Sell", 100, 6000)], s.Ion));
        (await Balance(s, s.Ion)).AssetsCents.ShouldBe(0);
    }

    [Fact]
    public async Task A_checking_account_that_pays_the_note_has_the_money_in_assets_too()
    {
        var s = await Begin();
        await using var _ = s.Factory;

        await Create(s, OwnerBuy(s, s.Itau));

        (await Balance(s, s.Itau)).AssetsCents.ShouldBe(574970);
    }

    [Fact]
    public async Task A_future_note_is_not_in_assets_yet_and_cards_have_no_assets()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        var card = await PostId(s.Client, "/accounts",
            new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });

        await Create(s, Body("2026-11-10", [Line(s.Petr4, "Buy", 100, 5700)], s.Ion));

        (await Balance(s, s.Ion)).AssetsCents.ShouldBe(0);
        (await Balance(s, card)).AssetsCents.ShouldBeNull();
    }

    // --- Ajudantes ---

    private async Task Execute(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("past", Start.AddHours(-2));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Setup> Begin()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, null,
            services => services.AddScoped<IAssetListSource>(_ => _source));
        _source.Assets =
        [
            Listed("PETR4", AssetKind.Stock, "PETROBRAS PN"),
            Listed("VALE3", AssetKind.Stock, "VALE ON"),
        ];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        var client = await factory.CreateAuthenticatedClientAsync();
        var itau = await PostId(client, "/accounts", new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
        var ion = await PostId(client, "/accounts", new { name = "Íon", type = "Investment", initialBalanceCents = 0 });
        return new Setup(factory, client, await AssetId(client, "PETR4"), await AssetId(client, "VALE3"), itau, ion);
    }

    private static async Task Transfer(Setup s, long cents) =>
        (await s.Client.PostAsJsonAsync("/transfers", new
        {
            fromAccountId = s.Itau, toAccountId = s.Ion, amountCents = cents, date = "2026-10-20", method = "Pix",
        })).EnsureSuccessStatusCode();

    private static Task<HttpResponseMessage> Patch(HttpClient client, string url, object body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) });

    private static async Task<NoteDto> Create(Setup s, object body)
    {
        var response = await s.Client.PostAsJsonAsync("/trade-notes", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<NoteDto>())!;
    }

    private static Task<HttpResponseMessage> Update(Setup s, Guid id, object body) => s.Client.PutAsJsonAsync($"/trade-notes/{id}", body);

    private static async Task<BalanceDto> Balance(Setup s, Guid account) =>
        (await s.Client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.Single(b => b.AccountId == account);

    private static async Task<SummaryDto> Summary(Setup s) =>
        (await s.Client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-10"))!;

    private static async Task<List<EntryDto>> Settlements(Setup s) =>
        (await s.Client.GetFromJsonAsync<List<EntryDto>>("/transactions?type=TradeSettlement"))!;

    private static async Task<string?> Detail(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Detail;

    private static async Task<Guid> PostId(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> AssetId(HttpClient client, string symbol) =>
        (await client.GetFromJsonAsync<List<AssetDto>>($"/assets?q={symbol}"))!.First(a => a.Symbol == symbol).Id;

    private async Task<long> Count(string table, Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
