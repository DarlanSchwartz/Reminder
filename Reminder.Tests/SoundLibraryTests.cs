using Xunit;

namespace Reminder.Tests;

public sealed class SoundLibraryTests : IDisposable
{
    private readonly TempDirectory folder = new();
    public void Dispose() => folder.Dispose();

    [Fact]
    public void Import_CopiesIntoTheLibrary_AndDeduplicatesByContent()
    {
        string source = folder.File("Sample.WAV");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        var library = new SoundLibrary(folder.File("sounds"));

        string stored = library.Import(source);
        Assert.NotEqual(source, stored);
        Assert.Equal(".wav", Path.GetExtension(stored));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(stored));

        string copy = folder.File("copy.wav");
        File.Copy(source, copy);
        Assert.Equal(stored, library.Import(copy));
        Assert.Single(Directory.GetFiles(folder.File("sounds")));
    }

    [Fact]
    public void Import_OfAMissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => new SoundLibrary(folder.File("sounds")).Import(folder.File("missing.mp3")));
    }

    [Fact]
    public void PickRandom_ReturnsOnlyMp3OrWav()
    {
        File.WriteAllText(folder.File("notes.txt"), "");
        File.WriteAllText(folder.File("song.MP3"), "");
        Assert.Equal(folder.File("song.MP3"), SoundLibrary.PickRandom(folder.Path));
    }

    [Fact]
    public void PickRandom_WithoutSongs_ReturnsNull()
    {
        Assert.Null(SoundLibrary.PickRandom(null));
        Assert.Null(SoundLibrary.PickRandom(" "));
        Assert.Null(SoundLibrary.PickRandom(folder.File("missing")));
        Assert.Null(SoundLibrary.PickRandom(folder.Path));
    }
}
