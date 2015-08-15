// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.

using System.Linq;
using LibGit2Sharp;
using Xunit;

namespace GitRocketFilter.Tests
{
    /// <summary>
    /// Tests for tree-filtering: --keep, --keep-from-file, --remove, --remove-from-file options.
    /// </summary>
    public class TestTreeFiltering : TestRepoBase
    {
        /// <summary>
        /// Keeps only /Test1 directory
        /// </summary>
        [Fact]
        public void KeepOnlyOneDirectory()
        {
            var test = InitializeTest();

            Program.Main("--keep",
                @"/Test1",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var newCommits = GetCommits(repo, headNewMaster);

            Assert.True(newCommits.Count > 0);

            for (int i = 0; i < newCommits.Count; i++)
            {
                var commit = newCommits[i];
                Assert.Equal(1, commit.Tree.Count);

                var entry = commit.Tree["Test1"];
                Assert.NotNull(entry);
                Assert.Equal(TreeEntryTargetType.Tree, entry.TargetType);

                var tree = (Tree) entry.Target;
                Assert.Equal(3, tree.Count);
            }

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Keeps /Test1 directory and a.txt file
        /// </summary>
        [Fact]
        public void KeepOnlyOneDirectoryAndFile()
        {
            var test = InitializeTest();

            Program.Main("--keep", "/Test1",
                "--keep", "a.txt",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            CheckKeepOnlyOneDirectoryAndFile(test);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Keeps /Test1 directory and a.txt file (version using --keep-from-file)
        /// </summary>
        [Fact]
        public void KeepAndKeepFromFile()
        {
            var test = InitializeTest();

            Program.Main("--keep-from-file", "KeepPatternFile1.txt",
                "--keep", "a.txt",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            CheckKeepOnlyOneDirectoryAndFile(test);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        private void CheckKeepOnlyOneDirectoryAndFile(DisposeTempRepo test)
        {
            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var newCommits = GetCommits(repo, headNewMaster);

            Assert.True(newCommits.Count > 0);

            bool haveCommit2 = false;

            for (int i = 0; i < newCommits.Count; i++)
            {
                var commit = newCommits[i];
                Assert.True(commit.Tree.Count == 1 || commit.Tree.Count == 2);

                Assert.NotNull(commit.Tree["a.txt"]);

                // /Test1 was added after a.txt
                if (commit.Tree.Count == 2)
                {
                    haveCommit2 = true;
                    var entry = commit.Tree["Test1"];
                    Assert.NotNull(entry);
                    Assert.Equal(TreeEntryTargetType.Tree, entry.TargetType);

                    var tree = (Tree)entry.Target;
                    Assert.Equal(3, tree.Count);
                }
            }

            Assert.True(haveCommit2, "Missing commits for /Test1 folder");
        }

        /// <summary>
        /// Removes all *.txt files
        /// </summary>
        [Fact]
        public void RemoveFilesWithTxtExtension()
        {
            var test = InitializeTest();

            Program.Main("--remove", "*.txt",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var newCommits = GetCommits(repo, headNewMaster);

            Assert.Equal(1, newCommits.Count);

            Assert.Equal(1, newCommits[0].Tree.Count);

            Assert.NotNull(newCommits[0].Tree["Binary/test.bin"]);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Keeps directory /Test[12], remove all files except a[12].txt
        /// </summary>
        [Fact]
        public void KeepTwoDirectoriesAndRemoveExcept()
        {
            var test = InitializeTest();

            Program.Main("--keep", "/Test[12]",
                "--remove", "*",  // removes all files
                "--remove", "!a[12].txt", // except a[12].txt
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            CheckKeepTwoDirectoriesAndRemoveExcept(test);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Keeps directory /Test[12], remove all files except a[12].txt using --remove-from-file
        /// </summary>
        [Fact]
        public void KeepTwoDirectoriesAndRemoveFromFileExcept()
        {
            var test = InitializeTest();

            Program.Main("--keep", "/Test[12]",
                "--remove-from-file", "RemovePatternFile1.txt",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            CheckKeepTwoDirectoriesAndRemoveExcept(test);

            // Cleanup the test only if we succeed
            test.Dispose();
        }
        
        private static void CheckKeepTwoDirectoriesAndRemoveExcept(DisposeTempRepo test)
        {
            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var newCommits = GetCommits(repo, headNewMaster);

            Assert.True(newCommits.Count > 0);

            for (int i = 0; i < newCommits.Count; i++)
            {
                var commit = newCommits[i];

                var entries = GetEntries(commit.Tree).ToList();

                // Check that we keps only a1/a2
                foreach (var entry in entries)
                {
                    Assert.True(entry.Name == "a1.txt" || entry.Name == "a2.txt");
                    Assert.True(entry.Path.StartsWith("Test1") || entry.Path.StartsWith("Test2"));
                }
            }
        }
    }
}