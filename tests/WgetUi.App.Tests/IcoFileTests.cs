using System.Buffers.Binary;
using WgetUi.App.Icons;

namespace WgetUi.App.Tests;

public sealed class IcoFileTests
{
    [Fact]
    public void PacksPngEntriesWithDirectoryAndOffsets()
    {
        byte[] small = [1, 2, 3];
        byte[] large = [4, 5, 6, 7, 8];

        var ico = IcoFile.Pack([(16, small), (256, large)]);

        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));
        Assert.Equal(16, ico[6]);
        Assert.Equal(0, ico[6 + 16]); // 256 is written as 0
        var firstOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + 12));
        var secondOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + 16 + 12));
        Assert.Equal(6 + (2 * 16), firstOffset);
        Assert.Equal(small, ico[firstOffset..(firstOffset + 3)]);
        Assert.Equal(large, ico[secondOffset..]);
    }

    [Fact]
    public void RejectsSizesOutsideIcoRange() =>
        Assert.Throws<ArgumentException>(() => IcoFile.Pack([(300, [1])]));
}
