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
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(
                    typeof(T),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 250000 });
                object? value = serializer.ReadObject(stream);
                if (value is T typed) return typed;
                throw new InvalidDataException("JSON 資料格式不正確：" + path);
            }
        }

        public static void Write<T>(string path, T value)
        {
            byte[] bytes;
            using (MemoryStream buffer = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(
                    typeof(T),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 250000 });
                serializer.WriteObject(buffer, value);
                bytes = buffer.ToArray();
            }

            BinaryUtil.WriteAtomically(path, bytes, overwrite: true);
        }

        public static string ReadUtf8Text(string path)
        {
            return File.ReadAllText(path, BinaryUtil.Utf8Strict);
        }

        public static void WriteUtf8Text(string path, string text)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text ?? string.Empty);
            BinaryUtil.WriteAtomically(path, bytes, overwrite: true);
        }
    }
}
