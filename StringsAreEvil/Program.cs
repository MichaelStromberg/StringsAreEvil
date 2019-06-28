using System;
using System.IO;
using StringsAreEvil.Utilities;

namespace StringsAreEvil
{
    class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("USAGE: {0} <input file path>", Path.GetFileName(Environment.GetCommandLineArgs()[0]));
                Environment.Exit(1);
            }

            string filePath = args[0];
            var lineParser  = new LineParser();
            var benchmark   = new Benchmark();

            Console.Write("Start parsing file... ");
            
            FileReader.Read(lineParser, filePath);
            string elapsedTime = Benchmark.ToHumanReadable(benchmark.GetElapsedTime());

            Console.WriteLine($"{lineParser.TotalMileage:N0} miles.\n");

            Console.WriteLine($"Took:             {elapsedTime}");
            Console.WriteLine($"Allocated:        {MemoryUtilities.ToHumanReadable(GC.GetAllocatedBytesForCurrentThread())}");
            Console.WriteLine($"Peak Working Set: {MemoryUtilities.ToHumanReadable(MemoryUtilities.GetPeakMemoryUsage())}\n");

            for (var index = 0; index <= GC.MaxGeneration; index++)
            {
                Console.WriteLine($"Gen {index} collections: {GC.CollectionCount(index)}");
            }
        }
    }
}
