using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Autofac;
using Common.Logging;
using Easy.MessageHub;
using JetBrains.Annotations;
using PropertyChanged;
using Remembrance.Contracts;
using Remembrance.Contracts.CardManagement;
using Remembrance.Contracts.CardManagement.Data;
using Remembrance.Contracts.DAL;
using Remembrance.Contracts.DAL.Model;
using Remembrance.Contracts.Translate;
using Remembrance.Contracts.View.Card;
using Remembrance.Contracts.View.Settings;
using Remembrance.ViewModel.Card;
using Remembrance.ViewModel.Translation;
using Scar.Common.DAL;
using Scar.Common.WPF.Commands;
using Scar.Common.WPF.Localization;
using Scar.Common.WPF.View;
using Scar.Common.WPF.View.Contracts;

namespace Remembrance.ViewModel.Settings
{
    [UsedImplicitly]
    [AddINotifyPropertyChangedInterface]
    public sealed class DictionaryViewModel : BaseViewModelWithAddTranslationControl
    {
        //TODO: config
        private const int PageSize = 20;

        [NotNull]
        private readonly WindowFactory<IDictionaryWindow> _dictionaryWindowFactory;

        [NotNull]
        private readonly ILifetimeScope _lifetimeScope;

        [NotNull]
        private readonly object _lockObject = new object();

        [NotNull]
        private readonly IMessageHub _messenger;

        [NotNull]
        private readonly IList<Guid> _subscriptionTokens = new List<Guid>();

        [NotNull]
        private readonly SynchronizationContext _syncContext = SynchronizationContext.Current;

        [NotNull]
        private readonly DispatcherTimer _timer;

        [NotNull]
        private readonly ITranslationDetailsRepository _translationDetailsRepository;

        [NotNull]
        private readonly ITranslationEntryRepository _translationEntryRepository;

        [NotNull]
        private readonly ObservableCollection<TranslationEntryViewModel> _translationList;

        [NotNull]
        private readonly IViewModelAdapter _viewModelAdapter;

        [NotNull]
        private readonly IWordPriorityRepository _wordPriorityRepository;

        [NotNull]
        private readonly IEqualityComparer<IWord> _wordsEqualityComparer;

        [NotNull]
        private readonly IWordsProcessor _wordsProcessor;

        private int _count;
        private bool _filterChanged;
        private int _lastRecordedCount;

