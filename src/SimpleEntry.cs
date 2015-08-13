// Copyright (c) Alexandre MUTEL. All rights reserved.
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
        private byte[] data;
        internal EntryValue newEntryValue;

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

        public ObjectId Id
        {
            get { return target.Id; }
        }

        public string Sha
        {
            get { return target.Sha; }
        }

        public string Name
        {
            get { return entry.Name; }
        }

        public string Path
        {
            get { return entry.Path; }
        }

        public bool IsBlob
        {
            get { return blob != null; }
        }

        public bool IsBinary
        {
            get { return blob != null && blob.IsBinary; }
        }

        public bool IsLink
        {
            get { return link != null; }
        }

        public Mode Attributes
        {
            get { return entry.Mode; }
        }

        public long Size
        {
            get { return blob != null ? blob.Size : 0; }
        }

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

        public Stream GetBlobAsStream()
        {
            if (blob != null)
            {
                return blob.GetContentStream();
            }
            return null;
        }

        public string GetBlobAsText()
        {
            if (blob != null)
            {
                return blob.GetContentText();
            }
            return null;
        }

        public void SetBlob(Stream content, Mode mode = Mode.NonExecutableFile)
        {
            if (content == null) throw new ArgumentNullException("content");
            newEntryValue = new EntryValue(repo.ObjectDatabase.CreateBlob(content), mode);
        }

        public void SetBlob(byte[] content, Mode mode = Mode.NonExecutableFile)
        {
            if (content == null) throw new ArgumentNullException("content");
            newEntryValue = new EntryValue(repo.ObjectDatabase.CreateBlob(new MemoryStream(content)), mode);
        }

        public void SetBlob(string content, Mode mode = Mode.NonExecutableFile)
        {
            SetBlob(content, Encoding.UTF8, mode);
        }

        public void SetBlob(string content, Encoding encoding, Mode mode = Mode.NonExecutableFile)
        {
            if (content == null) throw new ArgumentNullException("content");
            newEntryValue = new EntryValue(
                repo.ObjectDatabase.CreateBlob(new MemoryStream(encoding.GetBytes(content))), mode);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="SimpleEntry"/> should be discarded. Default is <c>false</c>.
        /// </summary>
        /// <value><c>true</c> if this commit should be discarded; otherwise, <c>false</c>.</value>
        public bool Discard { get; set; }

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