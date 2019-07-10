using System.IO;

namespace StringsAreEvil
{
    public static class FileReader
    {
        public static void Read2(ILineParser lineParser, string filePath)
        {
            using (var reader = new LeanStreamReader(File.OpenRead(filePath), lineParser))
            {
                while (reader.ReadLine()) {}
            }
        }
    }
}
