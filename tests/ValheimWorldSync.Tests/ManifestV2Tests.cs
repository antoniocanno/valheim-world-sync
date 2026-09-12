using ValheimWorldSync.Core.Models;
using Xunit;

namespace ValheimWorldSync.Tests;

public sealed class ManifestV2Tests
{
    [Fact]
    public void AcceptsLegacyAndCompleteV2Manifests()
    {
        new WorldManifest { WorldId = "world" }.Validate("world");
        new WorldManifest
        {
            SchemaVersion = 2, WorldId = "world", WorldDisplayName = "Midgard",
            WorldFolderName = "Midgard", RetentionCount = 10
        }.Validate("world");
    }

    [Fact]
    public void RejectsIncompleteV2Manifest()
    {
        Assert.Throws<InvalidDataException>(() => new WorldManifest { SchemaVersion = 2, WorldId = "world" }.Validate("world"));
    }
}
