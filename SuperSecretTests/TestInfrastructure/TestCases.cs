using SuperSecret.Validators;

namespace SuperSecretTests.TestInfrastructure;
public static class TestCases
{
    public static IEnumerable<TestCaseData> InvalidUsernameTestCases()
    {
        yield return new TestCaseData("", ValidationMessages.UsernameRequired);
        yield return new TestCaseData("    ", ValidationMessages.UsernameRequired);
        yield return new TestCaseData(new string('a', 51), ValidationMessages.UsernameLength);
        yield return new TestCaseData("user@test", ValidationMessages.UsernameAlphanumeric);
        yield return new TestCaseData("user test", ValidationMessages.UsernameAlphanumeric);
    }
}
