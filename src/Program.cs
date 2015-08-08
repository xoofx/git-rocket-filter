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
            //program.WhiteListSpecifiers = @"/** => entry.IsBlob && !entry.IsBinary && entry.DataAsText.Contains(""contact"")";
            program.WhiteListSpecifiers = @"/** {%
    if (entry.IsBlob && !entry.IsBinary && entry.DataAsText.Contains(""contact""))
    {
        Console.WriteLine(""Match {0}"", entry.Path);
        return true;
    }
    return false;
%}
";
            program.BranchName = "master2";

//            program.WhiteListSpecifiers = @"External/gccxml
//                External/Mono.Cecil
//                External/Mono.Options
//                External/ICSharpCode.SharpZipLib
//                External/HtmlAgilityPack
//                Source/Tools/SharpCli
//                Source/Tools/SharpGen
//                Source/Tools/SharpCore";
//            program.BranchName = "master2";
            
            
            
            program.Process();
            Console.WriteLine("Elapsed: {0}ms", clock.ElapsedMilliseconds);
        }
    }
}
    