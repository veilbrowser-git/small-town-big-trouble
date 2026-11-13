using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Unpacks a .unitypackage (a gzipped tar of guid folders) straight into the
    /// project. Unlike the editor's own importer this is synchronous, so it
    /// works from a headless -executeMethod run that quits immediately after.
    /// </summary>
    public static class UnityPackageExtractor
    {
        sealed class Entry
        {
            public string path;
            public byte[] asset;
            public byte[] meta;
        }

        /// <summary>Writes every asset in the package that does not already exist. Returns the number of files written.</summary>
        public static int Extract(string packagePath, string projectRoot)
        {
            var entries = new Dictionary<string, Entry>();
            using (var file = File.OpenRead(packagePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (ReadFully(gzip, header, 512) == 512)
                {
                    string name = ReadString(header, 0, 100);
                    if (string.IsNullOrEmpty(name)) break;
                    string prefix = ReadString(header, 345, 155);
                    if (!string.IsNullOrEmpty(prefix)) name = prefix + "/" + name;
                    long size = ReadOctal(header, 124, 12);
                    char type = (char)header[156];

                    var data = new byte[size];
                    ReadFully(gzip, data, (int)size);
                    int padding = (int)((512 - size % 512) % 512);
                    if (padding > 0) ReadFully(gzip, new byte[padding], padding);

                    if (type != '0' && type != '\0') continue; // directories, pax headers
                    name = name.Replace('\\', '/').TrimStart('.', '/');
                    int slash = name.IndexOf('/');
                    if (slash <= 0) continue;
                    string guid = name.Substring(0, slash);
                    string part = name.Substring(slash + 1);
                    if (!entries.TryGetValue(guid, out Entry entry)) entries[guid] = entry = new Entry();
                    if (part == "pathname") entry.path = Encoding.UTF8.GetString(data).Split('\n')[0].Trim();
                    else if (part == "asset") entry.asset = data;
                    else if (part == "asset.meta") entry.meta = data;
                }
            }

            int written = 0;
            foreach (Entry entry in entries.Values)
            {
                if (string.IsNullOrEmpty(entry.path) || !entry.path.StartsWith("Assets/") || entry.path.Contains("..")) continue;
                string target = Path.Combine(projectRoot, entry.path);
                if (entry.asset == null)
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (!File.Exists(target))
                    {
                        File.WriteAllBytes(target, entry.asset);
                        written++;
                    }
                }
                if (entry.meta != null && !File.Exists(target + ".meta")) File.WriteAllBytes(target + ".meta", entry.meta);
            }
            return written;
        }

        static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        static string ReadString(byte[] buffer, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && buffer[end] != 0) end++;
            return Encoding.ASCII.GetString(buffer, offset, end - offset);
        }

        static long ReadOctal(byte[] buffer, int offset, int length)
        {
            long value = 0;
            for (int i = offset; i < offset + length; i++)
            {
                byte b = buffer[i];
                if (b == 0 || b == ' ') { if (value > 0) break; continue; }
                if (b < '0' || b > '7') break;
                value = value * 8 + (b - '0');
            }
            return value;
        }
    }
}
