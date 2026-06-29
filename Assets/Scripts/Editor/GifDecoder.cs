using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Self-contained GIF87a/89a decoder ??? no external dependencies. Parses the byte stream, runs the image
/// data through a from-scratch LZW decoder, composites each frame respecting transparency and the three
/// GIF disposal methods, and returns every fully-composited frame as a Color32[] plus its delay.
///
/// Pixels are returned in GIF order (top-left origin, row-major); <see cref="GifImporter"/> flips Y when
/// uploading to a Texture2D. Editor-only: used at asset-import time.
/// </summary>
public static class GifDecoder
{
    public struct Frame
    {
        public Color32[] Pixels; // length Width*Height, top-left origin, row-major
        public float Delay;      // seconds
    }

    public class GifData
    {
        public int Width;
        public int Height;
        public readonly List<Frame> Frames = new List<Frame>();
        public bool Loops;    // true if a NETSCAPE looping extension was present
        public int LoopCount; // 0 == infinite
    }

    public static GifData Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 13)
            throw new Exception("Not a GIF: file too short.");
        if (bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F')
            throw new Exception("Not a GIF: missing 'GIF' signature.");

        int pos = 6; // skip "GIF87a"/"GIF89a"
        var data = new GifData();

        int width = bytes[pos] | (bytes[pos + 1] << 8); pos += 2;
        int height = bytes[pos] | (bytes[pos + 1] << 8); pos += 2;
        data.Width = width;
        data.Height = height;
        if (width <= 0 || height <= 0)
            throw new Exception("GIF has invalid dimensions.");

        byte packed = bytes[pos++];
        pos++; // background color index
        pos++; // pixel aspect ratio

        Color32[] globalPalette = null;
        if ((packed & 0x80) != 0)
            globalPalette = ReadPalette(bytes, ref pos, 2 << (packed & 0x07));

        // Graphic Control Extension state ??? applies to the next image descriptor.
        int gceTransparentIndex = -1;
        int gceDisposal = 0;
        float gceDelay = 0f;

        // Persistent compositing canvas + disposal bookkeeping.
        var canvas = new Color32[width * height]; // starts fully transparent (0,0,0,0)
        int prevDisposal = 0;
        int prevLeft = 0, prevTop = 0, prevW = 0, prevH = 0;
        Color32[] disposalSnapshot = null;
        bool haveDrawnFrame = false;

        while (pos < bytes.Length)
        {
            byte block = bytes[pos++];
            if (block == 0x3B) break; // trailer

            if (block == 0x21) // extension introducer
            {
                byte label = bytes[pos++];
                if (label == 0xF9) // graphic control extension
                {
                    pos++; // block size (4)
                    byte flags = bytes[pos++];
                    gceDisposal = (flags >> 2) & 0x07;
                    bool hasTransparency = (flags & 0x01) != 0;
                    int delayCs = bytes[pos] | (bytes[pos + 1] << 8); pos += 2;
                    gceDelay = delayCs / 100f;
                    int transIndex = bytes[pos++];
                    gceTransparentIndex = hasTransparency ? transIndex : -1;
                    pos++; // block terminator
                }
                else if (label == 0xFF) // application extension
                {
                    int size = bytes[pos++];
                    string app = size >= 8 ? Encoding.ASCII.GetString(bytes, pos, 8) : "";
                    pos += size;
                    if (app == "NETSCAPE")
                    {
                        while (true)
                        {
                            int sub = bytes[pos++];
                            if (sub == 0) break;
                            if (sub >= 3 && bytes[pos] == 1)
                            {
                                data.Loops = true;
                                data.LoopCount = bytes[pos + 1] | (bytes[pos + 2] << 8);
                            }
                            pos += sub;
                        }
                    }
                    else
                    {
                        SkipSubBlocks(bytes, ref pos);
                    }
                }
                else // comment (0xFE), plain text (0x01), etc.
                {
                    SkipSubBlocks(bytes, ref pos);
                }
            }
            else if (block == 0x2C) // image descriptor
            {
                int left = bytes[pos] | (bytes[pos + 1] << 8); pos += 2;
                int top = bytes[pos] | (bytes[pos + 1] << 8); pos += 2;
                int iw = bytes[pos] | (bytes[pos + 1] << 8); pos += 2;
                int ih = bytes[pos] | (bytes[pos + 1] << 8); pos += 2;
                byte ipacked = bytes[pos++];

                Color32[] palette = globalPalette;
                if ((ipacked & 0x80) != 0) // local color table
                    palette = ReadPalette(bytes, ref pos, 2 << (ipacked & 0x07));
                bool interlaced = (ipacked & 0x40) != 0;

                int minCodeSize = bytes[pos++];
                byte[] lzw = ReadSubBlockData(bytes, ref pos);
                int[] indices = LzwDecode(lzw, minCodeSize, iw * ih);
                if (interlaced) indices = Deinterlace(indices, iw, ih);

                // Apply the PREVIOUS frame's disposal before drawing this one.
                if (haveDrawnFrame)
                {
                    if (prevDisposal == 2)
                        ClearRect(canvas, width, height, prevLeft, prevTop, prevW, prevH);
                    else if (prevDisposal == 3 && disposalSnapshot != null)
                        Array.Copy(disposalSnapshot, canvas, canvas.Length);
                }

                // If THIS frame restores-to-previous, snapshot the canvas before drawing over it.
                if (gceDisposal == 3)
                {
                    disposalSnapshot = disposalSnapshot ?? new Color32[canvas.Length];
                    Array.Copy(canvas, disposalSnapshot, canvas.Length);
                }

                // Draw the frame, honoring transparency and clipping to the logical screen.
                if (palette != null)
                {
                    for (int y = 0; y < ih; y++)
                    {
                        int cy = top + y;
                        if (cy < 0 || cy >= height) continue;
                        int row = cy * width;
                        int srcRow = y * iw;
                        for (int x = 0; x < iw; x++)
                        {
                            int cx = left + x;
                            if (cx < 0 || cx >= width) continue;
                            int idx = indices[srcRow + x];
                            if (idx == gceTransparentIndex) continue;
                            if (idx >= 0 && idx < palette.Length)
                                canvas[row + cx] = palette[idx];
                        }
                    }
                }

                // Emit the composited frame.
                var snapshot = new Color32[canvas.Length];
                Array.Copy(canvas, snapshot, canvas.Length);
                data.Frames.Add(new Frame { Pixels = snapshot, Delay = gceDelay });

                // Remember this frame's disposal for the next iteration; reset per-image GCE state.
                prevDisposal = gceDisposal;
                prevLeft = left; prevTop = top; prevW = iw; prevH = ih;
                haveDrawnFrame = true;
                gceTransparentIndex = -1;
                gceDisposal = 0;
                gceDelay = 0f;
            }
            else
            {
                // Unknown block ??? stop rather than risk walking off into garbage.
                break;
            }
        }

        if (data.Frames.Count == 0)
            throw new Exception("GIF contained no decodable frames.");

        return data;
    }

    static Color32[] ReadPalette(byte[] bytes, ref int pos, int colors)
    {
        var palette = new Color32[colors];
        for (int i = 0; i < colors; i++)
        {
            byte r = bytes[pos++];
            byte g = bytes[pos++];
            byte b = bytes[pos++];
            palette[i] = new Color32(r, g, b, 255);
        }
        return palette;
    }

    static void SkipSubBlocks(byte[] bytes, ref int pos)
    {
        while (true)
        {
            int size = bytes[pos++];
            if (size == 0) break;
            pos += size;
        }
    }

    static byte[] ReadSubBlockData(byte[] bytes, ref int pos)
    {
        var buffer = new List<byte>();
        while (true)
        {
            int size = bytes[pos++];
            if (size == 0) break;
            for (int i = 0; i < size; i++) buffer.Add(bytes[pos++]);
        }
        return buffer.ToArray();
    }

    static int[] Deinterlace(int[] src, int iw, int ih)
    {
        var dst = new int[src.Length];
        int srcRow = 0;
        int[] starts = { 0, 4, 2, 1 };
        int[] steps = { 8, 8, 4, 2 };
        for (int pass = 0; pass < 4; pass++)
            for (int y = starts[pass]; y < ih; y += steps[pass])
            {
                Array.Copy(src, srcRow * iw, dst, y * iw, iw);
                srcRow++;
            }
        return dst;
    }

    static void ClearRect(Color32[] canvas, int width, int height, int left, int top, int w, int h)
    {
        var clear = new Color32(0, 0, 0, 0);
        for (int y = top; y < top + h; y++)
        {
            if (y < 0 || y >= height) continue;
            int row = y * width;
            for (int x = left; x < left + w; x++)
            {
                if (x < 0 || x >= width) continue;
                canvas[row + x] = clear;
            }
        }
    }

    // GIF variable-width LZW. Codes are packed LSB-first across the concatenated sub-block data.
    static int[] LzwDecode(byte[] data, int minCodeSize, int pixelCount)
    {
        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        int codeSize = minCodeSize + 1;

        var dict = new List<int[]>(4096);
        void Reset()
        {
            dict.Clear();
            for (int i = 0; i < clearCode; i++) dict.Add(new[] { i });
            dict.Add(null); // clear-code slot
            dict.Add(null); // end-code slot
            codeSize = minCodeSize + 1;
        }
        Reset();

        var output = new int[pixelCount];
        int outPos = 0;
        int bitPos = 0;
        int totalBits = data.Length * 8;
        int[] prev = null;

        int ReadCode()
        {
            int code = 0;
            for (int i = 0; i < codeSize; i++)
            {
                if (bitPos >= totalBits) return endCode;
                int bit = (data[bitPos >> 3] >> (bitPos & 7)) & 1;
                code |= bit << i;
                bitPos++;
            }
            return code;
        }

        while (outPos < pixelCount)
        {
            int code = ReadCode();
            if (code == clearCode) { Reset(); prev = null; continue; }
            if (code == endCode) break;

            int[] entry;
            if (code < dict.Count && dict[code] != null)
            {
                entry = dict[code];
            }
            else if (prev != null)
            {
                // KwKwK case: code == dict.Count (entry not yet added).
                entry = new int[prev.Length + 1];
                Array.Copy(prev, entry, prev.Length);
                entry[prev.Length] = prev[0];
            }
            else
            {
                break; // malformed stream
            }

            for (int i = 0; i < entry.Length && outPos < pixelCount; i++)
                output[outPos++] = entry[i];

            if (prev != null && dict.Count < 4096)
            {
                var newEntry = new int[prev.Length + 1];
                Array.Copy(prev, newEntry, prev.Length);
                newEntry[prev.Length] = entry[0];
                dict.Add(newEntry);
                if (dict.Count == (1 << codeSize) && codeSize < 12)
                    codeSize++;
            }
            prev = entry;
        }

        return output;
    }
}
