using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Prisma.Architecture.Tests;

// docs/fase-2.md, 2.15, D5: o calendário bancário tem dois usos de negócio, o débito automático (a série) e o
// vencimento da fatura (2.22). Pix, compra, parcela, estorno e transferência ficam na data em que
// aconteceram; um terceiro uso é decisão de produto, não detalhe de código.
//
// Mais um uso, de diagnóstico e não de negócio (docs/renda-fixa.md, D8, aprovada pelo dono em 2026-10-17): dizer quais dias o
// histórico do CDI deveria ter, para achar lacuna e defasagem. Mora no próprio namespace `Coverage`, liberado sozinho, e o
// cálculo do rendimento do CDI (Prisma.Domain.FixedIncome) continua proibido de usar o calendário.
//
// E um uso de negócio (docs/renda-fixa.md, 5.5, decisão do dono em 2026-10-18): o pré-fixado conta os dias úteis do prazo, porque
// não tem série de taxas para contar. Mora no namespace `Prefixed`, liberado sozinho; o motor do CDI e do CDI+ segue sem calendário.
public sealed class BankCalendarUsageTests
{
    private const string CalendarNamespace = "Prisma.Domain.Calendar";

    private static readonly string[] AllowedNamespaces =
    [
        CalendarNamespace,
        "Prisma.Domain.Recurrences",
        "Prisma.Domain.Statements",
        "Prisma.Domain.FixedIncome.Coverage",
        "Prisma.Domain.FixedIncome.Prefixed",
    ];

    [Fact]
    public void Only_recurrences_statements_and_the_rate_coverage_use_the_bank_calendar()
    {
        var violations = Types.InAssemblies([Assembly.Load("Prisma.Domain"), Assembly.Load("Prisma.Api")])
            .That()
            .HaveDependencyOn(CalendarNamespace)
            .GetTypes()
            .Where(type => !AllowedNamespaces.Contains(type.Namespace))
            .Select(type => type.FullName)
            .ToList();

        violations.ShouldBeEmpty("Só a série (débito automático), a fatura e o diagnóstico do CDI (Coverage) usam o calendário bancário.");
    }
}
