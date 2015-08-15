// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.

using System;
using LibGit2Sharp;

namespace GitRocketFilter
{
    /// <summary>
    /// Base class for a script.
    /// </summary>
    public abstract class RocketScriptBase
    {
        private readonly RocketFilterApp rocketFilterApp;

        /// <summary>
        /// Gets or sets the tag object (user).
        /// </summary>
        /// <value>The tag object.</value>
        protected object Tag { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RocketScriptBase"/> class.
        /// </summary>
        /// <param name="rocketFilterApp">The rocket filter application.</param>
        protected RocketScriptBase(RocketFilterApp rocketFilterApp)
        {
            if (rocketFilterApp == null) throw new ArgumentNullException("rocketFilterApp");
            this.rocketFilterApp = rocketFilterApp;
        }

        /// <summary>
        /// Maps the specified commit to an already mapped commit.
        /// </summary>
        /// <param name="commit">The commit.</param>
        /// <returns>The new commit that has been mapped.</returns>
        /// <exception cref="System.ArgumentNullException">commit</exception>
        public SimpleCommit Map(SimpleCommit commit)
        {
            if (commit == null) throw new ArgumentNullException("commit");
            return rocketFilterApp.GetMapCommit(commit);
        }

        /// <summary>
        /// Transforms a <see cref="Commit"/> object to a <see cref="SimpleCommit"/>.
        /// </summary>
        /// <param name="commit">The commit.</param>
        /// <returns>A simple commit instance.</returns>
        /// <exception cref="System.ArgumentNullException">commit</exception>
        public SimpleCommit Simple(Commit commit)
        {
            if (commit == null) throw new ArgumentNullException("commit");
            return rocketFilterApp.GetSimpleCommit(commit);
        }
    }
}