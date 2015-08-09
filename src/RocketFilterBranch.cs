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

namespace GitRocketFilterBranch
{
    public class RocketFilterBranch
    {
        private const string MethodCommitFilterName = "__CommitFilterMethod";

        private delegate bool CommitFilteringCallbackDelegate(Repository repo, SimpleCommit commit);

        private delegate bool PathPatternCallbackDelegate(Repository repo, SimpleCommit commit, ref SimpleEntry entry);

        private readonly Dictionary<Commit, SimpleCommit> simpleCommits = new Dictionary<Commit, SimpleCommit>(); 

        private readonly Dictionary<ObjectId, Commit> remap;
        private Repository repo;
        private Commit lastCommit;

        private readonly HashSet<TreeEntryWrapper> entriesToKeep = new HashSet<TreeEntryWrapper>();

        private readonly List<Task> pendingTasks = new List<Task>();

        private PathPatterns whiteListPathPatterns;
        private PathPatterns blackListPathPatterns;
        private readonly string tempRocketPath;
        private bool hasTreeFiltering;
        private bool hasTreeFilteringWithScripts;
        private bool hasCommitFiltering;
        private CommitFilteringCallbackDelegate commitFilteringCallback;

        private RevSpec revisionSpec;
        private string branchRef;

        public RocketFilterBranch()
        {
            remap = new Dictionary<ObjectId, Commit>();
            tempRocketPath = Path.Combine(Path.GetTempPath(), ".gitRocketFilterBranch");
            Repository.Init(tempRocketPath, true);
        }

        public string RepositoryPath { get; set; }

        public string ScriptUsingDirectives { get; set; }

        public string ScriptMemberDeclarations { get; set; }

        public string WhiteListPathPatterns { get; set; }

        public string BlackListPathPatterns { get; set; }

        public string BranchName { get; set; }

        public bool BranchOverwrite { get; set; }

        public string CommitFilter { get; set; }

        public bool Verbose { get; set; }

        public string RevisionRange { get; set; }

        public void Run()
        {
            // Validate paramters
            ValidateParameters();

            // Prepare filterings
            PrepareFiltering();

            // Compile any scripts (from commit or tree filtering)
            CompileScripts();

            // Process all commits
            ProcessCommits();

            // Output the branch refs
            WriteBranchRefs();
        }

        private void ValidateParameters()
        {
            if (!Repository.IsValid(RepositoryPath))
            {
                throw new RocketException("No valid git repository path found at [{0}]", RepositoryPath);
            }
            repo = new Repository(RepositoryPath);

            if (string.IsNullOrWhiteSpace(BranchName))
            {
                throw new RocketException("Branch name is required and cannot be null");
            }

            branchRef = "refs/heads/" + BranchName;
            if (repo.Refs[branchRef] != null && !BranchOverwrite)
            {
                throw new RocketException("The branch [{0}] already exist. Cannot overwrite without force option");
            }

            // Validate the revision range
            if (!string.IsNullOrWhiteSpace(RevisionRange))
            {
                string errorMessage = null;
                try
                {
                    revisionSpec = RevSpec.Parse(repo, RevisionRange);

                    if (revisionSpec.Type != RevSpecType.Range)
                    {
                        errorMessage = "Expecting a range spec from..to";
                    }
                }
                catch (LibGit2SharpException libGitException)
                {
                    errorMessage = libGitException.Message;
                }

                if (errorMessage != null)
                {
                    throw new RocketException("Invalid revspec [{0}]. Reason: {1}", RevisionRange, errorMessage);
                }
            }
        }

        private void PrepareFiltering()
        {
            // Prepare tree filtering
            whiteListPathPatterns = ParseTreeFilteringPathPatterns(WhiteListPathPatterns);
            blackListPathPatterns = ParseTreeFilteringPathPatterns(BlackListPathPatterns);
            hasTreeFiltering = whiteListPathPatterns.Count > 0 ||
                               blackListPathPatterns.Count > 0;

            hasTreeFilteringWithScripts = whiteListPathPatterns.Any(pattern => !string.IsNullOrWhiteSpace(pattern.ScriptText));
            hasTreeFilteringWithScripts = hasTreeFilteringWithScripts ||
                                          blackListPathPatterns.Any(pattern => !string.IsNullOrWhiteSpace(pattern.ScriptText));

            hasCommitFiltering = !string.IsNullOrWhiteSpace(CommitFilter);

            // If nothing to do, we are missing a parameter (either commit or tree filtering)
            if (!hasCommitFiltering && !hasTreeFiltering)
            {
                throw new RocketException("Expecting at least a commit or tree filtering option");
            }
        }

