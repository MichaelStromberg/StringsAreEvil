using System;
using System.IO;

namespace Baseline
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
            
            var benchmark = new Benchmark();
            
            var numLines = 0;

            using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.SequentialScan)))
            {
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null) break;
                    numLines++;
                }
            }
            
            Console.WriteLine($"# lines: {numLines}");
            
            string elapsedTime = Benchmark.ToHumanReadable(benchmark.GetElapsedTime());

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
