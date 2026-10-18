using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Features.FixedIncome;

// Criar e editar um objetivo (docs/renda-fixa.md, 2.2). O domínio decide o que vale (nome, meta, percentual); aqui se carrega,
// grava e traduz o nome repetido (índice único, sem diferenciar maiúsculas) em conflito.
public static class SaveGoal
{
    public sealed record Request(string Name, string? Description, long? TargetCents, long? DefaultIndexPercentE4);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Name).NotEmpty().WithMessage("Dê um nome ao objetivo.");
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, ILogger<Handler> logger)
    {
        public async Task<Result<GoalResponse>> Create(Request req, CancellationToken ct)
        {
            var created = SavingsGoal.Create(currentUser.UserId, req.Name, req.Description, req.TargetCents, req.DefaultIndexPercentE4);
            if (!created.IsSuccess)
                return created.Error;

            db.SavingsGoals.Add(created.Value);
            if (await SaveOrConflict(ct) is { } conflict)
                return conflict;

            logger.GoalCreated(created.Value.Id);
            return await Goals.Summary(db, created.Value, ct);
        }

        public async Task<Result<GoalResponse>> Update(Guid id, Request req, CancellationToken ct)
        {
            var goal = await db.SavingsGoals.SingleOrDefaultAsync(g => g.Id == id, ct);
            if (goal is null)
                return new Error(ErrorType.NotFound, "Objetivo não encontrado.");

            var error = goal.Update(req.Name, req.Description, req.TargetCents, req.DefaultIndexPercentE4);
            if (error is not null)
                return error;
            if (await SaveOrConflict(ct) is { } conflict)
                return conflict;

            logger.GoalEdited(goal.Id);
            return await Goals.Summary(db, goal, ct);
        }

        private async Task<Error?> SaveOrConflict(CancellationToken ct)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return null;
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(SavingsGoalConfiguration.NameIndex))
            {
                return new Error(ErrorType.Conflict, "Já existe um objetivo com esse nome.", "GoalNameTaken");
            }
        }
    }

    public static void MapCreate(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Create(request, ct);
                return result.IsSuccess ? Results.Created((string?)null, result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<GoalResponse>(201)
            .ProducesValidationProblem()
            .ProducesProblem(409);

    public static void MapUpdate(IEndpointRouteBuilder app) =>
        app.MapPut("/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Update(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<GoalResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);
}
