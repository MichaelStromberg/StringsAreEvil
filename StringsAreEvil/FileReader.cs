using System.Buffers;
using System.IO;
using System.Text;

namespace StringsAreEvil
{
    public static class FileReader
    {
        public static void Read(ILineParser lineParser, string filePath)
        {
            var sb       = new StringBuilder();
            var charPool = ArrayPool<char>.Shared;

            using (var reader = File.OpenRead(filePath))
            {
                var endOfFile = false;

                while (reader.CanRead)
                {
                    sb.Clear();

                    while (true)
                    {
                        int readByte = reader.ReadByte();

                        // -1 means end of file
                        if (readByte == -1)
                        {
                            endOfFile = true;
                            break;
                        }

                        var character = (char)readByte;

                        // this means the line is about to end so we skip
                        if (character == '\r') continue;

                        // this line has ended
                        if (character == '\n') break;

                        sb.Append(character);
                    }

                    if (endOfFile) break;

                    var rentedCharBuffer = charPool.Rent(sb.Length);

                    try
                    {
                        for (int index = 0; index < sb.Length; index++)
                        {
                            rentedCharBuffer[index] = sb[index];
                        }

                        lineParser.ParseLine(rentedCharBuffer);
                    }
                    finally
                    {
                        charPool.Return(rentedCharBuffer, true);
                    }
                }
            }
        }
    }
}