        private void ProcessCommits()
        {
            // Gets all commits in topological reverse order
            var commits =
                repo.Commits.QueryBy(new CommitFilter()
                {
                    FirstParentOnly = false,
                    SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse
                }).ToList();

            // TODO: filter revspec

            // Process commits
            for (int i = 0; i < commits.Count; i++)
            {
                var commit = GetSimpleCommit(commits[i]);
                // TODO: LOG
                // Console.Write("[{0}/{1}] Processing commit {2}\r", i, commits.Count - 1, commit.Id);
                ProcessCommit(commit);
            }
        }

        private void WriteBranchRefs()
        {
            if ((repo.Refs[branchRef] == null || BranchOverwrite) && lastCommit != null)
            {
                if (BranchOverwrite)
                {
                    repo.Refs.Remove(branchRef);
                }

                repo.Refs.Add(branchRef, lastCommit.Id);
            }
        }

        internal SimpleCommit GetSimpleCommit(Commit commit)
        {
            SimpleCommit simpleCommit;
            lock (simpleCommits)
            {
                if (!simpleCommits.TryGetValue(commit, out simpleCommit))
                {
                    simpleCommit = new SimpleCommit(this, commit);
                }
            }
            return simpleCommit;
        }

        private PathPatterns ParseTreeFilteringPathPatterns(string pathPatternsAsText)
        {
            var pathPatterns = new PathPatterns();

            if (string.IsNullOrWhiteSpace(pathPatternsAsText))
            {
                return pathPatterns;
            }

            var reader = new StringReader(pathPatternsAsText);

            // non scripted patterns
            var pathPatternsNoScript = new List<string>();

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

                        pathPatterns.Add(new PathPattern(tempRocketPath, currentMultilinePath,
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
                        var pathPatternText = line.Substring(0, scriptIndex).TrimEnd();
                        var textScript = line.Substring(scriptIndex + 2).TrimEnd();
                        var pathPattern = new PathPattern(tempRocketPath, pathPatternText, textScript, false);
                        pathPatterns.Add(pathPattern);
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
                        // If this is a normal path pattern line
                        pathPatternsNoScript.Add(line.TrimEnd());
                    }
                }
            }

            if (isInMultiLineScript)
            {
                throw new RocketException("Expecting the end %} of multiline script: {0}", multiLineScript);
            }

            if (pathPatternsNoScript.Count > 0)
            {
                var repoFilter = new Repository(tempRocketPath);
                repoFilter.Ignore.ResetAllTemporaryRules();
                repoFilter.Ignore.AddTemporaryRules(pathPatternsNoScript);
                // Add the white list repo at the end to let the scripted rules to run first
                pathPatterns.Add(new PathPattern(repoFilter));
            }

            return pathPatterns;
        }

        private void CompileScripts()
        {
            // Nothing to compiled?
            if (hasTreeFilteringWithScripts || hasCommitFiltering)
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
");
            // Add user custom script using directives
            if (!string.IsNullOrWhiteSpace(ScriptUsingDirectives))
            {
                classText.Append(ScriptUsingDirectives);
            }

            classText.Append(@"
namespace GitRocketFilterBranch
{
    public class RocketScript
    {
");
            // Add user custom member declarations
            if (!string.IsNullOrWhiteSpace(ScriptMemberDeclarations))
            {
                classText.Append(ScriptMemberDeclarations);
            }

            // Append commit filtering method
            AppendCommitFilterMethod(CommitFilter, classText);

            // Append any tree filtering methods
            var treeFilterMethods = new Dictionary<string, PathPattern>();
            var allPathPatterns = whiteListPathPatterns.Concat(blackListPathPatterns).ToList();
            AppendTreeFilterMethods(allPathPatterns, classText, treeFilterMethods);

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
                MetadataReference.CreateFromFile(typeof (Repository).Assembly.Location),
                MetadataReference.CreateFromFile(typeof (RocketFilterBranch).Assembly.Location),
            };

            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] {syntaxTree},
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var stream = new MemoryStream())
            {
                var result = compilation.Emit(stream);

                if (result.Success)
                {
                    stream.Position = 0;
                    var assembly = Assembly.Load(stream.ToArray());

                    var type = assembly.GetType("GitRocketFilterBranch.RocketScript");
                    var instance = Activator.CreateInstance(type);

                    // Rebing methods
                    foreach (var method in type.GetMethods())
                    {
                        PathPattern pathPattern;
                        if (treeFilterMethods.TryGetValue(method.Name, out pathPattern))
                        {
                            pathPattern.Callback = (PathPatternCallbackDelegate)Delegate.CreateDelegate(typeof(PathPatternCallbackDelegate), instance, method);
                        }
                        else if (method.Name == MethodCommitFilterName)
                        {
                            commitFilteringCallback = (CommitFilteringCallbackDelegate)Delegate.CreateDelegate(typeof(CommitFilteringCallbackDelegate), instance, method);
                        }
                    }
                }
                else
                {
                    var failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    throw new RocketException(BuildCodeErrors(code, failures));
                }
            }
        }

