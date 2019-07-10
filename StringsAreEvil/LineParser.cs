using System;

namespace StringsAreEvil
{
    public class LineParser
    {
        public decimal TotalMileage;

        private const int MaxCommas    = 7;
        private readonly int[] _commas = new int[MaxCommas];
        private const char Comma       = ',';

        public void ParseLine(Span<char> charSpan)
        {
            if (charSpan[0] != 'M' || charSpan[1] != 'N' || charSpan[2] != 'O') return;

            FindCommas(charSpan, _commas);

            int elementId = GetInt(charSpan, _commas[0], _commas[1]);
            int vehicleId = GetInt(charSpan, _commas[1], _commas[2]);
            int term      = GetInt(charSpan, _commas[2], _commas[3]);
            int mileage   = GetInt(charSpan, _commas[3], _commas[4]);
            decimal value = GetDecimal(charSpan, _commas[4], _commas[5]);

            var temp      = new ValueHolder(elementId, vehicleId, term, mileage, value);
            TotalMileage += temp.Mileage;
        }

        private static decimal GetDecimal(ReadOnlySpan<char> charSpan, int commaStart, int commaEnd) =>
            charSpan.Slice(commaStart + 1, commaEnd - commaStart - 1).OptimizedParseDecimal();

        private static int GetInt(ReadOnlySpan<char> charSpan, int commaStart, int commaEnd) =>
            charSpan.Slice(commaStart + 1, commaEnd - commaStart - 1).OptimizedParseInt32();

        private static void FindCommas(Span<char> charSpan, int[] commaArray)
        {
            var commaIndex = 0;

            for (var pos = 0; pos < charSpan.Length; pos++)
            {
                if (charSpan[pos] != Comma) continue;
                commaArray[commaIndex] = pos;
                commaIndex++;
            }
        }
    }
}
