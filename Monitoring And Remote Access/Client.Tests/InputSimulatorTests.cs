namespace Client.Tests;

public sealed class InputSimulatorTests
{
    [Theory]
    [InlineData(0, 1920, 0)]
    [InlineData(5000, 1920, 960)]
    [InlineData(10000, 1920, 1919)]
    [InlineData(12000, 1920, 1919)]
    public void ScaleCoordinate_MapsNormalizedInputToPhysicalScreen(int coordinate, int screenSize, int expected)
    {
        Assert.Equal(expected, InputSimulator.ScaleCoordinate(coordinate, screenSize));
    }
}
