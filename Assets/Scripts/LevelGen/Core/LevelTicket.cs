using System;
using System.IO;
using System.IO.Compression;

namespace Arena.LevelGen
{
    /// <summary>
    /// What a peer needs to generate the host's level itself: the generator version, the seed, the compact config (so
    /// it needs no preset asset), the art to build it with, and the description's hash to check its result against.
    /// <see cref="Match"/> counts the levels a server has issued, so a client can tell a new ticket from a resend.
    /// </summary>
    public struct LevelTicket
    {
        public int GeneratorVersion;
        public ulong Seed;
        /// <summary><see cref="LevelGenSettings.ToBytes"/> of the settings the server generated with.</summary>
        public byte[] Config;
        /// <summary>The preset (for its prop prefabs) and theme, as indices into the build's art registry.</summary>
        public int Preset;
        public int Theme;
        public ulong Hash;
        public int Match;

        public LevelGenSettings Settings => LevelGenSettings.FromBytes(Config);

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream))
            {
                w.Write(GeneratorVersion);
                w.Write(Seed);
                w.Write(Config.Length);
                w.Write(Config);
                w.Write(Preset);
                w.Write(Theme);
                w.Write(Hash);
                w.Write(Match);
            }
            return stream.ToArray();
        }

        public static LevelTicket FromBytes(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            var ticket = new LevelTicket { GeneratorVersion = r.ReadInt32(), Seed = r.ReadUInt64() };
            ticket.Config = r.ReadBytes(r.ReadInt32());
            ticket.Preset = r.ReadInt32();
            ticket.Theme = r.ReadInt32();
            ticket.Hash = r.ReadUInt64();
            ticket.Match = r.ReadInt32();
            return ticket;
        }
    }

    /// <summary>
    /// A level as one pasteable string: <c>L</c> and the base64url of the deflated <c>(generatorVersion, seed,
    /// config)</c>. It fixes the level's geometry; the art is chosen separately.
    /// </summary>
    public static class ShareCode
    {
        public const char Prefix = 'L';

        public static string Encode(int generatorVersion, ulong seed, in LevelGenSettings settings)
        {
            using var raw = new MemoryStream();
            using (var w = new BinaryWriter(raw))
            {
                w.Write(generatorVersion);
                w.Write(seed);
                settings.WriteTo(w);
            }
            var payload = raw.ToArray();
            using var packed = new MemoryStream();
            using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(payload, 0, payload.Length);
            return Prefix + Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>False, with why, for a code that isn't one or was made by another generator version.</summary>
        public static bool TryDecode(string code, out ulong seed, out LevelGenSettings settings, out string error)
        {
            seed = 0;
            settings = default;
            error = null;
            code = code?.Trim();
            if (string.IsNullOrEmpty(code) || code[0] != Prefix)
            {
                error = "That isn't a level share code.";
                return false;
            }
            try
            {
                string base64 = code.Substring(1).Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                using var unpacked = new MemoryStream();
                using (var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress))
                    deflate.CopyTo(unpacked);
                unpacked.Position = 0;
                using var r = new BinaryReader(unpacked);
                int version = r.ReadInt32();
                if (version != LevelGenerator.GeneratorVersion)
                {
                    error = $"That code is for generator version {version}; this build has version {LevelGenerator.GeneratorVersion}.";
                    return false;
                }
                seed = r.ReadUInt64();
                settings = LevelGenSettings.ReadFrom(r);
                return true;
            }
            catch (Exception e) when (e is FormatException or InvalidDataException or EndOfStreamException or IOException)
            {
                error = "That share code is damaged.";
                return false;
            }
        }
    }
}
