using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Subtitles.Pgs;

/// <summary>
/// Parses PGS (.sup) subtitle files into display sets.
/// </summary>
public class PgsParser
{
    /// <summary>
    /// PTS clock frequency (90kHz as per Blu-ray spec).
    /// </summary>
    private const double PtsFrequency = 90000.0;

    private readonly ILogger<PgsParser> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PgsParser"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PgsParser(ILogger<PgsParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parse all display sets from a PGS stream.
    /// </summary>
    /// <param name="stream">The PGS data stream.</param>
    /// <returns>Enumerable of decoded display sets.</returns>
    public IEnumerable<PgsDisplaySet> Parse(Stream stream)
    {
        return ParseInternal(stream, null, null);
    }

    /// <summary>
    /// Parse display sets within a specific time range.
    /// </summary>
    /// <param name="stream">The PGS data stream.</param>
    /// <param name="start">Start time of the range.</param>
    /// <param name="end">End time of the range.</param>
    /// <returns>Enumerable of display sets within the time range.</returns>
    public IEnumerable<PgsDisplaySet> ParseTimeRange(Stream stream, TimeSpan start, TimeSpan end)
    {
        return ParseInternal(stream, start, end);
    }

    private IEnumerable<PgsDisplaySet> ParseInternal(Stream stream, TimeSpan? start, TimeSpan? end)
    {
        var segments = ReadSegments(stream, start, end);
        return GroupIntoDisplaySets(segments, start, end);
    }

    private List<PgsSegment> ReadSegments(Stream stream, TimeSpan? start, TimeSpan? end)
    {
        var segments = new List<PgsSegment>();
        var buffer = new byte[13]; // Header size
        long? startPts = start.HasValue ? (long)(start.Value.TotalSeconds * PtsFrequency) : null;
        long? endPts = end.HasValue ? (long)(end.Value.TotalSeconds * PtsFrequency) : null;
        bool foundEndOfRange = false;
        bool foundNextDisplaySet = false;

        while (true)
        {
            // Read header
            int bytesRead = stream.Read(buffer, 0, 13);
            if (bytesRead < 13)
            {
                break;
            }

            // Check magic bytes "PG"
            if (buffer[0] != 'P' || buffer[1] != 'G')
            {
                _logger.LogWarning("Invalid PGS magic bytes");
                break;
            }

            // Parse header
            uint pts = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(2));
            // uint dts = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(6)); // Not used
            var segmentType = (PgsSegmentType)buffer[10];
            ushort segmentSize = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(11));

            // Read segment data
            var data = new byte[segmentSize];
            if (segmentSize > 0)
            {
                bytesRead = stream.Read(data, 0, segmentSize);
                if (bytesRead < segmentSize)
                {
                    _logger.LogWarning("Truncated PGS segment (expected {Expected} bytes, got {Actual})", segmentSize, bytesRead);
                    break;
                }
            }

            // Track if we've passed the end time
            if (endPts.HasValue && pts > endPts.Value)
            {
                foundEndOfRange = true;
            }

            // If we've passed end time and found a complete next display set, stop
            if (foundEndOfRange && foundNextDisplaySet && segmentType == PgsSegmentType.EndOfDisplaySet)
            {
                // Include this segment (it completes the next display set for end time calculation)
                segments.Add(new PgsSegment(pts, segmentType, data));
                break;
            }

            // Track when we find the start of the next display set after end time
            if (foundEndOfRange && segmentType == PgsSegmentType.PresentationComposition)
            {
                foundNextDisplaySet = true;
            }

