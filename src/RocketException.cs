using System;

namespace GitRocketFilterBranch
{
    /// <summary>
    /// An exception used to display errors (compilation, invalid parameters...etc).
    /// </summary>
    public class RocketException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="T:System.Exception" /> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public RocketException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RocketException"/> class.
        /// </summary>
        /// <param name="formatMessage">The format message.</param>
        /// <param name="args">The arguments.</param>
        public RocketException(string formatMessage, params object[] args)
            : base(string.Format(formatMessage, args))
        {
        }
    }
}