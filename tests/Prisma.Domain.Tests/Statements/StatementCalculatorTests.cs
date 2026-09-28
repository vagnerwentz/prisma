using Prisma.Domain.Statements;
using Shouldly;

namespace Prisma.Domain.Tests.Statements;

// Valores esperados calculados à mão a partir de docs/fase-1.md, seção 2.1:
// 1. A compra entra no primeiro ciclo cujo fechamento é igual ou posterior a ela.
// 2. Dia maior que o mês vira o último dia do mês.
// 3. DueDay <= ClosingDay: o vencimento cai no mês seguinte ao do fechamento.
// 4. SettlementDate = DueDate.
// 5. Datas de fatura existente (editadas) prevalecem sobre o cálculo.
public sealed class StatementCalculatorTests
{
    private static readonly StatementDates[] None = [];

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static StatementDates S(string reference, DateOnly closing, DateOnly due) => new(reference, closing, due);

    // --- Regra 1: véspera, dia e dia seguinte ao fechamento (fecha 5, vence 12) ---

    [Fact]
    public void Purchase_on_the_eve_of_closing_enters_the_current_statement() =>
        StatementCalculator.ForPurchase(D(2026, 3, 4), 5, 12, None)
            .ShouldBe(S("2026-03", D(2026, 3, 5), D(2026, 3, 12)));

    [Fact]
    public void Purchase_on_the_closing_day_enters_the_current_statement() =>
        StatementCalculator.ForPurchase(D(2026, 3, 5), 5, 12, None)
            .ShouldBe(S("2026-03", D(2026, 3, 5), D(2026, 3, 12)));

    [Fact]
    public void Purchase_on_the_day_after_closing_enters_the_next_statement() =>
        StatementCalculator.ForPurchase(D(2026, 3, 6), 5, 12, None)
            .ShouldBe(S("2026-04", D(2026, 4, 5), D(2026, 4, 12)));

    // --- Regra 2: fechamento no dia 31 (vence dia 10, no mês seguinte) ---

    [Fact]
    public void Closing_on_31_closes_on_30_in_april() =>
        StatementCalculator.ForPurchase(D(2026, 4, 15), 31, 10, None)
            .ShouldBe(S("2026-05", D(2026, 4, 30), D(2026, 5, 10)));

    [Fact]
    public void In_april_the_30th_is_the_closing_day_and_the_1st_of_may_is_the_next_cycle()
    {
        StatementCalculator.ForPurchase(D(2026, 4, 30), 31, 10, None)
            .ShouldBe(S("2026-05", D(2026, 4, 30), D(2026, 5, 10)));
        StatementCalculator.ForPurchase(D(2026, 5, 1), 31, 10, None)
            .ShouldBe(S("2026-06", D(2026, 5, 31), D(2026, 6, 10)));
    }

    [Fact]
    public void Closing_on_31_closes_on_28_in_a_common_february()
    {
        StatementCalculator.ForPurchase(D(2026, 2, 20), 31, 10, None)
            .ShouldBe(S("2026-03", D(2026, 2, 28), D(2026, 3, 10)));
        StatementCalculator.ForPurchase(D(2026, 3, 1), 31, 10, None)
            .ShouldBe(S("2026-04", D(2026, 3, 31), D(2026, 4, 10)));
    }

    [Fact]
    public void Closing_on_31_closes_on_29_in_a_leap_february()
    {
        StatementCalculator.ForPurchase(D(2028, 2, 20), 31, 10, None)
            .ShouldBe(S("2028-03", D(2028, 2, 29), D(2028, 3, 10)));
        StatementCalculator.ForPurchase(D(2028, 2, 29), 31, 10, None)
            .ShouldBe(S("2028-03", D(2028, 2, 29), D(2028, 3, 10)));
    }

    [Fact]
    public void Due_day_beyond_the_month_is_clamped_too()
    {
        // Fecha 20, vence 31 (mesmo mês, pois 31 > 20): em abril vence dia 30.
        StatementCalculator.ForPurchase(D(2026, 4, 10), 20, 31, None)
            .ShouldBe(S("2026-04", D(2026, 4, 20), D(2026, 4, 30)));

        // Fecha 31, vence 31 (mês seguinte, pois 31 <= 31): fecha 31/01, vence 28/02.
        StatementCalculator.ForPurchase(D(2026, 1, 10), 31, 31, None)
            .ShouldBe(S("2026-02", D(2026, 1, 31), D(2026, 2, 28)));
    }

    // --- Regra 3: vencimento antes (ou no mesmo dia) do fechamento cai no mês seguinte ---

    [Fact]
    public void Due_day_before_closing_day_falls_in_the_next_month() =>
        StatementCalculator.ForPurchase(D(2026, 1, 10), 25, 5, None)
            .ShouldBe(S("2026-02", D(2026, 1, 25), D(2026, 2, 5)));

    [Fact]
    public void Due_day_equal_to_closing_day_falls_in_the_next_month() =>
        StatementCalculator.ForPurchase(D(2026, 1, 10), 10, 10, None)
            .ShouldBe(S("2026-02", D(2026, 1, 10), D(2026, 2, 10)));

    [Fact]
    public void Due_day_after_closing_day_falls_in_the_same_month() =>
        StatementCalculator.ForPurchase(D(2026, 1, 10), 15, 25, None)
            .ShouldBe(S("2026-01", D(2026, 1, 15), D(2026, 1, 25)));

    [Fact]
    public void Cycles_cross_the_year()
    {
        StatementCalculator.ForPurchase(D(2026, 12, 10), 5, 12, None)
            .ShouldBe(S("2027-01", D(2027, 1, 5), D(2027, 1, 12)));
        StatementCalculator.ForPurchase(D(2026, 12, 26), 25, 5, None)
            .ShouldBe(S("2027-02", D(2027, 1, 25), D(2027, 2, 5)));
    }

