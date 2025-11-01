namespace SuperSecret.Validators;

public static class ValidationMessages
{
    // Username validation messages
    public const string UsernameRequired = "Username is required";
    public const string UsernameLength = "Username must be 1-50 characters";
    public const string UsernameAlphanumeric = "Username must be alphanumeric only";
    public const string UsernameLengthAlphanumeric = "Username must be 1-50 alphanumeric characters only";

    // Max validation messages
    public const string MaxClicksMinimum = "Max clicks must be at least 1";

    // ExpiresAt validation messages
    public const string ExpiryDateFuture = "Expiry date must be in the future";
}