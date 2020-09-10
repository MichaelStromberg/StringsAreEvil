using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StringsAreEvil
{
    public sealed class SpanReader : IDisposable
    {
        private const int DefaultBufferSize = 1024;
        private const int DefaultFileStreamBufferSize = 4096;
        private const int MinBufferSize = 128;

        private readonly Stream _stream;
        private Encoding _encoding;
        private Decoder _decoder; 
        private readonly byte[] _byteBuffer;
        private char[] _charBuffer;
        private int _charPos;
        private int _charLen;

        private int _byteLen;
        private int _bytePos;
        
        private int _maxCharsPerBuffer;
        
        private bool _disposed;
        
        private bool _detectEncoding;
        
        private bool _checkPreamble;
        
        private bool _isBlocked;
        
        private readonly bool _closable;
        
        private Task _asyncReadTask = Task.CompletedTask;

        private void CheckAsyncTaskInProgress()
        {
            // We are not locking the access to _asyncReadTask because this is not meant to guarantee thread safety.
            // We are simply trying to deter calling any Read APIs while an async Read from the same thread is in progress.
            if (!_asyncReadTask.IsCompleted)
            {
                ThrowAsyncIOInProgress();
            }
        }

        private static void ThrowAsyncIOInProgress() =>
            throw new InvalidOperationException();
        
        public SpanReader(Stream stream, bool leaveOpen = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException();

            _stream = stream;
            _encoding = Encoding.ASCII;
            _decoder = _encoding.GetDecoder();
            
            _byteBuffer = new byte[DefaultBufferSize];
            _maxCharsPerBuffer = _encoding.GetMaxCharCount(DefaultBufferSize);
            _charBuffer = new char[_maxCharsPerBuffer];
            _byteLen = 0;
            _bytePos = 0;
            _isBlocked = false;
            _closable = !leaveOpen;
        }

        protected void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            // Dispose of our resources if this StreamReader is closable.
            if (_closable)
            {
                try
                {
                    // Note that Stream.Close() can potentially throw here. So we need to
                    // ensure cleaning up internal resources, inside the finally block.
                    if (disposing)
                    {
                        _stream.Close();
                    }
                }
                finally
                {
                    _charPos = 0;
                    _charLen = 0;
                }
                
                Dispose();
            }
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public int Read(char[] buffer, int index, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (index < 0 || count < 0)
            {
                throw new ArgumentOutOfRangeException(index < 0 ? nameof(index) : nameof(count));
            }
            if (buffer.Length - index < count)
            {
                throw new ArgumentException();
            }

            return ReadSpan(new Span<char>(buffer, index, count));
        }

        public int Read(Span<char> buffer) => ReadSpan(buffer);
        
        public int ReadSpan(Span<char> buffer)
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            int charsRead = 0;
            // As a perf optimization, if we had exactly one buffer's worth of
            // data read in, let's try writing directly to the user's buffer.
            bool readToUserBuffer = false;
            int count = buffer.Length;
            while (count > 0)
            {
                int n = _charLen - _charPos;
                if (n == 0)
                {
                    n = ReadBuffer(buffer.Slice(charsRead), out readToUserBuffer);
                }
                if (n == 0)
                {
                    break;  // We're at EOF
                }
                if (n > count)
                {
                    n = count;
                }
                if (!readToUserBuffer)
                {
                    new Span<char>(_charBuffer, _charPos, n).CopyTo(buffer.Slice(charsRead));
                    _charPos += n;
                }

                charsRead += n;
                count -= n;
                // This function shouldn't block for an indefinite amount of time,
                // or reading from a network stream won't work right.  If we got
                // fewer bytes than we requested, then we want to break right here.
                if (_isBlocked)
                {
                    break;
                }
            }

            return charsRead;
        }
        
        public int ReadBlock(Span<char> buffer)
        {
            int i, n = 0;
            do
            {
                i = ReadSpan(buffer.Slice(n));
                n += i;
            } while (i > 0 && n < buffer.Length);
            return n;
        }

        // Trims n bytes from the front of the buffer.
        private void CompressBuffer(int n)
        {
            Debug.Assert(_byteLen >= n, "CompressBuffer was called with a number of bytes greater than the current buffer length.  Are two threads using this StreamReader at the same time?");
            Buffer.BlockCopy(_byteBuffer, n, _byteBuffer, 0, _byteLen - n);
            _byteLen -= n;
        }

        private void DetectEncoding()
        {
            if (_byteLen < 2)
            {
                return;
            }
            _detectEncoding = false;
            bool changedEncoding = false;
            if (_byteBuffer[0] == 0xFE && _byteBuffer[1] == 0xFF)
            {
                // Big Endian Unicode

                _encoding = Encoding.BigEndianUnicode;
                CompressBuffer(2);
                changedEncoding = true;
            }
            else if (_byteBuffer[0] == 0xFF && _byteBuffer[1] == 0xFE)
            {
                // Little Endian Unicode, or possibly little endian UTF32
                if (_byteLen < 4 || _byteBuffer[2] != 0 || _byteBuffer[3] != 0)
                {
                    _encoding = Encoding.Unicode;
                    CompressBuffer(2);
                    changedEncoding = true;
                }
                else
                {
                    _encoding = Encoding.UTF32;
                    CompressBuffer(4);
                    changedEncoding = true;
                }
            }
            else if (_byteLen >= 3 && _byteBuffer[0] == 0xEF && _byteBuffer[1] == 0xBB && _byteBuffer[2] == 0xBF)
            {
                // UTF-8
                _encoding = Encoding.UTF8;
                CompressBuffer(3);
                changedEncoding = true;
            }
            else if (_byteLen >= 4 && _byteBuffer[0] == 0 && _byteBuffer[1] == 0 &&
                _byteBuffer[2] == 0xFE && _byteBuffer[3] == 0xFF)
            {
                // Big Endian UTF32
                _encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
                CompressBuffer(4);
                changedEncoding = true;
            }
            else if (_byteLen == 2)
            {
                _detectEncoding = true;
            }
            // Note: in the future, if we change this algorithm significantly,
            // we can support checking for the preamble of the given encoding.

            if (changedEncoding)
            {
                _decoder = _encoding.GetDecoder();
                int newMaxCharsPerBuffer = _encoding.GetMaxCharCount(_byteBuffer.Length);
                if (newMaxCharsPerBuffer > _maxCharsPerBuffer)
                {
                    _charBuffer = new char[newMaxCharsPerBuffer];
                }
                _maxCharsPerBuffer = newMaxCharsPerBuffer;
            }
        }

        // Trims the preamble bytes from the byteBuffer. This routine can be called multiple times
        // and we will buffer the bytes read until the preamble is matched or we determine that
        // there is no match. If there is no match, every byte read previously will be available
        // for further consumption. If there is a match, we will compress the buffer for the
        // leading preamble bytes
        private bool IsPreamble()
        {
            if (!_checkPreamble)
            {
                return _checkPreamble;
            }

            ReadOnlySpan<byte> preamble = _encoding.Preamble;

            Debug.Assert(_bytePos <= preamble.Length, "_compressPreamble was called with the current bytePos greater than the preamble buffer length.  Are two threads using this StreamReader at the same time?");
            int len = (_byteLen >= (preamble.Length)) ? (preamble.Length - _bytePos) : (_byteLen - _bytePos);

            for (int i = 0; i < len; i++, _bytePos++)
            {
                if (_byteBuffer[_bytePos] != preamble[_bytePos])
                {
                    _bytePos = 0;
                    _checkPreamble = false;
                    break;
                }
            }

            Debug.Assert(_bytePos <= preamble.Length, "possible bug in _compressPreamble.  Are two threads using this StreamReader at the same time?");

            if (_checkPreamble)
            {
                if (_bytePos == preamble.Length)
                {
                    // We have a match
                    CompressBuffer(preamble.Length);
                    _bytePos = 0;
                    _checkPreamble = false;
                    _detectEncoding = false;
                }
            }

            return _checkPreamble;
        }

        internal int ReadBuffer()
        {
            _charLen = 0;
            _charPos = 0;

            if (!_checkPreamble)
            {
                _byteLen = 0;
            }

            do
            {
                if (_checkPreamble)
                {
                    Debug.Assert(_bytePos <= _encoding.Preamble.Length, "possible bug in _compressPreamble.  Are two threads using this StreamReader at the same time?");
                    int len = _stream.Read(_byteBuffer, _bytePos, _byteBuffer.Length - _bytePos);
                    Debug.Assert(len >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                    if (len == 0)
                    {
                        // EOF but we might have buffered bytes from previous
                        // attempt to detect preamble that needs to be decoded now
                        if (_byteLen > 0)
                        {
                            _charLen += _decoder.GetChars(_byteBuffer, 0, _byteLen, _charBuffer, _charLen);
                            // Need to zero out the byteLen after we consume these bytes so that we don't keep infinitely hitting this code path
                            _bytePos = _byteLen = 0;
                        }

                        return _charLen;
                    }

                    _byteLen += len;
                }
                else
                {
                    Debug.Assert(_bytePos == 0, "bytePos can be non zero only when we are trying to _checkPreamble.  Are two threads using this StreamReader at the same time?");
                    _byteLen = _stream.Read(_byteBuffer, 0, _byteBuffer.Length);
                    Debug.Assert(_byteLen >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                    if (_byteLen == 0)  // We're at EOF
                    {
                        return _charLen;
                    }
                }

                // _isBlocked == whether we read fewer bytes than we asked for.
                // Note we must check it here because CompressBuffer or
                // DetectEncoding will change byteLen.
                _isBlocked = (_byteLen < _byteBuffer.Length);

                // Check for preamble before detect encoding. This is not to override the
                // user supplied Encoding for the one we implicitly detect. The user could
                // customize the encoding which we will loose, such as ThrowOnError on UTF8
                if (IsPreamble())
                {
                    continue;
                }

                // If we're supposed to detect the encoding and haven't done so yet,
                // do it.  Note this may need to be called more than once.
                if (_detectEncoding && _byteLen >= 2)
                {
                    DetectEncoding();
                }

                _charLen += _decoder.GetChars(_byteBuffer, 0, _byteLen, _charBuffer, _charLen);
            } while (_charLen == 0);
            return _charLen;
        }


        // This version has a perf optimization to decode data DIRECTLY into the
        // user's buffer, bypassing StreamReader's own buffer.
        // This gives a > 20% perf improvement for our encodings across the board,
        // but only when asking for at least the number of characters that one
        // buffer's worth of bytes could produce.
        // This optimization, if run, will break SwitchEncoding, so we must not do
        // this on the first call to ReadBuffer.
        private int ReadBuffer(Span<char> userBuffer, out bool readToUserBuffer)
        {
            _charLen = 0;
            _charPos = 0;

            if (!_checkPreamble)
            {
                _byteLen = 0;
            }

            int charsRead = 0;

            // As a perf optimization, we can decode characters DIRECTLY into a
            // user's char[].  We absolutely must not write more characters
            // into the user's buffer than they asked for.  Calculating
            // encoding.GetMaxCharCount(byteLen) each time is potentially very
            // expensive - instead, cache the number of chars a full buffer's
            // worth of data may produce.  Yes, this makes the perf optimization
            // less aggressive, in that all reads that asked for fewer than AND
            // returned fewer than _maxCharsPerBuffer chars won't get the user
            // buffer optimization.  This affects reads where the end of the
            // Stream comes in the middle somewhere, and when you ask for
            // fewer chars than your buffer could produce.
            readToUserBuffer = userBuffer.Length >= _maxCharsPerBuffer;

            do
            {
                Debug.Assert(charsRead == 0);

                if (_checkPreamble)
                {
                    Debug.Assert(_bytePos <= _encoding.Preamble.Length, "possible bug in _compressPreamble.  Are two threads using this StreamReader at the same time?");
                    int len = _stream.Read(_byteBuffer, _bytePos, _byteBuffer.Length - _bytePos);
                    Debug.Assert(len >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                    if (len == 0)
                    {
                        // EOF but we might have buffered bytes from previous
                        // attempt to detect preamble that needs to be decoded now
                        if (_byteLen > 0)
                        {
                            if (readToUserBuffer)
                            {
                                charsRead = _decoder.GetChars(new ReadOnlySpan<byte>(_byteBuffer, 0, _byteLen), userBuffer.Slice(charsRead), flush: false);
                                _charLen = 0;  // StreamReader's buffer is empty.
                            }
                            else
                            {
                                charsRead = _decoder.GetChars(_byteBuffer, 0, _byteLen, _charBuffer, charsRead);
                                _charLen += charsRead;  // Number of chars in StreamReader's buffer.
                            }
                        }

                        return charsRead;
                    }

                    _byteLen += len;
                }
                else
                {
                    Debug.Assert(_bytePos == 0, "bytePos can be non zero only when we are trying to _checkPreamble.  Are two threads using this StreamReader at the same time?");

                    _byteLen = _stream.Read(_byteBuffer, 0, _byteBuffer.Length);

                    Debug.Assert(_byteLen >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                    if (_byteLen == 0)  // EOF
                    {
                        break;
                    }
                }

                // _isBlocked == whether we read fewer bytes than we asked for.
                // Note we must check it here because CompressBuffer or
                // DetectEncoding will change byteLen.
                _isBlocked = (_byteLen < _byteBuffer.Length);

                // Check for preamble before detect encoding. This is not to override the
                // user supplied Encoding for the one we implicitly detect. The user could
                // customize the encoding which we will loose, such as ThrowOnError on UTF8
                // Note: we don't need to recompute readToUserBuffer optimization as IsPreamble
                // doesn't change the encoding or affect _maxCharsPerBuffer
                if (IsPreamble())
                {
                    continue;
                }

                // On the first call to ReadBuffer, if we're supposed to detect the encoding, do it.
                if (_detectEncoding && _byteLen >= 2)
                {
                    DetectEncoding();
                    // DetectEncoding changes some buffer state.  Recompute this.
                    readToUserBuffer = userBuffer.Length >= _maxCharsPerBuffer;
                }

                _charPos = 0;
                if (readToUserBuffer)
                {
                    charsRead += _decoder.GetChars(new ReadOnlySpan<byte>(_byteBuffer, 0, _byteLen), userBuffer.Slice(charsRead), flush: false);
                    _charLen = 0;  // StreamReader's buffer is empty.
                }
                else
                {
                    charsRead = _decoder.GetChars(_byteBuffer, 0, _byteLen, _charBuffer, charsRead);
                    _charLen += charsRead;  // Number of chars in StreamReader's buffer.
                }
            } while (charsRead == 0);

            _isBlocked &= charsRead < userBuffer.Length;

            return charsRead;
        }


        // Reads a line. A line is defined as a sequence of characters followed by
        // a carriage return ('\r'), a line feed ('\n'), or a carriage return
        // immediately followed by a line feed. The resulting string does not
        // contain the terminating carriage return and/or line feed. The returned
        // value is null if the end of the input stream has been reached.
        //
        public string? ReadLine()
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            if (_charPos == _charLen)
            {
                if (ReadBuffer() == 0)
                {
                    return null;
                }
            }

            StringBuilder? sb = null;
            
            do
            {
                int i = _charPos;
                
                do
                {
                    char ch = _charBuffer[i];
                    // Note the following common line feed chars:
                    // \n - UNIX   \r\n - DOS   \r - Mac
                    if (ch == '\r' || ch == '\n')
                    {
                        string s;
                        if (sb != null)
                        {
                            sb.Append(_charBuffer, _charPos, i - _charPos);
                            s = sb.ToString();
                        }
                        else
                        {
                            s = new string(_charBuffer, _charPos, i - _charPos);
                        }
                        _charPos = i + 1;
                        if (ch == '\r' && (_charPos < _charLen || ReadBuffer() > 0))
                        {
                            if (_charBuffer[_charPos] == '\n')
                            {
                                _charPos++;
                            }
                        }
                        return s;
                    }
                    i++;
                } while (i < _charLen);

                i = _charLen - _charPos;
                sb ??= new StringBuilder(i + 80);
                sb.Append(_charBuffer, _charPos, i);
            } while (ReadBuffer() > 0);
            return sb.ToString();
        }
        
        public ReadOnlySpan<char> ReadLineSpan()
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            if (_charPos == _charLen)
            {
                if (ReadBuffer() == 0)
                {
                    return null;
                }
            }

            StringBuilder? sb = null;
            
            do
            {
                int i = _charPos;
                
                do
                {
                    char ch = _charBuffer[i];
                    
                    // Note the following common line feed chars:
                    // \n - UNIX   \r\n - DOS   \r - Mac
                    if (ch == '\r' || ch == '\n')
                    {
                        string s;
                        if (sb != null)
                        {
                            sb.Append(_charBuffer, _charPos, i - _charPos);
                            s = sb.ToString();
                        }
                        else
                        {
                            s = new string(_charBuffer, _charPos, i - _charPos);
                        }
                        
                        _charPos = i + 1;
                        
                        if (ch == '\r' && (_charPos < _charLen || ReadBuffer() > 0))
                        {
                            if (_charBuffer[_charPos] == '\n')
                            {
                                _charPos++;
                            }
                        }
                        
                        return s;
                    }
                    
                    i++;
                } while (i < _charLen);

                i = _charLen - _charPos;
                sb ??= new StringBuilder(i + 80);
                sb.Append(_charBuffer, _charPos, i);
            } while (ReadBuffer() > 0);
            
            return sb.ToString();
        }

        public Task<string?> ReadLineAsync()
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            Task<string?> task = ReadLineAsyncInternal();
            _asyncReadTask = task;

            return task;
        }

        private async Task<string?> ReadLineAsyncInternal()
        {
            if (_charPos == _charLen && (await ReadBufferAsync(CancellationToken.None).ConfigureAwait(false)) == 0)
            {
                return null;
            }

            StringBuilder? sb = null;

            do
            {
                char[] tmpCharBuffer = _charBuffer;
                int tmpCharLen = _charLen;
                int tmpCharPos = _charPos;
                int i = tmpCharPos;

                do
                {
                    char ch = tmpCharBuffer[i];

                    // Note the following common line feed chars:
                    // \n - UNIX   \r\n - DOS   \r - Mac
                    if (ch == '\r' || ch == '\n')
                    {
                        string s;

                        if (sb != null)
                        {
                            sb.Append(tmpCharBuffer, tmpCharPos, i - tmpCharPos);
                            s = sb.ToString();
                        }
                        else
                        {
                            s = new string(tmpCharBuffer, tmpCharPos, i - tmpCharPos);
                        }

                        _charPos = tmpCharPos = i + 1;

                        if (ch == '\r' && (tmpCharPos < tmpCharLen || (await ReadBufferAsync(CancellationToken.None).ConfigureAwait(false)) > 0))
                        {
                            tmpCharPos = _charPos;
                            if (_charBuffer[tmpCharPos] == '\n')
                            {
                                _charPos = ++tmpCharPos;
                            }
                        }

                        return s;
                    }

                    i++;
                } while (i < tmpCharLen);

                i = tmpCharLen - tmpCharPos;
                sb ??= new StringBuilder(i + 80);
                sb.Append(tmpCharBuffer, tmpCharPos, i);
            } while (await ReadBufferAsync(CancellationToken.None).ConfigureAwait(false) > 0);

            return sb.ToString();
        }

        public Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (index < 0 || count < 0)
            {
                throw new ArgumentOutOfRangeException(index < 0 ? nameof(index) : nameof(count));
            }
            if (buffer.Length - index < count)
            {
                throw new ArgumentException();
            }

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            Task<int> task = ReadAsyncInternal(new Memory<char>(buffer, index, count), CancellationToken.None).AsTask();
            _asyncReadTask = task;

            return task;
        }

        public ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
            }

            return ReadAsyncInternal(buffer, cancellationToken);
        }

        internal async ValueTask<int> ReadAsyncInternal(Memory<char> buffer, CancellationToken cancellationToken)
        {
            if (_charPos == _charLen && (await ReadBufferAsync(cancellationToken).ConfigureAwait(false)) == 0)
            {
                return 0;
            }

            int charsRead = 0;

            // As a perf optimization, if we had exactly one buffer's worth of
            // data read in, let's try writing directly to the user's buffer.
            bool readToUserBuffer = false;

            byte[] tmpByteBuffer = _byteBuffer;
            Stream tmpStream = _stream;

            int count = buffer.Length;
            while (count > 0)
            {
                // n is the characters available in _charBuffer
                int n = _charLen - _charPos;

                // charBuffer is empty, let's read from the stream
                if (n == 0)
                {
                    _charLen = 0;
                    _charPos = 0;

                    if (!_checkPreamble)
                    {
                        _byteLen = 0;
                    }

                    readToUserBuffer = count >= _maxCharsPerBuffer;

                    // We loop here so that we read in enough bytes to yield at least 1 char.
                    // We break out of the loop if the stream is blocked (EOF is reached).
                    do
                    {
                        Debug.Assert(n == 0);

                        if (_checkPreamble)
                        {
                            Debug.Assert(_bytePos <= _encoding.Preamble.Length, "possible bug in _compressPreamble.  Are two threads using this StreamReader at the same time?");
                            int tmpBytePos = _bytePos;
                            int len = await tmpStream.ReadAsync(new Memory<byte>(tmpByteBuffer, tmpBytePos, tmpByteBuffer.Length - tmpBytePos), cancellationToken).ConfigureAwait(false);
                            Debug.Assert(len >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                            if (len == 0)
                            {
                                // EOF but we might have buffered bytes from previous
                                // attempts to detect preamble that needs to be decoded now
                                if (_byteLen > 0)
                                {
                                    if (readToUserBuffer)
                                    {
                                        n = _decoder.GetChars(new ReadOnlySpan<byte>(tmpByteBuffer, 0, _byteLen), buffer.Span.Slice(charsRead), flush: false);
                                        _charLen = 0;  // StreamReader's buffer is empty.
                                    }
                                    else
                                    {
                                        n = _decoder.GetChars(tmpByteBuffer, 0, _byteLen, _charBuffer, 0);
                                        _charLen += n;  // Number of chars in StreamReader's buffer.
                                    }
                                }

                                // How can part of the preamble yield any chars?
                                Debug.Assert(n == 0);

                                _isBlocked = true;
                                break;
                            }
                            else
                            {
                                _byteLen += len;
                            }
                        }
                        else
                        {
                            Debug.Assert(_bytePos == 0, "_bytePos can be non zero only when we are trying to _checkPreamble.  Are two threads using this StreamReader at the same time?");

                            _byteLen = await tmpStream.ReadAsync(new Memory<byte>(tmpByteBuffer), cancellationToken).ConfigureAwait(false);

                            Debug.Assert(_byteLen >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                            if (_byteLen == 0)  // EOF
                            {
                                _isBlocked = true;
                                break;
                            }
                        }

                        // _isBlocked == whether we read fewer bytes than we asked for.
                        // Note we must check it here because CompressBuffer or
                        // DetectEncoding will change _byteLen.
                        _isBlocked = (_byteLen < tmpByteBuffer.Length);

                        // Check for preamble before detect encoding. This is not to override the
                        // user supplied Encoding for the one we implicitly detect. The user could
                        // customize the encoding which we will loose, such as ThrowOnError on UTF8
                        // Note: we don't need to recompute readToUserBuffer optimization as IsPreamble
                        // doesn't change the encoding or affect _maxCharsPerBuffer
                        if (IsPreamble())
                        {
                            continue;
                        }

                        // On the first call to ReadBuffer, if we're supposed to detect the encoding, do it.
                        if (_detectEncoding && _byteLen >= 2)
                        {
                            DetectEncoding();
                            // DetectEncoding changes some buffer state.  Recompute this.
                            readToUserBuffer = count >= _maxCharsPerBuffer;
                        }

                        Debug.Assert(n == 0);

                        _charPos = 0;
                        if (readToUserBuffer)
                        {
                            n += _decoder.GetChars(new ReadOnlySpan<byte>(tmpByteBuffer, 0, _byteLen), buffer.Span.Slice(charsRead), flush: false);

                            // Why did the bytes yield no chars?
                            Debug.Assert(n > 0);

                            _charLen = 0;  // StreamReader's buffer is empty.
                        }
                        else
                        {
                            n = _decoder.GetChars(tmpByteBuffer, 0, _byteLen, _charBuffer, 0);

                            // Why did the bytes yield no chars?
                            Debug.Assert(n > 0);

                            _charLen += n;  // Number of chars in StreamReader's buffer.
                        }
                    } while (n == 0);

                    if (n == 0)
                    {
                        break;  // We're at EOF
                    }
                }  // if (n == 0)

                // Got more chars in charBuffer than the user requested
                if (n > count)
                {
                    n = count;
                }

                if (!readToUserBuffer)
                {
                    new Span<char>(_charBuffer, _charPos, n).CopyTo(buffer.Span.Slice(charsRead));
                    _charPos += n;
                }

                charsRead += n;
                count -= n;

                // This function shouldn't block for an indefinite amount of time,
                // or reading from a network stream won't work right.  If we got
                // fewer bytes than we requested, then we want to break right here.
                if (_isBlocked)
                {
                    break;
                }
            }  // while (count > 0)

            return charsRead;
        }

        private async ValueTask<int> ReadBufferAsync(CancellationToken cancellationToken)
        {
            _charLen = 0;
            _charPos = 0;
            byte[] tmpByteBuffer = _byteBuffer;
            Stream tmpStream = _stream;

            if (!_checkPreamble)
            {
                _byteLen = 0;
            }
            do
            {
                if (_checkPreamble)
                {
                    Debug.Assert(_bytePos <= _encoding.Preamble.Length, "possible bug in _compressPreamble. Are two threads using this StreamReader at the same time?");
                    int tmpBytePos = _bytePos;
                    int len = await tmpStream.ReadAsync(new Memory<byte>(tmpByteBuffer, tmpBytePos, tmpByteBuffer.Length - tmpBytePos), cancellationToken).ConfigureAwait(false);
                    Debug.Assert(len >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                    if (len == 0)
                    {
                        // EOF but we might have buffered bytes from previous
                        // attempt to detect preamble that needs to be decoded now
                        if (_byteLen > 0)
                        {
                            _charLen += _decoder.GetChars(tmpByteBuffer, 0, _byteLen, _charBuffer, _charLen);
                            // Need to zero out the _byteLen after we consume these bytes so that we don't keep infinitely hitting this code path
                            _bytePos = 0; _byteLen = 0;
                        }

                        return _charLen;
                    }

                    _byteLen += len;
                }
                else
                {
                    Debug.Assert(_bytePos == 0, "_bytePos can be non zero only when we are trying to _checkPreamble. Are two threads using this StreamReader at the same time?");
                    _byteLen = await tmpStream.ReadAsync(new Memory<byte>(tmpByteBuffer), cancellationToken).ConfigureAwait(false);
                    Debug.Assert(_byteLen >= 0, "Stream.Read returned a negative number!  Bug in stream class.");

                    if (_byteLen == 0)  // We're at EOF
                    {
                        return _charLen;
                    }
                }

                // _isBlocked == whether we read fewer bytes than we asked for.
                // Note we must check it here because CompressBuffer or
                // DetectEncoding will change _byteLen.
                _isBlocked = (_byteLen < tmpByteBuffer.Length);

                // Check for preamble before detect encoding. This is not to override the
                // user supplied Encoding for the one we implicitly detect. The user could
                // customize the encoding which we will loose, such as ThrowOnError on UTF8
                if (IsPreamble())
                {
                    continue;
                }

                // If we're supposed to detect the encoding and haven't done so yet,
                // do it.  Note this may need to be called more than once.
                if (_detectEncoding && _byteLen >= 2)
                {
                    DetectEncoding();
                }

                _charLen += _decoder.GetChars(tmpByteBuffer, 0, _byteLen, _charBuffer, _charLen);
            } while (_charLen == 0);

            return _charLen;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                ThrowObjectDisposedException();
            }

            void ThrowObjectDisposedException() => throw new ObjectDisposedException(GetType().Name);
        }
    }
}