using System;
using System.Collections.Generic;
using System.IO;

namespace Arena.LevelGen
{
    /// <summary>
    /// Raw DEFLATE (RFC 1951) in managed code, for share codes. <see cref="System.IO.Compression.DeflateStream"/> calls
    /// native zlib, which traps on the web player ("RuntimeError: unreachable" when the host options open), so this
    /// replaces it everywhere. <see cref="Compress"/> writes one fixed-Huffman block with LZ77 matches, which suits
    /// payloads of a few hundred bytes; <see cref="Decompress"/> reads any raw deflate stream, including the dynamic
    /// blocks DeflateStream wrote, so codes made before the switch still decode.
    /// </summary>
    public static class Deflate
    {
        private static readonly int[] LengthBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
        private static readonly int[] LengthExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
        private static readonly int[] DistanceBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
        private static readonly int[] DistanceExtra = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
        private static readonly int[] CodeLengthOrder = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        private const int MinMatch = 3, MaxMatch = 258, Window = 32768, MaxChain = 128;

        public static byte[] Compress(byte[] data)
        {
            var w = new BitWriter();
            w.Write(1, 1); // the final block
            w.Write(1, 2); // fixed Huffman codes
            // Hash chains over three-byte prefixes find earlier matches.
            var head = new Dictionary<int, int>();
            var previous = new int[data.Length];
            int Key(int i) => data[i] | data[i + 1] << 8 | data[i + 2] << 16;
            void Insert(int i)
            {
                if (i + MinMatch > data.Length) return;
                int key = Key(i);
                previous[i] = head.TryGetValue(key, out int last) ? last : -1;
                head[key] = i;
            }

            int pos = 0;
            while (pos < data.Length)
            {
                int bestLength = 0, bestDistance = 0;
                if (pos + MinMatch <= data.Length && head.TryGetValue(Key(pos), out int candidate))
                {
                    int limit = Math.Min(MaxMatch, data.Length - pos);
                    for (int chain = 0; candidate >= 0 && pos - candidate <= Window && chain < MaxChain; chain++)
                    {
                        int length = 0;
                        while (length < limit && data[candidate + length] == data[pos + length]) length++;
                        if (length > bestLength)
                        {
                            bestLength = length;
                            bestDistance = pos - candidate;
                            if (length == limit) break;
                        }
                        candidate = previous[candidate];
                    }
                }
                if (bestLength >= MinMatch)
                {
                    WriteLength(w, bestLength);
                    WriteDistance(w, bestDistance);
                    for (int i = 0; i < bestLength; i++) Insert(pos + i);
                    pos += bestLength;
                }
                else
                {
                    WriteLiteral(w, data[pos]);
                    Insert(pos);
                    pos++;
                }
            }
            WriteLiteral(w, 256); // end of block
            return w.ToArray();
        }

        // Fixed literal/length codes (RFC 1951 3.2.6). Huffman codes go most significant bit first.
        private static void WriteLiteral(BitWriter w, int symbol)
        {
            if (symbol < 144) w.WriteCode(0x30 + symbol, 8);
            else if (symbol < 256) w.WriteCode(0x190 + symbol - 144, 9);
            else if (symbol < 280) w.WriteCode(symbol - 256, 7);
            else w.WriteCode(0xC0 + symbol - 280, 8);
        }

        private static void WriteLength(BitWriter w, int length)
        {
            int code = LengthBase.Length - 1;
            while (LengthBase[code] > length) code--;
            WriteLiteral(w, 257 + code);
            w.Write(length - LengthBase[code], LengthExtra[code]);
        }

        private static void WriteDistance(BitWriter w, int distance)
        {
            int code = DistanceBase.Length - 1;
            while (DistanceBase[code] > distance) code--;
            w.WriteCode(code, 5);
            w.Write(distance - DistanceBase[code], DistanceExtra[code]);
        }

        /// <summary>Throws <see cref="InvalidDataException"/> for data that isn't a whole raw deflate stream.</summary>
        public static byte[] Decompress(byte[] data)
        {
            var r = new BitReader(data);
            var output = new List<byte>(data.Length * 4);
            bool final;
            do
            {
                final = r.Read(1) == 1;
                switch (r.Read(2))
                {
                    case 0:
                        r.AlignToByte();
                        int length = r.Read(16);
                        if ((length ^ 0xFFFF) != r.Read(16)) throw new InvalidDataException("Stored block length mismatch.");
                        for (int i = 0; i < length; i++) output.Add((byte)r.Read(8));
                        break;
                    case 1:
                        InflateBlock(r, output, FixedLiterals, FixedDistances);
                        break;
                    case 2:
                        ReadDynamicTables(r, out var literals, out var distances);
                        InflateBlock(r, output, literals, distances);
                        break;
                    default:
                        throw new InvalidDataException("Invalid block type.");
                }
            } while (!final);
            return output.ToArray();
        }

        private static readonly Huffman FixedLiterals = FixedLiteralCodes();
        private static readonly Huffman FixedDistances = new(Repeat(5, 30));

        private static Huffman FixedLiteralCodes()
        {
            var lengths = new int[288];
            for (int i = 0; i < 288; i++) lengths[i] = i < 144 ? 8 : i < 256 ? 9 : i < 280 ? 7 : 8;
            return new Huffman(lengths);
        }

