using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Model;

public enum HttpVerb { Get, Post, Put, Patch, Delete }

/// <summary>When «Petición web» shows the answer on screen.</summary>
public enum HttpResultDisplay { Never, OnError, Always }

/// <summary>
/// Sends an HTTP request without opening the browser: Home Assistant, the ComfyUI API, ntfy, a service of your own…
/// </summary>
public partial class HttpRequestAction : CommandAction
{
    [ObservableProperty] private HttpVerb _method = HttpVerb.Get;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _body = string.Empty;

    /// <summary>Type of the body; empty = application/json.</summary>
    [ObservableProperty] private string _contentType = string.Empty;

    [ObservableProperty] private int _timeoutSeconds = 10;
    [ObservableProperty] private HttpResultDisplay _showResult = HttpResultDisplay.OnError;

    /// <summary>Accepts any certificate (local services with a certificate of their own).</summary>
    [ObservableProperty] private bool _ignoreCertificateErrors;

    /// <summary>Answer of the «Probar» button of the editor (not saved).</summary>
    [ObservableProperty]
    [property: JsonIgnore] private string? _testResult;

    [ObservableProperty]
    [property: JsonIgnore] private bool _testing;

    public ObservableCollection<HttpHeader> Headers
    {
        get;
        set
        {
            if (field is not null)
            {
                field.CollectionChanged -= OnHeadersChanged;
                foreach (var h in field) h.PropertyChanged -= OnHeaderChanged;
            }
            field = value ?? [];
            field.CollectionChanged += OnHeadersChanged;
            foreach (var h in field) h.PropertyChanged += OnHeaderChanged;
        }
    } = [];

    public HttpRequestAction() => Headers.CollectionChanged += OnHeadersChanged;

    private void OnHeadersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (HttpHeader h in e.OldItems) h.PropertyChanged -= OnHeaderChanged;
        if (e.NewItems is not null) foreach (HttpHeader h in e.NewItems) h.PropertyChanged += OnHeaderChanged;
        OnPropertyChanged(nameof(Headers));
    }

    private void OnHeaderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HttpHeader.Unreadable)) OnPropertyChanged(nameof(Headers));
    }

    public override bool IsTransient(string? property) => property is nameof(TestResult) or nameof(Testing) || base.IsTransient(property);

    /// <summary>The answer's text.</summary>
    public override bool GivesResult => true;

    public override ActionKind Kind => ActionKind.HttpRequest;

    /// <summary>«POST ha.local/api/services/light/turn_off»: never the headers (they may carry a token).</summary>
    public override string Summary
    {
        get
        {
            var url = Url.Trim();
            var scheme = url.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) url = url[(scheme + 3)..];
            return Method.ToString().ToUpperInvariant() + " " + Or(url, "summary.noUrl");
        }
    }
}

/// <summary>
/// A header of «Petición web». A secret one (a token) is saved encrypted for this Windows user (see
/// <see cref="Secrets"/>): a copy restored on another PC can't read it and it has to be typed again there.
/// </summary>
public partial class HttpHeader : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Unreadable))]
    [property: JsonIgnore] private string _value = string.Empty;

    [ObservableProperty] private bool _secret;

    /// <summary>Encrypted value that couldn't be read here (another PC or user): kept as it was until it is retyped.</summary>
    private string? _unreadable;

    [JsonIgnore] public bool Unreadable => _unreadable is not null && Value.Length == 0;

    [JsonPropertyName("value")]
    public string? PlainValue
    {
        get => Secret ? null : Value;
        set { if (value is not null) Value = value; }
    }

    [JsonPropertyName("protected")]
    public string? ProtectedValue
    {
        get => !Secret ? null
            : Value.Length > 0 ? Secrets.Protect(Value)
            : _unreadable ?? string.Empty;
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            if (Secrets.Unprotect(value) is { } plain) Value = plain;
            else
            {
                _unreadable = value;
                Log.Warn($"No se puede descifrar la cabecera «{Name}» (se guardó en otro PC o con otro usuario): hay que volver a escribirla");
                OnPropertyChanged(nameof(Unreadable));
            }
        }
    }
}
