using Mono.Cecil;
using Mono.Cecil.Cil;
using Prisma.Api.Infrastructure;
using Shouldly;

namespace Prisma.Architecture.Tests;

// CLAUDE.md, regra 6: o filtro de dono nunca é ignorado. A única exceção permitida é
// IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]), para restaurar registros.
public sealed class QueryFilterTests
{
    private static readonly ModuleDefinition Api =
        ModuleDefinition.ReadModule(typeof(AppDbContext).Assembly.Location);

    private static IEnumerable<(MethodDefinition Method, Instruction Instruction)> Instructions() =>
        Api.GetTypes()
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions.Select(instruction => (method, instruction)));

    [Fact]
    public void Nobody_ignores_all_query_filters()
    {
        var violations = Instructions()
            .Where(x => x.Instruction.Operand is MethodReference
            {
                Name: "IgnoreQueryFilters", Parameters.Count: 1,
            })
            .Select(x => $"{x.Method.DeclaringType.FullName}.{x.Method.Name}")
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty("Use IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]), nunca a versão sem argumentos.");
    }

    [Fact]
    public void The_owner_filter_name_is_only_used_inside_AppDbContext()
    {
        var violations = Instructions()
            .Where(x => x.Instruction.OpCode == OpCodes.Ldstr
                        && (string)x.Instruction.Operand == AppDbContext.OwnerFilter
                        && !IsInside(x.Method.DeclaringType, typeof(AppDbContext).FullName!))
            .Select(x => $"{x.Method.DeclaringType.FullName}.{x.Method.Name}")
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty("O filtro de dono não pode ser referenciado fora do AppDbContext.");
    }

    // Lambdas e máquinas de estado geradas pelo compilador são tipos aninhados.
    private static bool IsInside(TypeDefinition? type, string fullName)
    {
        for (; type is not null; type = type.DeclaringType)
        {
            if (type.FullName == fullName)
                return true;
        }

        return false;
    }
}