            segments.Add(new PgsSegment(pts, segmentType, data));
        }

        return segments;
    }

    private IEnumerable<PgsDisplaySet> GroupIntoDisplaySets(List<PgsSegment> segments, TimeSpan? start, TimeSpan? end)
    {
        var displaySets = new List<DisplaySetBuilder>();
        DisplaySetBuilder? currentBuilder = null;

        foreach (var segment in segments)
        {
            switch (segment.Type)
            {
                case PgsSegmentType.PresentationComposition:
                    // Start a new display set
                    if (currentBuilder is not null)
                    {
                        displaySets.Add(currentBuilder);
                    }

                    currentBuilder = new DisplaySetBuilder(segment.Pts);
                    currentBuilder.ParsePcs(segment.Data);
                    break;

                case PgsSegmentType.WindowDefinition:
                    currentBuilder?.ParseWds(segment.Data);
                    break;

                case PgsSegmentType.PaletteDefinition:
                    currentBuilder?.ParsePds(segment.Data);
                    break;

                case PgsSegmentType.ObjectDefinition:
                    currentBuilder?.ParseOds(segment.Data);
                    break;

                case PgsSegmentType.EndOfDisplaySet:
                    if (currentBuilder is not null)
                    {
                        displaySets.Add(currentBuilder);
                        currentBuilder = null;
                    }

                    break;
            }
        }

        // Add final builder if exists
        if (currentBuilder is not null)
        {
            displaySets.Add(currentBuilder);
        }

        // Calculate end times and convert to display sets
        for (int i = 0; i < displaySets.Count; i++)
        {
            var builder = displaySets[i];
            TimeSpan startTime = TimeSpan.FromSeconds(builder.Pts / PtsFrequency);
            TimeSpan endTime;

            if (i + 1 < displaySets.Count)
            {
                endTime = TimeSpan.FromSeconds(displaySets[i + 1].Pts / PtsFrequency);
            }
            else
            {
                // Last display set - give it a reasonable duration
                endTime = startTime + TimeSpan.FromSeconds(5);
            }

            // Filter by time range - only include cues that START within the range
            // This prevents duplicate cues appearing in adjacent segments
            if (start.HasValue && startTime < start.Value)
            {
                continue;
            }

            if (end.HasValue && startTime >= end.Value)
            {
                continue;
            }

            // Build the display set
            var displaySet = builder.Build(startTime, endTime);
            if (displaySet is not null)
            {
                yield return displaySet;
            }
        }
    }

    private readonly record struct PgsSegment(uint Pts, PgsSegmentType Type, byte[] Data);

    private sealed class DisplaySetBuilder
    {
        private readonly List<PgsPaletteEntry> _palette = new();
        private readonly List<byte> _objectData = new();
        private int _width;
        private int _height;
        private int _objectWidth;
        private int _objectHeight;

        public DisplaySetBuilder(uint pts)
        {
            Pts = pts;
        }

        public uint Pts { get; }

        public void ParsePcs(byte[] data)
        {
            if (data.Length < 11)
            {
                return;
            }

            _width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0));
            _height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        }

        public void ParseWds(byte[] data)
        {
            // Window definition - not needed for OCR
        }

        public void ParsePds(byte[] data)
        {
            if (data.Length < 2)
            {
                return;
            }

            // Skip palette ID and version
            int offset = 2;
            while (offset + 5 <= data.Length)
            {
                byte id = data[offset];
                byte y = data[offset + 1];
                byte cr = data[offset + 2];
                byte cb = data[offset + 3];
                byte alpha = data[offset + 4];

                // Ensure palette array is large enough
                while (_palette.Count <= id)
                {
                    _palette.Add(default);
                }

                _palette[id] = new PgsPaletteEntry(id, y, cb, cr, alpha);
                offset += 5;
            }
        }

        public void ParseOds(byte[] data)
        {
            if (data.Length < 7)
            {
                return;
            }

            // Skip object ID (2), version (1), sequence flags (1), total length (3)
            int offset = 7;

            // Check if this segment has width/height (first segment of object)
            byte sequenceFlags = data[3];
            bool isFirst = (sequenceFlags & 0x80) != 0;

            if (isFirst && data.Length >= 11)
            {
                _objectWidth = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(7));
                _objectHeight = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(9));
                offset = 11;
            }

            // Append RLE data
            if (offset < data.Length)
            {
                _objectData.AddRange(data.AsSpan(offset).ToArray());
            }
        }

        public PgsDisplaySet? Build(TimeSpan startTime, TimeSpan endTime)
        {
            if (_objectWidth == 0 || _objectHeight == 0 || _objectData.Count == 0)
            {
                return null;
            }

            // Decode RLE to palette indices
            var paletteIndices = RleDecoder.Decode(
                _objectData.ToArray(),
                _objectWidth,
                _objectHeight);

            // Convert to RGBA
            var rgbaPixels = PaletteConverter.ToRgba(
                paletteIndices,
                _palette.ToArray());

            return new PgsDisplaySet(
                startTime,
                endTime,
                _objectWidth,
                _objectHeight,
                rgbaPixels);
        }
    }
}
