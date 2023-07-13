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
            // Open the assembly file for reading
            using (var stream = File.OpenRead(assemblyInfo.FullName))
            {
                // Seek to the end of the file minus the tail size where the archive metadata is located
                stream.Seek(-TAIL_SIZE, SeekOrigin.End);
                var buffer = new byte[TAIL_SIZE];

                // Read the buffer containing the archive metadata
                if (stream.Read(buffer, 0, buffer.Length) == TAIL_SIZE)
                {
                    // Check if the magic number at the expected position is valid
                    if (BitConverter.ToInt64(buffer, 8) == STORROR_MAGIC)
                    {
                        // Retrieve the archive size and offset from the metadata
                        var archiveSize = BitConverter.ToInt64(buffer, 0);
                        var archiveOffset = stream.Length - archiveSize - TAIL_SIZE;

                        // Seek to the start of the archive within the executable file
                        if (stream.Seek(archiveOffset, SeekOrigin.Begin) == archiveOffset)
                        {
                            // Read the magic number at the beginning of the archive to verify it
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
            // Get the file system entries in the directory
            IEnumerable<FileSystemInfo> dirContent = dirInfo.GetFileSystemInfos();

            foreach (DirectoryInfo subDir in dirContent.OfType<DirectoryInfo>())
            {
                // Recursively get files from subdirectories
                foreach (var fileInfo in GetFiles(subDir))
                    yield return fileInfo;
            }

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
            // Create the self-extracting archive file from the directory name
            using (var selfStream = File.Create(Path.Combine(sourceDir.Parent.FullName, sourceDir.Name + ".exe")))
            {
                // Read the bytes of the assembly file
                byte[] assemblyBytes = File.ReadAllBytes(assemblyInfo.FullName);
                selfStream.Write(assemblyBytes, 0, assemblyBytes.Length);

                // Create a new ZipArchive using the self-extracting archive file stream
                using (var archive = new ZipArchive(selfStream, ZipArchiveMode.Create, true))
                {
                    // Add each file in the source directory and its subdirectories to the archive
                    foreach (var fileInfo in GetFiles(sourceDir))
                    {
                        var entryName = fileInfo.FullName.Substring(sourceDir.FullName.Length + 1);

                        Console.WriteLine($"Compressing {entryName}");

                        // Create an entry in the archive for the file
                        var zipArchiveEntry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                        
                        using (var zipStream = zipArchiveEntry.Open())                        
                        {
                            using (var fileStream = File.OpenRead(fileInfo.FullName))
                            {
                                // Write the file bytes to the entry stream
                                fileStream.CopyTo(zipStream, 1024 * 1024);
                            }
                        }
                    }
                }

                // Write the archive size and magic number to the end of the self-extracting archive
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
            // Open the self-extracting archive for reading
            using (var archiveStream = File.OpenRead(assemblyInfo.FullName))
            {
                // Seek to the start of the embedded archive within the executable file
                archiveStream.Seek(offset, SeekOrigin.Begin);

                // Create a ZipArchive from the embedded archive stream
                using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read))
                {
                    // Extract each entry from the archive to the target directory
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




       
        /// <summary>
        /// Create self-extracting archive from itself or extract content attached to this executable.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int Main(string[] args)
        {
            // Get the information about the current assembly file
            var assemblyInfo = new FileInfo(Assembly.GetExecutingAssembly().Location);

            // Get the information about the embedded archive within the assembly
            var archiveInfo = GetArchiveInfo(assemblyInfo);

            if (args.Length == 0)
            {
                if (archiveInfo != null)
                {
                    Console.WriteLine($"Provide a target path to extract the archive.");
                    Console.WriteLine($"Archive size: {archiveInfo.ArchiveSize}");
                    Console.WriteLine($"Usage: selfie.exe <target path>");
                }
                else
                {
                    Console.WriteLine($"Provide a source path to create a zip archive.");
                    Console.WriteLine($"Usage: selfie.exe <source path>");
                }

                return -1;
            }

            var directory = new DirectoryInfo(args[0]);

            if (archiveInfo != null)
            {
                // Extract the embedded archive from the self-extracting archive
                ExtractArchive(assemblyInfo, archiveInfo.ArchiveOffset, directory);
            }
            else
            {
                // Create a self-extracting archive from the source directory
                CreateArchive(assemblyInfo, new DirectoryInfo(args[0]));
            }

            return 0;
        }
    }
}
