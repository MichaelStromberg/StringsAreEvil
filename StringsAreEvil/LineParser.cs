namespace StringsAreEvil
{
    // Took:             00:00:09.3
    // Allocated:        162.8 KB
    // Peak Working Set: 14.1 MB
    //
    // Gen 0 collections: 0
    // Gen 1 collections: 0
    // Gen 2 collections: 0
    public sealed class LineParser : ILineParser
    {
        public long TotalMileage;

        public void ParseLine(char[] line)
        {
            if (line[0] == 'M' && line[1] == 'N' && line[2] == 'O')
            {
                int elementId = ParseSectionAsInt(line, 1);
                int vehicleId = ParseSectionAsInt(line, 2);
                int term      = ParseSectionAsInt(line, 3);
                int mileage   = ParseSectionAsInt(line, 4);
                decimal value = ParseSectionAsDecimal(line, 5);
                var temp      = new ValueHolder(elementId, vehicleId, term, mileage, value);
                TotalMileage += temp.Mileage;
            }
        }

        private static decimal ParseSectionAsDecimal(char[] line, int numberOfCommasToSkip)
        {
            decimal val           = 0;
            bool seenDot          = false;
            ulong fractionCounter = 10;
            int counter           = 0;
            bool flip             = false;

            for (var index = 0; index < line.Length; index++)
            {
                // move along the line until we have skipped the required amount of commas
                if (line[index] == ',')
                {
                    counter++;

                    if (counter > numberOfCommasToSkip)
                    {
                        break;
                    }
                    continue;
                }

                // we have skipped enough commas, the next section before the upcoming comma is what we are interested in
                if (counter == numberOfCommasToSkip)
                {
                    // the number is a negative means we have to flip it at the end.
                    if (line[index] == '-')
                    {
                        flip = true;
                        continue;
                    }

                    if (line[index] == '.')
                    {
                        seenDot = true;
                        continue;
                    }

                    // before the . eg; 12.34 this looks for the 12
                    if (char.IsNumber(line[index]) && seenDot == false)
                    {
                        val *= 10;
                        val += line[index] - '0';
                        continue;
                    }

                    // after the . eg; 12.34 this looks for the 34
                    if (char.IsNumber(line[index]) && seenDot)
                    {
                        val += decimal.Divide(line[index] - '0', fractionCounter);
                        fractionCounter *= 10;
                    }
                }
            }

            return flip ? -val : val;
        }

        private static int ParseSectionAsInt(char[] line, int numberOfCommasToSkip)
        {
            int val     = 0;
            int counter = 0;
            bool flip   = false;

            for (var index = 0; index < line.Length; index++)
            {
                // move along the line until we have skipped the required amount of commas
                if (line[index] == ',')
                {
                    counter++;

                    if (counter > numberOfCommasToSkip)
                    {
                        break;
                    }

                    continue;
                }

                // we have skipped enough commas, the next section before the upcoming comma is what we are interested in
                if (counter == numberOfCommasToSkip)
                {
                    // the number is a negative means we have to flip it at the end.
                    if (line[index] == '-')
                    {
                        flip = true;
                        continue;
                    }

                    val *= 10;
                    val += line[index] - '0';
                }
            }

            return flip ? -val : val;
        }
    }
}