using System;
using LibGit2Sharp;

namespace GitRocketFilterBranch
{
    /// <summary>
    /// Represents a git commit for scripting, with flattened properties with lower case name.
    /// </summary>
    public struct SimpleCommit
    {
        private readonly Commit commit;

        private string nameValue;
        private string emailValue;
        private DateTimeOffset dateValue;

        private string nameCommitterValue;
        private string emailCommitterValue;
        private DateTimeOffset dateCommitterValue;

        private string messageValue;
        private string messageShortValue;
        internal bool Changed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleCommit" /> class.
        /// </summary>
        /// <param name="commit">The commit.</param>
        /// <exception cref="System.ArgumentNullException">commit</exception>
        internal SimpleCommit(Commit commit) : this()
        {
            if (commit == null) throw new ArgumentNullException("commit");
            this.commit = commit;
        }

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        // ReSharper disable once InconsistentNaming
        public ObjectId id
        {
            get { return commit.Id; }
        }

        /// <summary>
        /// Gets or sets the author name.
        /// </summary>
        /// <value>The author name.</value>
        // ReSharper disable once InconsistentNaming
        public string name
        {
            get { return nameValue; }
            set
            {
                if (commit.Author.Name != value)
                {
                    nameValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the email.
        /// </summary>
        /// <value>The email.</value>
        // ReSharper disable once InconsistentNaming
        public string email
        {
            get { return emailValue; }
            set
            {
                if (commit.Author.Email != value)
                {
                    emailValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the date.
        /// </summary>
        /// <value>The date.</value>
        // ReSharper disable once InconsistentNaming
        public DateTimeOffset date
        {
            get { return dateValue; }
            set
            {
                if (!value.Equals(commit.Author.When))
                {
                    dateValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the author name.
        /// </summary>
        /// <value>The author name.</value>
        // ReSharper disable once InconsistentNaming
        public string nameCommitter
        {
            get { return nameCommitterValue; }
            set
            {
                if (commit.Committer.Name != value)
                {
                    nameCommitterValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the email.
        /// </summary>
        /// <value>The email.</value>
        // ReSharper disable once InconsistentNaming
        public string emailCommiter
        {
            get { return emailCommitterValue; }
            set
            {
                if (commit.Committer.Email != value)
                {
                    emailCommitterValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the date.
        /// </summary>
        /// <value>The date.</value>
        // ReSharper disable once InconsistentNaming
        public DateTimeOffset dateCommitter
        {
            get { return dateCommitterValue; }
            set
            {
                if (!value.Equals(commit.Committer.When))
                {
                    dateCommitterValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Gets or sets the commit message.
        /// </summary>
        /// <value>The message.</value>
        // ReSharper disable once InconsistentNaming
        public string message
        {
            get { return messageValue; }
            set
            {
                if (commit.Message != value)
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
        // ReSharper disable once InconsistentNaming
        public string messageShort
        {
            get { return messageShortValue; }
            set
            {
                if (commit.MessageShort != value)
                {
                    messageShortValue = value;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Resets this values to the original commit.
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public void reset()
        {
            dateValue = commit.Author.When;
            nameValue = commit.Author.Name;
            emailValue = commit.Author.Email;

            dateCommitterValue = commit.Committer.When;
            nameCommitterValue = commit.Committer.Name;
            emailCommitterValue = commit.Committer.Email;

            messageValue = commit.Message;
            messageShortValue = commit.MessageShort;
        }

        public override string ToString()
        {
            return string.Format("id: {0}, name: {1}, email: {2}, date: {3}, messageShort: {4}", id, name, email, date, messageShort);
        }
    }
}