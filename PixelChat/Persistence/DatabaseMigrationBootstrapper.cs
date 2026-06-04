using Microsoft.EntityFrameworkCore;
using System.Data;

namespace PixelChat.Persistence;

public static class DatabaseMigrationBootstrapper
{
    public static async Task MigrateAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
            return;

        await ClearStaleMigrationLockAsync(db, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task ClearStaleMigrationLockAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            if (!await TableExistsAsync(connection, "__EFMigrationsLock", cancellationToken))
                return;

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM \"__EFMigrationsLock\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = $tableName;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }
}
