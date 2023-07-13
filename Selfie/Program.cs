using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace Selfie
{
    internal class Program
    {
        const long STORROR_MAGIC = 0x524F52524F5453;
        const int ZIP_MAGIC = 0x4034B50;
        const long TAIL_SIZE = 16;


        /// <summary>
        /// Find offset to the embedded archive. 
        /// </summary>
        /// <param name="assemblyInfo">Information about the assembly file.</param>
        /// <returns>Offset to the archive stream within the executable file. If the archive is not found, returns -1.</returns>
        static ArchiveInfo GetArchiveInfo(FileInfo assemblyInfo)
        {
            using (var stream = File.OpenRead(assemblyInfo.FullName))
            {
                stream.Seek(-TAIL_SIZE, SeekOrigin.End);
                var buffer = new byte[TAIL_SIZE];

                if (stream.Read(buffer, 0, buffer.Length) == TAIL_SIZE)
                {
                    if (BitConverter.ToInt64(buffer, 8) == STORROR_MAGIC)
                    {
                        var archiveSize = BitConverter.ToInt64(buffer, 0);
                        var archiveOffset = stream.Length - archiveSize - TAIL_SIZE;

                        if (stream.Seek(archiveOffset, SeekOrigin.Begin) == archiveOffset)
                        {
                            if (stream.Read(buffer, 0, 4) == 4)
                            {
                                if (BitConverter.ToInt32(buffer, 0) == ZIP_MAGIC)
                                {
                                    return new ArchiveInfo()
                                    {
                                        ArchiveOffset = archiveOffset,
                                        ArchiveSize = archiveSize
                                    };
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }



        /// <summary>
        /// Recursively get all the files from the directory info.
        /// </summary>
        /// <param name="dirInfo">Information about the directory.</param>
        /// <returns>An enumerable collection of files in the directory and its subdirectories.</returns>       
        static IEnumerable<FileInfo> GetFiles(DirectoryInfo dirInfo)
        {
            IEnumerable<FileSystemInfo> dirContent = dirInfo.GetFileSystemInfos();

            foreach (DirectoryInfo subDir in dirContent.OfType<DirectoryInfo>()) 
                foreach (var fileInfo in GetFiles(subDir))
                    yield return fileInfo;
                               
            foreach (FileInfo fileInfo in dirContent.OfType<FileInfo>())
                yield return fileInfo;
        }


        /// <summary>
        /// Create a self-extracting archive.
        /// </summary>
        /// <param name="assemblyInfo">Information about the assembly file.</param>
        /// <param name="sourceDir">The source directory to create the archive from.</param>
        static void CreateArchive(FileInfo assemblyInfo, DirectoryInfo sourceDir)
        {            

            using (var selfStream = File.Create(Path.Combine(assemblyInfo.DirectoryName, sourceDir.Name + ".exe")))
            {
                byte[] assemblyBytes = File.ReadAllBytes(assemblyInfo.FullName);
                selfStream.Write(assemblyBytes, 0, assemblyBytes.Length);

                using (var archive = new ZipArchive(selfStream, ZipArchiveMode.Create, true))
                {                    
                    foreach (var fileInfo in GetFiles(sourceDir))
                    {
                        var entryName = fileInfo.FullName.Substring(sourceDir.FullName.Length + 1);

                        Console.WriteLine($"Compressing {entryName}");
                        
                        var zipArchiveEntry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

                        using (var zipStream = zipArchiveEntry.Open())
                        zipStream.Write(assemblyBytes, 0, assemblyBytes.Length);
                    }
                }

                var archiveSize = selfStream.Position - assemblyBytes.Length;
                selfStream.Write(BitConverter.GetBytes(archiveSize), 0, 8);
                selfStream.Write(BitConverter.GetBytes(STORROR_MAGIC), 0, 8);
            }
        }


        /// <summary>
        /// Extract the embedded archive from the self-extracting archive.
        /// </summary>
        /// <param name="assemblyInfo">Information about the assembly file.</param>
        /// <param name="offset">Offset to the archive within the executable file.</param>
        /// <param name="targetDir">The target directory to extract the archive into.</param>
        static void ExtractArchive(FileInfo assemblyInfo, long offset, DirectoryInfo targetDir)
        {
            using (var archiveStream = File.OpenRead(assemblyInfo.FullName))
            {
                archiveStream.Seek(offset, SeekOrigin.Begin);

                using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        var targetInfo = new FileInfo(Path.Combine(targetDir.FullName, entry.FullName));
                        targetInfo.Directory.Create();
                        
                        Console.WriteLine($"Extracting {targetInfo.FullName}...");
                        entry.ExtractToFile(targetInfo.FullName, true);
                    }
                }
            }
        }



        static int Main(string[] args)
        {
            var assemblyInfo = new FileInfo(Assembly.GetExecutingAssembly().Location);
            var archiveInfo = GetArchiveInfo(assemblyInfo);

            if (args.Length == 0)
            {
                if (archiveInfo != null)
                {
                    Console.WriteLine($"Provide target path to extract archive.");
                    Console.WriteLine($"Archive size: {archiveInfo.ArchiveSize}");
                    Console.WriteLine($"Usage: selfie.exe <target path>");
                }
                else
                {
                    Console.WriteLine($"Provide source path to create zip archive.");
                    Console.WriteLine($"Usage: selfie.exe <source path>");
                }

                return -1;
            }

            var directory = new DirectoryInfo(args[0]);

            if (archiveInfo != null) 
                ExtractArchive(assemblyInfo, archiveInfo.ArchiveOffset, directory);
            else
                CreateArchive(assemblyInfo, new DirectoryInfo(args[0]));


            return 0;
        }
    }
}