        public DictionaryViewModel(
            [NotNull] ITranslationEntryRepository translationEntryRepository,
            [NotNull] ISettingsRepository settingsRepository,
            [NotNull] ILanguageDetector languageDetector,
            [NotNull] IWordsProcessor wordsProcessor,
            [NotNull] ILog logger,
            [NotNull] ILifetimeScope lifetimeScope,
            [NotNull] WindowFactory<IDictionaryWindow> dictionaryWindowFactory,
            [NotNull] IEqualityComparer<IWord> wordsEqualityComparer,
            [NotNull] ITranslationDetailsRepository translationDetailsRepository,
            [NotNull] IViewModelAdapter viewModelAdapter,
            [NotNull] IMessageHub messenger,
            [NotNull] EditManualTranslationsViewModel editManualTranslationsViewModel,
            [NotNull] IWordPriorityRepository wordPriorityRepository)
            : base(settingsRepository, languageDetector, wordsProcessor, logger)
        {
            _wordPriorityRepository = wordPriorityRepository ?? throw new ArgumentNullException(nameof(wordPriorityRepository));
            EditManualTranslationsViewModel = editManualTranslationsViewModel ?? throw new ArgumentNullException(nameof(editManualTranslationsViewModel));
            _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
            _dictionaryWindowFactory = dictionaryWindowFactory ?? throw new ArgumentNullException(nameof(dictionaryWindowFactory));
            _wordsEqualityComparer = wordsEqualityComparer ?? throw new ArgumentNullException(nameof(wordsEqualityComparer));
            _translationEntryRepository = translationEntryRepository ?? throw new ArgumentNullException(nameof(translationEntryRepository));
            _viewModelAdapter = viewModelAdapter ?? throw new ArgumentNullException(nameof(viewModelAdapter));
            _lifetimeScope = lifetimeScope ?? throw new ArgumentNullException(nameof(lifetimeScope));
            _translationDetailsRepository = translationDetailsRepository ?? throw new ArgumentNullException(nameof(translationDetailsRepository));
            _wordsProcessor = wordsProcessor ?? throw new ArgumentNullException(nameof(wordsProcessor));

            FavoriteCommand = new CorrelationCommand<TranslationEntryViewModel>(Favorite);
            DeleteCommand = new CorrelationCommand<TranslationEntryViewModel>(Delete);
            OpenDetailsCommand = new CorrelationCommand<TranslationEntryViewModel>(OpenDetailsAsync);
            OpenSettingsCommand = new CorrelationCommand(OpenSettings);
            SearchCommand = new CorrelationCommand<string>(Search);

            Logger.Info("Starting...");

            _translationList = new ObservableCollection<TranslationEntryViewModel>();
            _translationList.CollectionChanged += TranslationList_CollectionChanged;
            View = CollectionViewSource.GetDefaultView(_translationList);

            BindingOperations.EnableCollectionSynchronization(_translationList, _lockObject);

            Logger.Trace("Creating NextCardShowTime update timer...");

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Start();
            _timer.Tick += Timer_Tick;

            Logger.Trace("Subscribing to the events...");

            _subscriptionTokens.Add(messenger.Subscribe<TranslationInfo>(OnTranslationInfoReceived));
            _subscriptionTokens.Add(messenger.Subscribe<TranslationInfo[]>(OnTranslationInfosBatchReceived));
            _subscriptionTokens.Add(messenger.Subscribe<CultureInfo>(OnUiLanguageChanged));
            _subscriptionTokens.Add(messenger.Subscribe<PriorityWordKey>(OnPriorityChangedAsync));

            Logger.Trace("Receiving translations...");

            LoadTranslationsAsync();

            Logger.Info("Started");
        }

        [NotNull]
        public EditManualTranslationsViewModel EditManualTranslationsViewModel { get; }

        protected override IWindow Window => _dictionaryWindowFactory.GetWindowIfExists();

        [NotNull]
        public ICollectionView View { get; }

        public int Count { get; private set; }

        [CanBeNull]
        public string SearchText { get; set; }

        [NotNull]
        public ICommand FavoriteCommand { get; }

        [NotNull]
        public ICommand DeleteCommand { get; }

        [NotNull]
        public ICommand OpenDetailsCommand { get; }

        [NotNull]
        public ICommand OpenSettingsCommand { get; }

        [NotNull]
        public ICommand SearchCommand { get; }

        protected override void Cleanup()
        {
            _translationList.CollectionChanged -= TranslationList_CollectionChanged;
            _timer.Tick -= Timer_Tick;
            _timer.Stop();
            foreach (var subscriptionToken in _subscriptionTokens)
            {
                _messenger.UnSubscribe(subscriptionToken);
            }
        }

        private void Delete([NotNull] TranslationEntryViewModel translationEntryViewModel)
        {
            // TODO: prompt
            Logger.Trace($"Deleting {translationEntryViewModel} from the list...");
            if (translationEntryViewModel == null)
            {
                throw new ArgumentNullException(nameof(translationEntryViewModel));
            }

            bool deleted;
            lock (_lockObject)
            {
                deleted = _translationList.Remove(translationEntryViewModel);
            }

            translationEntryViewModel.TextChanged -= TranslationEntryViewModel_TextChangedAsync;
            if (!deleted)
            {
                Logger.Warn($"{translationEntryViewModel} is not deleted from the list");
            }
            else
            {
                Logger.Trace($"{translationEntryViewModel} has been deleted from the list");
            }
        }

