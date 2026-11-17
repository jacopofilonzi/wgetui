using System.Buffers.Binary;

namespace WgetUi.App.Icons;

/// <summary>
/// Writes an ICO file whose entries are PNG images (supported since Windows Vista): a 6-byte ICONDIR, one
/// 16-byte ICONDIRENTRY per image (width/height 0 means 256), then the PNG bytes in entry order.
/// </summary>
public static class IcoFile
{
    private const int HeaderSize = 6;
    private const int EntrySize = 16;

    public static byte[] Pack(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count is 0 or > ushort.MaxValue)
        {
            throw new ArgumentException("An ICO file holds 1 to 65535 images.", nameof(images));
        }

        var dataOffset = HeaderSize + (EntrySize * images.Count);
        var total = dataOffset + images.Sum(i => i.Png.Length);
        var buffer = new byte[total];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1); // type: icon
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)images.Count);

        var offset = dataOffset;
        for (var i = 0; i < images.Count; i++)
        {
            var (size, png) = images[i];
            if (size is < 1 or > 256)
            {
                throw new ArgumentException($"Icon size {size} is outside 1-256.", nameof(images));
            }

            var entry = span.Slice(HeaderSize + (EntrySize * i), EntrySize);
            entry[0] = (byte)(size == 256 ? 0 : size);
            entry[1] = (byte)(size == 256 ? 0 : size);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1); // colour planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32); // bits per pixel
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)png.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            png.CopyTo(span[offset..]);
            offset += png.Length;
        }

        return buffer;
    }
}
