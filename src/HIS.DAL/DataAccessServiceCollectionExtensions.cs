using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HIS.DAL;

public static class DataAccessServiceCollectionExtensions
{
    static DataAccessServiceCollectionExtensions()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public static IServiceCollection AddHisDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var rawConnectionString = configuration.GetConnectionString("HisDb")
            ?? configuration["DATABASE_URL"];
        var connectionString = NormalizeConnectionString(rawConnectionString);

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            })
            .UseSnakeCaseNamingConvention();
        });

        return services;
    }

    public static string NormalizeConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        raw = raw.Trim();
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            var userInfo = uri.UserInfo.Split(':', 2);
            var username = Uri.UnescapeDataString(userInfo[0]);
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var database = uri.AbsolutePath.TrimStart('/');

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = database,
                Username = username,
                Password = password,
                SslMode = SslMode.Require
            };

            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                var query = uri.Query.TrimStart('?');
                var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var kv = pair.Split('=', 2);
                    if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse<SslMode>(kv[1], true, out var parsedMode))
                        {
                            builder.SslMode = parsedMode;
                        }
                    }
                }
            }

            return builder.ConnectionString;
        }

        return raw;
    }
}

