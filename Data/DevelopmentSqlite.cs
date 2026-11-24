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
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS ClientNotifications (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, SendKey TEXT NOT NULL,
                    RecipientId TEXT NULL REFERENCES AspNetUsers(Id), Title TEXT NOT NULL,
                    Body TEXT NOT NULL, CreatedAt TEXT NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ClientNotifications_SendKey ON ClientNotifications(SendKey);
                CREATE INDEX IF NOT EXISTS IX_ClientNotifications_RecipientId_Id ON ClientNotifications(RecipientId, Id);
                CREATE TABLE IF NOT EXISTS NotificationReads (
                    NotificationId INTEGER NOT NULL REFERENCES ClientNotifications(Id) ON DELETE CASCADE,
                    UserId TEXT NOT NULL REFERENCES AspNetUsers(Id), PRIMARY KEY (NotificationId, UserId));
                CREATE INDEX IF NOT EXISTS IX_NotificationReads_UserId ON NotificationReads(UserId);
                """);
            var categoryColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = "PRAGMA table_info('Categories');";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) categoryColumns.Add(reader.GetString(1));
            }
            if (!categoryColumns.Contains("IsHidden"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Categories ADD COLUMN IsHidden INTEGER NOT NULL DEFAULT 0;");
            var settingsColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = "PRAGMA table_info('StoreSettings');";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) settingsColumns.Add(reader.GetString(1));
            }
            if (!settingsColumns.Contains("HeroImageUrl"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN HeroImageUrl TEXT NULL;");
            if (!settingsColumns.Contains("FacebookUrl"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN FacebookUrl TEXT NULL;");
            if (!settingsColumns.Contains("InstagramUrl"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN InstagramUrl TEXT NULL;");
            if (!settingsColumns.Contains("TwitterUrl"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN TwitterUrl TEXT NULL;");
            if (!settingsColumns.Contains("WhatsAppUrl"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN WhatsAppUrl TEXT NULL;");
            if (!settingsColumns.Contains("CheckoutSessionLifetimeMinutes"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN CheckoutSessionLifetimeMinutes INTEGER NOT NULL DEFAULT 15;");
            if (!settingsColumns.Contains("FreeDeliveryOfferEnabled"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN FreeDeliveryOfferEnabled INTEGER NOT NULL DEFAULT 0;");
            if (!settingsColumns.Contains("FreeDeliveryOfferThreshold"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN FreeDeliveryOfferThreshold REAL NOT NULL DEFAULT 0;");
            if (!settingsColumns.Contains("FreeDeliveryOfferLabelEn"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN FreeDeliveryOfferLabelEn TEXT NULL;");
            if (!settingsColumns.Contains("FreeDeliveryOfferLabelAr"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE StoreSettings ADD COLUMN FreeDeliveryOfferLabelAr TEXT NULL;");
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
                CREATE TABLE IF NOT EXISTS EmailRateLimits (
                    Id INTEGER NOT NULL CONSTRAINT PK_EmailRateLimits PRIMARY KEY AUTOINCREMENT,
                    NormalizedEmail TEXT NOT NULL,
                    WindowStartUtc TEXT NOT NULL,
                    HourCount INTEGER NOT NULL,
                    LastSentUtc TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_EmailRateLimits_NormalizedEmail ON EmailRateLimits (NormalizedEmail);
                CREATE TABLE IF NOT EXISTS AccountEmailQueues (
                    Id INTEGER NOT NULL CONSTRAINT PK_AccountEmailQueues PRIMARY KEY AUTOINCREMENT,
                    RecipientEmail TEXT NOT NULL,
                    Subject TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    ChallengeKey TEXT NULL,
                    ExpiresAtUtc TEXT NULL,
                    Attempts INTEGER NOT NULL DEFAULT 0,
                    NextAttemptAtUtc TEXT NOT NULL,
                    SentAtUtc TEXT NULL,
                    LastError TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_AccountEmailQueues_SentAtUtc_NextAttemptAtUtc ON AccountEmailQueues (SentAtUtc, NextAttemptAtUtc);
                """);
        }
        finally
        {
            if (shouldClose) await conn.CloseAsync();
        }
    }
}