        private static int[] Repeat(int value, int count)
        {
            var a = new int[count];
            for (int i = 0; i < count; i++) a[i] = value;
            return a;
        }

        private static void ReadDynamicTables(BitReader r, out Huffman literals, out Huffman distances)
        {
            int literalCount = r.Read(5) + 257, distanceCount = r.Read(5) + 1, codeLengthCount = r.Read(4) + 4;
            if (literalCount > 286 || distanceCount > 30) throw new InvalidDataException("Too many codes.");
            var codeLengths = new int[19];
            for (int i = 0; i < codeLengthCount; i++) codeLengths[CodeLengthOrder[i]] = r.Read(3);
            var codeLengthCode = new Huffman(codeLengths);

            var lengths = new int[literalCount + distanceCount];
            for (int i = 0; i < lengths.Length;)
            {
                int symbol = codeLengthCode.Decode(r);
                if (symbol < 16)
                {
                    lengths[i++] = symbol;
                    continue;
                }
                int value = 0, repeat;
                if (symbol == 16)
                {
                    if (i == 0) throw new InvalidDataException("Repeat with no previous length.");
                    value = lengths[i - 1];
                    repeat = 3 + r.Read(2);
                }
                else if (symbol == 17) repeat = 3 + r.Read(3);
                else repeat = 11 + r.Read(7);
                if (i + repeat > lengths.Length) throw new InvalidDataException("Too many code lengths.");
                while (repeat-- > 0) lengths[i++] = value;
            }
            if (lengths[256] == 0) throw new InvalidDataException("No end-of-block code.");
            var literalLengths = new int[literalCount];
            var distanceLengths = new int[distanceCount];
            Array.Copy(lengths, literalLengths, literalCount);
            Array.Copy(lengths, literalCount, distanceLengths, 0, distanceCount);
            literals = new Huffman(literalLengths);
            distances = new Huffman(distanceLengths);
        }

        private static void InflateBlock(BitReader r, List<byte> output, Huffman literals, Huffman distances)
        {
            while (true)
            {
                int symbol = literals.Decode(r);
                if (symbol < 256)
                {
                    output.Add((byte)symbol);
                    continue;
                }
                if (symbol == 256) return;
                symbol -= 257;
                if (symbol >= LengthBase.Length) throw new InvalidDataException("Invalid length code.");
                int length = LengthBase[symbol] + r.Read(LengthExtra[symbol]);
                int distanceCode = distances.Decode(r);
                if (distanceCode >= DistanceBase.Length) throw new InvalidDataException("Invalid distance code.");
                int distance = DistanceBase[distanceCode] + r.Read(DistanceExtra[distanceCode]);
                if (distance > output.Count) throw new InvalidDataException("Distance before the start.");
                int from = output.Count - distance;
                for (int i = 0; i < length; i++) output.Add(output[from + i]);
            }
        }

        /// <summary>A canonical Huffman code, decoded a bit at a time (as zlib's puff does).</summary>
        private sealed class Huffman
        {
            private readonly int[] counts = new int[16];
            private readonly int[] symbols;

            public Huffman(int[] lengths)
            {
                foreach (int length in lengths) counts[length]++;
                var offsets = new int[16];
                for (int length = 1; length < 15; length++) offsets[length + 1] = offsets[length] + counts[length];
                symbols = new int[lengths.Length];
                for (int symbol = 0; symbol < lengths.Length; symbol++)
                    if (lengths[symbol] != 0) symbols[offsets[lengths[symbol]]++] = symbol;
            }

            public int Decode(BitReader r)
            {
                int code = 0, first = 0, index = 0;
                for (int length = 1; length < 16; length++)
                {
                    code |= r.Read(1);
                    int count = counts[length];
                    if (code - count < first) return symbols[index + code - first];
                    index += count;
                    first = (first + count) << 1;
                    code <<= 1;
                }
                throw new InvalidDataException("Invalid Huffman code.");
            }
        }

        private sealed class BitReader
        {
            private readonly byte[] data;
            private int position; // in bits

            public BitReader(byte[] data) => this.data = data;

            /// <summary>The next <paramref name="count"/> bits, least significant first.</summary>
            public int Read(int count)
            {
                int value = 0;
                for (int i = 0; i < count; i++, position++)
                {
                    int index = position >> 3;
                    if (index >= data.Length) throw new InvalidDataException("Unexpected end of deflate data.");
                    value |= (data[index] >> (position & 7) & 1) << i;
                }
                return value;
            }

            public void AlignToByte() => position = (position + 7) & ~7;
        }

        private sealed class BitWriter
        {
            private readonly List<byte> bytes = new();
            private int buffer, filled;

            /// <summary>Writes <paramref name="count"/> bits of <paramref name="value"/>, least significant first.</summary>
            public void Write(int value, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    buffer |= (value >> i & 1) << filled;
                    if (++filled == 8)
                    {
                        bytes.Add((byte)buffer);
                        buffer = filled = 0;
                    }
                }
            }

            /// <summary>Writes a Huffman code, most significant bit first.</summary>
            public void WriteCode(int code, int length)
            {
                for (int i = length - 1; i >= 0; i--) Write(code >> i & 1, 1);
            }

            public byte[] ToArray()
            {
                if (filled > 0) bytes.Add((byte)buffer);
                buffer = filled = 0;
                return bytes.ToArray();
            }
        }
    }
}
