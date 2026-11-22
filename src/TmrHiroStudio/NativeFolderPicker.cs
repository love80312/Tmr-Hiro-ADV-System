using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TmrHiroStudio
{
    /// <summary>
    /// 使用 Windows Shell IFileDialog 顯示現代化資料夾選擇視窗。
    /// 不依賴舊版 WinForms FolderBrowserDialog。
    /// </summary>
    internal static class NativeFolderPicker
    {
        private const int ErrorCancelled = unchecked((int)0x800704C7);
        private static readonly Guid ShellItemGuid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        [Flags]
        private enum FileOpenOptions : uint
        {
            OverwritePrompt = 0x00000002,
            StrictFileTypes = 0x00000004,
            NoChangeDirectory = 0x00000008,
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040,
            AllowMultiSelect = 0x00000200,
            PathMustExist = 0x00000800,
            FileMustExist = 0x00001000,
            DontAddToRecent = 0x02000000
        }

        private enum DisplayNameType : uint
        {
            FileSystemPath = 0x80058000
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialogComObject
        {
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FileOpenOptions fos);
            void GetOptions(out FileOpenOptions pfos);
            void SetDefaultFolder([MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void SetFolder([MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName(out IntPtr pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void AddPlace([MarshalAs(UnmanagedType.Interface)] IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void GetDisplayName(DisplayNameType sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare([MarshalAs(UnmanagedType.Interface)] IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        public static string? Show(Window owner, string title, string initialDirectory)
        {
            object? dialogObject = null;
            IFileDialog? dialog = null;
            IShellItem? initialItem = null;
            IShellItem? selectedItem = null;
            IntPtr displayName = IntPtr.Zero;

            try
            {
                dialogObject = new FileOpenDialogComObject();
                dialog = (IFileDialog)dialogObject;

                FileOpenOptions options;
                dialog.GetOptions(out options);
                options |= FileOpenOptions.PickFolders |
                           FileOpenOptions.ForceFileSystem |
                           FileOpenOptions.PathMustExist |
                           FileOpenOptions.DontAddToRecent |
                           FileOpenOptions.NoChangeDirectory;
                dialog.SetOptions(options);
                dialog.SetTitle(string.IsNullOrWhiteSpace(title) ? "選擇資料夾" : title);

                if (!string.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
                {
                    Guid iid = ShellItemGuid;
                    IShellItem folderItem;
                    SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iid, out folderItem);
                    initialItem = folderItem;
                    dialog.SetFolder(folderItem);
                }

                IntPtr ownerHandle = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
                int result = dialog.Show(ownerHandle);
                if (result == ErrorCancelled) return null;
                if (result < 0) Marshal.ThrowExceptionForHR(result);

                IShellItem resultItem;
                dialog.GetResult(out resultItem);
                selectedItem = resultItem;
                resultItem.GetDisplayName(DisplayNameType.FileSystemPath, out displayName);
                string? selectedPath = Marshal.PtrToStringUni(displayName);
                if (string.IsNullOrWhiteSpace(selectedPath))
                    throw new InvalidOperationException("Windows 資料夾選擇視窗沒有回傳有效路徑。");

                return selectedPath;
            }
            finally
            {
                if (displayName != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(displayName);

                ReleaseComObject(selectedItem);
                ReleaseComObject(initialItem);
                ReleaseComObject(dialog);
                ReleaseComObject(dialogObject);
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (value == null) return;
            try
            {
                if (Marshal.IsComObject(value))
                    Marshal.FinalReleaseComObject(value);
            }
            catch
            {
                // COM 釋放失敗不應覆蓋原始操作結果或例外。
            }
        }
    }
}
