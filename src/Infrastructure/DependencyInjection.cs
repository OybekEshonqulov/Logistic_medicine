using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaTrack.Application.Common;
using PharmaTrack.Infrastructure.Files;
using PharmaTrack.Infrastructure.Persistence;
using PharmaTrack.Infrastructure.Security;
using PharmaTrack.Infrastructure.Stock;

namespace PharmaTrack.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(configuration.GetConnectionString("Default"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<AuditInterceptor>()));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<DbSeeder>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISpreadsheet, ClosedXmlSpreadsheet>();
        services.AddScoped<IStockLedger, StockLedger>();

        return services;
    }
}