        private void Favorite([NotNull] TranslationEntryViewModel translationEntryViewModel)
        {
            var text = translationEntryViewModel.IsFavorited
                ? "Unfavoriting"
                : "Favoriting";
            Logger.Trace($"{text} {translationEntryViewModel}...");
            translationEntryViewModel.IsFavorited = !translationEntryViewModel.IsFavorited;
            var translationEntry = _viewModelAdapter.Adapt<TranslationEntry>(translationEntryViewModel);
            _translationEntryRepository.Update(translationEntry);
        }

        private async void LoadTranslationsAsync()
        {
            var pageNumber = 0;
            while (true)
            {
                var result = await LoadTranslationsPageAsync(pageNumber++)
                    .ConfigureAwait(false);
                if (!result)
                {
                    break;
                }
            }
        }

        private async Task<bool> LoadTranslationsPageAsync(int pageNumber)
        {
            return await Task.Run(
                    () =>
                    {
                        if (CancellationTokenSource.IsCancellationRequested)
                        {
                            return false;
                        }

                        Logger.Trace($"Receiving translations page {pageNumber}...");
                        var translationEntryViewModels = _viewModelAdapter.Adapt<TranslationEntryViewModel[]>(_translationEntryRepository.GetPage(pageNumber, PageSize, null, SortOrder.Descending));
                        if (!translationEntryViewModels.Any())
                        {
                            return false;
                        }

                        lock (_lockObject)
                        {
                            foreach (var translationEntryViewModel in translationEntryViewModels)
                            {
                                _translationList.Add(translationEntryViewModel);
                            }
                        }

                        Logger.Trace($"{translationEntryViewModels.Length} translations have been received");
                        return true;
                    },
                    CancellationTokenSource.Token)
                .ConfigureAwait(false);
        }

        private async void OnPriorityChangedAsync([NotNull] PriorityWordKey priorityWordKey)
        {
            Logger.Trace($"Changing priority for {priorityWordKey} in the list...");
            if (priorityWordKey == null)
            {
                throw new ArgumentNullException(nameof(priorityWordKey));
            }
            var wordKey = priorityWordKey.WordKey;
            var parentId = wordKey.TranslationEntryId;
            TranslationEntryViewModel translationEntryViewModel;
            lock (_lockObject)
            {
                translationEntryViewModel = _translationList.SingleOrDefault(x => x.Id.Equals(parentId));
            }

            if (translationEntryViewModel == null)
            {
                Logger.Warn($"{priorityWordKey} is not found in the list");
                return;
            }

            if (priorityWordKey.IsPriority)
            {
                ProcessPriority(wordKey, translationEntryViewModel);
            }
            else
            {
                await ProcessNonPriorityAsync(wordKey, translationEntryViewModel)
                    .ConfigureAwait(false);
            }
        }

        private void OnTranslationInfoReceived([NotNull] TranslationInfo translationInfo)
        {
            if (translationInfo == null)
            {
                throw new ArgumentNullException(nameof(translationInfo));
            }

            Logger.Trace($"Received {translationInfo} from external source...");
            var translationEntryViewModel = _viewModelAdapter.Adapt<TranslationEntryViewModel>(translationInfo.TranslationEntry);

            TranslationEntryViewModel existing;
            lock (_lockObject)
            {
                existing = _translationList.SingleOrDefault(x => x.Id.Equals(translationInfo.TranslationEntry.Id));
            }

            if (existing != null)
            {
                Logger.Trace($"Updating {existing} in the list...");

                // Prevent text change to fire
                using (existing.SupressNotification())
                {
                    _viewModelAdapter.Adapt(translationInfo.TranslationEntry, existing);
                }

                _syncContext.Post(x => View.MoveCurrentTo(existing), null);
                Logger.Trace($"{existing} has been updated in the list");
            }
            else
            {
                Logger.Trace($"Adding {translationEntryViewModel} to the list...");
                _syncContext.Post(
                    x =>
                    {
                        lock (_lockObject)
                        {
                            _translationList.Insert(0, translationEntryViewModel);
                        }

                        View.MoveCurrentTo(translationEntryViewModel);
                    },
                    null);
                Logger.Trace($"{translationEntryViewModel} has been added to the list...");
            }
        }

