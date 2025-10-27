CREATE PROCEDURE [dbo].[CreateMultiUseLink]
    @jti BINARY(16),
    @clicksLeft INT,
    @expiresAt DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO [dbo].[MultiUseLinks] ([Jti], [ClicksLeft], [ExpiresAt])
    VALUES (@jti, @clicksLeft, @expiresAt);
END
