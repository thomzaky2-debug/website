BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127195308_AddOffers'
)
BEGIN
    CREATE TABLE [Offers] (
        [Id] int NOT NULL IDENTITY,
        [TitleEn] nvarchar(150) NOT NULL,
        [TitleAr] nvarchar(150) NOT NULL,
        [BadgeEn] nvarchar(40) NULL,
        [BadgeAr] nvarchar(40) NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Value] decimal(18,2) NOT NULL,
        [Scope] nvarchar(20) NOT NULL,
        [CategoryId] int NULL,
        [ProductIds] nvarchar(2000) NULL,
        [MinSubtotal] decimal(18,2) NULL,
        [StartsAt] datetime2 NULL,
        [EndsAt] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Offers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127195308_AddOffers'
)
BEGIN
    CREATE INDEX [IX_Offers_IsActive_StartsAt_EndsAt] ON [Offers] ([IsActive], [StartsAt], [EndsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127195308_AddOffers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261127195308_AddOffers', N'10.0.15');
END;

COMMIT;
GO

