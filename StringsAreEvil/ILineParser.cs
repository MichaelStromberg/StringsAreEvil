using System;

namespace StringsAreEvil
{
    public interface ILineParser
    {
        void ParseLine(Span<char> charSpan);
    }
}
