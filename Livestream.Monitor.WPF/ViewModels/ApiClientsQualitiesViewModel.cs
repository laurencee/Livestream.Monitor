using System.Linq;
using Caliburn.Micro;
using Livestream.Monitor.Core.Settings;

namespace Livestream.Monitor.WPF.ViewModels;

public class ApiClientsQualitiesViewModel : PropertyChangedBase
{
    private readonly StreamQualityDraft draft;
    private bool loading;

    public ApiClientsQualitiesViewModel() : this(new Settings()) { }

    public ApiClientsQualitiesViewModel(Settings settings)
    {
        draft = new StreamQualityDraft(settings);

        foreach (var (apiName, displayName) in StreamQualityDraft.Platforms)
        {
            var editor = new ApiClientQualitiesViewModel { DisplayName = displayName };
            editor.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ApiClientQualitiesViewModel.FallbackQuality)) Changed(apiName, editor);
            };

            editor.Qualities.CollectionChanged += (_, _) => Changed(apiName, editor);
            Items.Add(editor);
        }

        Reload();
        SelectedPlatform = Items[0];
    }

    public BindableCollection<ApiClientQualitiesViewModel> Items { get; } = new();

    public ApiClientQualitiesViewModel SelectedPlatform
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;

            NotifyOfPropertyChange(() => ShowOverrideNote);
        }
    }

    public bool HasChanges => draft.HasChanges;

    public bool ShowOverrideNote => SelectedPlatform != Items[0] && draft.HasAllPreferences;

    public void SaveChanges()
    {
        draft.SaveChanges();
        NotifyOfPropertyChange(() => HasChanges);
    }

    public void Reload()
    {
        loading = true;
        draft.Revert();

        for (var i = 0; i < Items.Count; i++)
        {
            var saved = draft[StreamQualityDraft.Platforms[i].ApiName];
            Items[i].Qualities.Clear();
            Items[i].Qualities.AddRange(saved.Qualities);
            Items[i].FallbackQuality = saved.FallbackQuality;
            Items[i].NewQuality = "";
        }

        loading = false;
        UpdateHints();
        NotifyOfPropertyChange(() => HasChanges);
        NotifyOfPropertyChange(() => ShowOverrideNote);
    }

    private void Changed(string apiName, ApiClientQualitiesViewModel editor)
    {
        if (loading) return;

        var target = draft[apiName];
        target.Qualities = editor.Qualities.ToList();
        target.FallbackQuality = editor.FallbackQuality;
        UpdateHints();
        NotifyOfPropertyChange(() => HasChanges);
        NotifyOfPropertyChange(() => ShowOverrideNote);
    }

    private void UpdateHints()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            var apiName = StreamQualityDraft.Platforms[i].ApiName;
            var inheritsAll = apiName != Settings.AllStreamQualitiesKey && draft.HasAllPreferences && !draft[apiName].IsConfigured;
            Items[i].EmptyQualityHint = inheritsAll ? "Using 'All' settings." : "[Using the fallback quality]";
        }
    }
}
