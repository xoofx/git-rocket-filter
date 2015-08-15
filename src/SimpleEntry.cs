// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.
using System;
using System.IO;
using System.Text;
using LibGit2Sharp;

namespace GitRocketFilter
{
    /// <summary>
    /// Represents a git blob or link in a tree.
    /// </summary>
    public struct SimpleEntry
    {
        private readonly Repository repo;
        private readonly TreeEntry entry;
        private readonly GitObject target;
        private readonly GitLink link;
        private readonly Blob blob;

        internal EntryValue NewEntryValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleEntry" /> struct.
        /// </summary>
        /// <param name="repo">The repo.</param>
        /// <param name="entry">The tree entry.</param>
        /// <exception cref="System.ArgumentNullException">
        /// repo
        /// or
        /// entry
        /// </exception>
        internal SimpleEntry(Repository repo, TreeEntry entry)
            : this()
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (entry == null) throw new ArgumentNullException("entry");
            this.repo = repo;
            this.entry = entry;
            target = entry.Target;
            this.blob = entry.Target as Blob;
            this.link = entry.Target as GitLink;
        }

        /// <summary>
        /// Gets the identifier object the blob or link object.
        /// </summary>
        /// <value>The identifier.</value>
        public ObjectId Id
        {
            get { return target.Id; }
        }

        /// <summary>
        /// Gets the text representation of <see cref="Id"/>.
        /// </summary>
        /// <value>The sha.</value>
        public string Sha
        {
            get { return target.Sha; }
        }

        /// <summary>
        /// Gets the name of this entry.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return entry.Name; }
        }

        /// <summary>
        /// Gets the path of this entry.
        /// </summary>
        /// <value>The path.</value>
        public string Path
        {
            get { return entry.Path; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is a blob.
        /// </summary>
        /// <value><c>true</c> if this instance is a blob; otherwise, <c>false</c>.</value>
        public bool IsBlob
        {
            get { return blob != null; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is binary blob.
        /// </summary>
        /// <value><c>true</c> if this instance is binary blob; otherwise, <c>false</c>.</value>
        public bool IsBinary
        {
            get { return blob != null && blob.IsBinary; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is a git link.
        /// </summary>
        /// <value><c>true</c> if this instance is a git link; otherwise, <c>false</c>.</value>
        public bool IsLink
        {
            get { return link != null; }
        }

        /// <summary>
        /// Gets the attributes of this entry.
        /// </summary>
        /// <value>The attributes.</value>
        public Mode Attributes
        {
            get { return entry.Mode; }
        }

        /// <summary>
        /// Gets the size of the blob or 0 if it is not a blob.
        /// </summary>
        /// <value>The size.</value>
        public long Size
        {
            get { return blob != null ? blob.Size : 0; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="SimpleEntry"/> should be discarded. Default is <c>false</c>.
        /// </summary>
        /// <value><c>true</c> if this commit should be discarded; otherwise, <c>false</c>.</value>
        public bool Discard { get; set; }

        /// <summary>
        /// Gets or sets a tag object.
        /// </summary>
        /// <value>A tag object.</value>
        public object Tag { get; set; }

        /// <summary>
        /// Gets the content of the blob as a byte array.
        /// </summary>
        /// <returns>The content of the blob as a byte array or null if no blob.</returns>
        public byte[] GetBlobAsBytes()
        {
            if (blob == null)
            {
                return null;
            }

            var stream = blob.GetContentStream();
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            return memoryStream.ToArray();
        }

        /// <summary>
        /// Gets the content of the blob as a stream.
        /// </summary>
        /// <returns>The content of the blob as a stream or null if no blob.</returns>
        public Stream GetBlobAsStream()
        {
            if (blob != null)
            {
                return blob.GetContentStream();
            }
            return null;
        }

        /// <summary>
        /// Gets the content of the blob as a text.
        /// </summary>
        /// <returns>The content of the blob as a text or null if no blob.</returns>
        public string GetBlobAsText()
        {
            if (blob != null)
            {
                return blob.GetContentText();
            }
            return null;
        }

        /// <summary>
        /// Replace the content of this entry by a stream.
        /// </summary>
        /// <param name="content">The content stream.</param>
        /// <param name="mode">The mode.</param>
        /// <exception cref="System.ArgumentNullException">content</exception>
        public void SetBlob(Stream content, Mode mode = Mode.NonExecutableFile)
        {
            if (content == null) throw new ArgumentNullException("content");
            var newBlob = repo.ObjectDatabase.CreateBlob(content);
            SetBlob(newBlob, mode);
        }

        /// <summary>
        /// Replace the content of this entry by a byte array.
        /// </summary>
        /// <param name="content">The content byte array.</param>
        /// <param name="mode">The mode.</param>
        /// <exception cref="System.ArgumentNullException">content</exception>
        public void SetBlob(byte[] content, Mode mode = Mode.NonExecutableFile)
        {
            if (content == null) throw new ArgumentNullException("content");
            var newBlob = repo.ObjectDatabase.CreateBlob(new MemoryStream(content));
            SetBlob(newBlob, mode);
        }

        /// <summary>
        /// Replace the content of this entry by a string.
        /// </summary>
        /// <param name="content">The content string.</param>
        /// <param name="mode">The mode.</param>
        /// <exception cref="System.ArgumentNullException">content</exception>
        public void SetBlob(string content, Mode mode = Mode.NonExecutableFile)
        {
            SetBlob(content, Encoding.UTF8, mode);
        }

        /// <summary>
        /// Replace the content of this entry by a string with a particular encoding.
        /// </summary>
        /// <param name="content">The content string.</param>
        /// <param name="encoding">The encoding mode for the string.</param>
        /// <param name="mode">The mode.</param>
        /// <exception cref="System.ArgumentNullException">content</exception>
        public void SetBlob(string content, Encoding encoding, Mode mode = Mode.NonExecutableFile)
        {
            if (content == null) throw new ArgumentNullException("content");
            var newBlob = repo.ObjectDatabase.CreateBlob(new MemoryStream(encoding.GetBytes(content)));
            SetBlob(newBlob, mode);
        }

        /// <summary>
        /// Replace the content of this entry by a blob.
        /// </summary>
        /// <param name="newBlob">The content blob.</param>
        /// <param name="mode">The mode.</param>
        /// <exception cref="System.ArgumentNullException">content</exception>
        public void SetBlob(Blob newBlob, Mode mode = Mode.NonExecutableFile)
        {
            if (newBlob == null) throw new ArgumentNullException("newBlob");
            NewEntryValue = new EntryValue(newBlob, mode);
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="SimpleEntry"/> to <see cref="TreeEntry"/>.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator TreeEntry(SimpleEntry entry)
        {
            return entry.entry;
        }

        internal struct EntryValue
        {
            public EntryValue(Blob blob, Mode mode)
            {
                Blob = blob;
                Mode = mode;
            }

            public readonly Blob Blob;

            public readonly Mode Mode;
        }
    }
}