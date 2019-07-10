using System;

namespace StringsAreEvil
{
    public static class StringExtensions
    {
        /// <summary>
        /// handles -2_147_483_647 to +2_147_483_647
        /// </summary>
        public static int OptimizedParseInt32(this ReadOnlySpan<char> charSpan)
        {
            var number = 0;

            // 2_147_483_647
            if (charSpan.Length == 0 || charSpan.Length > 11) throw new ArgumentException();

            int index         = charSpan.Length - 1;
            int i             = 0;
            var applyNegative = false;

            if (charSpan[i] == '-')
            {
                applyNegative = true;
                i++;
                index--;
            }

            while (index >= 0)
            {
                var ptr = charSpan[i];
                if (ptr < 48 || ptr > 57) throw new ArgumentException();

                checked
                {
                    number *= 10;
                    number += ptr - '0';
                    i++;
                }

                index--;
            }

            if (applyNegative) number = -number;

            return number;
        }
    }
}