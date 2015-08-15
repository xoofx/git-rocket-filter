// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace GitRocketFilter.Tests
{
    /// <summary>
    /// Tests for commit filtering: --commit-filter option
    /// </summary>
    public class TestCommitFiltering : TestRepoBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestCommitFiltering"/> class.
        /// </summary>
        /// <param name="outputHelper">The output helper.</param>
        public TestCommitFiltering(ITestOutputHelper outputHelper) : base(outputHelper)
        {
        }

        /// <summary>
        /// Appends the message "This is a test" to all commits.
        /// </summary>
        [Fact]
        public void ModifyCommitMessageAll()
        {
            var test = InitializeTest();

            // Test directly the main program as we want to test also command line parameters
            Assert.Equal(0, Program.Main("--commit-filter",
                @"commit.Message += ""This is a test"";",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD"));

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var originalCommits = GetCommits(repo);
            var newCommits = GetCommits(repo, headNewMaster);

            // Make sure that we have the same number of commits
            Assert.Equal(originalCommits.Count, newCommits.Count);

            for (int i = 0; i < newCommits.Count; i++)
            {
                var originalCommit = originalCommits[i];
                var commit = newCommits[i];

                // All commits should have same tree id
                Assert.Equal(originalCommit.Tree.Id, commit.Tree.Id);

                // Check the new message
                Assert.EndsWith("This is a test", commit.Message);
                Assert.StartsWith(originalCommit.Message, commit.Message);
            }

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Appends the message "This is a test" to the previous 4 commits.
        /// </summary>
        [Fact]
        public void ModifyCommitMessageLast4()
        {
            var test = InitializeTest();

            // Test directly the main program as we want to test also command line parameters
            Assert.Equal(0, Program.Main("--commit-filter",
                @"commit.Message += ""This is a test"";",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD~4..HEAD"));

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var originalCommits = GetCommits(repo);
            var originalCommitsRange = GetCommitsFromRange(repo, @"HEAD~4..HEAD").Select(commit => commit.Id).ToList();
            var newCommits = GetCommits(repo, headNewMaster);

            // Make sure that we have the same number of commits
            Assert.Equal(originalCommits.Count, newCommits.Count);

            for (int i = 0; i < newCommits.Count; i++)
            {
                var originalCommit = originalCommits[i];
                var commit = newCommits[i];

                if (originalCommitsRange.Contains(originalCommit.Id))
                {
                    // All commits should have same tree id
                    Assert.Equal(originalCommit.Tree.Id, commit.Tree.Id);

                    // Check the new message
                    Assert.EndsWith("This is a test", commit.Message);
                    Assert.StartsWith(originalCommit.Message, commit.Message);
                }
                else
                {
                    // All commits should have same tree id
                    Assert.Equal(originalCommit.Id, commit.Id);
                }
            }

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Appends the message "This is a test" to the previous commit and detach it from its parent.
        /// </summary>
        [Fact]
        public void ModifyCommitMessageLast2WithDetach()
        {
            var test = InitializeTest();

            // Test directly the main program as we want to test also command line parameters
            Assert.Equal(0, Program.Main("--commit-filter",
                @"commit.Message += ""This is a test"";",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                "--detach",
                @"HEAD~2..HEAD"));

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var originalCommits = GetCommitsFromRange(repo, @"HEAD~2..HEAD");
            var newCommits = GetCommits(repo, headNewMaster);

            // Make sure that we have the same number of commits
            Assert.Equal(originalCommits.Count, newCommits.Count);

            for (int i = 0; i < newCommits.Count; i++)
            {
                var originalCommit = originalCommits[i];
                var commit = newCommits[i];

                // All commits should have same tree id
                Assert.Equal(originalCommit.Tree.Id, commit.Tree.Id);

                // Check the new message
                Assert.EndsWith("This is a test", commit.Message);
                Assert.StartsWith(originalCommit.Message, commit.Message);

                // The second commit is detached
                if (i == 1)
                {
                    Assert.Equal(0, commit.Parents.Count());
                }
            }

            // Cleanup the test only if we succeed
            test.Dispose();
        }


        /// <summary>
        /// Discard all commits that don't contain the 'test.bin' text in their message. Rewrite author and committer of the commit left.
        /// </summary>
        [Fact]
        public void DiscardAllCommitMessageExceptOne()
        {
            var test = InitializeTest();

            // Test directly the main program as we want to test also command line parameters
            Assert.Equal(0, Program.Main("--commit-filter",
                @"commit.Discard = !commit.Message.Contains(""test.bin""); if (!commit.Discard) { commit.AuthorName=""NewAuthor""; commit.AuthorEmail = ""test@gmail.com""; commit.CommitterName =""NewCommitter""; commit.CommitterEmail = ""test2@gmail.com""; }",
                "--repo-dir", test.Path,
                "--branch", NewBranch
                ));

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var newCommits = GetCommits(repo, headNewMaster);

            // We should have only 1 commit
            Assert.Equal(1, newCommits.Count);

            var commitLeft = newCommits[0];
            Assert.Contains("test.bin", commitLeft.Message);

            // Check new field
            Assert.Equal("NewAuthor", commitLeft.Author.Name);
            Assert.Equal("test@gmail.com", commitLeft.Author.Email);
            Assert.Equal("NewCommitter", commitLeft.Committer.Name);
            Assert.Equal("test2@gmail.com", commitLeft.Committer.Email);

            // Cleanup the test only if we succeed
            test.Dispose();
        }
    }
}