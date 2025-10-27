CREATE TABLE [dbo].[SingleUseLinks]
(
    [Jti] BINARY(16) NOT NULL PRIMARY KEY,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_SingleUseLinks_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [ExpiresAt] DATETIME2 NULL,
    INDEX [IX_SingleUse_Expires] NONCLUSTERED ([ExpiresAt])
);
