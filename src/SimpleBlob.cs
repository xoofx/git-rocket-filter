using System;
using System.IO;
using LibGit2Sharp;

namespace GitRocketFilterBranch
{
    /// <summary>
    /// Represents a git blob for link.
    /// </summary>
    public struct SimpleEntry
    {
        private readonly TreeEntryWrapper entryWrapper;
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
        /// <param name="entryWrapper">The entry wrapper.</param>
        internal SimpleEntry(TreeEntryWrapper entryWrapper) : this()
        {
            this.entryWrapper = entryWrapper;
            this.entry = entryWrapper.TreeEntry;
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
                    throw new ArgumentNullException(string.Format("Cannot set a null buffer to entry [{0}]", entryWrapper));
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
    }
}