        private string BuildCodeErrors(string code, IEnumerable<Diagnostic> failures)
        {
            var errorMessage = new StringBuilder(code.Length + 1024);

            var codeReader = new StringReader(code);
            string line;
            for(int i = 0; (line = codeReader.ReadLine()) != null; i++)
            {
                errorMessage.AppendFormat(CultureInfo.InvariantCulture, "{0}: {1}", i, line);
            }

            errorMessage.AppendLine("Error while compiling the scripts above:");

            foreach (var failure in failures)
            {
                errorMessage.AppendFormat("\t {0}: {1}\n", failure.Id, failure.GetMessage());
            }
            return errorMessage.ToString();
        }

        private static void AppendCommitFilterMethod(string commitFilter, StringBuilder classText)
        {
            if (commitFilter == null)
            {
                return;
            }

            classText.AppendFormat(@"
        public bool {0}", MethodCommitFilterName)
                .Append(@"(Repository repo, SimpleCommit commit)
        {
");
            classText.Append(GetSafeScriptBody(commitFilter, false));
            classText.Append(@"
        }
");
        }

        private static void AppendTreeFilterMethods(IEnumerable<PathPattern> pathPatterns, StringBuilder classText, Dictionary<string, PathPattern> methodNames)
        {
            const string methodTreeFilterPrefix = "__TreeFilterMethod";

            foreach (var pathPattern in pathPatterns)
            {
                var methodName = string.Format(CultureInfo.InvariantCulture, "{0}{1}", methodTreeFilterPrefix, methodNames.Count);
                methodNames.Add(methodName, pathPattern);

                classText.AppendFormat(@"
        public bool {0}", methodName)
                    .Append(@"(Repository repo, SimpleCommit commit, ref SimpleEntry entry)
        {
");
                classText.Append(GetSafeScriptBody(pathPattern.ScriptText, pathPattern.IsMultiLine));
                classText.Append(@"
        }
");
            }
        }

        private static string GetSafeScriptBody(string scriptText, bool isMultiline)
        {
            if (!isMultiline)
            {
                var scriptTextTrim = scriptText.Trim();

                // TODO: Would be more reliable if we were able to determine if this is a script enclosed by { } or a simple condition. Should check this with roslyn
                if (!scriptTextTrim.StartsWith("{"))
                {
                    return "return " + scriptText + ";\n";
                }
            }
            return scriptText;
        }

        private void BuildWhiteList(SimpleCommit commit, Tree tree)
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
                            BuildWhiteList(commit, subTree);
                        }
                        else
                        {
                            EvaluateEntry(commit, entry, whiteListPathPatterns, true);
                        }

                    }
                });

                pendingTasks.Add(task);
            }
        }

