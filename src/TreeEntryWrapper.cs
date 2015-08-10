using System;
using LibGit2Sharp;

namespace GitRocketFilter
{
    internal struct TreeEntryWrapper : IEquatable<TreeEntryWrapper>
    {
        private TreeEntryWrapper(TreeEntry entry)
        {
            TreeEntry = entry;
        }

        public readonly TreeEntry TreeEntry;

        public bool Equals(TreeEntryWrapper other)
        {
            return TreeEntry.Equals(other.TreeEntry);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is TreeEntryWrapper && Equals((TreeEntryWrapper)obj);
        }

        public override int GetHashCode()
        {
            return TreeEntry.GetHashCode();
        }

        public static bool operator ==(TreeEntryWrapper left, TreeEntryWrapper right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TreeEntryWrapper left, TreeEntryWrapper right)
        {
            return !left.Equals(right);
        }

        public static implicit operator TreeEntryWrapper(TreeEntry treeEntry)
        {
            return new TreeEntryWrapper(treeEntry);
        }

        public static implicit operator TreeEntry(TreeEntryWrapper treeEntry)
        {
            return treeEntry.TreeEntry;
        }

        public override string ToString()
        {
            return String.Format("TreeEntry: {0}", TreeEntry.Path);
        }
    }
}