using System.Windows.Input;

namespace RacerRemote.Controls;

public sealed class HoldButton : ContentView
{
    public static readonly BindableProperty PressedCommandProperty = BindableProperty.Create(
        nameof(PressedCommand),
        typeof(ICommand),
        typeof(HoldButton));

    public static readonly BindableProperty ReleasedCommandProperty = BindableProperty.Create(
        nameof(ReleasedCommand),
        typeof(ICommand),
        typeof(HoldButton));

    public static readonly BindableProperty IsPressedProperty = BindableProperty.Create(
        nameof(IsPressed),
        typeof(bool),
        typeof(HoldButton),
        false,
        BindingMode.TwoWay);

    private readonly ContentPresenter _presenter;
    private readonly GraphicsView _touch;

    public ICommand? PressedCommand
    {
        get => (ICommand?)GetValue(PressedCommandProperty);
        set => SetValue(PressedCommandProperty, value);
    }

    public ICommand? ReleasedCommand
    {
        get => (ICommand?)GetValue(ReleasedCommandProperty);
        set => SetValue(ReleasedCommandProperty, value);
    }

    public bool IsPressed
    {
        get => (bool)GetValue(IsPressedProperty);
        set => SetValue(IsPressedProperty, value);
    }

    public HoldButton()
    {
        _presenter = new ContentPresenter
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        _touch = new GraphicsView
        {
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        _touch.StartInteraction += (_, __) => BeginPress();
        _touch.EndInteraction += (_, __) => EndPress();
        _touch.CancelInteraction += (_, __) => EndPress();

        var grid = new Grid();
        grid.Children.Add(_presenter);
        grid.Children.Add(_touch);

        base.Content = grid;

        Loaded += (_, __) => SyncAppearance(false);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == IsPressedProperty.PropertyName)
        {
            SyncAppearance(true);
        }
    }

    protected override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);

        // When used from XAML, the first direct child becomes the visual content.
        if (child is View v && v != base.Content)
        {
            _presenter.Content = v;
        }
    }

    private void BeginPress()
    {
        if (IsPressed)
        {
            return;
        }

        IsPressed = true;
        PressedCommand?.Execute(null);
        SyncAppearance(true);
    }

    private void EndPress()
    {
        if (!IsPressed)
        {
            return;
        }

        IsPressed = false;
        ReleasedCommand?.Execute(null);
        SyncAppearance(true);
    }

    private void SyncAppearance(bool animate)
    {
        var targetScale = IsPressed ? 0.96 : 1.0;
        var targetOpacity = IsPressed ? 0.85 : 1.0;

        if (!animate)
        {
            Scale = targetScale;
            Opacity = targetOpacity;
            return;
        }

        this.AbortAnimation("HoldButtonScale");
        this.AbortAnimation("HoldButtonOpacity");

        _ = this.ScaleTo(targetScale, 80, Easing.CubicOut);
        _ = this.FadeTo(targetOpacity, 80, Easing.CubicOut);
    }
}