        private void OnTranslationInfosBatchReceived([NotNull] TranslationInfo[] translationInfos)
        {
            Logger.Trace($"Received a batch of translations ({translationInfos.Length} items) from external source...");
            foreach (var translationInfo in translationInfos)
            {
                OnTranslationInfoReceived(translationInfo);
            }
        }

        private void OnUiLanguageChanged([NotNull] CultureInfo cultureInfo)
        {
            Logger.Trace($"Changing UI language to {cultureInfo}...");
            if (cultureInfo == null)
            {
                throw new ArgumentNullException(nameof(cultureInfo));
            }

            CultureUtilities.ChangeCulture(cultureInfo);

            lock (_lockObject)
            {
                foreach (var translation in _translationList.SelectMany(translationEntryViewModel => translationEntryViewModel.Translations))
                {
                    translation.ReRender();
                }
            }
        }

        private async void OpenDetailsAsync([NotNull] TranslationEntryViewModel translationEntryViewModel)
        {
            Logger.Trace($"Opening details for {translationEntryViewModel}...");
            if (translationEntryViewModel == null)
            {
                throw new ArgumentNullException(nameof(translationEntryViewModel));
            }

            var translationEntry = _viewModelAdapter.Adapt<TranslationEntry>(translationEntryViewModel);
            var translationDetails = await _wordsProcessor.ReloadTranslationDetailsIfNeededAsync(
                    translationEntry.Id,
                    translationEntry.Key.Text,
                    translationEntry.Key.SourceLanguage,
                    translationEntry.Key.TargetLanguage,
                    translationEntry.ManualTranslations,
                    CancellationTokenSource.Token)
                .ConfigureAwait(false);
            var translationInfo = new TranslationInfo(translationEntry, translationDetails);
            var nestedLifeTimeScope = _lifetimeScope.BeginLifetimeScope();
            var translationDetailsCardViewModel = nestedLifeTimeScope.Resolve<TranslationDetailsCardViewModel>(new TypedParameter(typeof(TranslationInfo), translationInfo));
            var dictionaryWindow = nestedLifeTimeScope.Resolve<WindowFactory<IDictionaryWindow>>()
                .GetWindow();
            var detailsWindow = nestedLifeTimeScope.Resolve<ITranslationDetailsCardWindow>(
                new TypedParameter(typeof(Window), dictionaryWindow),
                new TypedParameter(typeof(TranslationDetailsCardViewModel), translationDetailsCardViewModel));
            detailsWindow.AssociateDisposable(nestedLifeTimeScope);
            detailsWindow.Show();
        }

        private void OpenSettings()
        {
            Logger.Trace("Opening settings...");
            var nestedLifeTimeScope = _lifetimeScope.BeginLifetimeScope();
            var dictionaryWindow = nestedLifeTimeScope.Resolve<WindowFactory<IDictionaryWindow>>()
                .GetWindow();
            dictionaryWindow.AssociateDisposable(nestedLifeTimeScope);
            var dictionaryWindowParameter = new TypedParameter(typeof(Window), dictionaryWindow);
            var windowFactory = nestedLifeTimeScope.Resolve<WindowFactory<ISettingsWindow>>();
            windowFactory.ShowWindow(dictionaryWindowParameter);
        }

        private async Task ProcessNonPriorityAsync([NotNull] WordKey wordKey, [NotNull] TranslationEntryViewModel translationEntryViewModel)
        {
            var translations = translationEntryViewModel.Translations;
            Logger.Trace("Removing non-priority word from the list...");
            for (var i = 0; i < translations.Count; i++)
            {
                var translation = translations[i];

                if (_wordsEqualityComparer.Equals(translation, wordKey))
                {
                    Logger.Trace($"Removing {translation} from the list...");
                    translations.RemoveAt(i--);
                }
            }

            if (!translations.Any())
            {
                Logger.Trace("No more translations left in the list. Restoring default...");
                await translationEntryViewModel.ReloadNonPriorityAsync()
                    .ConfigureAwait(false);
            }
        }

