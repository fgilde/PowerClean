using System.Windows.Data;
using System.Windows.Markup;

namespace Cleaner.App.Localization;

/// <summary>
/// XAML-Markup für übersetzte Strings: <c>{l:T Key=Dashboard.QuickScan}</c> oder kurz
/// <c>{l:T Dashboard.QuickScan}</c>. Bindet auf <see cref="L.Current"/>[Key] mit
/// OneWay-Update bei Sprachwechsel.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TExtension() { }
    public TExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = L.Current,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
