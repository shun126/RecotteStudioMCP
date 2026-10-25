using System.Buffers.Binary;

namespace RecotteStudio.Core;

/// <summary>Reads the picture size of an MP4 video from the track header of its first video track.</summary>
internal static class Mp4VideoSize
{
    // Box headers are read one at a time, so a damaged or hostile file costs at most this many of them.
    private const int MaxBoxes = 4096;

    /// <summary>
    /// Returns the width and height of the first video track, or false when the file is not a readable MP4 or the track is
    /// rotated or skewed, because the stored size would then not be the size the picture is shown at.
    /// </summary>
    internal static bool TryRead(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using FileStream stream = File.OpenRead(path);
            int budget = MaxBoxes;
            if (FindBox(stream, 0, stream.Length, "moov"u8, ref budget) is not (long moovStart, long moovEnd)) return false;
            long position = moovStart;
            while (FindBox(stream, position, moovEnd, "trak"u8, ref budget) is (long trakStart, long trakEnd))
            {
                position = trakEnd;
                if (!IsVideoTrack(stream, trakStart, trakEnd, ref budget)) continue;
                return FindBox(stream, trakStart, trakEnd, "tkhd"u8, ref budget) is (long tkhdStart, long tkhdEnd) &&
                    TryReadTrackHeader(stream, tkhdStart, tkhdEnd, out width, out height);
            }
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsVideoTrack(Stream stream, long start, long end, ref int budget)
    {
        if (FindBox(stream, start, end, "mdia"u8, ref budget) is not (long mdiaStart, long mdiaEnd) ||
            FindBox(stream, mdiaStart, mdiaEnd, "hdlr"u8, ref budget) is not (long hdlrStart, long hdlrEnd) || hdlrEnd - hdlrStart < 12)
            return false;
        // hdlr: version and flags (4), pre_defined (4), handler_type (4).
        Span<byte> handler = stackalloc byte[12];
        stream.Position = hdlrStart;
        stream.ReadExactly(handler);
        return handler[8..12].SequenceEqual("vide"u8);
    }

    private static bool TryReadTrackHeader(Stream stream, long start, long end, out int width, out int height)
    {
        width = height = 0;
        Span<byte> header = stackalloc byte[96];
        if (end - start < 84) return false;
        stream.Position = start;
        int length = (int)Math.Min(header.Length, end - start);
        stream.ReadExactly(header[..length]);
        // Version 1 widens the creation time, modification time, and duration to 64 bits.
        int matrixOffset = header[0] == 1 ? 52 : 40;
        int sizeOffset = matrixOffset + 36;
        if (header[0] > 1 || length < sizeOffset + 8) return false;
        ReadOnlySpan<byte> matrix = header.Slice(matrixOffset, 36);
        int[] identity = { 0x00010000, 0, 0, 0, 0x00010000, 0, 0, 0, 0x40000000 };
        for (int index = 0; index < identity.Length; index++)
        {
            // The translation entries move the picture without changing its size.
            if (index is 6 or 7) continue;
            if (BinaryPrimitives.ReadInt32BigEndian(matrix[(index * 4)..]) != identity[index]) return false;
        }
        // Width and height are 16.16 fixed-point numbers.
        uint fixedWidth = BinaryPrimitives.ReadUInt32BigEndian(header[sizeOffset..]);
        uint fixedHeight = BinaryPrimitives.ReadUInt32BigEndian(header[(sizeOffset + 4)..]);
        width = (int)(fixedWidth >> 16);
        height = (int)(fixedHeight >> 16);
        return width > 0 && height > 0;
    }

    // Returns the payload range of the first child box of the given type between start and end.
    private static (long Start, long End)? FindBox(Stream stream, long start, long end, ReadOnlySpan<byte> type, ref int budget)
    {
        Span<byte> header = stackalloc byte[16];
        long position = start;
        while (position + 8 <= end && budget-- > 0)
        {
            stream.Position = position;
            stream.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            int headerLength = 8;
            if (size == 1)
            {
                if (position + 16 > end) return null;
                stream.ReadExactly(header[8..16]);
                ulong large = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                if (large > long.MaxValue) return null;
                size = (long)large;
                headerLength = 16;
            }
            else if (size == 0) size = end - position;
            if (size < headerLength || position + size > end) return null;
            if (header[4..8].SequenceEqual(type)) return (position + headerLength, position + size);
            position += size;
        }
        return null;
    }
}
