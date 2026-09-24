using Microsoft.EntityFrameworkCore;

namespace Prisma.Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
