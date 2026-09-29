namespace HarnessGateway.Launcher.Tests;

public class SkeletonTests
{
    [Test]
    public async Task Solution_scaffolds_successfully()
    {
        int result = 1 + 1;
        await Assert.That(result).IsEqualTo(2);
    }
}
