// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.
using System;
using System.IO;
using System.Linq;
using System.Text;
using LibGit2Sharp;
using Mono.Options;

namespace GitRocketFilter
{
    /// <summary>
    /// Main entry point for git-rocket-filter
    /// </summary>
    public class Program
    {
        [ThreadStatic] 
        public static TextWriter RedirectOutput;

        public static int Main(params string[] args)
        {
            var rocket = new RocketFilterApp();

            if (RedirectOutput == null)
            {
                RedirectOutput = Console.Out;
            }
            rocket.OutputWriter = RedirectOutput;

            var exeName = Path.GetFileNameWithoutExtension(typeof(Program).Assembly.Location);
            bool showHelp = false;
            var keeps = new StringBuilder();
            var removes = new StringBuilder();

            string repositoryPath = Environment.CurrentDirectory;


            var _ = string.Empty;
            var options = new OptionSet
            {
                "Copyright (C) 2015 Alexandre Mutel. All Rights Reserved",
                "git-rocket-filter - Version: "
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
                {"d|repo-dir=", "By default git-rocket-filter is running in the current directory expected to be a git repository. You can change this repository by passing a new repository path with this option", v => repositoryPath = v },
                {"disable-threads", "By default git-rocket-filter is running on multiple threads. This option allow to disable this feature.", (bool v) => rocket.DisableTasks = v },
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
                {"r|remove=", "Remove files that match the {<pattern>} from the current tree being visited (blacklist).", v => removes.AppendLine(v)},
                _,
                {"remove-from-file=", "Remove files that match the patterns defined in the {<pattern_file>} from the current tree being visited (blacklist). ", v=> removes.Append(SafeReadText(v, "remove-from-file"))},
                _,
                {"include-links", "Include submodule git links for tree filtering (default is false). ", (bool v)=> rocket.IncludeLinks = v},
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
                "2) " + exeName + " --branch newMaster --remove /MyFolder",
                _,
                "   Removes only all files recursively from [/MyFolder] and write the new commits to the [newMaster]",
                "   branch.",
                _,
                "3) " + exeName + " --branch newMaster --keep /MyFolder --remove /MyFolder/Test.txt",
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
                "Note that on Windows with msysgit, path are interpreted and can lead to unexpected behavior when using --keep or --remove option on the command line.",
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
                    options.WriteOptionDescriptions(rocket.OutputWriter);
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
                    throw new RocketException("Expected only a single revspec. Unexpected arguments [{0}]",
                        string.Join(" ", arguments.Skip(1)));
                }
                else if (arguments.Count == 1)
                {
                    rocket.RevisionRange = arguments[0];
                }

                rocket.RepositoryPath = Repository.Discover(repositoryPath);

                if (rocket.RepositoryPath == null)
                {
                    throw new RocketException("No git directory found from [{0}]", repositoryPath);
                }

                rocket.KeepPatterns = keeps.ToString();
                rocket.RemovePatterns = removes.ToString();
                rocket.Run();
            }
            catch (Exception exception)
            {
                if (exception is OptionException || exception is RocketException)
                {
                    var backColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    RedirectOutput.WriteLine(exception.Message);
                    Console.ForegroundColor = backColor;
                    var rocketException = exception as RocketException;
                    if (rocketException != null && rocketException.AdditionalText != null)
                    {
                        RedirectOutput.WriteLine(rocketException.AdditionalText);
                    }
                    RedirectOutput.WriteLine("See --help for usage");
                    return 1;
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                rocket.Dispose();
            }
            return 0;
        }

        private static string SafeReadText(string path, string optionName)
        {
            var scriptPath = Path.Combine(Environment.CurrentDirectory, path);
            if (!File.Exists(scriptPath))
            {
                throw new OptionException(string.Format("File [{0}] not found", path), optionName);
            }

            // Make sure that we have a end-of-line at the end
            return File.ReadAllText(scriptPath) + "\n";
        }
    }
}
    