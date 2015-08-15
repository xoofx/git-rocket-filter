// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

namespace GitRocketFilter.Tests
{
    /// <summary>
    /// Tests various invalid options.
    /// </summary>
    public class TestInvalidOptions : TestRepoBase
    {
        public TestInvalidOptions(ITestOutputHelper outputHelper)
            : base(outputHelper)
        {
        }

        /// <summary>
        /// Forget the --branch option
        /// </summary>
        [Fact]
        public void ForgetBranch()
        {
            var test = InitializeTest();

            var result = Program.Main("--keep", "/Test1",
                "--repo-dir", test.Path,
                @"HEAD");

            Assert.NotEqual(0, result);

            Assert.Contains("Branch name is required and cannot be null", test.Output);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Branch already exist but no --force option
        /// </summary>
        [Fact]
        public void ForgetBranchForce()
        {
            var test = InitializeTest();

            // Add an existing branch reference
            test.Repo.Refs.Add(NewBranchRef, test.Repo.Refs.Head);

            var result = Program.Main("--keep", "/Test1",
                "--repo-dir", test.Path,
                "--branch", NewBranch,
                @"HEAD");

            Assert.NotEqual(0, result);

            Assert.Contains(string.Format("The branch [{0}] already exist. Cannot overwrite without force option", NewBranch), test.Output);

            // Cleanup the test only if we succeed
            test.Dispose();
        }

        /// <summary>
        /// Branch already but with pass --force option
        /// </summary>
        [Fact]
        public void BranchForce()
        {
            var test = InitializeTest();

            // Add an existing branch reference
            test.Repo.Refs.Add(NewBranchRef, test.Repo.Refs.Head);

            var result = Program.Main("--keep", "test.bin",
                "--repo-dir", test.Path,
                "--force",
                "--branch", NewBranch,
                @"HEAD");

            Assert.Equal(0, result);

            var repo = test.Repo;
            var headNewMaster = AssertBranchRef(repo);

            var newCommits = GetCommits(repo, headNewMaster);

            Assert.Equal(1, newCommits.Count);

            Assert.NotNull(newCommits[0].Tree["Binary/test.bin"]);

            // Cleanup the test only if we succeed
            test.Dispose();
        }
   }
}