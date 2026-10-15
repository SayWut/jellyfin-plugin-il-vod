using System.Text.RegularExpressions;
using Jellyfin.Plugin.IlVod.Providers.Mako;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

public class DeviceIdentityTests : IDisposable
{
    private static readonly Regex ExpectedFormat = new(@"^A1[0-9a-f]{23}$", RegexOptions.Compiled);

    private readonly List<string> _createdFilePaths = [];

    public void Dispose()
    {
        foreach (string filePath in _createdFilePaths)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void GeneratesAnIdInTheExpectedFormat()
    {
        DeviceIdentity device = new(NewTemporaryFilePath());

        string id = device.Get();

        Assert.Matches(ExpectedFormat, id);
    }

    [Fact]
    public void TheSameFileGivesTheSameIdToASecondInstance()
    {
        string filePath = NewTemporaryFilePath();
        string firstId = new DeviceIdentity(filePath).Get();

        string secondId = new DeviceIdentity(filePath).Get();

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public void AnUnwritableFileStillYieldsAUsableIdInsteadOfThrowing()
    {
        // A path under a directory that does not exist can neither be read nor written to,
        // exercising the fallback that keeps Get() from ever throwing (spec requirement: playback
        // must still work even when the device id cannot be persisted).
        DeviceIdentity device = new(Path.Combine($"{NewTemporaryFilePath()}-missing-dir", "device.id"));

        string id = device.Get();

        Assert.Matches(ExpectedFormat, id);
    }

    [Fact]
    public void AFileContainingGarbageIsReplacedWithAFreshValidId()
    {
        string filePath = NewTemporaryFilePath();
        File.WriteAllText(filePath, "garbage");

        string id = new DeviceIdentity(filePath).Get();

        Assert.Matches(ExpectedFormat, id);
        Assert.Equal(id, new DeviceIdentity(filePath).Get());
    }

    [Fact]
    public void AnIdWithUppercaseHexIsReplacedWithAFreshValidId()
    {
        string filePath = NewTemporaryFilePath();
        File.WriteAllText(filePath, "A1" + new string('A', 23));

        string id = new DeviceIdentity(filePath).Get();

        Assert.Matches(ExpectedFormat, id);
        Assert.Equal(id, new DeviceIdentity(filePath).Get());
    }

    [Fact]
    public void AnIdOneCharacterShortIsReplacedWithAFreshValidId()
    {
        string filePath = NewTemporaryFilePath();
        File.WriteAllText(filePath, "A1" + new string('a', 22));

        string id = new DeviceIdentity(filePath).Get();

        Assert.Matches(ExpectedFormat, id);
        Assert.Equal(id, new DeviceIdentity(filePath).Get());
    }

    [Fact]
    public void WhitespaceOnlyContentIsReplacedWithAFreshValidId()
    {
        string filePath = NewTemporaryFilePath();
        File.WriteAllText(filePath, "   \n\t  ");

        string id = new DeviceIdentity(filePath).Get();

        Assert.Matches(ExpectedFormat, id);
        Assert.Equal(id, new DeviceIdentity(filePath).Get());
    }

    [Fact]
    public void AValidIdSurroundedByWhitespaceIsAcceptedTrimmed()
    {
        const string validId = "A10123456789abcdef0123456";
        string filePath = NewTemporaryFilePath();
        File.WriteAllText(filePath, $"  {validId}\n");

        string id = new DeviceIdentity(filePath).Get();

        Assert.Equal(validId, id);
    }

    private string NewTemporaryFilePath()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"mako-device-identity-tests-{Guid.NewGuid():N}.id");
        _createdFilePaths.Add(filePath);
        return filePath;
    }
}