        private void ProcessPriority([NotNull] WordKey wordKey, [NotNull] TranslationEntryViewModel translationEntryViewModel)
        {
            var translations = translationEntryViewModel.Translations;
            Logger.Trace($"Removing all non-priority translations for {translationEntryViewModel} except the current...");
            var found = false;
            for (var i = 0; i < translations.Count; i++)
            {
                var translation = translations[i];
                if (_wordsEqualityComparer.Equals(translation, wordKey))
                {
                    if (!translation.IsPriority)
                    {
                        Logger.Debug($"Found {wordKey} in the list. Marking as priority...");
                        translation.IsPriority = true;
                    }
                    else
                    {
                        Logger.Trace($"Found {wordKey} in the list but it is already priority");
                    }

                    found = true;
                }

                if (!translation.IsPriority)
                {
                    translations.RemoveAt(i--);
                }
            }

            if (!found)
            {
                Logger.Trace($"Not found {wordKey} in the list. Adding...");
                var copy = _viewModelAdapter.Adapt<PriorityWordViewModel>(wordKey);
                translations.Add(copy);
            }
        }

        private void Search([CanBeNull] string text)
        {
            Logger.Trace($"Searching for {text}...");
            if (string.IsNullOrWhiteSpace(text))
            {
                View.Filter = null;
            }
            else
            {
                View.Filter = o => string.IsNullOrWhiteSpace(text) || ((TranslationEntryViewModel)o).Text.IndexOf(text, StringComparison.InvariantCultureIgnoreCase) >= 0;
            }

            _filterChanged = true;
            //Count = View.Cast<object>().Count();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            foreach (var translation in _translationList)
            {
                var time = translation.NextCardShowTime;
                translation.NextCardShowTime = time.AddTicks(1); // To launch converter
            }

            UpdateCount();
        }

        private void UpdateCount()
        {
            if (!_filterChanged && _count == _lastRecordedCount)
            {
                return;
            }

            _filterChanged = false;
            _lastRecordedCount = _count;

            Count = View.Filter == null
                ? _count
                : View.Cast<object>()
                    .Count();
        }

        private async void TranslationEntryViewModel_TextChangedAsync([NotNull] object sender, [NotNull] TextChangedEventArgs e)
        {
            var translationEntryViewModel = (TranslationEntryViewModel)sender;
            Logger.Info($"Changing translation's text for {translationEntryViewModel} to {e.NewValue}...");

            var sourceLanguage = translationEntryViewModel.Language;
            var targetLanguage = translationEntryViewModel.TargetLanguage;
            if (e.NewValue != null)
            {
                try
                {
                    await WordsProcessor.AddOrChangeWordAsync(
                            e.NewValue,
                            CancellationTokenSource.Token,
                            sourceLanguage,
                            targetLanguage,
                            Window,
                            id: translationEntryViewModel.Id,
                            manualTranslations: translationEntryViewModel.ManualTranslations)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Prevent text change to fire
                    using (translationEntryViewModel.SupressNotification())
                    {
                        translationEntryViewModel.Text = e.OldValue ?? "";
                    }

                    throw;
                }
            }
        }

        private void TranslationList_CollectionChanged([NotNull] object sender, [NotNull] NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Remove:
                    foreach (TranslationEntryViewModel translationEntryViewModel in e.OldItems)
                    {
                        Interlocked.Decrement(ref _count);
                        _translationDetailsRepository.Delete(translationEntryViewModel.Id);
                        _translationEntryRepository.Delete(translationEntryViewModel.Id);
                        _wordPriorityRepository.ClearForTranslationEntry(translationEntryViewModel.Id);
                    }

                    break;
                case NotifyCollectionChangedAction.Add:
                    foreach (TranslationEntryViewModel translationEntryViewModel in e.NewItems)
                    {
                        translationEntryViewModel.TextChanged += TranslationEntryViewModel_TextChangedAsync;
                        Interlocked.Increment(ref _count);
                    }

                    break;
            }
        }
    }
}