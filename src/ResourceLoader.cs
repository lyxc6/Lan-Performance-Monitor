using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace LanPerfMon
{
    /// <summary>
    /// All runtime payload is embedded in the single exe.  Managed assemblies are
    /// loaded from memory; tools such as PresentMon.exe are extracted to a temp
    /// folder when they need a real file path.
    /// </summary>
    internal static class ResourceLoader
    {
        private const string Prefix = "res.";
        private static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        public static byte[] Read(string name)
        {
            lock (CacheLock)
            {
                byte[] data;
                if (Cache.TryGetValue(name, out data))
                    return data;

                Assembly asm = Assembly.GetExecutingAssembly();
                string fullName = Prefix + name;
                Stream stream = asm.GetManifestResourceStream(fullName);
                if (stream == null)
                {
                    // Try a suffix match.  This keeps the resolver working even if
                    // an assembly name is cased differently on disk.
                    string[] names = asm.GetManifestResourceNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (names[i].EndsWith(fullName, StringComparison.OrdinalIgnoreCase))
                        {
                            fullName = names[i];
                            stream = asm.GetManifestResourceStream(fullName);
                            break;
                        }
                    }
                }

                if (stream == null)
                    throw new InvalidOperationException("缺少内嵌资源: " + name);

                using (stream)
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        ms.Write(buffer, 0, read);
                    data = ms.ToArray();
                }

                Cache[name] = data;
                return data;
            }
        }

        public static string ReadText(string name)
        {
            return Encoding.UTF8.GetString(Read(name));
        }

        public static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                AssemblyName requested = new AssemblyName(args.Name);
                string file = requested.Name + ".dll";
                Log.Write("AssemblyResolve: " + file);
                return Assembly.Load(Read("dep." + file));
            }
            catch
            {
                return null;
            }
        }

        public static string ExtractTool(string resourceName, string fileName)
        {
            byte[] data = Read(resourceName);
            string dir = Path.Combine(Path.GetTempPath(), "LanPerfMon");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);

            bool needsWrite = true;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length == data.Length)
                    needsWrite = false;
            }
            catch { }

            if (needsWrite)
            {
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, data);
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    File.Move(temp, path);
                }
                catch
                {
                    // Another instance may have the old file open.  A versioned name
                    // prevents this from being fatal.
                    path = Path.Combine(dir, Path.GetFileNameWithoutExtension(fileName) + "-" +
                        Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(fileName));
                    File.WriteAllBytes(path, data);
                }
            }

            return path;
        }

        public static System.Drawing.Icon LoadIcon()
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(Read("app.ico")))
                    return new System.Drawing.Icon(ms);
            }
            catch
            {
                return System.Drawing.SystemIcons.Application;
            }
        }
    }
}
