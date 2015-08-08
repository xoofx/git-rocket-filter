using System;
using System.Diagnostics;
using System.Text;

namespace GitRocketFilterBranch
{

    internal class Program
    {
        private static void Main(string[] args)
        {
            var program = new RocketFilterBranch(args[0]);

            var clock = Stopwatch.StartNew();
            program.WhiteListSpecifiers.AddRange(new[]
            {
                "External/gccxml",
                "External/Mono.Cecil",
                "External/Mono.Options",
                "External/ICSharpCode.SharpZipLib",
                "External/HtmlAgilityPack",
                "Source/Tools/SharpCli",
                "Source/Tools/SharpGen",
                "Source/Tools/SharpCore",
            });

            program.Process();
            Console.WriteLine("Elapsed: {0}ms", clock.ElapsedMilliseconds);
        }
    }
}
    