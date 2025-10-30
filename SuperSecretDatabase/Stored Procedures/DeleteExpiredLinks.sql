CREATE PROCEDURE [dbo].[DeleteExpiredLinks]
AS
BEGIN
	DELETE FROM dbo.SingleUseLinks 
    WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= SYSUTCDATETIME();
                
    DELETE FROM dbo.MultiUseLinks 
    WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= SYSUTCDATETIME();
                
    SELECT @@ROWCOUNT AS TotalDeleted;
END
