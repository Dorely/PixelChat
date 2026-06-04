using Microsoft.EntityFrameworkCore;

namespace PixelChat.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPixelChatPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var providerName = configuration["Persistence:Provider"] ?? "Sqlite";
        var connectionString = SqliteConnectionSettings.BuildConnectionString(configuration);

        services.AddDbContext<AppDbContext>(options =>
        {
            switch (providerName)
            {
                case "Sqlite":
                    options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(SqliteConnectionSettings.BusyTimeoutSeconds));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported persistence provider '{providerName}'. Supported values: Sqlite.");
            }
        });

        return services;
    }
}
