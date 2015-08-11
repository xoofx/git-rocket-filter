using System.Linq;
using LibGit2Sharp;
using Xunit;

namespace GitRocketFilter.Tests
{
    public class TestCommitFiltering : TestRepoBase
    {
        /// <summary>
        /// Appends the message "This is a test" to all commits.
        /// </summary>
        [Fact]
        public void ModifyCommitMessageAll()
        {
            var test = InitializeTest();

            Program.Main("--commit-filter",
                @"commit.Message += ""This is a test"";",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);
                
            var originalCommits = repo.Commits.QueryBy(new CommitFilter() { SortBy = CommitSortStrategies.Topological })
                .ToList();
            var newCommits = repo.Commits.QueryBy(new CommitFilter() { Since = headNewMaster, SortBy = CommitSortStrategies.Topological })
                .ToList();

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
    }
}