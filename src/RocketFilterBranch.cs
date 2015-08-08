using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace GitRocketFilterBranch
{

    public class RocketFilterBranch
    {
        private readonly Dictionary<ObjectId, Commit> remap;
        private readonly Repository repo;
        private readonly Repository whiteList;
        private readonly Repository blackList;
        private Commit lastCommit;
        private readonly List<Commit> commits = new List<Commit>();

        private readonly HashSet<TreeEntryKey> entriesToKeep = new HashSet<TreeEntryKey>();

        private readonly List<Task> calculateIgnoreTasks = new List<Task>();

        private readonly Dictionary<string, bool> whiteListIgnoreCache = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> blackListIgnoreCache = new Dictionary<string, bool>();

        public RocketFilterBranch(string repoPath)
        {
            repo = new LibGit2Sharp.Repository(repoPath);
            remap = new Dictionary<ObjectId, Commit>();
            WhiteListSpecifiers = new List<string>();
            BlackListSpecifiers = new List<string>();

            var tempRocketPath = Path.Combine(Path.GetTempPath(), ".gitRocketFilterBranch");
            Repository.Init(tempRocketPath, true);

            // Should we handle core.excludesfile?
            whiteList = new Repository(tempRocketPath);
            blackList = new Repository(tempRocketPath);
        }

        public List<string> WhiteListSpecifiers { get; private set; }

        public List<string> BlackListSpecifiers { get; private set; }

        public void Process()
        {
            var head = repo.Refs.Head;

            whiteList.Ignore.ResetAllTemporaryRules();
            blackList.Ignore.ResetAllTemporaryRules();

            whiteList.Ignore.AddTemporaryRules(WhiteListSpecifiers);
            blackList.Ignore.AddTemporaryRules(BlackListSpecifiers);

            //repo.ObjectDatabase.

            var directReference = head.ResolveToDirectReference();

            var processed = new HashSet<Commit>();
            CollectCommits((Commit) directReference.Target, processed);

            for (int i = 0; i < commits.Count; i++)
            {
                var commit = commits[i];
                Console.WriteLine("[{0}/{1}] Processing commit {2}", i, commits.Count, commit.Id);
                ProcessRawCommit(commit);
            }

            if (lastCommit != null)
            {
                repo.Refs.Remove("refs/heads/master2");
                repo.Refs.Add("refs/heads/master2", lastCommit.Id);
            }            
        }

        private void CollectCommits(Commit commit, HashSet<Commit> processed)
        {
            if (processed.Contains(commit))
            {
                return;
            }
            processed.Add(commit);

            foreach (var parentCommit in commit.Parents)
            {
                CollectCommits(parentCommit, processed);
            }
            commits.Add(commit);
        }

        private void BuildWhiteList(Tree tree)
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
                        EvaluateEntry(entry, whiteList.Ignore, whiteListIgnoreCache, true);
                    }

                }
            });

            lock (calculateIgnoreTasks)
            {
                calculateIgnoreTasks.Add(task);
            }
        }

        private void EvaluateEntry(TreeEntry entry, Ignore ignore, Dictionary<string, bool> ignoreCache,
            bool keepOnIgnore)
        {
            var checkTask = Task.Factory.StartNew(() =>
            {
                string path = entry.Path;
                if (IsIgnored(path, ignore, ignoreCache))
                {
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

            lock (calculateIgnoreTasks)
            {
                calculateIgnoreTasks.Add(checkTask);
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
                EvaluateEntry(entry, blackList.Ignore, blackListIgnoreCache, false);
            }
        }

        private void ProcessPendingTasks()
        {
            while (true)
            {
                Task[] taskToWait;
                lock (calculateIgnoreTasks)
                {
                    if (calculateIgnoreTasks.Count == 0)
                    {
                        break;
                    }
                    taskToWait = calculateIgnoreTasks.ToArray();
                    calculateIgnoreTasks.Clear();
                }
                Task.WaitAll(taskToWait);
            } 
        }

        private void ProcessRawCommit(Commit commit)
        {
            var tree = commit.Tree;

            // clear the cache of entries to keep and the tasks to run
            entriesToKeep.Clear();

            // Process white list
            BuildWhiteList(tree);
            ProcessPendingTasks();

            // Process black list
            if (BlackListSpecifiers.Count > 0)
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

            // Map parents of previous commit to new parrents
            // Check if at least a parent has the same tree, if yes, we don't need to create a new commit
            Commit newCommit = null;
            var newParents = new List<Commit>();
            foreach (var parent in commit.Parents)
            {
                var remapParent = remap[parent.Id];

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

        private struct TreeEntryKey : IEquatable<TreeEntryKey>
        {
            private TreeEntryKey(TreeEntry entry)
            {
                TreeEntry = entry;
            }

            public readonly TreeEntry TreeEntry;

            public bool Equals(TreeEntryKey other)
            {
                return TreeEntry.Equals(other.TreeEntry);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is TreeEntryKey && Equals((TreeEntryKey)obj);
            }

            public override int GetHashCode()
            {
                return TreeEntry.GetHashCode();
            }

            public static bool operator ==(TreeEntryKey left, TreeEntryKey right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(TreeEntryKey left, TreeEntryKey right)
            {
                return !left.Equals(right);
            }

            public static implicit operator TreeEntryKey(TreeEntry treeEntry)
            {
                return new TreeEntryKey(treeEntry);
            }

            public static implicit operator TreeEntry(TreeEntryKey treeEntry)
            {
                return treeEntry.TreeEntry;
            }

            public override string ToString()
            {
                return string.Format("TreeEntry: {0}", TreeEntry.Path);
            }
        }
    }
}