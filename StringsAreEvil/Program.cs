using System;
using System.IO;
using System.Text;
using StringsAreEvil.Utilities;

namespace StringsAreEvil
{
    class Program
    {
        private static void RunMps()
        {
            
        }

        private static void RunStreamReader()
        {
            
        }
        
        public static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("USAGE: {0} <input file path>", Path.GetFileName(Environment.GetCommandLineArgs()[0]));
                Environment.Exit(1);
            }

            string filePath = args[0];

            // using (var reader = new SpanReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan)))
            // {
            //     while (true)
            //     {
            //         var line = reader.ReadLine();
            //         if (line == null) break;
            //         Console.WriteLine(line);
            //     }
            // }
            
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
            
            // using (var reader = new MpsSpanReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan)))
            // {
            //     while (true)
            //     {
            //         var span = reader.ReadLine();
            //         if (span == null) break;
            //         // Console.WriteLine($"[{span.ToString()}]");
            //         numLines++;
            //     }
            // }
            
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
