CREATE PROCEDURE [dbo].[ConsumeSingleUseLink]
    @jti BINARY(16)
AS
BEGIN
 SET NOCOUNT ON;
    
 DELETE FROM [dbo].[SingleUseLinks]
   WHERE [Jti] = @jti 
    AND ([ExpiresAt] IS NULL OR [ExpiresAt] > SYSUTCDATETIME());
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
