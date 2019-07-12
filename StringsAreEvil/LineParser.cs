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
        private static ValueHolder _valueHolder = new ValueHolder();
        public void ParseLine(string line)
        {
            int i = 0;
            var intVal = 0;
            (i, intVal) = GetNextInt(line, i);
            (i, intVal) = GetNextInt(line, i+1);
            var elementId = intVal;

            (i, intVal) = GetNextInt(line, i+1);
            var vehicleId = intVal;
            (i, intVal) = GetNextInt(line, i+1);
            var term = intVal;

            (i, intVal) = GetNextInt(line, i+1);
            var mileage = intVal;

            (i, intVal) = GetNextInt(line, i+1);
            var value = intVal;

            //var parts = line.Split(',');
            //var elementId = int.Parse(parts[1]); 
            //var vehicleId = int.Parse(parts[2]);
            //var term = int.Parse(parts[3]);
            //var mileage = int.Parse(parts[4]);
            //var value = decimal.Parse(parts[5]);


            if (line[0] == 'M' && line[1]=='N' && line[2] == 'O')
            {
                _valueHolder.Set(elementId, vehicleId, term, mileage, value);
                var temp = _valueHolder;
                TotalMileage += temp.Mileage;
            }
        }

        private (int,int ) GetNextInt(string line, int i)
        {
            int num = 0;
            while (line[i] != ',')
            {
                num *= 10;
                num += (int)(line[i] - '0');
                i++;
            }
            return (i, num);
        }
    }
}