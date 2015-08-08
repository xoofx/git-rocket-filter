using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace GitRocketFilterBranch
{
    public class RocketFilterBranch
    {
        private delegate bool PathSpecifierCallbackDelegate(Repository repo, SimpleCommit commit, ref SimpleEntry entry);

        private readonly Dictionary<ObjectId, Commit> remap;
        private readonly Repository repo;
        private Commit lastCommit;

        private readonly HashSet<TreeEntryWrapper> entriesToKeep = new HashSet<TreeEntryWrapper>();

        private readonly List<Task> pendingTasks = new List<Task>();

        private PathSpecifiers whiteListPathSpecifiers;
        private PathSpecifiers blackListPathSpecifiers;
        private readonly string tempRocketPath;
        private SimpleCommit currentSimpleCommit;

        public RocketFilterBranch(string repoPath)
        {
            repo = new Repository(repoPath);
            remap = new Dictionary<ObjectId, Commit>();

            tempRocketPath = Path.Combine(Path.GetTempPath(), ".gitRocketFilterBranch");
            Repository.Init(tempRocketPath, true);
        }

        public string ScriptUsingDirectives { get; set; }

        public string ScriptMemberDeclarations { get; set; }

        public string WhiteListSpecifiers { get; set; }

        public string BlackListSpecifiers { get; set; }

        public string BranchName { get; set; }

        public void Process()
        {
            // Prepare specifiers for white and black list
            whiteListPathSpecifiers = PreparePathSpecifiers(WhiteListSpecifiers);
            blackListPathSpecifiers = PreparePathSpecifiers(BlackListSpecifiers);

            // Gets all commits in topological reverse order
            var commits =
                repo.Commits.QueryBy(new CommitFilter()
                {
                    FirstParentOnly = false,
                    SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse
                }).ToList();

            // Process commits
            for (int i = 0; i < commits.Count; i++)
            {
                var commit = commits[i];
                Console.Write("[{0}/{1}] Processing commit {2}\r", i, commits.Count-1, commit.Id);
                ProcessRawCommit(commit);
            }
            Console.WriteLine();

            if (lastCommit != null)
            {
                repo.Refs.Remove("refs/heads/" + BranchName);
                repo.Refs.Add("refs/heads/" + BranchName, lastCommit.Id);
            }            
        }

        private PathSpecifiers PreparePathSpecifiers(string specifiersAsString)
        {
            var specifiers = new PathSpecifiers();
            var repoFilter = new Repository(tempRocketPath);

            LoadPathSpecifiers(specifiersAsString, specifiers);
            CompileScriptedPathSpecifiers(specifiers);

            repoFilter.Ignore.ResetAllTemporaryRules();
            if (specifiers.Standard.Count > 0)
            {
                repoFilter.Ignore.AddTemporaryRules(specifiers.Standard);
                // Add the white list repo first
                specifiers.StandardAndScripted.Insert(0, new PathSpecifier(repoFilter));
            }
            return specifiers;
        }

        private void LoadPathSpecifiers(string pathSpecifiersAsText, PathSpecifiers pathSpecifiers)
        {
            if (string.IsNullOrWhiteSpace(pathSpecifiersAsText))
            {
                return;
            }

            var reader = new StringReader(pathSpecifiersAsText);

            bool isInMultiLineScript = false;
            var multiLineScript = new StringBuilder();

            string currentMultilinePath = null;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if ((!isInMultiLineScript && string.IsNullOrWhiteSpace(line)) || line.StartsWith("#"))
                {
                    continue;
                }
                if (isInMultiLineScript)
                {
                    var endOfScriptIndex = line.IndexOf("%}", StringComparison.InvariantCultureIgnoreCase);
                    if (endOfScriptIndex >= 0)
                    {
                        isInMultiLineScript = false;
                        multiLineScript.AppendLine(line.Substring(0, endOfScriptIndex));

                        pathSpecifiers.StandardAndScripted.Add(new PathSpecifier(tempRocketPath, currentMultilinePath,
                            multiLineScript.ToString(), true));
                        multiLineScript.Length = 0;
                    }
                    else
                    {
                        multiLineScript.AppendLine(line);
                    }
                }
                else
                {
                    var scriptIndex = line.IndexOf("=>", StringComparison.InvariantCultureIgnoreCase);
                    if (scriptIndex >= 0)
                    {
                        var pathSpecifier = line.Substring(0, scriptIndex).TrimEnd();
                        var textScript = line.Substring(scriptIndex + 2).TrimEnd();
                        var scriptedPathSpecifier = new PathSpecifier(tempRocketPath, pathSpecifier, textScript, false);
                        pathSpecifiers.StandardAndScripted.Add(scriptedPathSpecifier);
                    }

                    scriptIndex = line.IndexOf("{%", StringComparison.InvariantCultureIgnoreCase);
                    if (scriptIndex >= 0)
                    {
                        isInMultiLineScript = true;
                        multiLineScript.Length = 0;
                        currentMultilinePath = line.Substring(0, scriptIndex).TrimEnd();
                        var textScript = line.Substring(scriptIndex + 2).TrimEnd();
                        multiLineScript.AppendLine(textScript);
                    }
                    else
                    {
                        // If this is a normal path specifier line
                        pathSpecifiers.Standard.Add(line.TrimEnd());
                    }
                }
            }

            if (isInMultiLineScript)
            {
                throw new InvalidOperationException("TODO");
            }
        }

        private void CompileScriptedPathSpecifiers(PathSpecifiers pathSpecifiers)
        {
            // Nothing to compiled?
            if (pathSpecifiers.StandardAndScripted.Count == 0)
            {
                return;
            }

            var classText = new StringBuilder();
            classText.Append(@"// This file is automatically generated
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using GitRocketFilterBranch;
");
            // Add user custom script using directives
            if (!string.IsNullOrWhiteSpace(ScriptUsingDirectives))
            {
                classText.Append(ScriptUsingDirectives);
            }

            const string MethodPrefix = "__PathSpecifierScript";

            classText.Append(@"
namespace GitRocketFilterBranch
{
    public class CustomFilterBranch
    {
");
            // Add user custom member declarations
            if (!string.IsNullOrWhiteSpace(ScriptMemberDeclarations))
            {
                classText.Append(ScriptMemberDeclarations);
            }

            for (int i = 0; i < pathSpecifiers.StandardAndScripted.Count; i++)
            {
                var scriptedPathSpecifier = pathSpecifiers.StandardAndScripted[i];
                classText.AppendFormat(@"
        public bool {0}", MethodPrefix).Append(i).Append(@"(Repository repo, SimpleCommit commit, ref SimpleEntry entry)
        {
");
                if (scriptedPathSpecifier.IsMultiLine)
                {
                    classText.Append(scriptedPathSpecifier.ScriptText);
                }
                else
                {
                    classText.Append("return ").Append(scriptedPathSpecifier.ScriptText).AppendLine(";");
                }
                classText.Append(@"
        }
");
            }
            classText.Append(@"
    }
}
");
            var code = classText.ToString();
            var syntaxTree = CSharpSyntaxTree.ParseText(code);

            string assemblyName = Path.GetRandomFileName();
            var references = new List<MetadataReference>()
            {
                MetadataReference.CreateFromFile(typeof (object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof (Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof (File).Assembly.Location),
                MetadataReference.CreateFromFile(typeof (LibGit2Sharp.Repository).Assembly.Location),
                MetadataReference.CreateFromFile(typeof (RocketFilterBranch).Assembly.Location),
            };

            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] {syntaxTree},
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);

                if (!result.Success)
                {
                    var failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (Diagnostic diagnostic in failures)
                    {
                        Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                    }
                    throw new InvalidOperationException("TODO");
                }
                else
                {
                    ms.Position = 0;
                    var assembly = Assembly.Load(ms.ToArray());

                    var type = assembly.GetType("GitRocketFilterBranch.CustomFilterBranch");
                    var instance = Activator.CreateInstance(type);

                    foreach (var method in type.GetMethods())
                    {
                        if (method.Name.StartsWith(MethodPrefix))
                        {
                            var index = int.Parse(method.Name.Substring(MethodPrefix.Length), CultureInfo.InvariantCulture);
                            var scriptedPathSpecifier = pathSpecifiers.StandardAndScripted[index];
                            scriptedPathSpecifier.Callback = (PathSpecifierCallbackDelegate)Delegate.CreateDelegate(typeof(PathSpecifierCallbackDelegate), instance, method);
                        }
                    }
                }
            }
        }

        private void BuildWhiteList(Tree tree)
        {
            lock (pendingTasks)
            {
                var task = Task.Factory.StartNew(() =>
                {
                    foreach (var entryIt in tree)
                    {
                        var entry = entryIt;
                        if (entry.TargetType == TreeEntryTargetType.Tree)
                        {
                            var subTree = (Tree) entry.Target;
                            BuildWhiteList(subTree);
                        }
                        else
                        {
                            EvaluateEntry(entry, whiteListPathSpecifiers, true);
                        }

                    }
                });

                pendingTasks.Add(task);
            }
        }

        private void EvaluateEntry(TreeEntry entry, PathSpecifiers globalSpecifier, bool keepOnIgnore)
        {
            lock (pendingTasks)
            {
                var specifiers = globalSpecifier.StandardAndScripted;
                for (int i = 0; i < specifiers.Count; i++)
                {
                    var pathSpecifier = specifiers[i];
                    var checkTask = Task.Factory.StartNew(() =>
                    {
                        string path = entry.Path;
                        if (IsIgnored(path, pathSpecifier.Repository.Ignore, globalSpecifier.IgnoreCache))
                        {
                            if (pathSpecifier.Callback != null)
                            {
                                var simpleEntry = new SimpleEntry(entry);
                                if (!pathSpecifier.Callback(repo, currentSimpleCommit, ref simpleEntry))
                                {
                                    return;
                                }
                            }

                            lock (entriesToKeep)
                            {
                                if (keepOnIgnore)
                                {
                                    entriesToKeep.Add(entry);
                                }
                                else
                                {
                                    entriesToKeep.Remove(entry);
                                }
                            }
                        }
                    });

                    pendingTasks.Add(checkTask);
                }
            }
        }

        private static bool IsIgnored(string path, Ignore ignore, Dictionary<string, bool> ignoreCache)
        {
            bool pathIgnored;
            lock (ignoreCache)
            {
                if (!ignoreCache.TryGetValue(path, out pathIgnored))
                {
                    pathIgnored = ignore.IsPathIgnored(path);
                    ignoreCache.Add(path, pathIgnored);
                }
            }
            return pathIgnored;
        }

        private void BuildBlackList()
        {
            var entries = entriesToKeep.ToList();
            foreach (var entry in entries)
            {
                EvaluateEntry(entry, blackListPathSpecifiers, false);
            }
        }

        private void ProcessPendingTasks()
        {
            while (true)
            {
                Task[] taskToWait;
                lock (pendingTasks)
                {
                    if (pendingTasks.Count == 0)
                    {
                        break;
                    }
                    taskToWait = pendingTasks.ToArray();
                    pendingTasks.Clear();
                }
                Task.WaitAll(taskToWait);
            } 
        }

        private void ProcessRawCommit(Commit commit)
        {
            var tree = commit.Tree;
            currentSimpleCommit = new SimpleCommit(commit);

            // clear the cache of entries to keep and the tasks to run
            entriesToKeep.Clear();

            // Process white list
            BuildWhiteList(tree);
            ProcessPendingTasks();

            // Process black list
            if (blackListPathSpecifiers.StandardAndScripted.Count > 0)
            {
                BuildBlackList();
                ProcessPendingTasks();
            }

            // Rebuild a new tree based on the list of entries to keep
            var treeDef = new TreeDefinition();
            foreach (var entry in entriesToKeep)
            {
                treeDef.Add(entry.TreeEntry.Path, entry);
            }
            var newTree = repo.ObjectDatabase.CreateTree(treeDef);

            // Map parents of previous commit to new parents
            // Check if at least a parent has the same tree, if yes, we don't need to create a new commit
            Commit newCommit = null;
            var newParents = new List<Commit>();
            foreach (var parent in commit.Parents)
            {
                Commit remapParent;

                if (!remap.TryGetValue(parent.Id, out remapParent))
                {
                    throw new InvalidOperationException(string.Format("Unable to remap commit [{0}] with parent commit [{1}] to new commit.", commit.Id, parent.Id));
                }

                newParents.Add(remapParent);

                // If parent tree is equal, we can prune this commit
                if (remapParent.Tree.Id == newTree.Id)
                {
                    newCommit = remapParent;
                }
            }

            // If we need to create a new commit (new tree)
            if (newCommit == null)
            {
                newCommit = repo.ObjectDatabase.CreateCommit(commit.Author, commit.Committer, commit.Message,
                    newTree,
                    newParents, false);
            }
            // Store the remapping between the old commit and the new commit
            remap.Add(commit.Id, newCommit);

            // Store the last commit
            lastCommit = newCommit;
        }

        private class PathSpecifiers
        {
            public PathSpecifiers()
            {
                Standard = new List<string>();
                StandardAndScripted = new List<PathSpecifier>();
                IgnoreCache = new Dictionary<string, bool>();
            }

            public readonly List<string> Standard;

            public readonly List<PathSpecifier> StandardAndScripted;

            public readonly Dictionary<string, bool> IgnoreCache;
        }

        private class PathSpecifier
        {
            public PathSpecifier(Repository repoIgnore)
            {
                Repository = repoIgnore;
            }

            public PathSpecifier(string repoIgnorePath, string path, string scriptText, bool isMultiLine)
            {
                Path = path;
                ScriptText = scriptText;
                IsMultiLine = isMultiLine;
                Repository = new Repository(repoIgnorePath);
                Repository.Ignore.AddTemporaryRules(new[] { path });
            }

            public readonly string Path;

            public readonly string ScriptText;

            public readonly bool IsMultiLine;

            public PathSpecifierCallbackDelegate Callback;

            public readonly Repository Repository;
        }
    }
}