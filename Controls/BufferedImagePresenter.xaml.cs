using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CrossingVoidZDTool.Controls;

public sealed partial class BufferedImagePresenter : UserControl
{
    private bool _primaryIsVisible = true;
    private bool _hasPreparedSource;
    private ImageSource? _preparedSource;

    public BufferedImagePresenter()
    {
        InitializeComponent();
    }

    public void Show(ImageSource? source)
    {
        if (ShowPrepared(source))
        {
            return;
        }

        Prepare(source);
        _ = ShowPrepared(source);
    }

    public void Prepare(ImageSource? source)
    {
        var visibleImage = _primaryIsVisible ? PrimaryImage : SecondaryImage;
        _preparedSource = source;
        _hasPreparedSource = true;
        if (ReferenceEquals(visibleImage.Source, source))
        {
            return;
        }

        var nextImage = _primaryIsVisible ? SecondaryImage : PrimaryImage;
        if (!ReferenceEquals(nextImage.Source, source))
        {
            nextImage.Source = source;
        }
    }

    public bool ShowPrepared(ImageSource? source)
    {
        var visibleImage = _primaryIsVisible ? PrimaryImage : SecondaryImage;
        if (ReferenceEquals(visibleImage.Source, source))
        {
            _hasPreparedSource = false;
            _preparedSource = null;
            return true;
        }

        if (!_hasPreparedSource || !ReferenceEquals(_preparedSource, source))
        {
            return false;
        }

        var nextImage = _primaryIsVisible ? SecondaryImage : PrimaryImage;
        nextImage.Opacity = 1;
        visibleImage.Opacity = 0;
        _primaryIsVisible = !_primaryIsVisible;
        _hasPreparedSource = false;
        _preparedSource = null;
        return true;
    }

    public void Clear()
    {
        PrimaryImage.Source = null;
        SecondaryImage.Source = null;
        PrimaryImage.Opacity = 1;
        SecondaryImage.Opacity = 0;
        _primaryIsVisible = true;
        _hasPreparedSource = false;
        _preparedSource = null;
    }
}
