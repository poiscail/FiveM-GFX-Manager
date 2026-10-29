// Archives .rpf (RPF7 non chiffré « OPEN ») et paquets de mods FiveM
//
// FiveM ne charge que les .rpf posés à la racine de FiveM.app\mods qui contiennent un assembly.xml
// (code source : citizen-mod-loader-five). Les fichiers listés dans assembly.xml :
//  - un nom seul dans au moins deux <archive> imbriqués : streamé, il remplace le fichier du jeu de même nom
//    (c'est ainsi que marchent les skins d'armes .ydr / .ytd) ;
//  - un .rpf : tous les fichiers à sa racine sont streamés.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace FiveMGfxManager
{
    public class RpfEntry
    {
        public string Path;       // chemin dans l'archive, avec des /
        public bool Dossier, Ressource;
        public long Position;     // en octets
        public uint Taille;       // taille stockée (0 : non compressé)
        public uint TailleReelle; // fichier binaire : taille décompressée
    }

    public static partial class Core
    {
        const uint Rsc7Magic = 0x37435352;  // en-tête des fichiers ressources (.ydr, .ytd…)

        // Fichiers streamés par FiveM (remplacent le fichier du jeu de même nom)
        static readonly string[] StreamExts = { ".ytd", ".ydr", ".yft", ".ydd", ".ycd", ".ytyp", ".ymap", ".ybn" };
        static bool IsStreamFile(string name) => StreamExts.Contains(Path.GetExtension(name).ToLowerInvariant());

        // ------------------------------------------------------- lecture --

        // Entrées d'un .rpf ; null si ce n'est pas un RPF7 lisible (chiffré NG / AES, ou autre fichier).
        // offset : début d'un .rpf rangé tel quel dans file (les positions renvoyées sont relatives à file)
        public static List<RpfEntry> ReadRpfEntries(string file, long offset = 0)
        {
            try
            {
                using (var br = new BinaryReader(File.OpenRead(file)))
                {
                    long length = br.BaseStream.Length - offset;
                    if (length < 16) return null;
                    br.BaseStream.Position = offset;
                    if (br.ReadUInt32() != RpfMagic) return null;
                    uint count = br.ReadUInt32(), namesLength = br.ReadUInt32(), enc = br.ReadUInt32();
                    if (enc != RpfOpen && enc != 0) return null;
                    if (count == 0 || 16 + 16L * count + namesLength > length) return null;
                    var raw = new List<byte[]>();
                    for (int i = 0; i < count; i++) raw.Add(br.ReadBytes(16));
                    byte[] names = br.ReadBytes((int)namesLength);
                    string Name(int at)
                    {
                        int end = at;
                        while (end < names.Length && names[end] != 0) end++;
                        return Encoding.ASCII.GetString(names, at, end - at);
                    }

                    var list = new RpfEntry[count];
                    void Walk(int index, string path)
                    {
                        byte[] b = raw[index];
                        uint second = BitConverter.ToUInt32(b, 4);
                        if (second == 0x7FFFFF00)
                        {
                            list[index] = new RpfEntry { Path = path, Dossier = true };
                            uint first = BitConverter.ToUInt32(b, 8), n = BitConverter.ToUInt32(b, 12);
                            for (uint k = first; k < first + n && k < count; k++)
                                if (k != index && list[k] == null)
                                {
                                    string kname = Name((int)(BitConverter.ToUInt32(raw[(int)k], 0) & 0xFFFF));
                                    if (BitConverter.ToUInt32(raw[(int)k], 4) == 0x7FFFFF00)
                                        kname = Name((int)BitConverter.ToUInt32(raw[(int)k], 0));
                                    Walk((int)k, path == "" ? kname : path + "/" + kname);
                                }
                            return;
                        }
                        ulong v = BitConverter.ToUInt64(b, 0);
                        bool res = (second & 0x80000000) != 0;
                        list[index] = new RpfEntry
                        {
                            Path = path, Ressource = res,
                            Taille = (uint)((v >> 16) & 0xFFFFFF),
                            Position = offset + (long)((v >> 40) & (res ? 0x7FFFFFUL : 0xFFFFFFUL)) * 512,
                            TailleReelle = res ? 0 : BitConverter.ToUInt32(b, 8),
                        };
                    }
                    Walk(0, "");
                    return list.Where(e => e != null && e.Path != "").ToList();
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { return null; }
        }

        // Contenu d'un fichier binaire de l'archive (null s'il n'existe pas)
        public static byte[] ReadRpfFile(string file, string path)
        {
            var e = ReadRpfEntries(file)?.FirstOrDefault(x => !x.Dossier && x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            return e == null ? null : ReadRpfData(file, e);
        }

        static byte[] ReadRpfData(string file, RpfEntry e)
        {
            using (var fs = File.OpenRead(file))
            {
                fs.Position = e.Position;
                if (e.Ressource || e.Taille == 0)
                {
                    long len = e.Ressource ? e.Taille : e.TailleReelle;
                    if (e.Ressource && e.Taille == 0xFFFFFF)
                    {
                        // grosse ressource : taille réelle dans les octets 2, 5, 7 et 14 de l'en-tête
                        var h = new byte[16];
                        fs.Read(h, 0, 16);
                        len = (uint)(h[7] | h[14] << 8 | h[5] << 16 | h[2] << 24);
                        fs.Position = e.Position;
                    }
                    if (len > int.MaxValue - 64)
                        throw new GfxException($"{Path.GetFileName(e.Path)} est trop gros pour être lu en mémoire ({len / (1024 * 1024)} Mo).");
                    var data = new byte[len];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = fs.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return data;
                }
                var packed = new byte[e.Taille];
                fs.Read(packed, 0, packed.Length);
                using (var z = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress))
                using (var ms = new MemoryStream())
                {
                    z.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        // Copie un fichier de l'archive sur le disque, sans tout charger en mémoire s'il n'est pas compressé
        static void ExtractRpfFile(string file, RpfEntry e, string dest)
        {
            if (e.Ressource || e.Taille != 0)
            {
                File.WriteAllBytes(dest, ReadRpfData(file, e));
                return;
            }
            using (var fs = File.OpenRead(file))
            using (var outs = File.Create(dest))
            {
                fs.Position = e.Position;
                var buf = new byte[256 * 1024];
                long left = e.TailleReelle;
                while (left > 0)
                {
                    int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
                    if (n <= 0) break;
                    outs.Write(buf, 0, n);
                    left -= n;
                }
            }
        }

        // Noms des fichiers à la racine d'un .rpf rangé dans une archive (null si illisible)
        static List<string> NestedRpfRootFiles(string file, RpfEntry e)
        {
            if (!e.Ressource && e.Taille == 0) return RpfRootFiles(file, e.Position);  // rangé tel quel : lu sur place
            return WithTempDir(work =>
            {
                string inner = Path.Combine(work, "inner.rpf");
                ExtractRpfFile(file, e, inner);
                return RpfRootFiles(inner);
            });
        }

        // RPF7 chiffré (NG ou AES) : illisible ici
        static bool IsEncryptedRpf(string file)
        {
            try
            {
                using (var br = new BinaryReader(File.OpenRead(file)))
                {
                    if (br.BaseStream.Length < 16 || br.ReadUInt32() != RpfMagic) return false;
                    br.ReadUInt64();
                    uint enc = br.ReadUInt32();
                    return enc != RpfOpen && enc != 0;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
        }

        // Noms des fichiers à la racine de l'archive (null si illisible)
        static List<string> RpfRootFiles(string file, long offset = 0) =>
            ReadRpfEntries(file, offset)?.Where(e => !e.Dossier && !e.Path.Contains("/")).Select(e => e.Path).ToList();

        // ------------------------------------------------------ écriture --

        class RpfNode
        {
            public string Name;
            public List<RpfNode> Kids;   // null : fichier
            public string Source;        // fichier sur le disque
            public byte[] Data;          // ou contenu en mémoire
            public uint Index, NameOffset;
            public long Block;
        }

        // Écrit un .rpf OPEN : files = (chemin dans l'archive avec des /, fichier source ou contenu)
        static void WriteRpf(string dest, IEnumerable<(string path, string source, byte[] data)> files)
        {
            var root = new RpfNode { Name = "", Kids = new List<RpfNode>() };
            foreach (var (path, source, data) in files)
            {
                var parts = path.ToLowerInvariant().Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var dir = root;
                foreach (var part in parts.Take(parts.Length - 1))
                {
                    var next = dir.Kids.FirstOrDefault(k => k.Kids != null && k.Name == part);
                    if (next == null) dir.Kids.Add(next = new RpfNode { Name = part, Kids = new List<RpfNode>() });
                    dir = next;
                }
                dir.Kids.Add(new RpfNode { Name = parts.Last(), Source = source, Data = data });
            }

            // entrées : la racine, puis les enfants de chaque dossier à la suite, triés par nom
            var entries = new List<RpfNode> { root };
            var queue = new Queue<RpfNode>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var dir = queue.Dequeue();
                dir.Kids.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                dir.Index = (uint)entries.Count;
                entries.AddRange(dir.Kids);
                foreach (var k in dir.Kids.Where(k => k.Kids != null)) queue.Enqueue(k);
            }

            var names = new MemoryStream();
            foreach (var e in entries)
            {
                e.NameOffset = (uint)names.Length;
                var bytes = Encoding.ASCII.GetBytes(e.Name);
                names.Write(bytes, 0, bytes.Length);
                names.WriteByte(0);
            }
            if (names.Length > 0xFFFF) throw new GfxException("Trop de fichiers pour un seul .rpf.");
            while (names.Length % 16 != 0) names.WriteByte(0);

            long header = 16 + 16L * entries.Count + names.Length;
            long block = (header + 511) / 512;
            foreach (var e in entries.Where(e => e.Kids == null))
            {
                e.Block = block;
                block += (Length(e) + 511) / 512;
            }
            if (block > 0x7FFFFF) throw new GfxException("Archive .rpf trop grosse (plus de 4 Go).");

            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (File.Exists(dest)) MakeWritable(dest);
            using (var bw = new BinaryWriter(File.Create(dest)))
            {
                bw.Write(RpfMagic);
                bw.Write((uint)entries.Count);
                bw.Write((uint)names.Length);
                bw.Write(RpfOpen);
                foreach (var e in entries)
                {
                    if (e.Kids != null)
                    {
                        bw.Write(e.NameOffset);
                        bw.Write(0x7FFFFF00u);
                        bw.Write(e.Kids.Count == 0 ? 0u : e.Kids[0] == null ? 0u : (uint)entries.IndexOf(e.Kids[0]));
                        bw.Write((uint)e.Kids.Count);
                        continue;
                    }
                    long len = Length(e);
                    byte[] head = Head(e);
                    if (head.Length >= 16 && BitConverter.ToUInt32(head, 0) == Rsc7Magic)
                    {
                        // ressource : taille sur 24 bits (au-delà, elle est lue dans l'en-tête), flags de l'en-tête RSC7
                        uint size = (uint)Math.Min(len, 0xFFFFFF);
                        bw.Write((ushort)e.NameOffset);
                        bw.Write((byte)size); bw.Write((byte)(size >> 8)); bw.Write((byte)(size >> 16));
                        bw.Write((byte)e.Block); bw.Write((byte)(e.Block >> 8)); bw.Write((byte)((e.Block >> 16) | 0x80));
                        bw.Write(BitConverter.ToUInt32(head, 8));
                        bw.Write(BitConverter.ToUInt32(head, 12));
                    }
                    else
                    {
                        // fichier binaire non compressé (taille compressée = 0), non chiffré
                        bw.Write((ulong)e.NameOffset | ((ulong)e.Block << 40));
                        bw.Write((uint)len);
                        bw.Write(0u);
                    }
                }
                names.WriteTo(bw.BaseStream);

                foreach (var e in entries.Where(e => e.Kids == null))
                {
                    Pad(bw.BaseStream, e.Block * 512);
                    long len = Length(e);
                    byte[] head = Head(e);
                    bool bigResource = len > 0xFFFFFF && head.Length >= 16 && BitConverter.ToUInt32(head, 0) == Rsc7Magic;
                    using (var src = e.Data != null ? (Stream)new MemoryStream(e.Data) : File.OpenRead(e.Source))
                    {
                        if (bigResource)
                        {
                            // taille réelle rangée dans les octets 2, 5, 7 et 14 de l'en-tête
                            var h = new byte[16];
                            src.Read(h, 0, 16);
                            h[7] = (byte)len; h[14] = (byte)(len >> 8); h[5] = (byte)(len >> 16); h[2] = (byte)(len >> 24);
                            bw.Write(h);
                        }
                        src.CopyTo(bw.BaseStream);
                    }
                }
                Pad(bw.BaseStream, (bw.BaseStream.Length + 511) / 512 * 512);
            }
        }

        static long Length(RpfNode e) => e.Data != null ? e.Data.Length : new FileInfo(e.Source).Length;

        static byte[] Head(RpfNode e)
        {
            if (e.Data != null) return e.Data.Take(16).ToArray();
            using (var fs = File.OpenRead(e.Source))
            {
                var h = new byte[Math.Min(16, fs.Length)];
                fs.Read(h, 0, h.Length);
                return h;
            }
        }

        static void Pad(Stream s, long to)
        {
            if (s.Length < to) s.Write(new byte[to - s.Length], 0, (int)(to - s.Length));
        }

        // -------------------------------------------- paquet de mod FiveM --

        // Crée un paquet FiveM (.rpf avec assembly.xml) :
        //  streamed : fichiers .ydr, .ytd… qui remplacent ceux du jeu de même nom ;
        //  rpfs : .rpf dont tous les fichiers de la racine sont streamés.
        static void WriteModPackage(string dest, string name, List<string> streamed, List<string> rpfs)
        {
            var content = new List<(string path, string source, byte[] data)>();
            var xml = new StringBuilder();
            string Esc(string s) => SecurityElement.Escape(s);
            if (streamed.Count > 0)
            {
                // le chemin des archives est indicatif : un nom seul dans deux archives imbriquées est streamé
                xml.AppendLine("    <archive path=\"update\\x64\\dlcpacks\\patchday8ng\\dlc.rpf\" createIfNotExist=\"False\" type=\"RPF7\">");
                xml.AppendLine("      <archive path=\"x64\\models\\cdimages\\weapons.rpf\" createIfNotExist=\"False\" type=\"RPF7\">");
                foreach (var f in streamed)
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    content.Add(("content/" + n, f, null));
                    xml.AppendLine($"        <add source=\"{Esc(n)}\">{Esc(n)}</add>");
                }
                xml.AppendLine("      </archive>");
                xml.AppendLine("    </archive>");
            }
            if (rpfs.Count > 0)
            {
                xml.AppendLine("    <archive path=\"x64e.rpf\" createIfNotExist=\"False\" type=\"RPF7\">");
                foreach (var f in rpfs)
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    content.Add(("content/" + n, f, null));
                    xml.AppendLine($"      <add source=\"{Esc(n)}\">models\\cdimages\\{Esc(n)}</add>");
                }
                xml.AppendLine("    </archive>");
            }

            // id stable pour un même contenu (deux paquets avec le même id se gênent dans FiveM)
            string key = name + "|" + string.Join("|", content.Select(c => c.path + ":" + new FileInfo(c.source).Length));
            Guid id;
            using (var md5 = MD5.Create()) id = new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(key)));
            string assembly =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
                $"<package version=\"2.2\" id=\"{id.ToString("B").ToUpperInvariant()}\" target=\"Five\">\r\n" +
                "  <metadata>\r\n" +
                $"    <name>{Esc(name)}</name>\r\n" +
                "    <version>\r\n      <major>1</major>\r\n      <minor>0</minor>\r\n      <tag>Main</tag>\r\n    </version>\r\n" +
                "    <author>\r\n      <displayName>FiveM GFX Manager</displayName>\r\n    </author>\r\n" +
                $"    <description>{Esc(name)} (paquet créé par FiveM GFX Manager)</description>\r\n" +
                "  </metadata>\r\n" +
                "  <content>\r\n" + xml.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n") + "  </content>\r\n" +
                "</package>\r\n";
            content.Insert(0, ("assembly.xml", null, Encoding.UTF8.GetBytes(assembly)));
            WriteRpf(dest, content);
        }

        // Nom de fichier pour un paquet (lettres, chiffres, - et _)
        static string PackageFileName(string name)
        {
            var s = new string((name ?? "").Select(c => c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_').ToArray()).Trim('_');
            return "gfx_" + (s.Length == 0 ? "pack" : s.Length > 40 ? s.Substring(0, 40) : s).ToLowerInvariant() + ".rpf";
        }
    }
}
