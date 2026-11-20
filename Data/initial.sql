IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] int NOT NULL IDENTITY,
        [UserEmail] nvarchar(100) NULL,
        [Action] nvarchar(50) NOT NULL,
        [EntityName] nvarchar(100) NULL,
        [Details] nvarchar(500) NULL,
        [IpAddress] nvarchar(45) NULL,
        [Timestamp] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] int NOT NULL IDENTITY,
        [Slug] nvarchar(150) NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] int NOT NULL IDENTITY,
        [OrderNumber] nvarchar(40) NOT NULL,
        [CheckoutToken] nvarchar(64) NOT NULL,
        [CustomerName] nvarchar(150) NOT NULL,
        [CustomerEmail] nvarchar(254) NOT NULL,
        [CustomerPhone] nvarchar(30) NOT NULL,
        [ShippingAddress] nvarchar(1000) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Order_Status] CHECK ([Status] IN ('Pending','Processing','Shipped','Cancelled'))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [CategoryTranslations] (
        [Id] int NOT NULL IDENTITY,
        [CategoryId] int NOT NULL,
        [LanguageCode] nvarchar(5) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        CONSTRAINT [PK_CategoryTranslations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CategoryTranslation_Language] CHECK ([LanguageCode] IN ('en','ar')),
        CONSTRAINT [FK_CategoryTranslations_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] int NOT NULL IDENTITY,
        [Slug] nvarchar(200) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [OldPrice] decimal(18,2) NULL,
        [StockQuantity] int NOT NULL,
        [ImageUrl] nvarchar(500) NULL,
        [CategoryId] int NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsFeatured] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Product_Price] CHECK ([Price] >= 0 AND ([OldPrice] IS NULL OR [OldPrice] >= [Price])),
        CONSTRAINT [CK_Product_Stock] CHECK ([StockQuantity] >= 0),
        CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderItems] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [ProductId] int NOT NULL,
        [ProductName] nvarchar(250) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderItem_Quantity] CHECK ([Quantity] > 0 AND [UnitPrice] >= 0),
        CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrderItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductTranslations] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [LanguageCode] nvarchar(5) NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [ShortDescription] nvarchar(500) NULL,
        [FullDescription] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_ProductTranslations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductTranslation_Language] CHECK ([LanguageCode] IN ('en','ar')),
        CONSTRAINT [FK_ProductTranslations_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_Slug] ON [Categories] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CategoryTranslations_CategoryId_LanguageCode] ON [CategoryTranslations] ([CategoryId], [LanguageCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_OrderId] ON [OrderItems] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_ProductId] ON [OrderItems] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Orders_CheckoutToken] ON [Orders] ([CheckoutToken]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CreatedAt] ON [Orders] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Orders_OrderNumber] ON [Orders] ([OrderNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_IsActive_IsFeatured_CreatedAt] ON [Products] ([IsActive], [IsFeatured], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Products_Slug] ON [Products] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductTranslations_ProductId_LanguageCode] ON [ProductTranslations] ([ProductId], [LanguageCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193629_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113193629_InitialCreate', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    ALTER TABLE [Orders] ADD [PaidAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    ALTER TABLE [Orders] ADD [PaymentAccessToken] nvarchar(64) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    ALTER TABLE [Orders] ADD [PaymentStatus] nvarchar(20) NOT NULL DEFAULT N'Unpaid';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    UPDATE [Orders] SET [PaymentAccessToken] = CONVERT(varchar(64), CRYPT_GEN_RANDOM(32), 2) WHERE [PaymentAccessToken] = N''
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    CREATE TABLE [PaymentAttempts] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [IdempotencyKey] nvarchar(128) NOT NULL,
        [RequestJson] nvarchar(max) NOT NULL,
        [SessionId] nvarchar(200) NULL,
        [CheckoutUrl] nvarchar(2000) NULL,
        [GatewayStatus] nvarchar(30) NOT NULL,
        [LastCheckedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PaymentAttempts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentAttempts_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    CREATE TABLE [PaymentEvents] (
        [Id] int NOT NULL IDENTITY,
        [EventId] nvarchar(200) NOT NULL,
        [PaymentAttemptId] int NOT NULL,
        [ReceivedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PaymentEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentEvents_PaymentAttempts_PaymentAttemptId] FOREIGN KEY ([PaymentAttemptId]) REFERENCES [PaymentAttempts] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    CREATE INDEX [IX_Orders_PaymentAccessToken] ON [Orders] ([PaymentAccessToken]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentAttempts_IdempotencyKey] ON [PaymentAttempts] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentAttempts_OrderId] ON [PaymentAttempts] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PaymentAttempts_SessionId] ON [PaymentAttempts] ([SessionId]) WHERE [SessionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentEvents_EventId] ON [PaymentEvents] ([EventId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    CREATE INDEX [IX_PaymentEvents_PaymentAttemptId] ON [PaymentEvents] ([PaymentAttemptId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113195458_NilePayPayments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113195458_NilePayPayments', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113202618_InstaPaySenderHandle'
)
BEGIN
    ALTER TABLE [Orders] ADD [InstaPaySenderHandle] nvarchar(109) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113202618_InstaPaySenderHandle'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113202618_InstaPaySenderHandle', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Products] ADD [Colors] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Products] ADD [GalleryImages] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Products] ADD [Sizes] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [Carrier] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [DeliveryMaxDays] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [DeliveryMinDays] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [InventoryReleased] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [NeedsPaymentReview] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [RefundReference] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [RefundedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [ShippingFee] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [ShippingMethodName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [TrackingNumber] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [Orders] ADD [TrackingUrl] nvarchar(800) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [SelectedColor] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [SelectedSize] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    CREATE TABLE [OrderEmails] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAt] datetime2 NOT NULL,
        [SentAt] datetime2 NULL,
        [LastError] nvarchar(100) NULL,
        CONSTRAINT [PK_OrderEmails] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderEmails_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    CREATE TABLE [OrderUpdates] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_OrderUpdates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderUpdates_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    CREATE TABLE [ShippingMethods] (
        [Id] int NOT NULL IDENTITY,
        [NameEn] nvarchar(100) NOT NULL,
        [NameAr] nvarchar(100) NOT NULL,
        [Fee] decimal(18,2) NOT NULL,
        [MinDays] int NOT NULL,
        [MaxDays] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_ShippingMethods] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Shipping_Fee] CHECK ([Fee] >= 0 AND [MaxDays] >= [MinDays])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    CREATE TABLE [StoreSettings] (
        [Id] int NOT NULL IDENTITY,
        [ContactEmail] nvarchar(254) NULL,
        [ContactPhone] nvarchar(30) NULL,
        [AddressEn] nvarchar(500) NULL,
        [AddressAr] nvarchar(500) NULL,
        [ReturnDays] int NOT NULL,
        [DeliveryPolicyEn] nvarchar(max) NULL,
        [DeliveryPolicyAr] nvarchar(max) NULL,
        [ReturnsPolicyEn] nvarchar(max) NULL,
        [ReturnsPolicyAr] nvarchar(max) NULL,
        CONSTRAINT [PK_StoreSettings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderEmails_OrderId_Kind] ON [OrderEmails] ([OrderId], [Kind]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    CREATE INDEX [IX_OrderEmails_SentAt_NextAttemptAt] ON [OrderEmails] ([SentAt], [NextAttemptAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    CREATE INDEX [IX_OrderUpdates_OrderId] ON [OrderUpdates] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113210852_NileShopEnhancements'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113210852_NileShopEnhancements', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113212902_AdminMailSettings'
)
BEGIN
    CREATE TABLE [StoreMailSettings] (
        [Id] int NOT NULL IDENTITY,
        [Enabled] bit NOT NULL,
        [Host] nvarchar(255) NOT NULL,
        [Port] int NOT NULL,
        [Username] nvarchar(255) NOT NULL,
        [EncryptedPassword] nvarchar(max) NOT NULL,
        [From] nvarchar(254) NOT NULL,
        [EnableSsl] bit NOT NULL,
        CONSTRAINT [PK_StoreMailSettings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113212902_AdminMailSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113212902_AdminMailSettings', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    ALTER TABLE [Orders] ADD [CustomerUserId] nvarchar(450) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    CREATE TABLE [ProductReviews] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ProductId] int NOT NULL,
        [Rating] int NOT NULL,
        [DisplayName] nvarchar(100) NOT NULL,
        [Text] nvarchar(1000) NOT NULL,
        [IsApproved] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductReviews] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Review_Rating] CHECK ([Rating] >= 1 AND [Rating] <= 5),
        CONSTRAINT [FK_ProductReviews_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductReviews_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    CREATE TABLE [SavedProducts] (
        [UserId] nvarchar(450) NOT NULL,
        [ProductId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SavedProducts] PRIMARY KEY ([UserId], [ProductId]),
        CONSTRAINT [FK_SavedProducts_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SavedProducts_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerUserId] ON [Orders] ([CustomerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    CREATE INDEX [IX_ProductReviews_ProductId_IsApproved] ON [ProductReviews] ([ProductId], [IsApproved]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductReviews_UserId_ProductId] ON [ProductReviews] ([UserId], [ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    CREATE INDEX [IX_SavedProducts_ProductId] ON [SavedProducts] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_AspNetUsers_CustomerUserId] FOREIGN KEY ([CustomerUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114220722_CustomerExperience'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261114220722_CustomerExperience', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119120000_HomepageHeroImage'
)
BEGIN
    ALTER TABLE [StoreSettings] ADD [HeroImageUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119120000_HomepageHeroImage'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261119120000_HomepageHeroImage', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119130000_CategoryVisibility'
)
BEGIN
    ALTER TABLE [Categories] ADD [IsHidden] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119130000_CategoryVisibility'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261119130000_CategoryVisibility', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    ALTER TABLE [StoreSettings] ADD [FacebookUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    ALTER TABLE [StoreSettings] ADD [InstagramUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    ALTER TABLE [StoreSettings] ADD [TwitterUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    ALTER TABLE [StoreSettings] ADD [WhatsAppUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    ALTER TABLE [Orders] ADD [DetectedAmountEgp] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    ALTER TABLE [Orders] ADD [WalletAmountUsed] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    CREATE TABLE [WalletTransactions] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [OrderId] int NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [ReferenceKey] nvarchar(120) NULL,
        [Description] nvarchar(300) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WalletTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WalletTransactions_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WalletTransactions_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_OrderId] ON [WalletTransactions] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WalletTransactions_ReferenceKey] ON [WalletTransactions] ([ReferenceKey]) WHERE [ReferenceKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_UserId_CreatedAt] ON [WalletTransactions] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119171250_MissingProductionSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261119171250_MissingProductionSchema', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120010635_SupportedLtsSecurityBaseline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120010635_SupportedLtsSecurityBaseline', N'10.0.15');
END;

COMMIT;
GO
