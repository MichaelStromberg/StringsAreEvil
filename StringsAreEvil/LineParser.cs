namespace StringsAreEvil
{
    // Took:             00:00:10.5
    // Allocated:        7.024 GB
    // Peak Working Set: 18.0 MB
    //
    // Gen 0 collections: 1798
    // Gen 1 collections: 1
    // Gen 2 collections: 0
    public sealed class LineParser : ILineParser
    {
        public long TotalMileage;

        public void ParseLine(string line)
        {
            var parts = line.Split(',');
            if (parts[0] == "MNO")
            {
                var temp = new ValueHolder(line);
                TotalMileage += temp.Mileage;
            }
        }
    }
}