using TUnit;

namespace LmProxy.AgentLauncher.Tests;

public class SkeletonTests
{
    [Test]
    public async Task Solution_scaffolds_successfully()
    {
        var result = 1 + 1;
        await Assert.That(result).IsEqualTo(2);
    }
}
