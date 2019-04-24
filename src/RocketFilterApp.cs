// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GitRocketFilter
{
    /// <summary>
    /// Main class for git rocket filter.
    /// </summary>
    public class RocketFilterApp : IDisposable
    {
        private const string MethodCommitFilterName = "CommitFilterMethod";

        private delegate void CommitFilteringCallbackDelegate(Repository repo, SimpleCommit commit);

        private delegate void PathPatternCallbackDelegate(Repository repo, string pattern, SimpleCommit commit, ref SimpleEntry entry);

        private readonly Dictionary<Commit, SimpleCommit> simpleCommits = new Dictionary<Commit, SimpleCommit>(); 

        private readonly Dictionary<string, Commit> commitMap;
        private Repository repo;
        private Commit lastCommit;

        private readonly Dictionary<TreeEntry, SimpleEntry.EntryValue> entriesToKeep = new Dictionary<TreeEntry, SimpleEntry.EntryValue>(ObjectReferenceEqualityComparer<TreeEntry>.Default);

        private readonly List<Task> pendingTasks = new List<Task>();

        private PathPatterns keepPathPatterns;
        private PathPatterns removePathPatterns;
        private readonly string tempRocketPath;
        private bool hasTreeFiltering;
        private bool hasTreeFilteringWithScripts;
        private bool hasCommitFiltering;
        private CommitFilteringCallbackDelegate commitFilteringCallback;

        private RevSpec revisionSpec;
        private string branchRef;
        private Stopwatch clock;

        private readonly HashSet<string> commitsDiscarded = new HashSet<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RocketFilterApp"/> class.
        /// </summary>
        public RocketFilterApp()
        {
            commitMap = new Dictionary<string, Commit>();
            tempRocketPath = Path.Combine(Path.GetTempPath(), ".gitRocketFilter");
            Repository.Init(tempRocketPath, true);
        }

        /// <summary>
        /// Gets or sets the repository path.
        /// </summary>
        /// <value>The repository path.</value>
        public string RepositoryPath { get; set; }

        /// <summary>
        /// Gets or sets the keep patterns.
        /// </summary>
        /// <value>The keep patterns.</value>
        public string KeepPatterns { get; set; }

        /// <summary>
        /// Gets or sets the remove patterns.
        /// </summary>
        /// <value>The remove patterns.</value>
        public string RemovePatterns { get; set; }

        /// <summary>
        /// Gets or sets the name of the branch.
        /// </summary>
        /// <value>The name of the branch.</value>
        public string BranchName { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether if the branch already exist, it can be overridden.
        /// </summary>
        /// <value><c>true</c> if the branch already exist, it can be overridden; otherwise, <c>false</c>.</value>
        public bool BranchOverwrite { get; set; }

        /// <summary>
        /// Gets or sets the commit filter code.
        /// </summary>
        /// <value>The commit filter code.</value>
        public string CommitFilter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="RocketFilterApp"/> is outputting verbose log.
        /// </summary>
        /// <value><c>true</c> if is outputting verbose log; otherwise, <c>false</c>.</value>
        public bool Verbose { get; set; }

        /// <summary>
        /// Gets or sets the revision range to work on. (Default is from first commit to HEAD).
        /// </summary>
        /// <value>The revision range.</value>
        public string RevisionRange { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to detach first commits from their parents.
        /// </summary>
        /// <value><c>true</c> if to detach first commits from their parents; otherwise, <c>false</c>.</value>
        public bool DetachFirstCommits { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include submodule git link in tree-filtering.
        /// </summary>
        /// <value><c>true</c> to include submodule git link in tree-filtering; otherwise, <c>false</c>.</value>
        public bool IncludeLinks { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to disable multi-threaded tasks.
        /// </summary>
        /// <value><c>true</c> to disable multi-threaded tasks, <c>false</c>.</value>
        public bool DisableTasks { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to preserve empty merge commits.
        /// </summary>
        /// <value><c>true</c> to preserve empty merge commits; otherwise, <c>false</c>.</value>
        public bool PreserveMergeCommits { get; set; }

        /// <summary>
        /// Gets or sets the output writer.
        /// </summary>
        /// <value>The output writer.</value>
        public TextWriter OutputWriter { get; set; }

        /// <summary>
        /// Runs the filtering.
        /// </summary>
        public void Run()
        {
            clock = Stopwatch.StartNew();

            if (OutputWriter == null)
            {
                OutputWriter = Console.Out;
            }

            // Validate parameters
            ValidateParameters();

            // Prepare filtering
            PrepareFiltering();

            // Compile any scripts (from commit or tree filtering)
            CompileScripts();

            // Process all commits
            ProcessCommits();

            // Output the branch refs
            WriteBranchRefs();
        }

        /// <summary>
        /// This method Validates the parameters.
        /// </summary>
        /// <exception cref="GitRocketFilter.RocketException">
        /// No valid git repository path found at [{0}]
        /// or
        /// Branch name is required and cannot be null
        /// or
        /// The branch [{0}] already exist. Cannot overwrite without force option
        /// or
        /// Invalid revspec [{0}]. Reason: {1}
        /// </exception>
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
                throw new RocketException("The branch [{0}] already exist. Cannot overwrite without force option", BranchName);
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

        /// <summary>
        /// Prepares the filtering by processing keep and remove entries.
        /// </summary>
        /// <exception cref="GitRocketFilter.RocketException">Expecting at least a commit or tree filtering option</exception>
        private void PrepareFiltering()
        {
            // Prepare tree filtering
            keepPathPatterns = ParseTreeFilteringPathPatterns(KeepPatterns, "--keep");
            removePathPatterns = ParseTreeFilteringPathPatterns(RemovePatterns, "--remove");
            hasTreeFiltering = keepPathPatterns.Count > 0 ||
                               removePathPatterns.Count > 0;

            hasTreeFilteringWithScripts = keepPathPatterns.Any(pattern => !string.IsNullOrWhiteSpace(pattern.ScriptText));
            hasTreeFilteringWithScripts = hasTreeFilteringWithScripts ||
                                          removePathPatterns.Any(pattern => !string.IsNullOrWhiteSpace(pattern.ScriptText));

            hasCommitFiltering = !string.IsNullOrWhiteSpace(CommitFilter);

            // If nothing to do, we are missing a parameter (either commit or tree filtering)
            if (!hasCommitFiltering && !hasTreeFiltering)
            {
                throw new RocketException("Expecting at least a commit or tree filtering option");
            }
        }

        /// <summary>
        /// Processes all commits.
        /// </summary>
        private void ProcessCommits()
        {
            // We are working only in a topological-reverse order (from parent commits to child)
            var commitFilter = new CommitFilter()
            {
                FirstParentOnly = false,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse
            };

            // If a revision range is specified, try to use it
            if (RevisionRange != null)
            {
                var revSpec = RevSpec.Parse(repo, RevisionRange);
                if (revSpec.Type == RevSpecType.Single)
                {
                    commitFilter.IncludeReachableFrom = revSpec.From.Id;
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

                OutputWriter.Write("Rewrite {0} ({1}/{2}){3}", commit.Id, i + 1, commits.Count, (i+1) == commits.Count ? string.Empty : "\r");
                ProcessCommit(commit);
            }
            if (commits.Count == 0)
            {
                OutputWriter.WriteLine("Nothing to rewrite.");
            }
            else
            {
                OutputWriter.WriteLine(" in {0:#.###}s", clock.Elapsed.TotalSeconds);
            }
        }

        /// <summary>
        /// Writes the final branch refs.
        /// </summary>
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
                OutputWriter.WriteLine("Ref '{0}' was {1}", branchRef, originalRef != null && BranchOverwrite ? "overwritten" : "created");
            }
        }

        /// <summary>
        /// Processes a commit.
        /// </summary>
        /// <param name="commit">The commit.</param>
        private void ProcessCommit(SimpleCommit commit)
        {
            // ------------------------------------
            // commit-filtering
            // ------------------------------------
            if (PerformCommitFiltering(commit))
                return;

            // Map parents of previous commit to new parents
            // Check if at least a parent has the same tree, if yes, we don't need to create a new commit
            Commit newCommit = null;
            Tree newTree;

            // ------------------------------------
            // tree-filtering
            // ------------------------------------
            if (PerformTreeFiltering(commit, out newTree))
                return;

            // Process parents
            var newParents = new List<Commit>();
            bool hasOriginalParents = false;
            Commit pruneCommitParentCandidate = null;
            foreach (var parent in commit.Parents)
            {
                // Find a non discarded parent
                var remapParent = FindRewrittenParent(parent);

                // If remap parent is null, we can skip it
                if (remapParent == null)
                {
                    continue;
                }

                // If parent is same, then it is an original parent that can be detached by DetachFirstCommits
                hasOriginalParents = parent.GitCommit == remapParent;

                newParents.Add(remapParent);

                // If parent tree is equal, we might be able to prune this commit
                if (pruneCommitParentCandidate == null && remapParent.Tree.Id == newTree.Id)
                {
                    pruneCommitParentCandidate = remapParent;
                }
            }

            if (pruneCommitParentCandidate != null && !(PreserveMergeCommits && newParents.Count == 2))
            {
               newCommit = pruneCommitParentCandidate;
               commitsDiscarded.Add(commit.Sha);
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
            commitMap.Add(commit.Sha, newCommit);

            // Store the last commit
            lastCommit = newCommit;
        }

        private bool PerformTreeFiltering(SimpleCommit commit, out Tree newTree)
        {
            newTree = null;
            if (hasTreeFiltering)
            {
                // clear the cache of entries to keep and the tasks to run
                entriesToKeep.Clear();

                // Process white list
                if (keepPathPatterns.Count == 0)
                {
                    KeepAllEntries(commit.Tree);
                }
                else
                {
                    KeepEntries(commit, commit.Tree);
                }
                ProcessPendingTasks();

                // Process black list
                if (removePathPatterns.Count > 0)
                {
                    RemoveEntries(commit);
                    ProcessPendingTasks();
                }

                // If the commit was discarded by a tree-filtering, we need to skip it also here
                if (commit.Discard || entriesToKeep.Count == 0)
                {
                    commit.Discard = true;

                    // Store that this commit was discarded (used for re-parenting commits)
                    commitsDiscarded.Add(commit.Sha);
                    return true;
                }

                // Rebuild a new tree based on the list of entries to keep
                var treeDef = new TreeDefinition();
                foreach (var entryIt in entriesToKeep)
                {
                    var entry = entryIt.Key;
                    var entryValue = entryIt.Value;
                    if (entryValue.Blob != null)
                    {
                        treeDef.Add(entry.Path, entryValue.Blob, entryValue.Mode);

                    }
                    else
                    {
                        treeDef.Add(entry.Path, entry);
                    }
                }
                newTree = repo.ObjectDatabase.CreateTree(treeDef);
            }
            else
            {
                // If we don't have any tree filtering, just use the original tree
                newTree = commit.Tree;
            }
            return false;
        }

        private bool PerformCommitFiltering(SimpleCommit commit)
        {
            if (commitFilteringCallback != null)
            {
                // Filter this commit
                commitFilteringCallback(repo, commit);

                if (commit.Discard)
                {
                    // Store that this commit was discarded (used for reparenting commits)
                    commitsDiscarded.Add(commit.Sha);
                    return true;
                }
            }
            return false;
        }

        private Commit FindRewrittenParent(Commit commit)
        {
            Commit newCommit;
            if (!commitMap.TryGetValue(commit.Sha, out newCommit))
            {
                if (commitsDiscarded.Contains(commit.Sha))
                {
                    foreach (var parent in commit.Parents)
                    {
                        var newParent = FindRewrittenParent(parent);
                        if (newParent != null)
                        {
                            newCommit = newParent;
                            break;
                        }
                    }
                }
                else
                {
                    newCommit = commit;
                }

                commitMap.Add(commit.Sha, newCommit);
            }

            return newCommit;
        }
        
        private void KeepEntries(SimpleCommit commit, Tree tree)
        {
            // Early exit if the commit was discarded by a tree-filtering
            if (commit.Discard)
            {
                return;
            }

            var task = Task.Factory.StartNew(() =>
            {
                foreach (var entryIt in tree)
                {
                    var entry = entryIt;
                    if (entry.TargetType == TreeEntryTargetType.Tree)
                    {
                        var subTree = (Tree)entry.Target;
                        KeepEntries(commit, subTree);
                    }
                    else if (entry.TargetType == TreeEntryTargetType.Blob || IncludeLinks)
                    {
                        KeepEntry(commit, entry, keepPathPatterns, true);
                    }
                }
            });

            if (DisableTasks)
            {
                task.RunSynchronously();
            }
            else
            {
                lock (pendingTasks)
                {
                    pendingTasks.Add(task);
                }
            }
        }

        private void KeepAllEntries(Tree tree)
        {
            var task = Task.Factory.StartNew(() =>
            {
                foreach (var entryIt in tree)
                {
                    var entry = entryIt;
                    if (entry.TargetType == TreeEntryTargetType.Tree)
                    {
                        var subTree = (Tree) entry.Target;
                        KeepAllEntries(subTree);
                    }
                    else if (entry.TargetType == TreeEntryTargetType.Blob || IncludeLinks)
                    {
                        // We can update entries to keep
                        lock (entriesToKeep)
                        {
                            entriesToKeep.Add(entry, new SimpleEntry.EntryValue());
                        }
                    }
                }
            });

            if (DisableTasks)
            {
                task.RunSynchronously();
            }
            else
            {
                lock (pendingTasks)
                {
                    pendingTasks.Add(task);
                }
            }
        }

        private void KeepEntry(SimpleCommit commit, TreeEntry entry, PathPatterns globalPattern, bool keepOnIgnore)
        {
            // Early exit if the commit was discarded by a tree-filtering
            if (commit.Discard)
            {
                return;
            }

            PathMatch match;
            var path = entry.Path;
            if (TryMatch(path, globalPattern, out match))
            {
                // If path is ignored we can update the entries to keep
                if (match.IsIgnored)
                {
                    DirectMatch(commit, entry, keepOnIgnore, ref match);
                }
            }
            else
            {
                var checkTask = Task.Factory.StartNew(() =>
                {
                    Match(path, globalPattern, ref match);
                    // If path is ignored we can update the entries to keep
                    if (match.IsIgnored)
                    {
                        DirectMatch(commit, entry, keepOnIgnore, ref match);
                    }
                });

                if (DisableTasks)
                {
                    checkTask.RunSynchronously();
                }
                else
                {
                    lock (pendingTasks)
                    {
                        pendingTasks.Add(checkTask);
                    }
                }
            }
        }

        private void DirectMatch(SimpleCommit commit, TreeEntry entry, bool keepOnIgnore, ref PathMatch match)
        {
            // If callback return false, then we don't update entries to keep or delete
            SimpleEntry simpleEntry;
            var pattern = match.Pattern;
            var callback = pattern.Callback;
            if (callback != null)
            {
                simpleEntry = new SimpleEntry(repo, entry);
                simpleEntry.Discard = !keepOnIgnore;

                // Calls the script
                callback(repo, pattern.Path, commit, ref simpleEntry);

                // Skip if this commit is discarded by the tree filtering
                // Skip if this entry was discarded
                if (commit.Discard || (simpleEntry.Discard == keepOnIgnore))
                {
                    return;
                }
            }
            else
            {
                simpleEntry = default(SimpleEntry);
            }

            // We can update entries to keep
            lock (entriesToKeep)
            {
                if (keepOnIgnore)
                {
                    entriesToKeep.Add(entry, simpleEntry.NewEntryValue);
                }
                else
                {
                    entriesToKeep.Remove(entry);
                }
            }
        }

        private static bool TryMatch(string path, PathPatterns pathPatterns, out PathMatch match)
        {
            var ignoreCache = pathPatterns.IgnoreCache;
            // Try first to get a previously match from the cache
            lock (ignoreCache)
            {
                return ignoreCache.TryGetValue(path, out match);
            }
        }

        private static void Match(string path, PathPatterns pathPatterns, ref PathMatch match)
        {
            var ignoreCache = pathPatterns.IgnoreCache;

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

        private void RemoveEntries(SimpleCommit commit)
        {
            var entries = entriesToKeep.ToList();
            foreach (var entry in entries)
            {
                KeepEntry(commit, entry.Key, removePathPatterns, false);
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
                if ((!isInMultiLineScript && string.IsNullOrWhiteSpace(line)) || line.TrimStart().StartsWith("#"))
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

                        pathPatterns.Add(new PathPattern(tempRocketPath, currentMultilinePath, multiLineScript.ToString()));
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
                        var pathPattern = new PathPattern(tempRocketPath, pathPatternText, textScript);
                        pathPatterns.Add(pathPattern);
                    }
                    else
                    {
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
            }

            if (isInMultiLineScript)
            {
                throw new RocketException("Expecting the end %}} of multiline script: {0}", multiLineScript);
            }

            if (pathPatternsNoScript.Count > 0)
            {
                var repoFilter = new Repository(tempRocketPath);
                repoFilter.Ignore.ResetAllTemporaryRules();

                if (Verbose)
                {
                    foreach (var pattern in pathPatternsNoScript)
                    {
                        OutputWriter.WriteLine("Found {0} pattern [{1}]", context, pattern);
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

            classText.AppendFormat(@"
namespace {0}", typeof(RocketFilterApp).Namespace).Append(@"
{
    public class RocketScript : RocketScriptBase
    {
        public RocketScript(RocketFilterApp app) : base(app)
        {
        }
");

            // Append commit filtering method
            AppendCommitFilterMethod(CommitFilter, classText);

            // Append any tree filtering methods
            var treeFilterMethods = new Dictionary<string, PathPattern>();
            var allPathPatterns = keepPathPatterns.Concat(removePathPatterns).ToList();
            AppendTreeFilterMethods(allPathPatterns, classText, treeFilterMethods);

            classText.Append(@"
    }
}
");
            var code = classText.ToString();

            // Dumps pretty code
            if (Verbose)
            {
                var prettyCode = DumpPrettyCode(code);
                OutputWriter.WriteLine("Patterns with scripting:");
                OutputWriter.WriteLine(prettyCode);
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(code);

            string assemblyName = Path.GetRandomFileName();
            var references = new List<MetadataReference>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    if (Verbose)
                    {
                        OutputWriter.WriteLine("Used assembly for scripting: " + assembly.Location);
                    }
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { syntaxTree },
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

                    throw new RocketException(BuildCodeErrors(failures)) { AdditionalText = DumpPrettyCode(code) };
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
            classText.Append(commitFilter);
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
                classText.Append(pathPattern.ScriptText);
                classText.Append(@"
        }
");
            }
        }

        internal SimpleCommit GetMapCommit(SimpleCommit commit)
        {
            Commit rewritten;
            if (commitMap.TryGetValue(commit.Id.Sha, out rewritten))
            {
                return GetSimpleCommit(rewritten);
            }
            return null;
        }

        internal SimpleCommit GetSimpleCommit(Commit commit)
        {
            SimpleCommit simpleCommit;
            lock (simpleCommits)
            {
                if (!simpleCommits.TryGetValue(commit, out simpleCommit))
                {
                    simpleCommit = new SimpleCommit(this, commit);
                    simpleCommits.Add(commit, simpleCommit);
                }
            }
            return simpleCommit;
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

            public PathPattern(string repoIgnorePath, string path, string scriptText)
            {
                if (repoIgnorePath == null) throw new ArgumentNullException("repoIgnorePath");
                if (path == null) throw new ArgumentNullException("path");
                Path = path;
                ScriptText = scriptText;
                Repository = new Repository(repoIgnorePath);
                Repository.Ignore.AddTemporaryRules(new[] { path });
                Ignore = Repository.Ignore;
            }

            public readonly string Path;

            public readonly string ScriptText;

            public PathPatternCallbackDelegate Callback;

            private readonly Repository Repository;

            public readonly Ignore Ignore;
        }

        private struct PathMatch
        {
            public PathMatch(bool isIgnored, PathPattern pattern)
            {
                IsIgnored = isIgnored;
                Pattern = pattern;
            }

            public readonly bool IsIgnored;

            public readonly PathPattern Pattern;
        }

        public void Dispose()
        {
            if (repo != null)
            {
                repo.Dispose();
                repo = null;
            }
        }

        /// <summary>
        /// A generic object comparerer that would only use object's reference, 
        /// ignoring any <see cref="IEquatable{T}"/> or <see cref="object.Equals(object)"/>  overrides.
        /// http://stackoverflow.com/a/1890230/1356325
        /// </summary>
        private class ObjectReferenceEqualityComparer<T> : EqualityComparer<T>
            where T : class
        {
            private static IEqualityComparer<T> _defaultComparer;

            public new static IEqualityComparer<T> Default
            {
                get { return _defaultComparer ?? (_defaultComparer = new ObjectReferenceEqualityComparer<T>()); }
            }

            #region IEqualityComparer<T> Members

            public override bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public override int GetHashCode(T obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }

            #endregion
        }
    }
}