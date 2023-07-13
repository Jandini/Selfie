using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Selfie
{
    internal class Program
    {

        static long FindArchive()
        {
            using (var stream = File.OpenRead(Assembly.GetExecutingAssembly().Location))
            {
                const int BUFFER_SIZE = 512;
                const int ZIP_MAGIC = 0x4034B50;

                for (int i = 0; i < 16; i++)
                {
                    var buffer = new byte[BUFFER_SIZE];

                    if (stream.Read(buffer, 0, BUFFER_SIZE) < 4)
                        break;

                    if (BitConverter.ToInt32(buffer, 0) == ZIP_MAGIC)
                        return stream.Position - BUFFER_SIZE;
                }
            }
        
            return -1;
        }

        static void CreateArchive()
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);

            using (var selfStream = File.Create(Path.ChangeExtension(assemblyPath, $"{DateTime.Now.Ticks}.exe")))
            {
                selfStream.Write(assemblyBytes, 0, assemblyBytes.Length);

                using (var archive = new ZipArchive(selfStream, ZipArchiveMode.Create, true))
                {
                    var zipArchiveEntry = archive.CreateEntry("HelloWorld", CompressionLevel.Fastest);

                    using (var zipStream = zipArchiveEntry.Open())
                    {
                        zipStream.Write(assemblyBytes, 0, assemblyBytes.Length);
                    }
                }
            }
        }


        static void ExtractArchive(long offset, string path)
        {
            using (var archiveStream = File.OpenRead(Assembly.GetExecutingAssembly().Location))
            {
                archiveStream.Seek(offset, SeekOrigin.Begin);

                using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read))
                {

                    foreach (var entry in zip.Entries)
                    {
                        Console.WriteLine($"Extracting {entry.FullName}...");
                        entry.ExtractToFile(entry.Name, true);

                    }
                }
            }
        }



        static void Main(string[] args)
        {                     
            var offset = FindArchive();


            if (offset > 0)
                ExtractArchive(offset, ".");
            else 
                CreateArchive();
        }
    }
}
