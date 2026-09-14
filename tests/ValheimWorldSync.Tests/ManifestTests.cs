using ValheimWorldSync.Core.Models;
using Xunit;

namespace ValheimWorldSync.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void AcceptsCompleteManifests()
    {
        new WorldManifest
        {
            WorldId = "world",
            WorldDisplayName = "Midgard",
            WorldFolderName = "Midgard",
            RetentionCount = 10
        }.Validate("world");
    }

    [Fact]
    public void RejectsIncompleteManifests()
    {
        Assert.Throws<InvalidDataException>(() => new WorldManifest { WorldId = "world" }.Validate("world"));
    }

    [Fact]
    public void RejectsLegacySchemaVersions()
    {
        Assert.Throws<InvalidDataException>(() => new WorldManifest
        {
            SchemaVersion = 1,
            WorldId = "world",
            WorldDisplayName = "Midgard",
            WorldFolderName = "Midgard",
            RetentionCount = 10
        }.Validate("world"));
    }
}