        private void EvaluateEntry(SimpleCommit commit, TreeEntry entry, PathPatterns globalPattern, bool keepOnIgnore)
        {
            lock (pendingTasks)
            {
                var checkTask = Task.Factory.StartNew(() =>
                {
                    var path = entry.Path;
                    var match = Match(path, globalPattern);

                    // If path is ignored we can update the entries to keep
                    if (match.IsIgnored)
                    {
                        // If callback return false, then we don't update entries to keep or delete
                        var callback = match.Pattern.Callback;
                        if (callback != null)
                        {
                            var simpleEntry = new SimpleEntry(entry);
                            if (!callback(repo, commit, ref simpleEntry))
                            {
                                return;
                            }
                        }

                        // We can update entries to keep
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

        private static PathMatch Match(string path, PathPatterns pathPatterns)
        {
            PathMatch match;
            var ignoreCache = pathPatterns.IgnoreCache;
            bool matchFound;

            // Try first to get a previously match from the cache
            lock (ignoreCache)
            {
                matchFound = ignoreCache.TryGetValue(path, out match);
            }

            // Otherwise, loop through all patterns to find a match
            if (!matchFound)
            {
                foreach (var pathPattern in pathPatterns)
                {
                    if (pathPattern.Ignore.IsPathIgnored(path))
                    {
                        match = new PathMatch(true, pathPattern);
                        break;
                    }
                }
                lock (ignoreCache)
                {
                    ignoreCache.Add(path, match);
                }
            }

            return match;
        }

        private void BuildBlackList(SimpleCommit commit)
        {
            var entries = entriesToKeep.ToList();
            foreach (var entry in entries)
            {
                EvaluateEntry(commit, entry, blackListPathPatterns, false);
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

        private void ProcessCommit(SimpleCommit commit)
        {
            bool discardCommit = false;
            if (commitFilteringCallback != null)
            {
                discardCommit = !commitFilteringCallback(repo, commit);
            }

            // Map parents of previous commit to new parents
            // Check if at least a parent has the same tree, if yes, we don't need to create a new commit
            Commit newCommit = null;
            Tree newTree = null;

            // clear the cache of entries to keep and the tasks to run
            entriesToKeep.Clear();

            if (!discardCommit)
            {
                if (hasTreeFiltering)
                {
                    var tree = commit.Tree;

                    // Process white list
                    BuildWhiteList(commit, tree);
                    ProcessPendingTasks();

                    // Process black list
                    if (blackListPathPatterns.Count > 0)
                    {
                        BuildBlackList(commit);
                        ProcessPendingTasks();
                    }

                    // Rebuild a new tree based on the list of entries to keep
                    var treeDef = new TreeDefinition();
                    foreach (var entry in entriesToKeep)
                    {
                        treeDef.Add(entry.TreeEntry.Path, entry);
                    }
                    newTree = repo.ObjectDatabase.CreateTree(treeDef);
                }

                var newParents = new List<Commit>();
                foreach (var parent in commit.Parents)
                {
                    Commit remapParent;

                    if (!remap.TryGetValue(parent.Id, out remapParent))
                    {
                        throw new RocketException(
                            "Unexpected error while processing parent commits. Unable to remap commit [{0}] with parent commit [{1}] to new commit.",
                            commit.Id, parent.Id);
                    }

                    newParents.Add(remapParent);

                    // If parent tree is equal, we can prune this commit
                    if (newTree != null && remapParent.Tree.Id == newTree.Id)
                    {
                        newCommit = remapParent;
                        newTree = null;
                    }
                }

                TODO THIS CODE IS NOT WORKING ANYMORE

                // If we need to create a new commit (new tree)
                if (newCommit == null)
                {
                    var author = new Signature(commit.AuthorName, commit.AuthorEmail, commit.AuthorDate);
                    var committer = new Signature(commit.CommitterName, commit.CommitterEmail, commit.CommitterDate);

                    newCommit = repo.ObjectDatabase.CreateCommit(author, committer, commit.Message,
                        newTree,
                        newParents, false);
                }
                // Store the remapping between the old commit and the new commit
                remap.Add(commit.Id, newCommit);
            }
            else
            {
                newCommit = lastCommit;
            }

            // Store the last commit
            lastCommit = newCommit;
        }

        private class PathPatterns : List<PathPattern>
        {
            public PathPatterns()
            {
                IgnoreCache = new Dictionary<string, PathMatch>();
            }

            public readonly Dictionary<string, PathMatch> IgnoreCache;
        }

        private class PathPattern
        {
            public PathPattern(Repository repoIgnore)
            {
                if (repoIgnore == null) throw new ArgumentNullException("repoIgnore");
                Repository = repoIgnore;
                Ignore = Repository.Ignore;
            }

            public PathPattern(string repoIgnorePath, string path, string scriptText, bool isMultiLine)
            {
                if (repoIgnorePath == null) throw new ArgumentNullException("repoIgnorePath");
                if (path == null) throw new ArgumentNullException("path");
                Path = path;
                ScriptText = scriptText;
                IsMultiLine = isMultiLine;
                Repository = new Repository(repoIgnorePath);
                Repository.Ignore.AddTemporaryRules(new[] { path });
                Ignore = Repository.Ignore;
            }

            public readonly string Path;

            public readonly string ScriptText;

            public readonly bool IsMultiLine;

            public PathPatternCallbackDelegate Callback;

            private readonly Repository Repository;

            public readonly Ignore Ignore;
        }

        struct PathMatch
        {
            public PathMatch(bool isIgnored, PathPattern pattern)
            {
                IsIgnored = isIgnored;
                Pattern = pattern;
            }

            public readonly bool IsIgnored;

            public readonly PathPattern Pattern;
        }
    }
}