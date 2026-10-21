using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WinDock.Native
{
    public static class ShortcutHelper
    {
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        internal class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        internal interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        internal interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
        }

        /// <summary>
        /// Resolves a Windows shortcut (.lnk) file to its target path.
        /// </summary>
        public static string ResolveShortcut(string lnkPath)
        {
            var (target, _, _) = ResolveShortcutDetails(lnkPath);
            return target;
        }

        /// <summary>
        /// Resolves a Windows shortcut (.lnk) file to its target path, icon location, and icon index.
        /// </summary>
        public static (string targetPath, string iconPath, int iconIndex) ResolveShortcutDetails(string lnkPath)
        {
            if (!File.Exists(lnkPath)) return (lnkPath, string.Empty, 0);

            try
            {
                var link = (IShellLinkW)new ShellLink();
                var file = (IPersistFile)link;
                file.Load(lnkPath, 0);

                var sbTarget = new StringBuilder(1024);
                link.GetPath(sbTarget, sbTarget.Capacity, out _, 0);

                var sbIcon = new StringBuilder(1024);
                int iconIndex = 0;
                try
                {
                    link.GetIconLocation(sbIcon, sbIcon.Capacity, out iconIndex);
                }
                catch { }

                string target = sbTarget.ToString();
                string iconLoc = sbIcon.ToString();

                if (!string.IsNullOrWhiteSpace(iconLoc))
                {
                    iconLoc = Environment.ExpandEnvironmentVariables(iconLoc);
                }

                return (string.IsNullOrWhiteSpace(target) ? lnkPath : target, iconLoc, iconIndex);
            }
            catch
            {
                return (lnkPath, string.Empty, 0);
            }
        }
    }
}
