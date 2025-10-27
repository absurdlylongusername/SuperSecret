CREATE PROCEDURE [dbo].[CreateSingleUseLink]
    @jti BINARY(16),
    @expiresAt DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO [dbo].[SingleUseLinks] ([Jti], [ExpiresAt])
    VALUES (@jti, @expiresAt);
END
