using LibGit2Sharp;

namespace GitRocketFilterBranch
{
    /// <summary>
    /// Represents a git blob for scripting.
    /// </summary>
    public struct SimpleBlob
    {
        private readonly Blob blob;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleBlob"/> struct.
        /// </summary>
        /// <param name="blob">The BLOB.</param>
        public SimpleBlob(Blob blob)
        {
            this.blob = blob;
        }
        
        // TODO
    }
}