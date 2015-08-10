using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LibGit2Sharp;
using Mono.Options;

namespace GitRocketFilter
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            var clock = Stopwatch.StartNew();

            var rocket = new RocketFilterApp();

            var exeName = Path.GetFileNameWithoutExtension(typeof(Program).Assembly.Location);
            bool showHelp = false;
            var keeps = new StringBuilder();
            var deletes = new StringBuilder();

            var _ = string.Empty;
            var options = new OptionSet
            {
                "Copyright (C) 2015 Alexandre Mutel. All Rights Reserved",
                "git-rocket-filter-branch - Version: "
                +
                String.Format(
                    "{0}.{1}.{2}",
                    typeof (Program).Assembly.GetName().Version.Major,
                    typeof (Program).Assembly.GetName().Version.Minor,
                    typeof (Program).Assembly.GetName().Version.Build) + string.Empty,
                _,
                string.Format("Usage: {0} --branch <new_branch_name> [options]+ [revspec]", exeName),
                _,
                "A single [revspec] is currently supported (either HEAD, commit-id, or a from..to). Default is HEAD ",
                _,
                "## Options",
                _,
                {"b|branch=", "All filtering will be created into the new branch specified by the {<name>}.", v=> rocket.BranchName = v},
                _,
                {"force", "If a branch is specified and a branch with the same name already exists, it will be overwritten.", (bool v) => rocket.BranchOverwrite = v},
                _,
                {"h|help", "Show this message and exit", (bool v) => showHelp = v},
                {"v|verbose", "Show more verbose progress logs", (bool v) => rocket.Verbose = v},
                _,
                "## Options for commit filtering",
                _,
                {"c|commit-filter=", "Perform a rewrite of each commit by passing an {<expression>}. If the <expression> is true, the commit is kept, otherwise it is skipped. See Examples.", v => rocket.CommitFilter = v},
                {"commit-filter-script=", "Perform a rewrite of each commit by passing a file {<script>}. See Examples.", v => rocket.CommitFilter = SafeReadText(v, "commit-filter-script")},
                {"detach", "Detach first commits rewritten from their original parents.", (bool v) => rocket.DetachFirstCommits = v },
                _,
                _,
                "## Options for tree filtering",
                _,
                "The following options are using .gitignore like patterns with extended C# scripting support. See Examples for more details.",
                _,
                {"k|keep=", "Keep files that match the {<pattern>} from the current tree being visited (whitelist).", v => keeps.AppendLine(v)},
                _,
                {"keep-from-file=", "Keep files that match the patterns defined in the {<pattern_file>} from the current tree being visited (whitelist).", v=> keeps.Append(SafeReadText(v, "keep-from-file"))},
                _,
                {"d|delete=", "Delete files that match the {<pattern>} from the current tree being visited (blacklist).", v => deletes.AppendLine(v)},
                _,
                {"delete-from-file=", "Delete files that match the patterns defined in the {<pattern_file>} from the current tree being visited (blacklist). ", v=> deletes.Append(SafeReadText(v, "delete-from-file"))},
                _,
                "## Examples",
                _,
                "Both commit filtering and tree filtering can run at the same time.",
                _,
                "### Commit-Filtering",
                _,
                "1) " + exeName + " --branch newMaster --commit-filter 'commit.Discard = commit.AuthorName.Length <= 10;'",
                _,
                "   Keeps only commits with an author name with a length > 10.",
                _,
                "2) " + exeName + " -b newMaster -c 'if (commit.AuthorName.Contains(\"Marc\")) {{ commit.AuthorName = \"Jim\"; }}'",
                _,
                "   Keeps all commits and rewrite commits with author name [Marc] by replacing by [Jim].",
                _,
                "3) " + exeName + " -b newMaster -c 'commit.Message += \"Added by rewrite!\";' HEAD~10..HEAD",
                _,
                "   Rewrite 10 last commits from HEAD by adding a text to their commit message.",
                _,
                "### Tree-Filtering",
                _,
                "1) " + exeName + " --branch newMaster --keep /MyFolder",
                _,
                "   Keeps only all files recursively from [/MyFolder] and write the new commits to the [newMaster]",
                "   branch.",
                _,
                "2) " + exeName + " --branch newMaster --delete /MyFolder",
                _,
                "   Delete only all files recursively from [/MyFolder] and write the new commits to the [newMaster]",
                "   branch.",
                _,
                "3) " + exeName + " --branch newMaster --keep /MyFolder --delete /MyFolder/Test.txt",
                _,
                "   Keeps all files recursively from [/MyFolder] except [Test.txt] and write the new commits to the",
                "   [newMaster] branch.",
                _,
                "4) " + exeName + " --branch newMaster --keep /MyFolder 158085b5..HEAD",
                _,
                "   Keeps only all files recursively from [/MyFolder] from a specific commit to the head and write",
                "   the new commits to the [newMaster] branch.",
                _,
                "5) " + exeName + " --branch newMaster --keep \"/MyFolder => entry.Discard = entry.Size > 1024;\"",
                _,
                "   Keeps recursively only files that are less than 1024 bytes from [/MyFolder] and write the new ",
                "   commits to the [newMaster] branch.",
                _,
                "Note that on Windows with msysgit, path are interpreted and can lead to unexpected behavior when using --keep or --delete option on the command line.",
                "Check http://www.mingw.org/wiki/Posix_path_conversion for more details",
                _,
                "For more advanced usages, see https://github.com/xoofx/GitRocketFilter"
            };

            options.OptionWidth = 40;
            options.LineWidth = 100;
            options.ShiftNewLine = 0;
            try
            {
                var arguments = options.Parse(args);

                if (showHelp)
                {
                    options.WriteOptionDescriptions(Console.Out);
                    return 0;
                }

                // Check that we don't have any options ending in the arguments
                foreach (var argument in arguments)
                {
                    if (argument.StartsWith("-"))
                    {
                        throw new RocketException("Unexpected option [{0}]", argument);
                    }
                }

                if (arguments.Count > 1)
                {
                    throw new RocketException("Expected only a single revspec. Unexpected arguments [{0}]", string.Join(" ", arguments.Skip(1)));
                }
                else if (arguments.Count == 1)
                {
                    rocket.RevisionRange = arguments[0];
                }

                rocket.RepositoryPath = Repository.Discover(Environment.CurrentDirectory);

                if (rocket.RepositoryPath == null)
                {
                    throw new RocketException("No git directory found from [{0}]", Environment.CurrentDirectory);
                }

                rocket.WhiteListPathPatterns = keeps.ToString();
                rocket.BlackListPathPatterns = deletes.ToString();
                rocket.Run();
            }
            catch (Exception exception)
            {
                if (exception is OptionException || exception is RocketException)
                {
                    var backColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(exception.Message);
                    Console.ForegroundColor = backColor;
                    var rocketException = exception as RocketException;
                    if (rocketException != null && rocketException.AdditionalText != null)
                    {
                        Console.WriteLine(rocketException.AdditionalText);
                    }
                    Console.WriteLine("See --help for usage");
                    return 1;
                }
                else
                {
                    throw;
                }
            }

            //if (options.RepositoryPath != null &&
            //    !Directory.Exists(options.RepositoryPath))
            //{
            //    Console.Error.WriteLine("Invalid directory [{0}]",
            //        options.RepositoryPath);
            //    return 1;
            //}

            //var repoPath = options.RepositoryPath ??
            //                Environment.CurrentDirectory;

            //if (!Repository.IsValid(repoPath))
            //{
            //    Console.Error.WriteLine("There is no valid repository in the directory [{0}]", repoPath);


            //    Console.Error.WriteLine(new AutomaticHelpGenerator<Options>().GetUsage());
            //    return 1;
            //}

            //var rocket =
            //    new RocketFilterApp(options.RepositoryPath ??
            //                            Environment.CurrentDirectory);

            //if (!options.KeepPatterns.Any() && !options.DeletePatterns.Any() && !options.KeepPatternsFiles.Any() &&
            //    !options.DeletePatternsFiles.Any())
            //{
            //    Console.Error.WriteLine(new AutomaticHelpGenerator<Options>().GetUsage());

            //    Console.Error.WriteLine("Expecting at least a keep or delete pattern");
            //    return 1;
            //}

            //rocket.WhiteListPathPatterns = string.Join("\n",
            //    options.KeepPatterns);

            //rocket.BlackListPathPatterns = string.Join("\n",
            //    options.DeletePatterns);


            //// TODO: Add from files

            //rocket.BranchName = options.Branch;

            //

            return 0;

