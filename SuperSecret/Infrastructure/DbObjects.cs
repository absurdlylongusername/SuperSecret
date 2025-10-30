namespace SuperSecret.Infrastructure;

public static class DbObjects
{
    public static class Procs
    {
        public const string CreateSingleUseLink = "dbo.CreateSingleUseLink";
        public const string CreateMultiUseLink = "dbo.CreateMultiUseLink";
        public const string ConsumeSingleUseLink = "dbo.ConsumeSingleUseLink";
        public const string ConsumeMultiUseLink = "dbo.ConsumeMultiUseLink";
        public const string DeleteExpiredLinks = "dbo.DeleteExpiredLinks";
    }

    public static class Tables
    {
        public const string SingleUseLinks = "dbo.SingleUseLinks";
        public const string MultiUseLinks = "dbo.MultiUseLinks";
    }
}