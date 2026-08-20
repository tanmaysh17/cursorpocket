using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public class AudioCleanupFilterBuilderTests
{
    [Fact]
    public void NoCleanupMeansNoFilterArgumentAtAll() =>
        Assert.Null(AudioCleanupFilterBuilder.Build(noiseSuppression: false, autoLevel: false));

    [Fact]
    public void NoiseSuppressionCutsRumbleBeforeDenoising()
    {
        var chain = AudioCleanupFilterBuilder.Build(noiseSuppression: true, autoLevel: false);
        Assert.NotNull(chain);
        Assert.True(chain!.IndexOf("highpass", StringComparison.Ordinal) < chain.IndexOf("afftdn", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoLevelAloneOnlyNormalizes()
    {
        var chain = AudioCleanupFilterBuilder.Build(noiseSuppression: false, autoLevel: true);
        Assert.Equal(AudioCleanupFilterBuilder.AutoLevelChain, chain);
        Assert.DoesNotContain("afftdn", chain);
    }

    /// <summary>
    /// loudnorm emits at its internal 192 kHz, which would quadruple every file
    /// and contradict the 48 kHz recorded in capture metadata.
    /// </summary>
    [Fact]
    public void AutoLevelResamplesBackToTheRecordingRate()
    {
        var chain = AudioCleanupFilterBuilder.Build(noiseSuppression: false, autoLevel: true);
        Assert.NotNull(chain);
        Assert.Contains("loudnorm", chain);
        Assert.EndsWith("aresample=48000", chain);
    }

    [Fact]
    public void AutoLevelKeepsATruePeakCeilingSoLevelingCannotClip() =>
        Assert.Contains("TP=-1.5", AudioCleanupFilterBuilder.AutoLevelChain);

    /// <summary>
    /// Order matters: leveling before denoising would amplify the noise floor
    /// and then hand the denoiser a harder problem.
    /// </summary>
    [Fact]
    public void DenoisesBeforeLevelingWhenBothAreOn()
    {
        var chain = AudioCleanupFilterBuilder.Build(noiseSuppression: true, autoLevel: true);
        Assert.NotNull(chain);
        Assert.True(chain!.IndexOf("afftdn", StringComparison.Ordinal) < chain.IndexOf("loudnorm", StringComparison.Ordinal));
    }

    [Fact]
    public void UsesOnlyCommaSeparatedFiltersSoItDropsStraightIntoDashAf()
    {
        var chain = AudioCleanupFilterBuilder.Build(noiseSuppression: true, autoLevel: true);
        Assert.NotNull(chain);
        Assert.DoesNotContain(" ", chain);
        Assert.DoesNotContain(";", chain);
        Assert.All(chain!.Split(','), filter => Assert.False(string.IsNullOrWhiteSpace(filter)));
    }
}
