using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Data;
public static class DevelopmentSqlite
{
    public static void Initialize()
    {
        if (OperatingSystem.IsLinux())
            NativeLibrary.SetDllImportResolver(typeof(SQLitePCL.SQLite3Provider_sqlite3).Assembly,
                (name, assembly, path) => name == "sqlite3" ? NativeLibrary.Load("libsqlite3.so.0") : IntPtr.Zero);
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
    }

    public static async Task EnsureWalletSchemaAsync(ApplicationDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync();
        try
        {
            var orderColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('Orders');";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    orderColumns.Add(reader.GetString(1));
            }
            if (!orderColumns.Contains("WalletAmountUsed"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Orders ADD COLUMN WalletAmountUsed REAL NOT NULL DEFAULT 0;");
            if (!orderColumns.Contains("DetectedAmountEgp"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Orders ADD COLUMN DetectedAmountEgp REAL NULL;");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS WalletTransactions (
                    Id INTEGER NOT NULL CONSTRAINT PK_WalletTransactions PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NOT NULL,
                    OrderId INTEGER NULL,
                    Amount REAL NOT NULL,
                    Kind TEXT NOT NULL,
                    ReferenceKey TEXT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_WalletTransactions_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES AspNetUsers (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_WalletTransactions_Orders_OrderId FOREIGN KEY (OrderId) REFERENCES Orders (Id) ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS IX_WalletTransactions_UserId_CreatedAt ON WalletTransactions (UserId, CreatedAt);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_WalletTransactions_ReferenceKey ON WalletTransactions (ReferenceKey) WHERE ReferenceKey IS NOT NULL;
                CREATE INDEX IF NOT EXISTS IX_WalletTransactions_OrderId ON WalletTransactions (OrderId);
                """);
        }
        finally
        {
            if (shouldClose) await conn.CloseAsync();
        }
    }
}
