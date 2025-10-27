CREATE TABLE [dbo].[MultiUseLinks]
(
    [Jti] BINARY(16) NOT NULL PRIMARY KEY,
    [ClicksLeft] INT NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_MultiUseLinks_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [ExpiresAt] DATETIME2 NULL,
    CONSTRAINT [CK_ClicksLeft_Positive] CHECK ([ClicksLeft] >= 0),
    INDEX [IX_MultiUse_Expires] NONCLUSTERED ([ExpiresAt])
);