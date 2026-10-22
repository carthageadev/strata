using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Strata
{
    public static class StrataIO
    {
        const uint Magic = 0x54525453;
        //! Change the file version if binary field order changes.
        public const int Version = 1;

        public static void Save(StrataMap m, string path)
        {
            m.Validate();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
            using (var bw = new BinaryWriter(gz, Encoding.UTF8))
                Write(m, bw);
            //! Validate temp data before replacing terrain; keep the .strata~ backup.
            Load(tmp);
            if (File.Exists(path))
            {
                string bak = path + "~";
                if (File.Exists(bak)) File.Delete(bak);
                File.Replace(tmp, path, bak);
            }
            else File.Move(tmp, path);
        }

        public static StrataMap Load(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var br = new BinaryReader(gz, Encoding.UTF8))
            {
                var m = Read(br);
                m.Validate();
                return m;
            }
        }

        static void Write(StrataMap m, BinaryWriter b)
        {
            b.Write(Magic);
            b.Write(Version);
            b.Write(m.name ?? ""); b.Write(m.profile ?? ""); b.Write(m.tileSet ?? "");
            b.Write(m.x0); b.Write(m.z0); b.Write(m.w); b.Write(m.h);
            b.Write(m.bakeChunk);
            b.Write(m.revision); b.Write(m.bakedRevision);
            WriteFloats(b, m.corner);
            WriteFloats(b, m.natural);
            b.Write(m.kind); b.Write(m.wall); b.Write(m.rim);
            var so = new byte[m.stairOf.Length * 2];
            Buffer.BlockCopy(m.stairOf, 0, so, 0, so.Length);
            b.Write(so);
            b.Write(m.flights.Count);
            foreach (var f in m.flights)
            {
                b.Write(f.x0); b.Write(f.x1); b.Write(f.z0); b.Write(f.z1);
                b.Write((byte)f.dir); b.Write(f.y0); b.Write(f.y1); b.Write(f.steps); b.Write(f.stair);
                b.Write(f.low);
                b.Write(f.ground?.Length ?? 0);
                if (f.ground != null) foreach (var g in f.ground) b.Write(g);
            }
            b.Write(m.stairs.Count);
            foreach (var s in m.stairs) { b.Write(s.id ?? ""); b.Write(s.style); b.Write(s.walls); }
            b.Write(m.splatRes); b.Write(m.splatW); b.Write(m.splatH);
            var bytes = new byte[m.splatW * m.splatH * 4];
            for (int k = 0; k < 3; k++)
            {
                var p = m.splat[k];
                for (int i = 0; i < p.Length; i++) { bytes[i * 4] = p[i].r; bytes[i * 4 + 1] = p[i].g; bytes[i * 4 + 2] = p[i].b; bytes[i * 4 + 3] = p[i].a; }
                b.Write(bytes);
            }
            b.Write(Magic);
        }

        static StrataMap Read(BinaryReader b)
        {
            if (b.ReadUInt32() != Magic) throw new InvalidDataException("Not a Strata terrain file");
            int version = b.ReadInt32();
            if (version < 1 || version > Version) throw new InvalidDataException($"Strata file version {version} is newer than this editor ({Version})");
            var m = new StrataMap { name = b.ReadString(), profile = b.ReadString(), tileSet = b.ReadString() };
            m.x0 = b.ReadInt32(); m.z0 = b.ReadInt32(); m.w = b.ReadInt32(); m.h = b.ReadInt32();
            if (m.w <= 0 || m.h <= 0 || m.w > 8192 || m.h > 8192) throw new InvalidDataException("Strata file has an impossible grid size");
            m.bakeChunk = b.ReadInt32();
            m.revision = b.ReadInt64(); m.bakedRevision = b.ReadInt64();
            m.Allocate();
            ReadFloats(b, m.corner);
            ReadFloats(b, m.natural);
            Exact(b, m.kind); Exact(b, m.wall); Exact(b, m.rim);
            var so = new byte[m.stairOf.Length * 2];
            Exact(b, so);
            Buffer.BlockCopy(so, 0, m.stairOf, 0, so.Length);
            int nf = b.ReadInt32();
            if (nf < 0 || nf > 100000) throw new InvalidDataException("Strata file has an impossible number of stairs");
            for (int k = 0; k < nf; k++)
            {
                var f = new Flight { x0 = b.ReadInt32(), x1 = b.ReadInt32(), z0 = b.ReadInt32(), z1 = b.ReadInt32(), dir = (StairDir)b.ReadByte(), y0 = b.ReadSingle(), y1 = b.ReadSingle(), steps = b.ReadInt32(), stair = b.ReadInt32(), low = b.ReadSingle() };
                int ng = b.ReadInt32();
                if (ng < 0 || ng > 10000) throw new InvalidDataException("Strata file has a damaged stair");
                if (ng > 0) { f.ground = new float[ng]; for (int g = 0; g < ng; g++) f.ground[g] = b.ReadSingle(); }
                m.flights.Add(f);
            }
            int ns = b.ReadInt32();
            if (ns < 0 || ns > 100000) throw new InvalidDataException("Strata file has an impossible number of stairs");
            for (int k = 0; k < ns; k++) m.stairs.Add(new Stair { id = b.ReadString(), style = b.ReadByte(), walls = b.ReadBoolean() });
            int res = b.ReadInt32(), sw = b.ReadInt32(), sh = b.ReadInt32();
            if (res != m.splatRes || sw != m.splatW || sh != m.splatH) throw new InvalidDataException("Strata file's ground paint does not match its grid");
            var bytes = new byte[sw * sh * 4];
            for (int k = 0; k < 3; k++)
            {
                Exact(b, bytes);
                var p = m.splat[k];
                for (int i = 0; i < p.Length; i++) p[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
            }
            if (b.ReadUInt32() != Magic) throw new InvalidDataException("Strata file is incomplete");
            return m;
        }

        static void WriteFloats(BinaryWriter b, float[] a)
        {
            var bytes = new byte[a.Length * 4];
            Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
            b.Write(bytes);
        }

        static void ReadFloats(BinaryReader b, float[] a)
        {
            var bytes = new byte[a.Length * 4];
            Exact(b, bytes);
            Buffer.BlockCopy(bytes, 0, a, 0, bytes.Length);
        }

        static void Exact(BinaryReader b, byte[] into)
        {
            int got = 0;
            while (got < into.Length)
            {
                int n = b.Read(into, got, into.Length - got);
                if (n <= 0) throw new EndOfStreamException("Strata file ends early");
                got += n;
            }
        }
    }
}