    // --- Parcelas (regra 2.2): a parcela i entra i-1 ciclos após o statement da compra ---

    [Fact]
    public void First_installment_enters_the_purchase_statement() =>
        StatementCalculator.ForInstallment(D(2026, 3, 10), 1, 5, 12, None)
            .ShouldBe(StatementCalculator.ForPurchase(D(2026, 3, 10), 5, 12, None));

    [Fact]
    public void Installment_10_of_10_enters_the_10th_statement_9_cycles_after_the_purchase() =>
        // Compra em 10/03 cai na fatura de abril (fecha 05/04); 9 ciclos depois: janeiro de 2027.
        StatementCalculator.ForInstallment(D(2026, 3, 10), 10, 5, 12, None)
            .ShouldBe(S("2027-01", D(2027, 1, 5), D(2027, 1, 12)));

    [Fact]
    public void Ten_installments_fall_in_ten_consecutive_statements()
    {
        var references = Enumerable.Range(1, 10)
            .Select(i => StatementCalculator.ForInstallment(D(2026, 3, 10), i, 5, 12, None).Reference);

        references.ShouldBe([
            "2026-04", "2026-05", "2026-06", "2026-07", "2026-08",
            "2026-09", "2026-10", "2026-11", "2026-12", "2027-01",
        ]);
    }

    [Fact]
    public void Installments_follow_month_lengths() =>
        // Fecha 31: parcela 2 de uma compra em 15/01 fecha no último dia de fevereiro.
        StatementCalculator.ForInstallment(D(2026, 1, 15), 2, 31, 10, None)
            .ShouldBe(S("2026-03", D(2026, 2, 28), D(2026, 3, 10)));

    // --- Regra 5: datas editadas prevalecem ---

    [Fact]
    public void Postponed_closing_keeps_the_purchase_in_the_edited_statement()
    {
        // Fechamento adiado de 05/03 para 07/03: compra em 06/03 ainda entra na fatura de março.
        var edited = S("2026-03", D(2026, 3, 7), D(2026, 3, 14));

        StatementCalculator.ForPurchase(D(2026, 3, 6), 5, 12, [edited]).ShouldBe(edited);
    }

    [Fact]
    public void Anticipated_closing_moves_the_purchase_to_the_next_statement()
    {
        // Fechamento antecipado de 05/03 para 03/03: compra em 04/03 vai para abril.
        var edited = S("2026-03", D(2026, 3, 3), D(2026, 3, 12));

        StatementCalculator.ForPurchase(D(2026, 3, 4), 5, 12, [edited])
            .ShouldBe(S("2026-04", D(2026, 4, 5), D(2026, 4, 12)));
    }

    [Fact]
    public void Edited_due_date_becomes_the_settlement_date()
    {
        var edited = S("2026-03", D(2026, 3, 5), D(2026, 3, 16));

        StatementCalculator.ForPurchase(D(2026, 3, 4), 5, 12, [edited]).DueDate.ShouldBe(D(2026, 3, 16));
    }

    [Fact]
    public void Installment_landing_on_an_edited_statement_uses_its_dates()
    {
        var edited = S("2026-06", D(2026, 6, 8), D(2026, 6, 15));

        StatementCalculator.ForInstallment(D(2026, 3, 10), 3, 5, 12, [edited]).ShouldBe(edited);
    }

    [Fact]
    public void Statements_of_other_cycles_do_not_interfere()
    {
        var unrelated = S("2026-09", D(2026, 9, 1), D(2026, 9, 20));

        StatementCalculator.ForPurchase(D(2026, 3, 4), 5, 12, [unrelated])
            .ShouldBe(S("2026-03", D(2026, 3, 5), D(2026, 3, 12)));
    }

    // --- Argumentos inválidos são erro de programação ---

    [Theory]
    [InlineData(0, 12)]
    [InlineData(32, 12)]
    [InlineData(5, 0)]
    [InlineData(5, 32)]
    public void Rejects_days_outside_1_to_31(int closingDay, int dueDay) =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            StatementCalculator.ForPurchase(D(2026, 3, 4), closingDay, dueDay, None));

    [Fact]
    public void Rejects_installment_number_below_1() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            StatementCalculator.ForInstallment(D(2026, 3, 4), 0, 5, 12, None));

    // --- Datas de uma fatura pela referência (mês do vencimento), para abrir a fatura de destino
    //     ao mover uma compra (docs/fase-2.md, 2.9, regra 2) ---

    [Fact]
    public void Reference_of_a_card_that_is_due_after_closing_in_the_next_month() =>
        StatementCalculator.ForReference("2026-11", 26, 5)
            .ShouldBe(S("2026-11", D(2026, 10, 26), D(2026, 11, 5)));

    [Fact]
    public void Reference_of_a_card_that_is_due_in_the_same_month() =>
        StatementCalculator.ForReference("2026-03", 5, 12)
            .ShouldBe(S("2026-03", D(2026, 3, 5), D(2026, 3, 12)));

    [Fact]
    public void Reference_clamps_days_beyond_the_month() =>
        StatementCalculator.ForReference("2026-03", 31, 10)
            .ShouldBe(S("2026-03", D(2026, 2, 28), D(2026, 3, 10)));

    [Fact]
    public void Reference_agrees_with_the_purchase_calculation() =>
        StatementCalculator.ForReference(StatementCalculator.ForPurchase(D(2026, 9, 25), 26, 5, None).Reference, 26, 5)
            .ShouldBe(StatementCalculator.ForPurchase(D(2026, 9, 25), 26, 5, None));
}
