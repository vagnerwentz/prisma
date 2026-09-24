using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Prisma.Architecture.Tests;

public sealed class DomainDependencyTests
{
    private static readonly Assembly DomainAssembly = Assembly.Load("Prisma.Domain");

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Npgsql")]
    public void Domain_does_not_depend_on_infrastructure(string forbiddenNamespace)
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Tipos do domínio dependem de {forbiddenNamespace}: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
