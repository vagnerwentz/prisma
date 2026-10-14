using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Privacy;

namespace Prisma.Api.Infrastructure.Configurations;

// As contas apagadas nos últimos 90 dias (etapa A.3). Sem chave estrangeira: o usuário já não existe.
public sealed class DeletedAccountConfiguration : IEntityTypeConfiguration<DeletedAccount>
{
    public void Configure(EntityTypeBuilder<DeletedAccount> builder)
    {
        builder.ToTable("deleted_accounts");
        builder.HasKey(d => d.UserId);
        builder.Property(d => d.UserId).ValueGeneratedNever();
        builder.HasIndex(d => d.DeletedAt);
    }
}
