// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.

using System.Linq;
using LibGit2Sharp;
using Xunit;
using Xunit.Abstractions;

namespace GitRocketFilter.Tests
{
    /// <summary>
    /// Tests for tree-filtering with scripts: --keep, --keep-script, --remove, --remove-script options.
    /// </summary>
    public class TestTreeFilteringScripts : TestRepoBase
    {
        public TestTreeFilteringScripts(ITestOutputHelper outputHelper) : base(outputHelper)
        {
        }

        /// <summary>
        /// Keeps only non binary files less than 10 bytes
        /// </summary>
        [Fact]
        public void KeepOnlyNonBinaryFilesLessThan10Bytes()
        {
            var test = InitializeTest();

            Assert.Equal(0, Program.Main("--keep", "* => entry.Discard = entry.IsBinary || entry.Size > 10; ",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD"));

            CheckKeepOnlyNonBinaryFilesLessThan10Bytes(test);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Keeps only non binary files less than 10 bytes (using multiline script
        /// </summary>
        [Fact]
        public void KeepOnlyNonBinaryFilesLessThan10BytesWithMultiLine()
        {
            var test = InitializeTest();

            Assert.Equal(0, Program.Main("--keep", @"* {% 
entry.Discard = entry.IsBinary || entry.Size > 10; 
%}",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD"));

            CheckKeepOnlyNonBinaryFilesLessThan10Bytes(test);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Make sure that order is satisfied when using scripts (so first pattern is matched and stops).
        /// </summary>
        [Fact]
        public void PatternsScriptsOrder()
        {
            var test = InitializeTest();

            Assert.Equal(0, Program.Main("--keep", "a.txt => entry.Discard = false;",
                "--keep", "* => entry.Discard = true;",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD"));

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var newCommits = GetCommits(repo, headNewMaster);

            Assert.Equal(1, newCommits.Count);

            var commit = newCommits[0];
            {
                Assert.Equal(1, commit.Tree.Count);
                Assert.NotNull(commit.Tree["a.txt"]);
            }

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Forget the closing multiline script %}
        /// </summary>
        [Fact]
        public void InvalidMultiLineScript()
        {
            var test = InitializeTest();

            var result = Program.Main("--keep", @"* {% 
entry.Discard = entry.IsBinary || entry.Size > 10; 
",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            Assert.NotEqual(0, result);

            Assert.Contains("Expecting the end %} of multiline script", test.Output);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Deletes all binary files bigger than 10 bytes
        /// </summary>
        [Fact]
        public void RemoveBinaryFilesBiggerThan10Bytes()
        {
            var test = InitializeTest();

            Assert.Equal(0, Program.Main("--remove", "* => entry.Discard = entry.IsBinary && entry.Size > 10; ",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD"));

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var originalCommits = GetCommits(repo);
            var newCommits = GetCommits(repo, headNewMaster);

            // We have only a binary file in one commit, so we should have one commit less
            Assert.Equal(originalCommits.Count -1, newCommits.Count);

            foreach (var commit in newCommits)
            {
                var entries = GetEntries(commit.Tree).ToList();

                foreach (var entry in entries)
                {
                    var blob = (Blob)entry.Target;
                    Assert.False(blob.IsBinary);
                }
            }

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        private static void CheckKeepOnlyNonBinaryFilesLessThan10Bytes(DisposeTempRepo test)
        {
            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var newCommits = GetCommits(repo, headNewMaster);

            Assert.True(newCommits.Count > 0);

            foreach (var commit in newCommits)
            {
                var entries = GetEntries(commit.Tree).ToList();

                foreach (var entry in entries)
                {
                    var blob = (Blob) entry.Target;
                    Assert.False(blob.IsBinary);
                    Assert.True(blob.Size <= 10);
                }
            }
        }
    }
}