using System;
using System.Buffers.Binary;

namespace MediaBrowser.MediaEncoding.Spatial;

/// <summary>
/// Helper methods for parsing and manipulating ISO base media file format (MP4) boxes.
/// </summary>
public static class Mp4BoxHelper
{
    /// <summary>
    /// Find a box by type within a range of data.
    /// </summary>
    /// <param name="data">The data to search.</param>
    /// <param name="start">Start position.</param>
    /// <param name="end">End position (exclusive).</param>
    /// <param name="boxType">The 4-char box type to find.</param>
    /// <returns>The position of the box, or -1 if not found.</returns>
    public static int FindBox(ReadOnlySpan<byte> data, int start, int end, ReadOnlySpan<byte> boxType)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var size = GetBoxSize(data, pos);
            if (size < 8 || pos + size > end)
            {
                break;
            }

            if (data.Slice(pos + 4, 4).SequenceEqual(boxType))
            {
                return pos;
            }

            pos += size;
        }

        return -1;
    }

    /// <summary>
    /// Get the size of a box at the given position.
    /// </summary>
    /// <param name="data">The data containing the box.</param>
    /// <param name="pos">The position of the box.</param>
    /// <returns>The size of the box.</returns>
    public static int GetBoxSize(ReadOnlySpan<byte> data, int pos)
    {
        return (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
    }

    /// <summary>
    /// Update the size field of a box at the given position.
    /// </summary>
    /// <param name="data">The data containing the box.</param>
    /// <param name="pos">The position of the box.</param>
    /// <param name="newSize">The new size value.</param>
    public static void UpdateBoxSize(Span<byte> data, int pos, uint newSize)
    {
        BinaryPrimitives.WriteUInt32BigEndian(data.Slice(pos, 4), newSize);
    }

    /// <summary>
    /// Check if the data contains an hvc1 or dvh1 (HEVC/Dolby Vision) sample entry box.
    /// </summary>
    /// <param name="data">The data to check.</param>
    /// <returns>True if an hvc1 or dvh1 box is found.</returns>
    public static bool HasHvc1Box(ReadOnlySpan<byte> data)
    {
        return ContainsBoxType(data, "hvc1"u8) || ContainsBoxType(data, "dvh1"u8);
    }

    /// <summary>
    /// Check if the data contains a box type (simple scan, not following box structure).
    /// </summary>
    private static bool ContainsBoxType(ReadOnlySpan<byte> data, ReadOnlySpan<byte> boxType)
    {
        for (var i = 0; i <= data.Length - 4; i++)
        {
            if (data.Slice(i, 4).SequenceEqual(boxType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scan for a box type anywhere in the data (not following box structure).
    /// Used when buffer might not start at a box boundary.
    /// </summary>
    /// <param name="data">The data to scan.</param>
    /// <param name="boxType">The 4-char box type to find.</param>
    /// <returns>The position of the box, or -1 if not found.</returns>
    public static int ScanForBox(ReadOnlySpan<byte> data, ReadOnlySpan<byte> boxType)
    {
        // Search for the 4-byte type, then verify the size before it is valid
        for (var i = 4; i < data.Length - 4; i++)
        {
            if (data.Slice(i, 4).SequenceEqual(boxType))
            {
                // Potential box found - size is 4 bytes before the type
                var sizePos = i - 4;
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(sizePos, 4));

                // Validate: size must be >= 8 and box must fit in remaining data
                if (size >= 8 && sizePos + size <= data.Length)
                {
                    return sizePos;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Rename FFmpeg's dvwC box to dvcC for Vision Pro compatibility.
    /// </summary>
    /// <remarks>
    /// FFmpeg outputs Dolby Vision configuration as "dvwC" (writer config) but
    /// Apple Vision Pro only recognizes the standard "dvcC" box type.
    /// This is a simple in-place 4-byte rename.
    /// </remarks>
    /// <param name="data">The data to modify.</param>
    /// <returns>True if a rename was performed.</returns>
    public static bool RenameDvwcToDvcc(Span<byte> data)
    {
        ReadOnlySpan<byte> dvwc = "dvwC"u8;
        ReadOnlySpan<byte> dvcc = "dvcC"u8;

        for (var i = 0; i <= data.Length - 4; i++)
        {
            if (data.Slice(i, 4).SequenceEqual(dvwc))
            {
                dvcc.CopyTo(data.Slice(i, 4));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Strip a box from data and return the new data with updated sizes.
    /// </summary>
    /// <param name="data">Original data.</param>
    /// <param name="boxPos">Position of the box to remove.</param>
    /// <param name="boxSize">Size of the box to remove.</param>
    /// <returns>New data without the box.</returns>
    public static byte[] StripBox(byte[] data, int boxPos, int boxSize)
    {
        var result = new byte[data.Length - boxSize];
        Buffer.BlockCopy(data, 0, result, 0, boxPos);
        Buffer.BlockCopy(data, boxPos + boxSize, result, boxPos, data.Length - boxPos - boxSize);
        return result;
    }

    /// <summary>
    /// Insert bytes at a position in the data.
    /// </summary>
    /// <param name="data">Original data.</param>
    /// <param name="position">Position to insert at.</param>
    /// <param name="toInsert">Bytes to insert.</param>
    /// <returns>New data with bytes inserted.</returns>
    public static byte[] InsertBytes(byte[] data, int position, byte[] toInsert)
    {
        var result = new byte[data.Length + toInsert.Length];
        Buffer.BlockCopy(data, 0, result, 0, position);
        Buffer.BlockCopy(toInsert, 0, result, position, toInsert.Length);
        Buffer.BlockCopy(data, position, result, position + toInsert.Length, data.Length - position);
        return result;
    }
}
