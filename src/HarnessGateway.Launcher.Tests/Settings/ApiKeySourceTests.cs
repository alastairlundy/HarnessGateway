using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Tests.Settings;

public class ApiKeySourceTests
{
    [Test]
    public async Task Resolve_reads_the_named_environment_variable_for_the_env_flag()
    {
        int readCount = 0;
        ApiKeySource source = new(name =>
        {
            readCount++;
            return name == "HG_WORK_KEY" ? "env-secret" : null;
        });

        ApiKeySourceResult result = source.Resolve(key: null, environmentVariableName: "HG_WORK_KEY");

        await Assert.That(result.IsSuccess).IsTrue();
        ApiKeySourceResult.Resolved resolved = (ApiKeySourceResult.Resolved)result;
        await Assert.That(resolved.Secret).IsEqualTo("env-secret");
        await Assert.That(readCount).IsEqualTo(1);
    }

    [Test]
    public async Task Resolve_returns_the_inline_key_without_touching_the_environment()
    {
        int readCount = 0;
        ApiKeySource source = new(_ =>
        {
            readCount++;
            return "should-not-be-read";
        });

        ApiKeySourceResult result = source.Resolve("inline-secret", environmentVariableName: null);

        await Assert.That(result.IsSuccess).IsTrue();
        ApiKeySourceResult.Resolved resolved = (ApiKeySourceResult.Resolved)result;
        await Assert.That(resolved.Secret).IsEqualTo("inline-secret");
        await Assert.That(readCount).IsEqualTo(0);
    }

    [Test]
    public async Task Resolve_without_either_flag_requests_the_prompt()
    {
        ApiKeySource source = new(_ => "should-not-be-read");

        ApiKeySourceResult result = source.Resolve(key: null, environmentVariableName: null);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result is ApiKeySourceResult.Prompt).IsTrue();
    }

    [Test]
    public async Task Resolve_with_both_flags_fails_with_flag_conflict_before_reading_the_environment()
    {
        int readCount = 0;
        ApiKeySource source = new(_ =>
        {
            readCount++;
            return "should-not-be-read";
        });

        ApiKeySourceResult result = source.Resolve("inline-secret", "HG_WORK_KEY");

        await Assert.That(result.IsSuccess).IsFalse();
        ApiKeySourceResult.Failed failed = (ApiKeySourceResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ApiKeySourceFailure.FlagConflict);
        await Assert.That(failed.Message).Contains("--key-env");
        await Assert.That(failed.Message.Contains("cannot be used together")).IsTrue();
        await Assert.That(readCount).IsEqualTo(0);
    }

    [Test]
    public async Task Resolve_with_an_unset_variable_reports_environment_unavailable_naming_it()
    {
        ApiKeySource source = new(_ => null);

        ApiKeySourceResult result = source.Resolve(key: null, environmentVariableName: "HG_MISSING_KEY");

        await Assert.That(result.IsSuccess).IsFalse();
        ApiKeySourceResult.Failed failed = (ApiKeySourceResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ApiKeySourceFailure.EnvironmentVariableUnavailable);
        await Assert.That(failed.Message).Contains("HG_MISSING_KEY");
    }

    [Test]
    public async Task Resolve_with_an_empty_variable_reports_environment_unavailable_naming_it()
    {
        ApiKeySource source = new(_ => string.Empty);

        ApiKeySourceResult result = source.Resolve(key: null, environmentVariableName: "HG_EMPTY_KEY");

        await Assert.That(result.IsSuccess).IsFalse();
        ApiKeySourceResult.Failed failed = (ApiKeySourceResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ApiKeySourceFailure.EnvironmentVariableUnavailable);
        await Assert.That(failed.Message).Contains("HG_EMPTY_KEY");
        await Assert.That(failed.Message.Contains("not set or is empty")).IsTrue();
    }
}
