// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.
using System;

namespace GitRocketFilter
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

        /// <summary>
        /// Gets or sets the additional text.
        /// </summary>
        /// <value>The additional text.</value>
        public string AdditionalText { get; set; }
    }
}