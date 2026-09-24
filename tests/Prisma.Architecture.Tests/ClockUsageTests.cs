using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Prisma.Domain;
using Shouldly;

namespace Prisma.Architecture.Tests;

// O NetArchTest enxerga dependência de tipo, não de membro: não distingue DateTime.Now de um
// campo DateTime legítimo como CreatedAt. Por isso esta regra inspeciona o IL com Mono.Cecil,
// que é a biblioteca sobre a qual o próprio NetArchTest é construído.
public sealed class ClockUsageTests
{
    private static readonly HashSet<(string Type, string Member)> ForbiddenMembers =
    [
        ("System.DateTime", "get_Now"),
        ("System.DateTime", "get_UtcNow"),
        ("System.DateTime", "get_Today"),
        ("System.DateTimeOffset", "get_Now"),
        ("System.DateTimeOffset", "get_UtcNow"),
    ];

    [Theory]
    [InlineData("Prisma.Domain")]
    [InlineData("Prisma.Api")]
    public void Only_the_IClock_implementation_reads_the_system_clock(string assemblyName)
    {
        var location = Assembly.Load(assemblyName).Location;
        using var module = ModuleDefinition.ReadModule(location);

        var violations = module.GetTypes()
            .Where(type => !IsClockImplementation(type))
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions
                .Where(instruction => instruction.Operand is MethodReference called && IsForbidden(called))
                .Select(instruction => $"{method.DeclaringType.FullName}.{method.Name} usa " +
                                       $"{((MethodReference)instruction.Operand).DeclaringType.Name}." +
                                       $"{((MethodReference)instruction.Operand).Name[4..]}"))
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty("Use IClock em vez do relógio do sistema.");
    }

    private static bool IsForbidden(MethodReference method) =>
        ForbiddenMembers.Contains((method.DeclaringType.FullName, method.Name));

    // Tipos aninhados (lambdas, máquinas de estado de async) herdam a isenção do tipo externo.
    private static bool IsClockImplementation(TypeDefinition? type)
    {
        for (; type is not null; type = type.DeclaringType)
        {
            if (type.Interfaces.Any(i => i.InterfaceType.FullName == typeof(IClock).FullName))
                return true;
        }

        return false;
    }
}
