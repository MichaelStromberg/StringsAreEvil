using System;
using System.IO;
using System.Text;

namespace Lean
{
    public sealed class MpsSpanReader : IDisposable
    {
        private const int  MaxBufferSize  = 4096;
        private const byte NewLine        = 0x0a;
        private const byte CarriageReturn = 0x0d;
        
        private readonly Stream _stream;
        
        private readonly byte[] _byteBuffer = new byte[MaxBufferSize];
        private int _byteBufferOffset;
        private int _numBytes;

        private bool lastBuffer;
        private bool eof;
        
        // this is only used for the current line
        private readonly char[] _charLineBuffer = new char[MaxBufferSize];

        public MpsSpanReader(Stream stream)
        {
            _stream = stream;
        }

        public ReadOnlySpan<char> ReadLine()
        {
            if (eof) return null;
            
            (int lineEndOffset, int nextLineOffset) = GetNewLinePosition();
            
            if (lineEndOffset == -1)
            {
                FillBuffer();
                (lineEndOffset, nextLineOffset) = GetNewLinePosition();
                if (lineEndOffset == -1) throw new InvalidDataException("Could not adjust the buffers");
            }

            int length = lineEndOffset - _byteBufferOffset + 1;
            // GetChars(length);
            Encoding.ASCII.GetChars(_byteBuffer, _byteBufferOffset, length, _charLineBuffer, 0);
            _byteBufferOffset = nextLineOffset;

            if (lastBuffer && nextLineOffset == _numBytes) eof = true;
            
            return new ReadOnlySpan<char>(_charLineBuffer, 0, length);
        }

        private void FillBuffer()
        {
            MoveBuffer();

            int numAvailableBytes = MaxBufferSize - _numBytes;
            _numBytes += _stream.Read(_byteBuffer, _numBytes, numAvailableBytes);

            if (_numBytes < numAvailableBytes) lastBuffer = true;
        }

        private void MoveBuffer()
        {
            int numRemainingBytes = _numBytes - _byteBufferOffset;

            if (numRemainingBytes > 0)
            {
                Array.Copy(_byteBuffer, _byteBufferOffset, _byteBuffer, 0, numRemainingBytes);
            }

            _byteBufferOffset = 0;
            _numBytes = numRemainingBytes;
        }

        private unsafe (int LineEndOffset, int NextLineOffset) GetNewLinePosition()
        {
            int newLinePosition = -1;

            fixed (byte* pByte = _byteBuffer)
            {
                var remaining = _numBytes - _byteBufferOffset;
                var offset = _byteBufferOffset;
                
                while (remaining-- > 0)
                {
                    if (*pByte == NewLine)
                    {
                        newLinePosition = offset;
                        break;
                    }

                    pByte++;
                }
            }
            
            for (int offset = _byteBufferOffset; offset < _numBytes; offset++)
            {
                if (_byteBuffer[offset] == NewLine)
                {
                    newLinePosition = offset;
                    break;
                }
            }

            if (newLinePosition != -1)
            {
                int lineEndOffset  = newLinePosition - 1;
                // int nextLineOffset = ;

                // if (nextLineOffset > MaxBufferSize) nextLineOffset = MaxBufferSize;
                    
                if (lineEndOffset > 0 && _byteBuffer[lineEndOffset] == CarriageReturn) lineEndOffset--;
                return (lineEndOffset, newLinePosition + 1);
            }
            
            if (lastBuffer && _numBytes != MaxBufferSize) return (_numBytes - 1, _numBytes);
            return (-1, -1);
        }

        public void Dispose() => _stream?.Dispose();
    }
}