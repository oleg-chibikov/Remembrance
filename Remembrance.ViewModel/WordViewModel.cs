using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PropertyChanged;
using Remembrance.Contracts.Processing;
using Remembrance.Contracts.Processing.Data;
using Remembrance.Contracts.Translate;
using Remembrance.Contracts.Translate.Data.WordsTranslator;
using Scar.Common.MVVM.Commands;
using Scar.Common.MVVM.ViewModel;

// ReSharper disable VirtualMemberCallInConstructor
namespace Remembrance.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class WordViewModel : BaseViewModel
    {
        private readonly ITextToSpeechPlayer _textToSpeechPlayer;

        protected readonly ITranslationEntryProcessor TranslationEntryProcessor;

        public WordViewModel(
            Word word,
            string language,
            ITextToSpeechPlayer textToSpeechPlayer,
            ITranslationEntryProcessor translationEntryProcessor,
            ICommandManager commandManager)
            : base(commandManager)
        {
            _ = textToSpeechPlayer ?? throw new ArgumentNullException(nameof(textToSpeechPlayer));
            _ = translationEntryProcessor ?? throw new ArgumentNullException(nameof(translationEntryProcessor));
            Language = language ?? throw new ArgumentNullException(nameof(language));
            Word = word ?? throw new ArgumentNullException(nameof(word));

            _textToSpeechPlayer = textToSpeechPlayer ?? throw new ArgumentNullException(nameof(textToSpeechPlayer));
            TranslationEntryProcessor = translationEntryProcessor ?? throw new ArgumentNullException(nameof(translationEntryProcessor));
            PlayTtsCommand = AddCommand(PlayTtsAsync);
            LearnWordCommand = AddCommand(LearnWordAsync, () => CanLearnWord);
            TogglePriorityCommand = AddCommand(TogglePriority);
        }

        [DoNotNotify]
        public virtual bool CanEdit => CanLearnWord;

        [DoNotNotify]
        public bool CanLearnWord { get; set; } = true;

        public bool IsPriority { get; protected set; }

        [DoNotNotify]
        public virtual string Language { get; }

        public ICommand LearnWordCommand { get; }

        public ICommand PlayTtsCommand { get; }

        public int? PriorityGroup { get; protected set; }

        public ICommand TogglePriorityCommand { get; }

        [DoNotNotify]
        public Word Word { get; }

        public string? WordInfo =>
            Word.VerbType == null && Word.NounAnimacy == null && Word.NounGender == null
                ? null
                : string.Join(
                    ", ",
                    new[]
                    {
                        Word.VerbType,
                        Word.NounAnimacy,
                        Word.NounGender
                    }.Where(x => x != null));

        // A hack to raise NotifyPropertyChanged for other properties
        [AlsoNotifyFor(nameof(Word))]
        private bool ReRenderWordSwitch { get; set; }

        public void ReRenderWord()
        {
            ReRenderWordSwitch = !ReRenderWordSwitch;
        }

        public override string ToString()
        {
            return $"{Word} [{Language}]";
        }

        protected virtual void TogglePriority()
        {
        }

        private async Task LearnWordAsync()
        {
            await TranslationEntryProcessor.AddOrUpdateTranslationEntryAsync(new TranslationEntryAdditionInfo(Word.Text, Language), CancellationToken.None).ConfigureAwait(false);
        }

        private async Task PlayTtsAsync()
        {
            await _textToSpeechPlayer.PlayTtsAsync(Word.Text, Language, CancellationToken.None).ConfigureAwait(false);
        }
    }
}