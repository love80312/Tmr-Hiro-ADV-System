using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TmrHiroStudio.Core
{
    internal static class JsonUtil
    {
        public static T Read<T>(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 250000 });
                object? value = serializer.ReadObject(stream);
                if (value is T typed) return typed;
                throw new InvalidDataException("JSON 資料格式不正確：" + path);
            }
        }

        public static void Write<T>(string path, T value)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 250000 });
                serializer.WriteObject(stream, value);
            }
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        public static string ReadUtf8Text(string path)
        {
            return File.ReadAllText(path, new UTF8Encoding(false, true));
        }

        public static void WriteUtf8Text(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
}
