using System.IO;
using System.IO.Compression;
using NUnit.Framework;

namespace Arena.LevelGen.Tests
{
    public class DeflateTests
    {
        private static byte[][] Inputs()
        {
            var random = new System.Random(7);
            var noise = new byte[3000];
            random.NextBytes(noise);
            var repetitive = new byte[70000]; // longer than the window, with runs past the longest match
            for (int i = 0; i < repetitive.Length; i++) repetitive[i] = (byte)(i % 1000 < 600 ? 'a' : i * 31 % 7);
            var text = System.Text.Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("level gen share code ", 40)));
            return new[] { new byte[0], new byte[] { 42 }, new byte[] { 1, 2 }, noise, repetitive, text, LevelGenSettings.Default.Sanitized().ToBytes() };
        }

        private static byte[] DeflateStreamCompress(byte[] data)
        {
            using var packed = new MemoryStream();
            using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(data, 0, data.Length);
            return packed.ToArray();
        }

        private static byte[] DeflateStreamDecompress(byte[] data)
        {
            using var unpacked = new MemoryStream();
            using (var deflate = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress)) deflate.CopyTo(unpacked);
            return unpacked.ToArray();
        }

        [Test]
        public void Compress_RoundTrips_AndShrinksRepetitiveData()
        {
            foreach (var input in Inputs())
                CollectionAssert.AreEqual(input, Deflate.Decompress(Deflate.Compress(input)), $"{input.Length} bytes");
            var text = Inputs()[5];
            Assert.Less(Deflate.Compress(text).Length, text.Length / 4);
        }

        // Codes shared before the switch were written by DeflateStream; other tools may read ours.
        [Test]
        public void Agrees_WithDeflateStream_BothWays()
        {
            foreach (var input in Inputs())
            {
                // Mono's DeflateStream writes nothing at all for no input, not an empty block.
                if (input.Length > 0)
                    CollectionAssert.AreEqual(input, Deflate.Decompress(DeflateStreamCompress(input)), $"{input.Length} bytes from DeflateStream");
                CollectionAssert.AreEqual(input, DeflateStreamDecompress(Deflate.Compress(input)), $"{input.Length} bytes to DeflateStream");
            }
        }

        [Test]
        public void Decompress_RefusesTruncatedAndInvalidData()
        {
            var packed = Deflate.Compress(Inputs()[5]);
            Assert.Throws<InvalidDataException>(() => Deflate.Decompress(new byte[0]));
            Assert.Throws<InvalidDataException>(() => Deflate.Decompress(packed[..(packed.Length / 2)]));
            Assert.Throws<InvalidDataException>(() => Deflate.Decompress(new byte[] { 0x07 }), "block type 3");
        }
    }
}
