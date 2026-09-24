using FluentValidation;

namespace Prisma.Api.Features.Categories;

public static class CategoriesModule
{
    public static IServiceCollection AddCategoryFeatures(this IServiceCollection services)
    {
        services.AddScoped<ListCategories.Handler>();
        services.AddScoped<CreateCategory.Handler>();
        services.AddScoped<UpdateCategory.Handler>();
        services.AddScoped<DeleteCategory.Handler>();

        services.AddSingleton<IValidator<CreateCategory.Request>, CreateCategory.Validator>();

        return services;
    }

    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories");
        ListCategories.Map(group);
        CreateCategory.Map(group);
        UpdateCategory.Map(group);
        DeleteCategory.Map(group);
    }
}
