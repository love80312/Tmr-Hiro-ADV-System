using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace TmrHiroStudio
{
    /// <summary>
    /// 記錄檔案與資料夾選擇器的最後使用位置。
    /// 設定存放於 LocalApplicationData，不會寫入程式目錄，適合免安裝／唯讀目錄使用。
    /// </summary>
    internal static class PathHistory
    {
        private static readonly object SyncRoot = new object();
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TmrHiroStudio");
        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
        private static SettingsData _settings = Load();

        [DataContract]
        private sealed class SettingsData
        {
            [DataMember(Order = 1)] public string LastFileDirectory { get; set; } = string.Empty;
            [DataMember(Order = 2)] public string LastFolderDirectory { get; set; } = string.Empty;
        }

        public static string GetInitialDirectory(string currentPath, bool folderPicker)
        {
            lock (SyncRoot)
            {
                string? currentDirectory = FindExistingDirectory(currentPath);
                if (!string.IsNullOrWhiteSpace(currentDirectory))
                    return currentDirectory;

                string preferred = folderPicker ? _settings.LastFolderDirectory : _settings.LastFileDirectory;
                string secondary = folderPicker ? _settings.LastFileDirectory : _settings.LastFolderDirectory;

                if (Directory.Exists(preferred)) return Path.GetFullPath(preferred);
                if (Directory.Exists(secondary)) return Path.GetFullPath(secondary);

                string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (Directory.Exists(applicationDirectory)) return Path.GetFullPath(applicationDirectory);

                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }

        public static void RememberFileSelection(string filePath)
        {
            string? directory = null;
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                directory = Path.GetDirectoryName(fullPath);
            }
            catch
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                lock (SyncRoot)
                {
                    _settings.LastFileDirectory = directory;
                    Save();
                }
            }
        }

        public static void RememberFolderSelection(string folderPath)
        {
            string? directory = FindExistingDirectory(folderPath);
            if (string.IsNullOrWhiteSpace(directory)) return;

            lock (SyncRoot)
            {
                _settings.LastFolderDirectory = directory;
                Save();
            }
        }

        public static void RememberInputPath(string path)
        {
            if (File.Exists(path))
                RememberFileSelection(path);
            else
                RememberFolderSelection(path);
        }

        public static void RememberOutputDirectory(string path)
        {
            RememberFolderSelection(path);
        }

        private static string? FindExistingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                string fullPath = Path.GetFullPath(path.Trim().Trim('"'));
                if (File.Exists(fullPath))
                    return Path.GetDirectoryName(fullPath);
                if (Directory.Exists(fullPath))
                    return fullPath;

                // 輸出資料夾可能尚未建立；沿父層向上找最近存在的資料夾。
                string? candidate = Path.GetDirectoryName(fullPath.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                while (!string.IsNullOrWhiteSpace(candidate))
                {
                    if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
                    string? parent = Path.GetDirectoryName(candidate.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase)) break;
                    candidate = parent;
                }
            }
            catch
            {
                // 不合法的欄位值不應阻止使用其他預設位置。
            }

            return null;
        }

        private static SettingsData Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new SettingsData();
                using (FileStream stream = File.OpenRead(SettingsPath))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SettingsData));
                    object? value = serializer.ReadObject(stream);
                    if (value is SettingsData data) return data;
                }
            }
            catch
            {
                // 設定檔損壞時回復預設值；不影響主要資源處理功能。
            }

            return new SettingsData();
        }

        private static void Save()
        {
            string? tempPath = null;
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                tempPath = SettingsPath + ".tmp";
                using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SettingsData));
                    serializer.WriteObject(stream, _settings);
                    stream.Flush(true);
                }

                if (File.Exists(SettingsPath))
                    File.Replace(tempPath, SettingsPath, null);
                else
                    File.Move(tempPath, SettingsPath);
            }
            catch
            {
                // 路徑偏好是非必要設定。唯讀或受限環境下仍允許程式正常使用。
                try
                {
                    if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch { }
            }
        }
    }
}
