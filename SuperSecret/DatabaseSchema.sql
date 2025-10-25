-- SuperSecret Database Schema

-- Create database (run this first if needed)
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'SuperSecretDb')
BEGIN
    CREATE DATABASE SuperSecretDb;
END
GO

USE SuperSecretDb;
GO

-- Single-use links table (presence-only)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SingleUseLinks' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.SingleUseLinks (
        Jti       CHAR(26)    NOT NULL PRIMARY KEY,
      CreatedAt DATETIME2   NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt DATETIME2   NULL
    );
    
    CREATE INDEX IX_SingleUse_Expires ON dbo.SingleUseLinks (ExpiresAt);
END
GO

-- Multi-use links table (countdown only)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MultiUseLinks' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.MultiUseLinks (
        Jti         CHAR(26)   NOT NULL PRIMARY KEY,
 ClicksLeft  INT        NOT NULL,
        CreatedAt   DATETIME2  NOT NULL DEFAULT SYSUTCDATETIME(),
        ExpiresAt   DATETIME2  NULL,
        CONSTRAINT CK_ClicksLeft_Positive CHECK (ClicksLeft >= 0)
    );
    
    CREATE INDEX IX_MultiUse_Expires ON dbo.MultiUseLinks (ExpiresAt);
END
GO

PRINT 'SuperSecret database schema created successfully!';
