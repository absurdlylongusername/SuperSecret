CREATE PROCEDURE [dbo].[ConsumeMultiUseLink]
    @jti BINARY(16)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;

    UPDATE dbo.MultiUseLinks
    SET ClicksLeft = ClicksLeft - 1
    WHERE Jti = @jti
      AND ClicksLeft > 0
      AND (ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME());

    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK TRANSACTION;
        SELECT NULL AS Remaining;
        RETURN;
    END;

    DELETE FROM dbo.MultiUseLinks
    WHERE Jti = @jti AND ClicksLeft = 0;

    COMMIT TRANSACTION;

    SELECT COALESCE(
        (SELECT ClicksLeft FROM dbo.MultiUseLinks WHERE Jti = @jti),
        0
    ) AS Remaining;
END
