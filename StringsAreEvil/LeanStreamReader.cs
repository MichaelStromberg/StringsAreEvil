using System;
using System.IO;
using System.Text;

namespace StringsAreEvil
{
    public class LeanStreamReader : IDisposable
    {
        private readonly Stream _stream;
        private readonly ILineParser _lineParser;

        private const int MaxBufferSize   = 4096;
        private const byte NewLine        = 0x0a;
        private const byte CarriageReturn = 0x0d;

        private readonly byte[] _byteBuffer = new byte[MaxBufferSize];
        private int _numBufferBytes;
        private int _byteBufferOffset;

        private readonly char[] _charBuffer = new char[MaxBufferSize];

        public LeanStreamReader(Stream stream, ILineParser lineParser)
        {
            _stream     = stream;
            _lineParser = lineParser;
        }

        private bool FillBuffer()
        {
            //Console.WriteLine("FillBuffer");
            int numRemainingBytes = _numBufferBytes - _byteBufferOffset;

            if (numRemainingBytes > 0)
            {
                Array.Copy(_byteBuffer, _byteBufferOffset, _byteBuffer, 0, numRemainingBytes);
            }

            _byteBufferOffset = numRemainingBytes;

            int numAdditionalBytes = MaxBufferSize - _byteBufferOffset;

            int numBytesRead = _stream.Read(_byteBuffer, _byteBufferOffset, numAdditionalBytes);
            if (numBytesRead == 0) return false;

            _numBufferBytes   = _byteBufferOffset + numBytesRead;
            _byteBufferOffset = 0;

            return true;
        }

        public bool ReadLine()
        {
            int length;
            int nextPosition;

            while (true)
            {
                (length, nextPosition) = GetLineLength();
                if (length != -1) break;
                if (!FillBuffer()) return false;
            }

            Encoding.ASCII.GetChars(_byteBuffer, _byteBufferOffset, length, _charBuffer, 0);
            _byteBufferOffset = nextPosition;

            var span = new Span<char>(_charBuffer, 0, length);
            _lineParser.ParseLine(span);

            return true;
        }

        private (int Length, int NextPosition) GetLineLength()
        {
            var length       = 0;
            var foundEol     = false;
            int nextPosition = 0;

            for (int offset = _byteBufferOffset; offset < _numBufferBytes; offset++, length++)
            {
                if (_byteBuffer[offset] != NewLine) continue;

                nextPosition = offset + 1;
                if (_byteBuffer[offset - 1] == CarriageReturn) length--;
                foundEol = true;
                break;
            }

            return foundEol ? (length, nextPosition) : (-1, -1);
        }

        public void Dispose() => _stream?.Dispose();
    }
}
