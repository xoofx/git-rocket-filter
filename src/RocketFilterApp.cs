using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GitRocketFilter
{
    public class RocketFilterApp
    {
        private const string MethodCommitFilterName = "CommitFilterMethod";

        private delegate void CommitFilteringCallbackDelegate(Repository repo, SimpleCommit commit);

        private delegate void PathPatternCallbackDelegate(Repository repo, string pattern, SimpleCommit commit, ref SimpleEntry entry);

        private readonly Dictionary<Commit, SimpleCommit> simpleCommits = new Dictionary<Commit, SimpleCommit>(); 

        private readonly Dictionary<ObjectId, Commit> commitMap;
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
        private Stopwatch clock;

        private readonly HashSet<Commit> commitsDiscarded = new HashSet<Commit>();

        public RocketFilterApp()
        {
            commitMap = new Dictionary<ObjectId, Commit>();
            tempRocketPath = Path.Combine(Path.GetTempPath(), ".gitRocketFilter");
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

        public bool DetachFirstCommits { get; set; }

        public void Run()
        {
            clock = Stopwatch.StartNew();

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

        internal SimpleCommit GetMapCommit(SimpleCommit commit)
        {
            Commit rewritten;
            if (commitMap.TryGetValue(commit.Id, out rewritten))
            {
                return GetSimpleCommit(rewritten);
            }
            return null;
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

                    if (revisionSpec.Type == RevSpecType.MergeBase)
                    {
                        errorMessage = "Merge base revspec are not supported";
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
            whiteListPathPatterns = ParseTreeFilteringPathPatterns(WhiteListPathPatterns, "--keep");
            blackListPathPatterns = ParseTreeFilteringPathPatterns(BlackListPathPatterns, "--delete");
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
            var commitFilter = new CommitFilter()
            {
                FirstParentOnly = false,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse
            };

            if (RevisionRange != null)
            {
                var revSpec = RevSpec.Parse(repo, RevisionRange);
                if (revSpec.Type == RevSpecType.Single)
                {
                    commitFilter.Since = revSpec.From.Id;
                }
                else if (revSpec.Type == RevSpecType.Range)
                {
                    commitFilter.Range = RevisionRange;
                }
            }

            // Gets all commits in topological reverse order
            var commits = repo.Commits.QueryBy(commitFilter).ToList();

            // Process commits
            for (int i = 0; i < commits.Count; i++)
            {
                var commit = GetSimpleCommit(commits[i]);

                Console.Write("Rewrite {0} ({1}/{2}){3}", commit.Id, i + 1, commits.Count, (i+1) == commits.Count ? string.Empty : "\r");
                ProcessCommit(commit);
            }
            if (commits.Count == 0)
            {
                Console.WriteLine("Nothing to rewrite.");
            }
            else
            {
                Console.WriteLine(" in {0:#.###}s", clock.Elapsed.TotalSeconds);
            }
        }

        private void WriteBranchRefs()
        {
            var originalRef = repo.Refs[branchRef];
            if ((originalRef == null || BranchOverwrite) && lastCommit != null)
            {
                if (BranchOverwrite)
                {
                    repo.Refs.Remove(branchRef);
                }

                repo.Refs.Add(branchRef, lastCommit.Id);
                Console.WriteLine("Ref '{0}' was {1}", branchRef, originalRef != null && BranchOverwrite ? "overwritten" : "created");
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

        private PathPatterns ParseTreeFilteringPathPatterns(string pathPatternsAsText, string context)
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

                if (Verbose)
                {
                    foreach (var pattern in pathPatternsNoScript)
                    {
                        Console.WriteLine("Found {0} pattern [{1}]", context, pattern);
                    }
                }

                repoFilter.Ignore.AddTemporaryRules(pathPatternsNoScript);
                // Add the white list repo at the end to let the scripted rules to run first
                pathPatterns.Add(new PathPattern(repoFilter));
            }

            return pathPatterns;
        }

        private void CompileScripts()
        {
            // Nothing to compiled?
            if (!hasTreeFilteringWithScripts && !hasCommitFiltering)
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

            classText.AppendFormat(@"
namespace {0}", typeof(RocketFilterApp).Namespace).Append(@"
{
    public class RocketScript : RocketScriptBase
    {
        public RocketScript(RocketFilterApp app) : base(app)
        {
        }
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
                MetadataReference.CreateFromFile(typeof (RocketFilterApp).Assembly.Location),
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

                    var type = assembly.GetType(typeof(RocketFilterApp).Namespace + ".RocketScript");
                    var instance = Activator.CreateInstance(type, this);

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

                    throw new RocketException(BuildCodeErrors(failures)) { AdditionalText =  DumpPrettyCode(code)};
                }
            }
        }

        private string DumpPrettyCode(string code)
        {
            var prettyCode = new StringBuilder(code.Length + 1024);
            var codeReader = new StringReader(code);
            string line;
            for (int i = 0; (line = codeReader.ReadLine()) != null; i++)
            {
                prettyCode.AppendFormat(CultureInfo.InvariantCulture, "{0,4}: {1}\n", i, line);
            }
            return prettyCode.ToString();
        }

        private string BuildCodeErrors(IEnumerable<Diagnostic> failures)
        {
            var errorMessage = new StringBuilder();
            errorMessage.AppendLine();
            errorMessage.AppendLine("Error while compiling the script:");
            errorMessage.AppendLine();
            foreach (var failure in failures)
            {
                var lineSpan = failure.Location.GetLineSpan();
                errorMessage.AppendFormat("  ({0}): {1} {2}: {3}\n", lineSpan.StartLinePosition, failure.Severity, failure.Id, failure.GetMessage());
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
        // commit-filtering
        public void {0}", MethodCommitFilterName)
                .Append(@"(Repository repo, SimpleCommit commit)
        {
");
            classText.Append(GetSafeScriptBody(commitFilter));
            classText.Append(@"
        }
");
        }

        private void AppendTreeFilterMethods(IEnumerable<PathPattern> pathPatterns, StringBuilder classText, Dictionary<string, PathPattern> methodNames)
        {
            if (!hasTreeFilteringWithScripts)
            {
                return;
            }

            const string methodTreeFilterPrefix = "TreeFilterMethod";

            foreach (var pathPattern in pathPatterns)
            {
                // Skip non script text
                if (pathPattern.ScriptText == null)
                {
                    continue;
                }

                var methodName = string.Format(CultureInfo.InvariantCulture, "{0}{1}", methodTreeFilterPrefix, methodNames.Count);
                methodNames.Add(methodName, pathPattern);

                classText.AppendFormat(@"
        // tree-filtering: {0}", pathPattern.Path).AppendFormat(@"
        public void {0}", methodName)
                    .Append(@"(Repository repo, string pattern, SimpleCommit commit, ref SimpleEntry entry)
        {
");
                classText.Append(GetSafeScriptBody(pathPattern.ScriptText));
                classText.Append(@"
        }
");
            }
        }

        private static string GetSafeScriptBody(string scriptText)
        {
            return "{\n" + scriptText + "\n}\n";
        }

        private void BuildWhiteList(SimpleCommit commit, Tree tree)
        {
            // Early exit if the commit was discarded by a tree-filtering
            if (commit.Discard)
            {
                return;
            }

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
            // Early exit if the commit was discarded by a tree-filtering
            if (commit.Discard)
            {
                return;
            }

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
                        var pattern = match.Pattern;
                        var callback = pattern.Callback;
                        if (callback != null)
                        {
                            var simpleEntry = new SimpleEntry(entry);

                            // Calls the script
                            callback(repo, pattern.Path, commit, ref simpleEntry);

                            // Skip if this entry was discarded
                            if (simpleEntry.Discard || commit.Discard)
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
            // ------------------------------------
            // commit-filtering
            // ------------------------------------
            if (commitFilteringCallback != null)
            {
                // Filter this commit
                commitFilteringCallback(repo, commit);

                if (commit.Discard)
                {
                    // Store that this commit was discarded (used for reparenting commits)
                    commitsDiscarded.Add(commit.GitCommit);
                    return;
                }
            }

            // Map parents of previous commit to new parents
            // Check if at least a parent has the same tree, if yes, we don't need to create a new commit
            Commit newCommit = null;
            Tree newTree;

            // ------------------------------------
            // tree-filtering
            // ------------------------------------
            if (hasTreeFiltering)
            {
                // clear the cache of entries to keep and the tasks to run
                entriesToKeep.Clear();

                // Process white list
                BuildWhiteList(commit, commit.Tree);
                ProcessPendingTasks();

                // Process black list
                if (blackListPathPatterns.Count > 0)
                {
                    BuildBlackList(commit);
                    ProcessPendingTasks();
                }

                // If the commit was discarded by a tree-filtering, we need to skip it also here
                if (commit.Discard)
                {
                    // Store that this commit was discarded (used for reparenting commits)
                    commitsDiscarded.Add(commit.GitCommit);
                    return;
                }

                // Rebuild a new tree based on the list of entries to keep
                var treeDef = new TreeDefinition();
                foreach (var entry in entriesToKeep)
                {
                    treeDef.Add(entry.TreeEntry.Path, entry);
                }
                newTree = repo.ObjectDatabase.CreateTree(treeDef);
            }
            else
            {
                // If we don't have any tree filtering, just use the original tree
                newTree = commit.Tree;
            }

            var newParents = new List<Commit>();
            bool hasOriginalParents = false;
            bool treePruned = false;
            foreach (var parent in commit.Parents)
            {
                // Find a non discarded parent
                var remapParent = FindRewrittenParent(parent.GitCommit);

                // If parent is same, then it is an original parent that can be detached by DetachFirstCommits
                hasOriginalParents = parent.GitCommit == remapParent;

                newParents.Add(remapParent);

                // If parent tree is equal, we can prune this commit
                if (!treePruned && remapParent.Tree.Id == newTree.Id)
                {
                    newCommit = remapParent;
                    commitsDiscarded.Add(commit.GitCommit);
                    treePruned = true;
                }
            }

            // If we detach first commits from their parents
            if (DetachFirstCommits && hasOriginalParents)
            {
                // Remove original parents
                foreach (var parent in commit.Parents)
                {
                    newParents.Remove(parent.GitCommit);
                }
            }

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
            commitMap.Add(commit.Id, newCommit);

            // Store the last commit
            lastCommit = newCommit;
        }

        private Commit FindRewrittenParent(Commit commit)
        {
            Commit newCommit;
            if (!commitMap.TryGetValue(commit.Id, out newCommit))
            {
                newCommit = commit;

                if (commitsDiscarded.Contains(newCommit))
                {
                    // If parent commit was discarded, we need to find an available parent of this commit
                    foreach (var parent in newCommit.Parents)
                    {
                        var newParent = FindRewrittenParent(parent);
                        if (newParent != null)
                        {
                            return newParent;
                        }
                    }
                }
            }

            return newCommit;
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