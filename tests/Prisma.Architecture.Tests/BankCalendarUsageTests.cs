using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Prisma.Architecture.Tests;

// docs/fase-2.md, 2.15, D5: o calendário bancário tem dois usos só, o débito automático (a série) e o
// vencimento da fatura (2.22). Pix, compra, parcela, estorno e transferência ficam na data em que
// aconteceram; um terceiro uso é decisão de produto, não detalhe de código.
public sealed class BankCalendarUsageTests
{
    private const string CalendarNamespace = "Prisma.Domain.Calendar";

    private static readonly string[] AllowedNamespaces =
    [
        CalendarNamespace,
        "Prisma.Domain.Recurrences",
        "Prisma.Domain.Statements",
    ];

    [Fact]
    public void Only_recurrences_and_statements_use_the_bank_calendar()
    {
        var violations = Types.InAssemblies([Assembly.Load("Prisma.Domain"), Assembly.Load("Prisma.Api")])
            .That()
            .HaveDependencyOn(CalendarNamespace)
            .GetTypes()
            .Where(type => !AllowedNamespaces.Contains(type.Namespace))
            .Select(type => type.FullName)
            .ToList();

        violations.ShouldBeEmpty("Só a série (débito automático) e a fatura usam o calendário bancário.");
    }
}
