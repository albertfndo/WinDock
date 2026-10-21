using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace WinDock.Services
{
    /// <summary>
    /// Service providing smart file operations such as batch ZIP archiving and Explorer integration.
    /// </summary>
    public class FileToolsService
    {
        private static FileToolsService? _instance;
        public static FileToolsService Instance => _instance ??= new FileToolsService();

        private FileToolsService() { }

        /// <summary>
        /// Compresses the specified files and directories into a single ZIP archive.
        /// If outputZipPath is null or empty, creates a timestamped archive on the user's Desktop
        /// (e.g. WinDock_Archive_20261021_2052.zip) or in the temp directory.
        /// </summary>
        /// <param name="filePaths">List of file and/or directory paths to archive.</param>
        /// <param name="outputZipPath">Optional explicit destination ZIP path.</param>
        /// <returns>The full path to the created ZIP archive.</returns>
        public string CreateZipArchive(IEnumerable<string> filePaths, string? outputZipPath = null)
        {
            if (filePaths == null)
            {
                throw new ArgumentNullException(nameof(filePaths));
            }

            var pathList = filePaths
                .Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pathList.Count == 0)
            {
                throw new InvalidOperationException("No existing files or directories provided to archive.");
            }

            string tempZipPath = Path.Combine(Path.GetTempPath(), $"WinDock_Archive_{Guid.NewGuid():N}.zip");
            int addedFilesCount = 0;
            var skippedOfflineFiles = new List<string>();

            try
            {
                using (ZipArchive archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
                {
                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (string path in pathList)
                    {
                        if (Directory.Exists(path))
                        {
                            string rootDirName = new DirectoryInfo(path).Name;
                            IEnumerable<string> files;
                            try
                            {
                                files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
                            }
                            catch (Exception ex) when (IsCloudFileError(ex))
                            {
                                skippedOfflineFiles.Add(rootDirName);
                                continue;
                            }

                            foreach (string file in files)
                            {
                                try
                                {
                                    string relPath = Path.GetRelativePath(path, file);
                                    string entryName = Path.Combine(rootDirName, relPath).Replace('\\', '/');

                                    string uniqueName = entryName;
                                    int dup = 1;
                                    string entryDir = Path.GetDirectoryName(entryName)?.Replace('\\', '/') ?? "";
                                    string entryBase = Path.GetFileNameWithoutExtension(entryName);
                                    string entryExt = Path.GetExtension(entryName);

                                    while (usedNames.Contains(uniqueName))
                                    {
                                        string newFile = $"{entryBase}_{dup++}{entryExt}";
                                        uniqueName = string.IsNullOrEmpty(entryDir) ? newFile : $"{entryDir}/{newFile}";
                                    }

                                    usedNames.Add(uniqueName);
                                    archive.CreateEntryFromFile(file, uniqueName);
                                    addedFilesCount++;
                                }
                                catch (Exception ex) when (IsCloudFileError(ex))
                                {
                                    skippedOfflineFiles.Add(Path.GetFileName(file));
                                }
                            }
                        }
                        else if (File.Exists(path))
                        {
                            try
                            {
                                string fileName = Path.GetFileName(path);
                                string uniqueName = fileName;
                                int dup = 1;
                                string baseName = Path.GetFileNameWithoutExtension(fileName);
                                string ext = Path.GetExtension(fileName);

                                while (usedNames.Contains(uniqueName))
                                {
                                    uniqueName = $"{baseName}_{dup++}{ext}";
                                }

                                usedNames.Add(uniqueName);
                                archive.CreateEntryFromFile(path, uniqueName);
                                addedFilesCount++;
                            }
                            catch (Exception ex) when (IsCloudFileError(ex))
                            {
                                skippedOfflineFiles.Add(Path.GetFileName(path));
                            }
                        }
                    }
                }

                if (addedFilesCount == 0 && skippedOfflineFiles.Count > 0)
                {
                    throw new InvalidOperationException("Files are offline in OneDrive. Start OneDrive or download files first.");
                }

                // Determine final destination
                string baseFileName = Path.GetFileName(outputZipPath) ?? $"WinDock_Archive_{DateTime.Now:yyyyMMdd_HHmm}.zip";
                if (string.IsNullOrWhiteSpace(baseFileName))
                {
                    baseFileName = $"WinDock_Archive_{DateTime.Now:yyyyMMdd_HHmm}.zip";
                }

                string finalDestination = string.IsNullOrWhiteSpace(outputZipPath) ? baseFileName : outputZipPath;
                bool copied = false;

                // 1. Try specified outputZipPath
                try
                {
                    string? dir = Path.GetDirectoryName(finalDestination);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    if (File.Exists(finalDestination))
                    {
                        File.Delete(finalDestination);
                    }
                    File.Copy(tempZipPath, finalDestination, true);
                    copied = true;
                }
                catch (Exception ex) when (IsCloudFileError(ex) || ex is UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileToolsService] Cloud path failed: {ex.Message}. Falling back to local Desktop/Downloads.");
                }

                // 2. Fallback to physical local Desktop (bypassing OneDrive sync folder)
                if (!copied)
                {
                    try
                    {
                        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        string localDesktop = Path.Combine(userProfile, "Desktop");
                        if (!Directory.Exists(localDesktop)) Directory.CreateDirectory(localDesktop);
                        finalDestination = Path.Combine(localDesktop, baseFileName);
                        File.Copy(tempZipPath, finalDestination, true);
                        copied = true;
                    }
                    catch (Exception ex) when (IsCloudFileError(ex) || ex is UnauthorizedAccessException) { }
                }

                // 3. Fallback to local Downloads directory
                if (!copied)
                {
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string localDownloads = Path.Combine(userProfile, "Downloads");
                    if (!Directory.Exists(localDownloads)) Directory.CreateDirectory(localDownloads);
                    finalDestination = Path.Combine(localDownloads, baseFileName);
                    File.Copy(tempZipPath, finalDestination, true);
                }

                return finalDestination;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                }
                catch { }
            }
        }

        private static bool IsCloudFileError(Exception ex)
        {
            if (ex is IOException ioEx && ((uint)ioEx.HResult == 0x8007016A || ioEx.Message.Contains("cloud file provider", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            if (ex is System.Runtime.InteropServices.COMException comEx && (uint)comEx.HResult == 0x8007016A)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Opens Windows File Explorer with the specified file or folder selected.
        /// </summary>
        /// <param name="path">The file or folder path to reveal.</param>
        public void OpenInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileToolsService] OpenInExplorer failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Formats an enumeration of file paths into a newline-separated string.
        /// </summary>
        public string FormatFilePaths(IEnumerable<string> filePaths)
        {
            if (filePaths == null) return string.Empty;
            return string.Join(Environment.NewLine, filePaths.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }
}
