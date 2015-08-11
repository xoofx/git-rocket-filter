// Copyright (c) Alexandre MUTEL. All rights reserved.
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

        private string authorNameValue;
        private string authorEmailValue;
        private DateTimeOffset authorDateValue;

        private string committerNameValue;
        private string committerEmailValue;
        private DateTimeOffset committerDateValue;

        private string messageValue;
        private string messageShortValue;

        private Tree tree;

        // True if this commit has been changed
        internal bool Changed;

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
        public string AuthorName
        {
            get { return authorNameValue; }
            set
            {
                if (authorNameValue != value)
                {
                    authorNameValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the email.
        /// </summary>
        /// <value>The email.</value>
        public string AuthorEmail
        {
            get { return authorEmailValue; }
            set
            {
                if (authorEmailValue != value)
                {
                    authorEmailValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the date.
        /// </summary>
        /// <value>The date.</value>
        public DateTimeOffset AuthorDate
        {
            get { return authorDateValue; }
            set
            {
                if (!value.Equals(authorDateValue))
                {
                    authorDateValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the committer name.
        /// </summary>
        /// <value>The author name.</value>
        public string CommitterName
        {
            get { return committerNameValue; }
            set
            {
                if (committerNameValue != value)
                {
                    committerNameValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the committer email.
        /// </summary>
        /// <value>The email.</value>
        public string CommitterEmail
        {
            get { return committerEmailValue; }
            set
            {
                if (committerEmailValue != value)
                {
                    committerEmailValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the committer date.
        /// </summary>
        /// <value>The date.</value>
        public DateTimeOffset CommitterDate
        {
            get { return committerDateValue; }
            set
            {
                if (!value.Equals(committerDateValue))
                {
                    committerDateValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the commit message.
        /// </summary>
        /// <value>The message.</value>
        public string Message
        {
            get { return messageValue; }
            set
            {
                if (messageValue != value)
                {
                    messageValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the commit short message.
        /// </summary>
        /// <value>The commit short message.</value>
        public string MessageShort
        {
            get { return messageShortValue; }
        }

        /// <summary>
        /// Gets or sets the tree object.
        /// </summary>
        /// <value>The tree.</value>
        public Tree Tree
        {
            get { return tree; }

            set
            {
                if (tree != value)
                {
                    tree = value;
                    Changed = true;
                }
            }
        }

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
        /// Resets this values to the original commit.
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public void Reset()
        {
            authorNameValue = commit.Author.Name;
            authorEmailValue = commit.Author.Email;
            authorDateValue = commit.Author.When;

            committerNameValue = commit.Committer.Name;
            committerEmailValue = commit.Committer.Email;
            committerDateValue = commit.Committer.When;

            messageValue = commit.Message;
            messageShortValue = commit.MessageShort;

            tree = commit.Tree;

            Changed = false;
        }

        /// <summary>
        /// Gets the LibGit2 commit object.
        /// </summary>
        /// <value>The LibGit2 commit object.</value>
        public Commit GitCommit
        {
            get { return commit; }
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