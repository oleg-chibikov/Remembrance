using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Scar.Common.DAL.Model;

namespace Remembrance.Contracts.DAL.Model
{
    public sealed class TranslationEntry : TrackedEntity<TranslationEntryKey>
    {
        [NotNull]
        private LinkedListNode<RepeatType> _current;

        private DateTime _lastCardShowTime;

        private RepeatType _repeatType;

        [UsedImplicitly]
        public TranslationEntry()
        {
        }

        public TranslationEntry([NotNull] TranslationEntryKey key)
        {
            Id = key;
            LastCardShowTime = DateTime.Now;
            RepeatType = RepeatTypeSettings.RepeatTypes.First.Value;
            _current = RepeatTypeSettings.RepeatTypes.First;
        }

        [CanBeNull]
        public ICollection<ManualTranslation> ManualTranslations { get; set; }

        [CanBeNull]
        public ICollection<BaseWord> PriorityWords { get; set; }

        public RepeatType RepeatType
        {
            get => _repeatType;
            set
            {
                _repeatType = value;
                _current = RepeatTypeSettings.RepeatTypes.Find(value) ?? RepeatTypeSettings.RepeatTypes.First;
                SetNextCardShowTime();
            }
        }

        public int ShowCount { get; set; }

        public DateTime LastCardShowTime
        {
            get => _lastCardShowTime;
            set
            {
                _lastCardShowTime = value;
                SetNextCardShowTime();
            }
        }

        public DateTime NextCardShowTime { get; set; }

        public bool IsFavorited { get; set; }

        public void DecreaseRepeatType()
        {
            var prev = _current.Previous;
            if (prev == null)
            {
                return;
            }

            RepeatType = prev.Value;
            _current = prev;
        }

        public void IncreaseRepeatType()
        {
            var next = _current.Next;
            if (next == null)
            {
                return;
            }

            RepeatType = next.Value;
            _current = next;
        }

        private void SetNextCardShowTime()
        {
            NextCardShowTime = _lastCardShowTime.Add(RepeatTypeSettings.RepeatTimes[_repeatType]);
        }

        public override string ToString()
        {
            return $"{Id}";
        }
    }
}