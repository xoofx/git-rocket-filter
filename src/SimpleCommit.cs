// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using LibGit2Sharp;

namespace GitRocketFilter
{
    /// <summary>
    /// Represents a git commit for scripting, with flattened properties with lower case name.
    /// </summary>
    public sealed class SimpleCommit
    {
        private readonly RocketFilterApp rocket;
        private readonly Commit commit;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleCommit" /> class.
        /// </summary>
        /// <param name="rocket">The rocket.</param>
        /// <param name="commit">The commit.</param>
        /// <exception cref="System.ArgumentNullException">commit</exception>
        internal SimpleCommit(RocketFilterApp rocket, Commit commit)
        {
            if (commit == null) throw new ArgumentNullException("commit");
            this.rocket = rocket;
            this.commit = commit;

            // Update all properties
            Reset();
        }

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        public ObjectId Id
        {
            get { return commit.Id; }
        }

        /// <summary>
        /// Gets the sha identifier.
        /// </summary>
        /// <value>The sha.</value>
        public string Sha
        {
            get { return commit.Sha; }
        }

        /// <summary>
        /// Gets the encoding of the message.
        /// </summary>
        /// <value>The encoding of the message.</value>
        public string Encoding
        {
            get { return commit.Encoding; }
        }

        /// <summary>
        /// Gets or sets the author name.
        /// </summary>
        /// <value>The author name.</value>
        public string AuthorName { get; set; }

        /// <summary>
        /// Gets or sets the email.
        /// </summary>
        /// <value>The email.</value>
        public string AuthorEmail { get; set; }

        /// <summary>
        /// Gets or sets the date.
        /// </summary>
        /// <value>The date.</value>
        public DateTimeOffset AuthorDate { get; set; }

        /// <summary>
        /// Gets or sets the committer name.
        /// </summary>
        /// <value>The author name.</value>
        public string CommitterName { get; set; }

        /// <summary>
        /// Gets or sets the committer email.
        /// </summary>
        /// <value>The email.</value>
        public string CommitterEmail { get; set; }

        /// <summary>
        /// Gets or sets the committer date.
        /// </summary>
        /// <value>The date.</value>
        public DateTimeOffset CommitterDate { get; set; }

        /// <summary>
        /// Gets or sets the commit message.
        /// </summary>
        /// <value>The message.</value>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the commit short message.
        /// </summary>
        /// <value>The commit short message.</value>
        public string MessageShort
        {
            get { return commit.MessageShort; }
        }

        /// <summary>
        /// Gets or sets the tree object.
        /// </summary>
        /// <value>The tree.</value>
        public Tree Tree { get; set; }

        /// <summary>
        /// Gets the parent commits.
        /// </summary>
        /// <value>The parent commits.</value>
        public IEnumerable<SimpleCommit> Parents
        {
            get
            {
                foreach (var parentCommit in commit.Parents)
                {
                    yield return rocket.GetSimpleCommit(parentCommit);
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="SimpleCommit"/> should be discarded. Default is <c>false</c>.
        /// </summary>
        /// <value><c>true</c> if this commit should be discarded; otherwise, <c>false</c>.</value>
        public bool Discard { get; set; }

        /// <summary>
        /// Gets or sets a tag object.
        /// </summary>
        /// <value>A tag object.</value>
        public object Tag { get; set; }

        /// <summary>
        /// Gets the LibGit2 commit object.
        /// </summary>
        /// <value>The LibGit2 commit object.</value>
        public Commit GitCommit
        {
            get { return commit; }
        }

        /// <summary>
        /// Resets this values to the original commit.
        /// </summary>
        public void Reset()
        {
            AuthorName = commit.Author.Name;
            AuthorEmail = commit.Author.Email;
            AuthorDate = commit.Author.When;

            CommitterName = commit.Committer.Name;
            CommitterEmail = commit.Committer.Email;
            CommitterDate = commit.Committer.When;

            Message = commit.Message;

            Tree = commit.Tree;
        }

        public override string ToString()
        {
            return string.Format("id: {0}, name: {1}, email: {2}, date: {3}, messageShort: {4}", Id, AuthorName, AuthorEmail, AuthorDate, MessageShort);
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="SimpleCommit"/> to <see cref="Commit"/>.
        /// </summary>
        /// <param name="commit">The commit.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator Commit(SimpleCommit commit)
        {
            return commit != null ? commit.GitCommit : null;
        }
    }
}