//            var program = new RocketFilterApp(args[0]);

//            var clock = Stopwatch.StartNew();
//            //program.WhiteListPathPatterns = @"/** => entry.IsBlob && !entry.IsBinary && entry.DataAsText.Contains(""contact"")";
//            program.WhiteListPathPatterns = @"/** {%
//    if (entry.IsBlob && !entry.IsBinary && entry.DataAsText.Contains(""contact""))
//    {
//        Console.WriteLine(""Match {0}"", entry.Path);
//        return true;
//    }
//    return false;
//%}
//";
//            program.BranchName = "master2";

//            program.WhiteListPathPatterns = @"External/gccxml
//                External/Mono.Cecil
//                External/Mono.Options
//                External/ICSharpCode.SharpZipLib
//                External/HtmlAgilityPack
//                Source/Tools/SharpCli
//                Source/Tools/SharpGen
//                Source/Tools/SharpCore";
//            program.BranchName = "master2";
            
            
            
            //program.Process();
            Console.WriteLine("Elapsed: {0}ms", clock.ElapsedMilliseconds);
        }

        private static string SafeReadText(string path, string optionName)
        {
            var scriptPath = Path.Combine(Environment.CurrentDirectory, path);
            if (!File.Exists(scriptPath))
            {
                throw new OptionException(string.Format("File [{0}] not found", path), optionName);
            }

            return File.ReadAllText(scriptPath);
        }
    }
}
    