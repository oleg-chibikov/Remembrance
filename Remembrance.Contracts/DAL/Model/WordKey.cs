using System;
using JetBrains.Annotations;

namespace Remembrance.Contracts.DAL.Model
{
    public sealed class WordKey : IEquatable<WordKey>
    {
        [UsedImplicitly]
        public WordKey()
        {
        }

        public WordKey([NotNull] TranslationEntryKey translationEntryKey, [NotNull] BaseWord word)
        {
            Word = word ?? throw new ArgumentNullException(nameof(word));
            TranslationEntryKey = translationEntryKey ?? throw new ArgumentNullException(nameof(translationEntryKey));
        }

        [NotNull]
        public TranslationEntryKey TranslationEntryKey { get; set; }

        [NotNull]
        public BaseWord Word { get; set; }

        #region Equality

        public bool Equals(WordKey other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Word.Equals(other.Word) && TranslationEntryKey.Equals(other.TranslationEntryKey);
        }

        public static bool operator ==([CanBeNull] WordKey obj1, [CanBeNull] WordKey obj2)
        {
            if (ReferenceEquals(obj1, obj2))
            {
                return true;
            }

            return obj1?.Equals(obj2) == true;
        }

        // this is second one '!='
        public static bool operator !=([CanBeNull] WordKey obj1, [CanBeNull] WordKey obj2)
        {
            return !(obj1 == obj2);
        }

        public override bool Equals(object obj)
        {
            return obj is WordKey key && Equals(key);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = TranslationEntryKey.GetHashCode();
                hashCode = (hashCode * 397) ^ Word.GetHashCode();
                return hashCode;
            }
        }

        #endregion

        public override string ToString()
        {
            return $"{TranslationEntryKey} - {Word}";
        }
    }
}