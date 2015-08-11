// Copyright (c) Alexandre MUTEL. All rights reserved.
// Licensed under the BSD license. See LICENSE file in the project root for full license information.
using System;
using System.IO;
using LibGit2Sharp;

namespace GitRocketFilter
{
    /// <summary>
    /// Represents a git blob or link in a tree.
    /// </summary>
    public struct SimpleEntry
    {
        private readonly TreeEntry entry;
        private readonly GitObject target;
        private readonly GitLink link;
        private readonly Blob blob;
        private byte[] originalData;
        private byte[] data;
        internal bool changed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleEntry" /> struct.
        /// </summary>
        /// <param name="entry">The tree entry.</param>
        internal SimpleEntry(TreeEntry entry)
            : this()
        {
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
            get
            {
                if (data != null)
                {
                    return data.Length;
                }

                return blob != null ? blob.Size : 0;
            }
        }

        public byte[] Data
        {
            get
            {
                if (data != null)
                {
                    return data;
                }

                if (blob != null)
                {
                    var stream = blob.GetContentStream();
                    var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Position = 0;
                    originalData = memoryStream.ToArray();
                    data = originalData;
                }

                return data;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(string.Format("Cannot set a null buffer to entry [{0}]", entry.Path));
                }

                if (data != originalData)
                {
                    data = value;
                    changed = true;
                }
            }
        }

        public Stream DataAsStream
        {
            get
            {
                if (blob != null)
                {
                    return blob.GetContentStream();
                }
                return null;
            }
        }

        public string DataAsText
        {
            get
            {
                if (blob != null)
                {
                    return blob.GetContentText();
                }
                return null;
            }
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
    }
}