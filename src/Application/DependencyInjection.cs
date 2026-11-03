using FluentValidation;
using PharmaTrack.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace PharmaTrack.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining(typeof(DependencyInjection));
        ValidatorOptions.Global.LanguageManager = new ValidationLanguageManager();

        var serviceTypes = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Name.EndsWith("Service"));
        foreach (var type in serviceTypes) services.AddScoped(type);

        return services;
    }
}
