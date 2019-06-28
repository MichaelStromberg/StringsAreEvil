using System.IO;

namespace StringsAreEvil
{
    public static class FileReader
    {
        public static void Read(ILineParser lineParser, string filePath)
        {
            using (StreamReader reader = File.OpenText(filePath))
            {
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    lineParser.ParseLine(line);
                }
            }
        }
    }
}
