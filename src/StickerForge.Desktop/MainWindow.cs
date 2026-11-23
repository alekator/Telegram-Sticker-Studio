using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using StickerForge.Core;
using StickerForge.Infrastructure;
using StickerForge.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls.Templates;
using SkiaSharp;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Styling;
using System.Globalization;

namespace StickerForge.Desktop;

public sealed class MainWindow : Window
{
    private readonly ProjectStore store = new();
    private readonly FfmpegService ffmpeg = FfmpegLocator.CreateDefault();
    private readonly ImageImporter importer;
    private readonly ListBox gallery = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBox packName = new() { PlaceholderText = "Название набора", Width = 180 };
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock details = new() { Text = "Выберите стикер", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { Text = "Готово" };
    private readonly TextBlock selectionCount = new() { Margin = new Thickness(4, 0, 4, 8), Opacity = .8 };
    private Button? exportButton;
    private Button? applySelectedButton;
    private ContextMenu? galleryMenu;
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Width = 150, Height = 8 };
    private readonly Dictionary<Guid, ImageInfo> cache = [];
    private StickerProject project = new();
    private string? projectPath;
    private bool dirty;
    private bool refreshing;
    private bool busy;
    private CancellationTokenSource? importing;
    private long previewGeneration;
    private Bitmap? previewBitmap;
    private readonly Button gifPlay = new() { Content = "Воспроизвести", IsVisible = false };
    private CancellationTokenSource? gifPlayback;
    private string? gifPath;
    private int[]? gifDurations;
    private int gifFrame;
    private readonly Button videoPlay = new() { Content = "Воспроизвести", IsVisible = false };
    private readonly Slider videoSeek = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    private readonly TextBlock videoTime = new() { IsVisible = false };
    private CancellationTokenSource? videoPlayback;
    private CancellationTokenSource? videoSeekRequest;
    private string? videoPath;
    private VideoMetadata? videoMetadata;
    private bool updatingSeek;
    private readonly Border dropZone = new() { BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };
    private readonly TextBlock galleryHint = new() { Text = "Перетащите файлы или папки сюда либо нажмите «Добавить файлы»", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 8) };
    private readonly ComboBox fitMode = new() { ItemsSource = new[] { "Вместить целиком", "Заполнить с обрезкой", "Растянуть (искажает пропорции)", "Вручную" }, SelectedIndex = 0 };
    private readonly Slider scale = new() { Minimum = .1, Maximum = 3, Value = 1 };
    private readonly Slider offsetX = new() { Minimum = -512, Maximum = 512 };
    private readonly Slider offsetY = new() { Minimum = -512, Maximum = 512 };
    private readonly Slider rotation = new() { Minimum = -180, Maximum = 180 };
    private readonly CheckBox flipHorizontal = new() { Content = "По горизонтали" };
    private readonly CheckBox flipVertical = new() { Content = "По вертикали" };
    private readonly NumericUpDown fragmentStart = new() { Minimum = 0, Maximum = 36000, Increment = .05m, FormatString = "0.00", Width = 110, MinWidth = 110, ShowButtonSpinner = false, Height = 34 };
    private readonly NumericUpDown fragmentDuration = new() { Minimum = .01m, Maximum = 3, Increment = .05m, Value = 3, FormatString = "0.00", Width = 110, MinWidth = 110, ShowButtonSpinner = false, Height = 34 };
    private readonly TextBlock scaleLabel = new();
    private readonly TextBlock rotationLabel = new();
    private readonly TextBlock offsetXLabel = new();
    private readonly TextBlock offsetYLabel = new();
    private readonly StackPanel animationPanel = new() { Spacing = 4 };
    private readonly AnimationTimeline timeline = new();
    private readonly TextBlock sourceDurationLabel = new();
    private double animationDuration;
    private bool updatingFragment;
    private readonly Border animationBar = new() { IsVisible = false };
    private readonly StackPanel groupPanel = new() { IsVisible = false, Spacing = 8 };
    private readonly TextBlock groupDescription = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Opacity = .8 };
    private bool updatingTransform;
    private PointerPressedEventArgs? dragPress;
    private Point dragStart;
    private StickerItem[]? dragItems;
    private ListBoxItem? insertionCard;
    private bool insertAfter;
    private StickerItem[]? pressedSelection;
    private double scrollDirection;
    private readonly DispatcherTimer dragScroll = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Point? previewDragStart;
    private double previewStartX, previewStartY;
    private readonly PanelLayoutStore layoutStore;
    private readonly UiPreferencesStore preferencesStore;
    private readonly StudioTheme studioTheme = new();
    private UiPreferences preferences;
    private PanelLayout layout = new();
    private Grid? workspaceGrid;
    private Control? galleryPanel;
    private Control? propertiesSide;
    private GridSplitter? gallerySplitter;
    private GridSplitter? propertiesSplitter;
    private Button? galleryVisibilityButton;
    private Button? propertiesVisibilityButton;
    private Viewbox? previewViewbox;
    private Border? previewCanvas;
    private readonly TextBlock emptyCanvas = new() { Text = "Перетащите файлы сюда или нажмите «Добавить файлы»", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock emptyProperties = new() { Text = "Выберите стикер для редактирования", TextWrapping = TextWrapping.Wrap };
    private StackPanel? editingProperties;
    private Control? editorPage;
    private Control? settingsPage;
    private ContextMenu? projectMenu;
    private Button? settingsButton;

    public MainWindow(PanelLayoutStore? layoutStore = null, UiPreferencesStore? preferencesStore = null)
    {
        this.layoutStore = layoutStore ?? new();
        this.preferencesStore = preferencesStore ?? new(); preferences = this.preferencesStore.Load();
        studioTheme.Apply(preferences.Theme == "light");
        importer = new ImageImporter(new SkiaImageDecoder(), ffmpeg);
        Title = "StickerForge Studio";
        FontSize = 14;
        Styles.Add(new Style(x => x.OfType<Button>()) { Setters = { new Setter(MinHeightProperty, 38d), new Setter(PaddingProperty, new Thickness(12, 6)), new Setter(Avalonia.Controls.Button.CornerRadiusProperty, new CornerRadius(10)), new Setter(BackgroundProperty, studioTheme.Surface), new Setter(ForegroundProperty, studioTheme.Text) } });
        Styles.Add(new Style(x => x.OfType<TextBox>()) { Setters = { new Setter(MinHeightProperty, 34d), new Setter(PaddingProperty, new Thickness(8, 5)), new Setter(BackgroundProperty, studioTheme.Panel), new Setter(BorderBrushProperty, studioTheme.Border), new Setter(ForegroundProperty, studioTheme.Text) } });
        Width = 1280; Height = 760; MinWidth = 700; MinHeight = 520;
        Background = studioTheme.Background; Foreground = studioTheme.Text; Resources["SystemAccentColor"] = studioTheme.Accent.Color;
        Resources["SystemControlHighlightAccentBrush"] = studioTheme.Accent;
        Resources["SystemControlHighlightListAccentLowBrush"] = studioTheme.Selected;
        layout = this.layoutStore.Load();
        var root = new DockPanel { Margin = new Thickness(8) };
        projectMenu = new ContextMenu();
        projectMenu.Items.Add(MenuAction("Новый проект", NewAsync));
        projectMenu.Items.Add(MenuAction("Сохранить как…", async () => { await SaveAsync(true); }));
        projectMenu.Items.Add(MenuAction("Добавить папку…", PickFolderAsync));
        projectMenu.Items.Add(MenuAction("О программе", ShowAboutAsync));
        StudioLocalization.Capture(projectMenu);
        var top = new DockPanel { LastChildFill = false };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) };
        brand.Children.Add(new Border { Background = studioTheme.Accent, CornerRadius = new CornerRadius(8), Padding = new Thickness(7), Child = Icon("M3 12 L21 4 L16 20 L12 14 Z M12 14 L21 4", 20, Brushes.White) });
        var brandName = new TextBlock { Text = "StickerForge Studio", FontWeight = FontWeight.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(brandName);
        DockPanel.SetDock(brand, Dock.Left); top.Children.Add(brand);
        var addAction = ActionButton("M12 4 L12 20 M4 12 L20 12", "Добавить", PickFilesAsync, "Добавить файлы · Ctrl+I"); addAction.Name = "AddFilesAction";
        var openAction = ActionButton("M3 7 L9 7 L11 9 L21 9 L19 19 L3 19 Z M3 7 L3 5 L9 5 L11 7", "Открыть", OpenAsync, "Открыть проект"); openAction.Name = "OpenProjectAction";
        var saveAction = ActionButton("M4 3 L18 3 L21 6 L21 21 L3 21 L3 3 Z M7 3 L7 9 L17 9 L17 3 M7 21 L7 14 L17 14 L17 21", "Сохранить", async () => { await SaveAsync(false); }, "Сохранить проект · Ctrl+S"); saveAction.Name = "SaveProjectAction";
        DockPanel.SetDock(addAction, Dock.Left); top.Children.Add(addAction);
        DockPanel.SetDock(openAction, Dock.Left); top.Children.Add(openAction);
        DockPanel.SetDock(saveAction, Dock.Left); top.Children.Add(saveAction);
        var projectButton = Button("⋯", () => Task.CompletedTask, "Другие действия"); projectButton.Name = "MoreActions";
        projectButton.ContextMenu = projectMenu; projectButton.Click += (_, _) => projectMenu.Open(projectButton);
        DockPanel.SetDock(projectButton, Dock.Left); top.Children.Add(projectButton);
        packName.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(packName, Dock.Left); top.Children.Add(packName);
        settingsButton = IconButton("M12 3 L14 4 L15 6 L18 6 L20 9 L19 11 L20 14 L18 17 L15 17 L14 20 L10 20 L9 17 L6 17 L4 14 L5 12 L4 9 L6 6 L9 6 L10 3 Z M9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12", ShowSettingsAsync, "Настройки"); settingsButton.Name = "SettingsAction";
        DockPanel.SetDock(settingsButton, Dock.Right); top.Children.Add(settingsButton);
        void AdaptToolbar() { var compact = ClientSize.Width < 920; brandName.IsVisible = !compact; packName.Width = compact ? 100 : 180; }
        SizeChanged += (_, _) => AdaptToolbar(); AdaptToolbar();
        exportButton = Button("Экспортировать стикеры", ExportAsync, "Подготовить файлы для Telegram");
        exportButton.Background = studioTheme.Accent; exportButton.Foreground = Brushes.White;
        var toolbar = new Border { Child = top, Background = studioTheme.Panel, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5), Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
        DockPanel.SetDock(exportButton, Dock.Right); bottom.Children.Add(exportButton);
        var cancelButton = Button("Отменить", () => { importing?.Cancel(); return Task.CompletedTask; }, "Отменить текущую операцию");
        DockPanel.SetDock(cancelButton, Dock.Right); bottom.Children.Add(cancelButton);
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center }; statusRow.Children.Add(status); statusRow.Children.Add(progress); bottom.Children.Add(statusRow);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("320,6,*,6,280") };
        workspaceGrid = columns;
        columns.ColumnDefinitions[0].MinWidth = 180; columns.ColumnDefinitions[0].MaxWidth = 600;
        columns.ColumnDefinitions[2].MinWidth = 320;
        columns.ColumnDefinitions[4].MinWidth = 270; columns.ColumnDefinitions[4].MaxWidth = 500;
        var left = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
        var actions = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var removeButton = IconButton("M4 6 L20 6 M9 6 L9 4 L15 4 L15 6 M6 6 L7 20 L17 20 L18 6 M10 9 L10 17 M14 9 L14 17", RemoveSelected, "Удалить выделенные");
        DockPanel.SetDock(removeButton, Dock.Right); actions.Children.Add(removeButton);
        var addGallery = Button("＋  Добавить файлы", PickFilesAsync); addGallery.Background = studioTheme.Accent; addGallery.Foreground = Brushes.White; addGallery.HorizontalContentAlignment = HorizontalAlignment.Center; actions.Children.Add(addGallery);
        DockPanel.SetDock(actions, Dock.Top); left.Children.Add(actions);
        DockPanel.SetDock(galleryHint, Dock.Top); left.Children.Add(galleryHint);
        DockPanel.SetDock(selectionCount, Dock.Top); left.Children.Add(selectionCount);
        gallery.SelectionChanged += async (_, _) => { if (!refreshing) { UpdateSelection(); await SelectAsync(); } };
        gallery.AddHandler(InputElement.PointerPressedEvent, OnGalleryPointerPressed, RoutingStrategies.Tunnel, true);
        gallery.AddHandler(InputElement.PointerMovedEvent, StartGalleryDrag, RoutingStrategies.Tunnel, true);
        gallery.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { dragPress = null; pressedSelection = null; }, RoutingStrategies.Tunnel, true);
        dragScroll.Tick += (_, _) =>
        {
            var scroller = gallery.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is not null && scrollDirection != 0) scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, scroller.Offset.Y + scrollDirection * 16));
        };
        gallery.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel());
        gallery.Background = studioTheme.Panel; gallery.BorderThickness = new Thickness(0);
        gallery.Resources["SystemControlHighlightListAccentLowBrush"] = studioTheme.Selected;
        gallery.Resources["SystemControlHighlightListAccentMediumBrush"] = studioTheme.Selected;
        gallery.Resources["SystemControlHighlightListAccentHighBrush"] = studioTheme.Accent;
        gallery.Resources["SystemControlHighlightListLowBrush"] = studioTheme.Surface;
        gallery.Resources["SystemControlHighlightListMediumBrush"] = studioTheme.Selected;
        ScrollViewer.SetHorizontalScrollBarVisibility(gallery, ScrollBarVisibility.Disabled);
        var menu = new ContextMenu();
        galleryMenu = menu;
        var remove = new MenuItem { Header = "Удалить выделенные" };
        remove.Click += async (_, _) => await RemoveSelected();
        var earlier = new MenuItem { Header = "Переместить выше" };
        earlier.Click += async (_, _) => await MoveSelected(-1);
        var later = new MenuItem { Header = "Переместить ниже" };
        later.Click += async (_, _) => await MoveSelected(1);
        menu.Items.Add(remove); menu.Items.Add(earlier); menu.Items.Add(later);
        StudioLocalization.Capture(menu);
        gallery.ContextMenu = menu;
        dropZone.Child = gallery; dropZone.Background = studioTheme.Panel; dropZone.CornerRadius = new CornerRadius(14); dropZone.Padding = new Thickness(6);
        DragDrop.SetAllowDrop(dropZone, true);
        dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, true);
        dropZone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { dropZone.BorderBrush = Brushes.Transparent; ClearInsertion(); scrollDirection = 0; });
        dropZone.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, true);
        left.Children.Add(dropZone);
        galleryPanel = left;
        Grid.SetColumn(left, 0); columns.Children.Add(left);
        gallerySplitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Center, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = studioTheme.Border };
        ToolTip.SetTip(gallerySplitter, "Изменить ширину галереи"); Grid.SetColumn(gallerySplitter, 1); columns.Children.Add(gallerySplitter);
        var layers = new Grid { Width = 512, Height = 512, ClipToBounds = true };
        layers.Children.Add(new Checkerboard());
        layers.Children.Add(preview);
        layers.Children.Add(new Border { BorderBrush = StudioTheme.StickerBoundaryOuter, BorderThickness = new Thickness(2), IsHitTestVisible = false,
            Child = new Border { BorderBrush = StudioTheme.StickerBoundaryInner, BorderThickness = new Thickness(1), IsHitTestVisible = false } });
        var stickerBoundary = new Border { Width = 512, Height = 512, Child = layers };
        var viewbox = new Viewbox { Stretch = Stretch.Uniform, Child = stickerBoundary };
        previewViewbox = viewbox;
        var canvasContent = new Grid(); canvasContent.Children.Add(viewbox);
        var welcome = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 14, MaxWidth = 360 };
        var illustration = new Grid { Width = 180, Height = 140, HorizontalAlignment = HorizontalAlignment.Center };
        illustration.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M18 51 L104 28 Q112 26 115 35 L130 103 Q132 112 123 114 L37 132 Q28 134 26 125 L13 62 Q11 54 18 51 Z"), Fill = new SolidColorBrush(Color.Parse("#0754A9")) });
        illustration.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M46 23 L126 9 Q136 8 138 18 L150 94 Q151 103 141 105 L60 119 Q51 120 49 110 L37 34 Q36 25 46 23 Z"), Fill = new SolidColorBrush(Color.Parse("#299BFA")) });
        illustration.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M56 94 L76 57 L91 73 L104 49 L131 81 Z M110 33 A7 7 0 1 0 124 33 A7 7 0 1 0 110 33"), Fill = new SolidColorBrush(Color.Parse("#92D2FF")) });
        illustration.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M128 79 L169 92 Q176 94 173 101 L161 132 Q158 138 151 135 L112 119 Q106 116 109 109 L120 84 Q122 77 128 79 Z"), Fill = studioTheme.Accent });
        illustration.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M133 94 L149 109 L129 114 Z"), Fill = Brushes.White });
        welcome.Children.Add(illustration);
        welcome.Children.Add(new TextBlock { Text = "Создавайте стикеры для Telegram", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 23, FontWeight = FontWeight.Bold });
        welcome.Children.Add(emptyCanvas);
        var addFiles = Button("＋  Добавить файлы", PickFilesAsync); addFiles.Background = studioTheme.Accent; addFiles.Foreground = Brushes.White; addFiles.HorizontalAlignment = HorizontalAlignment.Center; addFiles.MinWidth = 220; addFiles.HorizontalContentAlignment = HorizontalAlignment.Center; welcome.Children.Add(addFiles);
        welcome.Children.Add(new TextBlock { Text = "PNG · JPG · GIF · MP4", Foreground = studioTheme.Secondary, HorizontalAlignment = HorizontalAlignment.Center });
        canvasContent.Children.Add(welcome); emptyCanvas.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) welcome.IsVisible = emptyCanvas.IsVisible; };
        var canvas = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(14), Background = studioTheme.Panel, Child = canvasContent, ClipToBounds = true };
        DragDrop.SetAllowDrop(canvas, true);
        canvas.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, true);
        canvas.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, true);
        previewCanvas = canvas;
        canvas.SizeChanged += (_, _) => EnsurePreviewFits();
        canvas.PointerPressed += (_, e) =>
        {
            if (SelectedEntry() is null || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            var point = e.GetPosition(stickerBoundary);
            if (point.X < 0 || point.Y < 0 || point.X > 512 || point.Y > 512) return;
            previewDragStart = e.GetPosition(canvas); previewStartX = offsetX.Value; previewStartY = offsetY.Value; e.Pointer.Capture(canvas);
        };
        canvas.PointerMoved += (_, e) =>
        {
            if (previewDragStart is not { } start || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            var point = e.GetPosition(canvas); var size = Math.Min(Math.Max(0, canvas.Bounds.Width - 40), Math.Max(0, canvas.Bounds.Height - 40));
            if (!double.IsNaN(viewbox.Width)) size = Math.Min(size, viewbox.Width);
            if (size <= 0) return;
            offsetX.Value = Math.Clamp(previewStartX + (point.X - start.X) * 512 / size, offsetX.Minimum, offsetX.Maximum);
            offsetY.Value = Math.Clamp(previewStartY + (point.Y - start.Y) * 512 / size, offsetY.Minimum, offsetY.Maximum);
        };
        canvas.PointerReleased += (_, e) => { previewDragStart = null; e.Pointer.Capture(null); };
        canvas.PointerWheelChanged += (_, e) =>
        {
            if (SelectedEntry() is null) return;
            var point = e.GetPosition(stickerBoundary);
            if (point.X < 0 || point.Y < 0 || point.X > 512 || point.Y > 512) return;
            scale.Value = Math.Clamp(scale.Value * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), scale.Minimum, scale.Maximum);
            e.Handled = true;
        };
        var center = new DockPanel { Margin = new Thickness(4, 0) };
        animationBar.Background = studioTheme.Panel; animationBar.CornerRadius = new CornerRadius(12); animationBar.Padding = new Thickness(8, 4); animationBar.Margin = new Thickness(0, 6, 0, 0);
        animationBar.Child = new ScrollViewer { Content = animationPanel, MaxHeight = 165, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(animationBar, Dock.Bottom); center.Children.Add(animationBar); center.Children.Add(canvas);
        Grid.SetColumn(center, 2); columns.Children.Add(center);
        propertiesSplitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Center, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = studioTheme.Border };
        ToolTip.SetTip(propertiesSplitter, "Изменить ширину панели свойств"); Grid.SetColumn(propertiesSplitter, 3); columns.Children.Add(propertiesSplitter);
        var properties = new StackPanel { Spacing = 10 };
        editingProperties = properties;
        properties.Children.Add(new TextBlock { Text = "Редактирование", FontSize = 17, FontWeight = FontWeight.SemiBold });
        properties.Children.Add(details);
        properties.Children.Add(new TextBlock { Text = "Режим подгонки", FontWeight = FontWeight.SemiBold });
        properties.Children.Add(fitMode);
        properties.Children.Add(new TextBlock { Text = "Точная настройка", FontWeight = FontWeight.SemiBold });
        foreach (var (label, slider) in new[] { (scaleLabel, scale), (rotationLabel, rotation), (offsetXLabel, offsetX), (offsetYLabel, offsetY) })
        {
            var input = CreateSliderInput(slider, ReferenceEquals(slider, scale) ? 100 : 1);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100"), ColumnSpacing = 8 };
            label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
            Grid.SetColumn(input, 1); row.Children.Add(input);
            var parameter = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
            parameter.Children.Add(row); parameter.Children.Add(slider); properties.Children.Add(parameter);
            slider.PropertyChanged += async (_, e) => { if (e.Property == RangeBase.ValueProperty) { UpdateTransformLabels(); await TransformChangedAsync(); } };
            if (ReferenceEquals(slider, rotation))
            {
                var rotationActions = new WrapPanel();
                rotationActions.Children.Add(Button("↶ 90°", () => { RotateBy(-90); return Task.CompletedTask; }, "Повернуть на 90° влево"));
                rotationActions.Children.Add(Button("↷ 90°", () => { RotateBy(90); return Task.CompletedTask; }, "Повернуть на 90° вправо"));
                rotationActions.Children.Add(Button("Сбросить поворот", () => { rotation.Value = 0; return Task.CompletedTask; }));
                properties.Children.Add(rotationActions);
            }
        }
        properties.Children.Add(new TextBlock { Text = "Отражение", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        var flipActions = new WrapPanel(); flipActions.Children.Add(flipHorizontal); flipActions.Children.Add(flipVertical); properties.Children.Add(flipActions);
        flipHorizontal.Margin = new Thickness(0, 0, 12, 0);
        flipHorizontal.PropertyChanged += async (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) await TransformChangedAsync(); };
        flipVertical.PropertyChanged += async (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) await TransformChangedAsync(); };
        fitMode.SelectionChanged += async (_, _) => await FitModeChangedAsync();
        properties.Children.Add(Button("Сбросить подгонку", ResetSelectedAsync));
        var timelineHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        timelineHeader.Children.Add(new TextBlock { Text = "Фрагмент анимации", FontWeight = FontWeight.SemiBold });
        Grid.SetColumn(sourceDurationLabel, 1); timelineHeader.Children.Add(sourceDurationLabel); animationPanel.Children.Add(timelineHeader);
        animationPanel.Children.Add(timeline);
        var timelineControls = new WrapPanel { Orientation = Orientation.Horizontal };
        var startField = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 8, 0) };
        startField.Children.Add(new TextBlock { Text = "Начало, сек." }); startField.Children.Add(fragmentStart); timelineControls.Children.Add(startField);
        var durationField = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 8, 0) };
        durationField.Children.Add(new TextBlock { Text = "Длительность, сек." }); durationField.Children.Add(fragmentDuration); timelineControls.Children.Add(durationField);
        fragmentStart.ValueChanged += async (_, _) => await FragmentFieldsChangedAsync();
        fragmentDuration.ValueChanged += async (_, _) => await FragmentFieldsChangedAsync();
        timeline.RangeChanged += (start, duration) => { SetFragmentFields(start, duration); _ = TransformChangedAsync(); };
        timeline.SeekRequested += seconds => _ = SeekAnimationAsync(seconds);
        applySelectedButton = Button("Применить настройки ко всем выделенным", ApplySelectedAsync);
        applySelectedButton.Content = new TextBlock { Text = "Применить настройки ко всем выделенным", TextWrapping = TextWrapping.Wrap };
        applySelectedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        applySelectedButton.IsEnabled = false;
        groupPanel.Children.Add(new TextBlock { Text = "ГРУППОВАЯ ПОДГОНКА", FontWeight = FontWeight.Bold });
        groupPanel.Children.Add(groupDescription);
        groupPanel.Children.Add(applySelectedButton);
        gifPlay.Click += (_, _) => ToggleGif();
        timelineControls.Children.Add(gifPlay);
        videoPlay.Click += (_, _) => { if (videoPlayback is null) StartVideo(); else StopVideo(); };
        timelineControls.Children.Add(videoPlay);
        videoTime.VerticalAlignment = VerticalAlignment.Center; videoTime.Margin = new Thickness(6, 0);
        timelineControls.Children.Add(videoTime); animationPanel.Children.Add(timelineControls);
        properties.Children.Add(groupPanel);
        properties.Children.Add(new TextBlock { Text = "Масштаб просмотра", FontWeight = FontWeight.SemiBold });
        properties.Children.Add(Button("Уместить в окно", () => { FitPreview(); return Task.CompletedTask; }));
        properties.Children.Add(Button("Показать 1:1", () => { ActualPreviewSize(); return Task.CompletedTask; }));
        videoSeek.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !updatingSeek && videoPath is not null)
            {
                var playing = videoPlayback is not null;
                StopVideo();
                if (playing) StartVideo();
                else _ = ShowSeekFrameAsync(videoPath, videoSeek.Value, ++previewGeneration);
                UpdateVideoTime(videoSeek.Value);
            }
        };
        var propertyContent = new Grid(); propertyContent.Children.Add(emptyProperties); propertyContent.Children.Add(properties);
        var propertiesPanel = new Border { Background = studioTheme.Panel, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = new ScrollViewer { Content = propertyContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        propertiesSide = propertiesPanel;
        Grid.SetColumn(propertiesPanel, 4); columns.Children.Add(propertiesPanel);
        galleryVisibilityButton = PanelVisibilityButton("GalleryVisibility", ToggleGallery);
        propertiesVisibilityButton = PanelVisibilityButton("PropertiesVisibility", ToggleProperties);
        Grid.SetColumn(galleryVisibilityButton, 1); columns.Children.Add(galleryVisibilityButton);
        Grid.SetColumn(propertiesVisibilityButton, 3); columns.Children.Add(propertiesVisibilityButton);
        gallerySplitter.AddHandler(Thumb.DragCompletedEvent, (_, _) => Dispatcher.UIThread.Post(() => CapturePanelWidth(true), DispatcherPriority.Loaded));
        propertiesSplitter.AddHandler(Thumb.DragCompletedEvent, (_, _) => Dispatcher.UIThread.Post(() => CapturePanelWidth(false), DispatcherPriority.Loaded));
        gallerySplitter.KeyUp += (_, _) => Dispatcher.UIThread.Post(() => CapturePanelWidth(true), DispatcherPriority.Loaded);
        propertiesSplitter.KeyUp += (_, _) => Dispatcher.UIThread.Post(() => CapturePanelWidth(false), DispatcherPriority.Loaded);
        ApplyPanelLayout();
        SizeChanged += (_, _) => ApplyPanelLayout();
        editorPage = columns;
        settingsPage = CreateSettingsPage(); settingsPage.IsVisible = false;
        var pages = new Grid(); pages.Children.Add(columns); pages.Children.Add(settingsPage);
        root.Children.Add(pages); Content = root;
        StudioLocalization.Capture(root); ApplyPreferences();
        ActualThemeVariantChanged += (_, _) => { if (preferences.Theme == "system") studioTheme.Apply(ActualThemeVariant == ThemeVariant.Light); };
        packName.TextChanged += (_, _) =>
        {
            var name = packName.Text ?? "";
            if (!refreshing && !busy && project.Name != name) { project.Name = name; MarkDirty(); }
        };
        KeyDown += OnKeyDown;
        Closing += OnClosing;
        Closed += (_, _) => { StopGif(); StopVideo(); this.layoutStore.Save(layout); };
        RefreshGallery();
    }

    private static MenuItem MenuAction(string title, Func<Task> action)
    {
        var item = new MenuItem { Header = title };
        item.Click += async (_, _) => await action();
        return item;
    }
    private string Tr(string text) => StudioLocalization.Text(text, preferences.Language);
    private void ApplyPreferences()
    {
        RequestedThemeVariant = preferences.Theme switch { "light" => ThemeVariant.Light, "system" => ThemeVariant.Default, _ => ThemeVariant.Dark };
        studioTheme.Apply(preferences.Theme == "light" || (preferences.Theme == "system" && ActualThemeVariant == ThemeVariant.Light));
        if (Content is Control root) StudioLocalization.Apply(root, preferences.Language);
        if (galleryMenu is not null) StudioLocalization.Apply(galleryMenu, preferences.Language);
        if (projectMenu is not null) StudioLocalization.Apply(projectMenu, preferences.Language);
        UpdatePanelButtons();
        foreach (var card in gallery.Items.OfType<ListBoxItem>()) StudioLocalization.Apply(card, preferences.Language);
        packName.PlaceholderText = Tr("Название набора");
        var mode = fitMode.SelectedIndex; updatingTransform = true;
        fitMode.ItemsSource = new[] { Tr("Вместить целиком"), Tr("Заполнить с обрезкой"), Tr("Растянуть (искажает пропорции)"), Tr("Вручную") };
        fitMode.SelectedIndex = mode; updatingTransform = false;
        gifPlay.Content = Tr(gifPlayback is null ? "Воспроизвести" : "Пауза");
        videoPlay.Content = Tr(videoPlayback is null ? "Воспроизвести" : "Пауза");
        if (animationDuration > 0) sourceDurationLabel.Text = $"{(preferences.Language == "en" ? "Source" : "Источник")}: {FormatTime(animationDuration)}";
        if (SelectedEntry() is { } current && cache.TryGetValue(current.Item.Id, out var info))
        {
            details.Text = $"{Path.GetFileName(current.Item.SourcePath)}\n{SourceFormat(info, current.Item.SourcePath)} · {info.Width} × {info.Height}\n{(preferences.Language == "en" ? "Ready to export" : "Готов к подготовке")}";
            var duration = info.Video?.DurationSeconds ?? (info.FrameDurations is { Length: > 1 } durations ? durations.Sum() / 1000d : (double?)null);
            if (duration is > 0) details.Text += $"\n{duration:0.00} {(preferences.Language == "en" ? "sec" : "с")}";
        }
        UpdateTransformLabels(); UpdateSelection();
    }
    private void ShowEditor() { if (editorPage is not null) editorPage.IsVisible = true; if (settingsPage is not null) settingsPage.IsVisible = false; if (settingsButton is not null) settingsButton.Background = studioTheme.Surface; }
    private Task ShowSettingsAsync()
    {
        if (settingsPage?.IsVisible == true) { ShowEditor(); return Task.CompletedTask; }
        if (editorPage is not null) editorPage.IsVisible = false;
        if (settingsPage is not null) settingsPage.IsVisible = true;
        if (settingsButton is not null) settingsButton.Background = studioTheme.Selected;
        return Task.CompletedTask;
    }
    private Control CreateSettingsPage()
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = "Настройки", FontSize = 24, FontWeight = FontWeight.Bold });
        var language = new ComboBox { ItemsSource = new[] { "Русский", "English" }, SelectedIndex = preferences.Language == "en" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var appearance = new ComboBox { ItemsSource = new[] { Tr("Тёмная"), Tr("Светлая"), Tr("Как в системе") }, SelectedIndex = preferences.Theme switch { "light" => 1, "system" => 2, _ => 0 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var general = new StackPanel { Spacing = 10 };
        general.Children.Add(new TextBlock { Text = "Язык", FontWeight = FontWeight.SemiBold }); general.Children.Add(language);
        general.Children.Add(new TextBlock { Text = "Оформление", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) }); general.Children.Add(appearance);
        panel.Children.Add(new Border { Background = studioTheme.Surface, BorderBrush = studioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Child = general });
        var about = new StackPanel { Spacing = 8 };
        about.Children.Add(new TextBlock { Text = "StickerForge Studio", FontWeight = FontWeight.Bold, FontSize = 16 });
        about.Children.Add(new TextBlock { Text = "Локальная подготовка изображений и видео для стикеров Telegram.", TextWrapping = TextWrapping.Wrap, Foreground = studioTheme.Secondary });
        about.Children.Add(Button("О программе", ShowAboutAsync));
        panel.Children.Add(new Border { Background = studioTheme.Surface, BorderBrush = studioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Child = about });
        language.SelectionChanged += (_, _) =>
        {
            preferences = preferences with { Language = language.SelectedIndex == 1 ? "en" : "ru" }; preferencesStore.Save(preferences); ApplyPreferences();
            var selected = appearance.SelectedIndex; appearance.ItemsSource = new[] { Tr("Тёмная"), Tr("Светлая"), Tr("Как в системе") }; appearance.SelectedIndex = selected;
        };
        appearance.SelectionChanged += (_, _) =>
        {
            if (appearance.SelectedIndex < 0) return;
            preferences = preferences with { Theme = appearance.SelectedIndex switch { 1 => "light", 2 => "system", _ => "dark" } }; preferencesStore.Save(preferences); ApplyPreferences();
        };
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private static new Control Icon(string path, double size, IBrush brush) => new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Width = size, Height = size, Stretch = Stretch.Uniform, Stroke = brush, StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
    private Button IconButton(string path, Func<Task> action, string tooltip)
    {
        var button = Button("", action, tooltip); button.Content = Icon(path, 20, studioTheme.Text); button.Background = studioTheme.Surface; return button;
    }
    private Button ActionButton(string path, string title, Func<Task> action, string tooltip)
    {
        var button = Button("", action, tooltip);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(Icon(path, 16, studioTheme.Text));
        content.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        return button;
    }
    private Button PanelVisibilityButton(string name, Action action)
    {
        var button = new Button { Name = name, Width = 32, Height = 40, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0), Background = studioTheme.Surface, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => action();
        return button;
    }
    private void UpdatePanelButtons()
    {
        if (galleryVisibilityButton is null || propertiesVisibilityButton is null || galleryPanel is null || propertiesSide is null) return;
        void Update(Button button, bool visible, bool left)
        {
            var pointsLeft = left == visible;
            button.Content = Icon(pointsLeft ? "M15 5 L8 12 L15 19" : "M9 5 L16 12 L9 19", 14, studioTheme.Text);
            var label = left ? (visible ? "Свернуть галерею" : "Показать галерею") : (visible ? "Свернуть свойства" : "Показать свойства");
            var localized = Tr(label);
            ToolTip.SetTip(button, localized);
            Avalonia.Automation.AutomationProperties.SetName(button, localized);
        }
        Update(galleryVisibilityButton, galleryPanel.IsVisible, true);
        Update(propertiesVisibilityButton, propertiesSide.IsVisible, false);
    }
    private async Task ResetSelectedAsync()
    {
        if (busy || SelectedEntry() is null) return;
        ResetGeometry(resetFlips: true); await TransformChangedAsync();
    }
    private async Task ApplySelectedAsync()
    {
        if (busy || gallery.SelectedItems is not { Count: > 1 } || SelectedEntry() is not { } entry) return;
        foreach (var row in gallery.SelectedItems.OfType<ListBoxItem>()) if (row.Tag is GalleryEntry target) target.Item.Transform = CopyTransform(entry.Item.Transform);
        MarkDirty(); await SelectAsync();
    }
    private Task ShowAboutAsync() => ShowAboutAsync(this);
    private async Task ShowAboutAsync(Window owner)
    {
        var dialog = new Window { Title = Tr("О программе"), Width = 460, Height = 300, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "StickerForge Studio", FontSize = 18, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = Tr("Локальная подготовка изображений и видео для стикеров Telegram."), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"{typeof(MainWindow).Assembly.GetName().Version} · FFmpeg LGPL-3.0 · Avalonia MIT · SkiaSharp MIT", Foreground = studioTheme.Secondary, TextWrapping = TextWrapping.Wrap });
        var notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        if (File.Exists(notices)) panel.Children.Add(Button(Tr("Лицензии"), () => { try { Process.Start(new ProcessStartInfo(notices) { UseShellExecute = true }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { status.Text = preferences.Language == "en" ? "Could not open licenses." : "Не удалось открыть лицензии."; } return Task.CompletedTask; }));
        panel.Children.Add(Button(Tr("Закрыть"), () => { dialog.Close(); return Task.CompletedTask; }));
        dialog.Content = panel; await dialog.ShowDialog(owner);
    }
    private void FitPreview() { if (previewViewbox is not null) { previewViewbox.Width = double.NaN; previewViewbox.Height = double.NaN; } }
    private void ActualPreviewSize()
    {
        if (previewViewbox is null || previewCanvas is null) return;
        var size = 512 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        if (Math.Min(previewCanvas.Bounds.Width - 40, previewCanvas.Bounds.Height - 40) < size)
        { FitPreview(); status.Text = Tr("Размер 1:1 не помещается; полотно показано целиком."); return; }
        previewViewbox.Width = previewViewbox.Height = size;
    }
    private void EnsurePreviewFits()
    {
        if (previewViewbox is null || previewCanvas is null || double.IsNaN(previewViewbox.Width)) return;
        if (Math.Min(previewCanvas.Bounds.Width - 40, previewCanvas.Bounds.Height - 40) < previewViewbox.Width) FitPreview();
    }
    private static TextBox CreateSliderInput(Slider slider, double factor)
    {
        var input = new TextBox { MinWidth = 100, Height = 34, VerticalAlignment = VerticalAlignment.Center,
            Text = Math.Round(slider.Value * factor).ToString(CultureInfo.CurrentCulture) };
        void Refresh() => input.Text = Math.Round(slider.Value * factor).ToString(CultureInfo.CurrentCulture);
        void Commit()
        {
            if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value))
                slider.Value = Math.Clamp(value / factor, slider.Minimum, slider.Maximum);
            Refresh();
        }
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) Refresh();
        };
        input.LostFocus += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return input;
    }
    private void ToggleGallery()
    {
        if (galleryPanel is { IsVisible: false })
            layout = layout with { GalleryVisible = true, PropertiesVisible = ClientSize.Width < 930 ? false : layout.PropertiesVisible };
        else layout = layout with { GalleryVisible = false };
        ApplyPanelLayout();
    }
    private void ToggleProperties() { layout = layout with { PropertiesVisible = !layout.PropertiesVisible }; ApplyPanelLayout(); }
    private void CapturePanelWidth(bool gallerySide)
    {
        if (workspaceGrid is null) return;
        if (gallerySide && galleryPanel is { IsVisible: true }) layout = layout with { GalleryWidth = Math.Clamp(workspaceGrid.ColumnDefinitions[0].ActualWidth, 180, 600) };
        if (!gallerySide && layout.PropertiesVisible) layout = layout with { PropertiesWidth = Math.Clamp(workspaceGrid.ColumnDefinitions[4].ActualWidth, 270, 500) };
    }
    private void ApplyPanelLayout()
    {
        if (workspaceGrid is null || galleryPanel is null || propertiesSide is null || gallerySplitter is null || propertiesSplitter is null) return;
        var hasItems = project.Stickers.Count > 0;
        var showProperties = hasItems && layout.PropertiesVisible;
        var showGallery = hasItems && layout.GalleryVisible && (!showProperties || ClientSize.Width >= 930);
        galleryPanel.IsVisible = gallerySplitter.IsVisible = showGallery;
        propertiesSide.IsVisible = propertiesSplitter.IsVisible = showProperties;
        if (galleryVisibilityButton is not null) galleryVisibilityButton.IsVisible = hasItems;
        if (propertiesVisibilityButton is not null) propertiesVisibilityButton.IsVisible = hasItems;
        var columns = workspaceGrid.ColumnDefinitions;
        columns[0].MinWidth = showGallery ? 180 : 0; columns[4].MinWidth = showProperties ? 270 : 0;
        columns[1].Width = new GridLength(hasItems ? 32 : 0); columns[3].Width = new GridLength(hasItems ? 32 : 0);
        var leftWidth = showGallery ? Math.Min(layout.GalleryWidth, 300) : 0;
        var rightWidth = showProperties ? Math.Clamp(layout.PropertiesWidth, 270, 340) : 0;
        var clientWidth = ClientSize.Width > 0 ? ClientSize.Width : Width;
        var centralMinimum = Math.Clamp(clientWidth - 16 - (hasItems ? 64 : 0) - (showProperties ? 270 : 0), 320, 400);
        var available = Math.Max(0, clientWidth - 16 - centralMinimum - (hasItems ? 64 : 0));
        var excess = Math.Max(0, leftWidth + rightWidth - available);
        var leftReduction = Math.Min(excess / 2, Math.Max(0, leftWidth - (showGallery ? 180 : 0)));
        leftWidth -= leftReduction; excess -= leftReduction;
        var rightReduction = Math.Min(excess, Math.Max(0, rightWidth - (showProperties ? 270 : 0)));
        rightWidth -= rightReduction; excess -= rightReduction;
        leftWidth -= Math.Min(excess, Math.Max(0, leftWidth - (showGallery ? 180 : 0)));
        columns[0].Width = new GridLength(leftWidth); columns[4].Width = new GridLength(rightWidth);
        columns[2].Width = new GridLength(1, GridUnitType.Star);
        UpdatePanelButtons();
    }

    private static Button Button(string text, Func<Task> action, string? tooltip = null)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4) };
        if (tooltip is not null) ToolTip.SetTip(button, tooltip);
        button.Click += async (_, _) => await action();
        return button;
    }
    private void MarkDirty() { dirty = true; UpdateTitle(); }
    private void UpdateTitle() => Title = $"StickerForge Studio — {project.Name}{(dirty ? " *" : "")}";

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!dirty) return true;
        var dialog = new Window { Title = Tr("Несохранённые изменения"), Width = 390, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = Tr("Отменить несохранённые изменения?") });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button(Tr("Не сохранять"), () => { dialog.Close(true); return Task.CompletedTask; }));
        buttons.Children.Add(Button(Tr("Отмена"), () => { dialog.Close(false); return Task.CompletedTask; }));
        panel.Children.Add(buttons); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!dirty && !busy) return;
        e.Cancel = true;
        if (busy) { status.Text = Tr("Перед закрытием отмените текущую операцию."); return; }
        if (await ConfirmDiscardAsync()) { dirty = false; Close(); }
    }
    private async Task NewAsync()
    {
        if (busy || !await ConfirmDiscardAsync()) return;
        project = new(); projectPath = null; dirty = false; cache.Clear(); FitPreview(); RefreshGallery();
    }
    private async Task OpenAsync()
    {
        if (busy || !await ConfirmDiscardAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Tr("Открыть проект"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Проект StickerForge") { Patterns = ["*.stickerforge.json"] }] });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            project = await store.LoadAsync(path); projectPath = path; dirty = false; cache.Clear(); FitPreview(); RefreshGallery();
            await LoadThumbnailsAsync();
            status.Text = Tr("Проект открыт");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { status.Text = preferences.Language == "en" ? "Could not open the project. Check the file." : "Не удалось открыть проект. Проверьте файл."; }
    }
    private async Task LoadThumbnailsAsync()
    {
        busy = true;
        try
        {
            foreach (var item in project.Stickers)
            {
                if (!File.Exists(item.SourcePath)) continue;
                var result = (await importer.ImportAsync([item.SourcePath]))[0];
                if (result.Info is not null) cache[item.Id] = result.Info;
            }
        }
        finally { busy = false; RefreshGallery(); }
    }
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (busy) return false;
        var path = saveAs ? null : projectPath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Tr("Сохранить проект"), SuggestedFileName = "pack.stickerforge.json",
                FileTypeChoices = [new FilePickerFileType("Проект StickerForge") { Patterns = ["*.stickerforge.json"] }] });
            path = file?.TryGetLocalPath();
        }
        if (path is null) return false;
        try { await store.SaveAsync(project, path); projectPath = path; dirty = false; UpdateTitle(); status.Text = Tr("Проект сохранён"); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { status.Text = preferences.Language == "en" ? "Could not save the project. Check folder access." : "Не удалось сохранить проект. Проверьте доступ к папке."; return false; }
    }
    private async Task PickFilesAsync()
    {
        if (busy) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Tr("Добавить файлы"), AllowMultiple = true });
        await ImportAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
    private async Task PickFolderAsync()
    {
        if (busy) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Tr("Добавить папку"), AllowMultiple = false });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        FolderScanResult scan;
        busy = true;
        importing = new CancellationTokenSource();
        try
        {
            var token = importing.Token;
            var report = new Progress<int>(count => status.Text = preferences.Language == "en" ? $"Searching folder: {count}" : $"Поиск файлов в папке: {count}");
            scan = await Task.Run(() => new FolderScanner().Scan(path, report, token), token);
        }
        catch (OperationCanceledException) { status.Text = preferences.Language == "en" ? "Folder search cancelled." : "Поиск файлов отменён."; return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { status.Text = preferences.Language == "en" ? "Could not read the folder." : "Не удалось прочитать папку."; return; }
        finally { importing.Dispose(); importing = null; busy = false; }
        await ImportAsync(scan.Files);
        if (scan.Errors.Count > 0)
        {
            var dialog = new Window { Title = Tr("Ошибки при чтении папки"), Width = 620, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, scan.Errors), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
            await dialog.ShowDialog(this);
        }
    }
    private async Task ExportAsync()
    {
        if (busy) return;
        if (project.Stickers.Count == 0) { status.Text = Tr("Добавьте файлы перед экспортом."); return; }
        busy = true; importing = new CancellationTokenSource();
        Window? progressWindow = null;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = preferences.Language == "en" ? $"Choose a folder for {project.Stickers.Count} stickers" : $"Выберите папку для экспорта {project.Stickers.Count} файлов", AllowMultiple = false });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (path is null) return;
            var sources = project.Stickers.ToArray();
            var name = project.Name;
            var progressText = new TextBlock { Text = Tr("Подготовка файлов для Telegram"), Foreground = studioTheme.Secondary, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
            var progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 10, HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = studioTheme.Accent };
            var progressContent = new StackPanel { Spacing = 18, Margin = new Thickness(28), VerticalAlignment = VerticalAlignment.Center };
            progressContent.Children.Add(new TextBlock { Text = Tr("Экспорт стикеров…"), FontSize = 22, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
            progressContent.Children.Add(progressText); progressContent.Children.Add(progressBar);
            progressContent.Children.Add(Button(Tr("Отменить"), () => { importing?.Cancel(); return Task.CompletedTask; }));
            progressWindow = new Window { Title = Tr("Экспорт стикеров…"), Width = 440, Height = 260, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = studioTheme.Background, Foreground = studioTheme.Text, RequestedThemeVariant = RequestedThemeVariant, Content = progressContent };
            progressWindow.Show(this);
            var reportProgress = new Progress<ExportProgress>(p => { progress.Value = (double)p.Completed / p.Total; progressBar.Value = progress.Value; status.Text = preferences.Language == "en" ? $"Exporting {p.Completed}/{p.Total}: {p.FileName}" : $"Экспорт {p.Completed}/{p.Total}: {p.FileName}"; progressText.Text = status.Text; });
            var result = await new StickerPackExporter().ExportAsync(sources, path, name, reportProgress, importing.Token);
            progressWindow.Close(); progressWindow = null;
            status.Text = result.Exported == sources.Length ? (preferences.Language == "en" ? $"Export complete! {result.Exported} of {sources.Length} stickers prepared." : $"Экспорт завершён! Подготовлено {result.Exported} из {sources.Length} стикеров.") : Tr("Экспорт не завершён. Готовый набор не создан.");
            var lines = new List<string> { status.Text, preferences.Language == "en" ? $"Errors: {result.Errors.Count}" : $"Ошибок: {result.Errors.Count}" };
            if (result.Exported == sources.Length) lines.Add(preferences.Language == "en" ? $"Folder: {result.Directory}" : $"Папка: {result.Directory}");
            if (result.Successes.Count > 0) { lines.Add(""); lines.Add(Tr("Подготовлены:")); lines.AddRange(result.Successes); }
            if (result.Errors.Count > 0) { lines.Add(""); lines.Add(Tr("Не удалось подготовить:")); lines.AddRange(result.Errors.Select(ExportError)); }
            if (result.Errors.Count > 0) { lines.Add(""); lines.Add(preferences.Language == "en" ? $"Diagnostic log: {ExportDiagnostics.LogPath}" : $"Журнал диагностики: {ExportDiagnostics.LogPath}"); }
            var dialog = new Window { Title = Tr("Результат экспорта"), Width = 620, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = studioTheme.Background, Foreground = studioTheme.Text, RequestedThemeVariant = RequestedThemeVariant };
            var reportPanel = new DockPanel();
            var openFolder = Button(Tr("Открыть папку"), () => { try { Process.Start(new ProcessStartInfo(result.Directory) { UseShellExecute = true }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { status.Text = preferences.Language == "en" ? "Could not open the folder." : "Не удалось открыть папку."; } return Task.CompletedTask; });
            var reportActions = new WrapPanel { Margin = new Thickness(16, 0, 16, 16) };
            if (result.Exported == sources.Length) { openFolder.Background = studioTheme.Accent; openFolder.Foreground = Brushes.White; reportActions.Children.Add(openFolder); }
            reportActions.Children.Add(Button(Tr("Продолжить работу"), () => { dialog.Close(); return Task.CompletedTask; }));
            DockPanel.SetDock(reportActions, Dock.Bottom); reportPanel.Children.Add(reportActions);
            if (result.Exported == sources.Length)
            {
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(16, 16, 16, 0) };
                heading.Children.Add(new PathIcon { Data = Geometry.Parse("M3 12 L9 18 L21 5"), Width = 32, Height = 32, Foreground = studioTheme.Success });
                heading.Children.Add(new TextBlock { Text = Tr("Экспорт завершён!"), FontSize = 20, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
                DockPanel.SetDock(heading, Dock.Top); reportPanel.Children.Add(heading);
            }
            reportPanel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, lines), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } }); dialog.Content = reportPanel;
            await dialog.ShowDialog(this);
        }
        catch (OperationCanceledException) { status.Text = Tr("Экспорт отменён."); }
        catch (Exception ex)
        {
            ExportDiagnostics.Write($"export failed: {ex}");
            status.Text = preferences.Language == "en" ? "Export incomplete. Check the files and folder access; see the diagnostic log." : "Экспорт не завершён. Проверьте файлы и доступ к папке; подробности в журнале.";
        }
        finally { progressWindow?.Close(); importing.Dispose(); importing = null; busy = false; progress.Value = 0; }
    }
    private async Task ImportAsync(IEnumerable<string> paths)
    {
        if (busy) return;
        var files = paths.ToArray(); if (files.Length == 0) return;
        busy = true; importing = new CancellationTokenSource();
        var errors = new List<string>(); var added = 0;
        try
        {
            for (var i = 0; i < files.Length; i++)
            {
                importing.Token.ThrowIfCancellationRequested();
                var results = await importer.ImportAsync([files[i]], cancellationToken: importing.Token);
                var result = results[0];
                if (result.Item is not null && result.Info is not null)
                { project.Stickers.Add(result.Item); cache[result.Item.Id] = result.Info; added++; MarkDirty(); }
                else errors.Add(preferences.Language == "en" ? $"{Path.GetFileName(result.Path)}: Could not read this file." : $"{Path.GetFileName(result.Path)}: {LocalizeError(result.Error ?? "Неизвестная ошибка.")}");
                progress.Value = (i + 1d) / files.Length;
                status.Text = preferences.Language == "en" ? $"Importing {i + 1}/{files.Length}" : $"Импорт {i + 1}/{files.Length}";
            }
        }
        catch (OperationCanceledException) { errors.Add(Tr("Импорт отменён.")); }
        finally { importing.Dispose(); importing = null; busy = false; progress.Value = 0; RefreshGallery(); }
        if (added > 0 && gallery.Items.Count > 0) gallery.SelectedIndex = 0;
        status.Text = preferences.Language == "en" ? $"Added: {added}; errors: {errors.Count}." : $"Добавлено: {added}; ошибок: {errors.Count}.";
        if (errors.Count > 0)
        {
            var dialog = new Window { Title = Tr("Результат импорта"), Width = 620, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, errors), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
            await dialog.ShowDialog(this);
        }
    }
    private void RefreshGallery()
    {
        ClearVideo();
        StopGif();
        animationBar.IsVisible = false;
        gifPath = null; gifDurations = null; gifPlay.IsVisible = false;
        refreshing = true;
        packName.Text = project.Name;
        previewGeneration++;
        foreach (var old in gallery.Items.OfType<ListBoxItem>())
            if (old.Tag is GalleryEntry entry) entry.Thumbnail?.Dispose();
        gallery.Items.Clear();
        var number = 0;
        foreach (var item in project.Stickers)
        {
            number++;
            var name = Path.GetFileName(item.SourcePath);
            var exists = File.Exists(item.SourcePath);
            Bitmap? thumbnail = null;
            if (cache.TryGetValue(item.Id, out var info))
            {
                using var stream = new MemoryStream(info.Thumbnail);
                thumbnail = new Bitmap(stream);
            }
            var row = new StackPanel { Width = 78, Spacing = 4 };
            var thumbnailGrid = new Grid { Width = 74, Height = 74, Background = studioTheme.Surface };
            thumbnailGrid.Children.Add(new Image { Source = thumbnail, Stretch = Stretch.Uniform });
            thumbnailGrid.Children.Add(Badge(number.ToString("000"), HorizontalAlignment.Left, VerticalAlignment.Top));
            thumbnailGrid.Children.Add(Badge(exists ? SourceFormat(info, item.SourcePath) : "НЕТ ФАЙЛА", HorizontalAlignment.Right, VerticalAlignment.Top));
            var duration = info?.Video?.DurationSeconds ?? (info?.FrameDurations is { Length: > 1 } frames ? frames.Sum(x => Math.Max(x, 20)) / 1000d : (double?)null);
            if (duration is > 0) thumbnailGrid.Children.Add(Badge($"{duration:0.##} с", HorizontalAlignment.Right, VerticalAlignment.Bottom));
            var activeBadge = Badge("ТЕКУЩИЙ", HorizontalAlignment.Left, VerticalAlignment.Bottom); activeBadge.IsVisible = false; thumbnailGrid.Children.Add(activeBadge);
            var frame = new Border { BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, CornerRadius = new CornerRadius(6), Child = thumbnailGrid };
            row.Children.Add(frame);
            row.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            var card = new ListBoxItem { Content = row, Tag = new GalleryEntry(item, thumbnail, frame, activeBadge), Margin = new Thickness(2), Padding = new Thickness(2), Background = Brushes.Transparent };
            StudioLocalization.Capture(card); StudioLocalization.Apply(card, preferences.Language);
            ToolTip.SetTip(card, name);
            gallery.Items.Add(card);
        }
        refreshing = false;
        galleryHint.IsVisible = project.Stickers.Count == 0;
        details.Text = preferences.Language == "en" ? $"Stickers: {project.Stickers.Count}. Select a file." : $"Стикеров: {project.Stickers.Count}. Выберите файл.";
        preview.Source = null; previewBitmap?.Dispose(); previewBitmap = null;
        UpdateSelection();
        UpdateTitle();
    }
    private static Border Badge(string text, HorizontalAlignment horizontal, VerticalAlignment vertical) => new()
    {
        HorizontalAlignment = horizontal, VerticalAlignment = vertical, Margin = new Thickness(4), Padding = new Thickness(5, 2),
        Background = StudioTheme.BadgeBackground, CornerRadius = new CornerRadius(4),
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }
    };
    private static string LocalizeError(string text)
    {
        foreach (var (english, russian) in new[] {
            ("File exceeds 100 MB.", "Размер файла превышает 100 МБ. Уменьшите исходный файл."),
            ("Unsupported or corrupt image.", "Формат не поддерживается или файл повреждён."),
            ("Unsupported or damaged image.", "Формат не поддерживается или файл повреждён."),
            ("Image dimensions exceed the limit.", "Размер изображения превышает допустимый."),
            ("Image decoding failed.", "Не удалось прочитать изображение."),
            ("Animation exceeds the frame limit.", "В анимации слишком много кадров."),
            ("GIF exceeds the frame limit.", "В GIF слишком много кадров."),
            ("Cannot fit this animation into 256 KiB. Choose a shorter or simpler fragment.", "Не удалось уменьшить анимацию до 256 КиБ. Выберите более короткий или простой фрагмент."),
            ("Cannot fit this image into Telegram's 512 KiB limit without exceeding the format constraints.", "Не удалось уменьшить изображение до 512 КиБ. Попробуйте изображение с меньшим количеством деталей."),
            ("Encoded video failed format validation.", "Полученное видео не прошло проверку формата."),
            ("Encoded video duration is invalid.", "Длительность полученного видео недопустима."),
            ("Encoded video duration is invalid:", "Недопустимая длительность полученного видео:"),
            ("FFmpeg and FFprobe are unavailable; configure them to import video.", "Для импорта видео нужны FFmpeg и FFprobe."),
            ("FFmpeg and FFprobe are unavailable; install or configure them to import video.", "Для импорта видео нужны FFmpeg и FFprobe."),
            ("Media process failed:", "Ошибка обработки медиа:"),
            ("Video duration is unavailable.", "Не удалось определить длительность видео.") }) text = text.Replace(english, russian, StringComparison.Ordinal);
        return text;
    }
    private string ExportError(string text)
    {
        if (preferences.Language == "en")
        {
            var index = text.IndexOf(": ", StringComparison.Ordinal);
            var file = index >= 0 ? text[..index] : text;
            return $"{file}: Could not prepare this file. Check the source and media components; details are in the diagnostic log.";
        }
        if (text.Contains("Media process failed", StringComparison.Ordinal))
            return $"{text.Split(':')[0]}: не удалось обработать файл. Проверьте исходник; подробности записаны в журнал.";
        if (text.Contains("Video dimensions exceed the limit.", StringComparison.Ordinal))
            return $"{text.Split(':')[0]}: не удалось прочитать размеры видео или они превышают допустимые. Проверьте файл и попробуйте другой источник.";
        var localized = LocalizeError(text);
        if (text.Contains("Cannot fit this", StringComparison.Ordinal) || text.Contains("File exceeds 100 MB.", StringComparison.Ordinal)) return localized;
        var separator = localized.IndexOf(": ", StringComparison.Ordinal);
        var name = separator >= 0 ? localized[..separator] : "Файл";
        var reason = separator >= 0 ? localized[(separator + 2)..] : localized;
        if (!reason.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё')) reason = "Не удалось прочитать или проверить файл.";
        return $"{name}: {reason} Проверьте файл и доступ к нему.";
    }
    private static string SourceFormat(ImageInfo? info, string path)
    {
        if (info is null) return "…";
        if (info.Video is not { } video) return info.Format.Equals("Jpeg", StringComparison.OrdinalIgnoreCase) ? "JPG" : info.Format.ToUpperInvariant();
        var format = video.Container ?? "";
        if (format.Contains("apng", StringComparison.OrdinalIgnoreCase)) return "APNG";
        if (format.Contains("matroska", StringComparison.OrdinalIgnoreCase))
        {
            try { using var stream = File.OpenRead(path); var bytes = new byte[Math.Min(4096, (int)Math.Min(stream.Length, 4096))]; stream.ReadExactly(bytes); return System.Text.Encoding.ASCII.GetString(bytes).Contains("webm", StringComparison.Ordinal) ? "WEBM" : "MKV"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "MKV/WEBM"; }
        }
        if (format.Contains("mov", StringComparison.OrdinalIgnoreCase))
        {
            try { using var stream = File.OpenRead(path); Span<byte> bytes = stackalloc byte[12]; stream.ReadExactly(bytes); return bytes[8..12].SequenceEqual("qt  "u8) ? "MOV" : "MP4"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "MP4/MOV"; }
        }
        return format.Contains("avi", StringComparison.OrdinalIgnoreCase) ? "AVI" : string.IsNullOrEmpty(format) ? "ВИДЕО" : format.Split(',')[0].ToUpperInvariant();
    }
    private void UpdateSelection()
    {
        ApplyPanelLayout();
        var count = gallery.SelectedItems?.Count ?? 0;
        selectionCount.Text = preferences.Language == "en" ? (count > 0 ? $"Selected: {count} of {project.Stickers.Count}" : $"Files: {project.Stickers.Count}") : (count > 0 ? $"Выбрано: {count} из {project.Stickers.Count}" : $"Всего файлов: {project.Stickers.Count}");
        if (exportButton is not null) { exportButton.Content = preferences.Language == "en" ? $"Export {project.Stickers.Count} stickers" : $"Экспортировать {project.Stickers.Count} стикеров"; exportButton.IsEnabled = project.Stickers.Count > 0 && !busy; }
        var hasSelection = SelectedEntry() is not null;
        if (editingProperties is not null) editingProperties.IsVisible = hasSelection;
        emptyProperties.IsVisible = !hasSelection; emptyCanvas.IsVisible = !hasSelection;
        if (previewViewbox is not null) previewViewbox.IsVisible = hasSelection;
        if (applySelectedButton is not null) applySelectedButton.IsEnabled = count > 1;
        groupPanel.IsVisible = count > 1;
        groupDescription.Text = preferences.Language == "en" ? $"Settings from the current sticker will be applied to {count} selected files. Original images will not change." : $"Настройки текущего стикера будут применены к {count} выделенным файлам. Изображения и оригиналы не изменятся.";
        foreach (var card in gallery.Items.OfType<ListBoxItem>())
        {
            if (card.Tag is not GalleryEntry entry) continue;
            var active = card.IsSelected && ReferenceEquals(gallery.SelectedItem, card);
            card.Background = card.IsSelected ? studioTheme.Selected : Brushes.Transparent;
            entry.Frame.BorderBrush = active || card.IsSelected ? studioTheme.Accent : Brushes.Transparent;
            entry.ActiveBadge.IsVisible = active && count > 1;
        }
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (dragItems is not null)
        {
            e.DragEffects = DragDropEffects.Move;
            ClearInsertion();
            insertionCard = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (insertionCard is not null) { insertAfter = e.GetPosition(insertionCard).X >= insertionCard.Bounds.Width / 2; insertionCard.BorderBrush = Brushes.DodgerBlue; insertionCard.BorderThickness = insertAfter ? new Thickness(0, 0, 4, 0) : new Thickness(4, 0, 0, 0); }
            var scroller = gallery.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is not null) { var y = e.GetPosition(scroller).Y; scrollDirection = y < 35 ? -1 : y > scroller.Bounds.Height - 35 ? 1 : 0; }
            e.Handled = true; return;
        }
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = !busy && files is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        dropZone.BorderBrush = e.DragEffects == DragDropEffects.Copy ? Brushes.DodgerBlue : Brushes.Transparent;
        e.Handled = true;
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (dragItems is not null)
        {
            var target = insertionCard?.Tag is GalleryEntry entry ? project.Stickers.IndexOf(entry.Item) + (insertAfter ? 1 : 0) : project.Stickers.Count;
            var selected = dragItems.ToHashSet();
            target -= project.Stickers.Take(target).Count(selected.Contains);
            project.Stickers.RemoveAll(selected.Contains);
            project.Stickers.InsertRange(Math.Clamp(target, 0, project.Stickers.Count), dragItems);
            MarkDirty(); RefreshGallery();
            foreach (var card in gallery.Items.OfType<ListBoxItem>()) if (card.Tag is GalleryEntry item && selected.Contains(item.Item)) gallery.SelectedItems?.Add(card);
            e.Handled = true; return;
        }
        dropZone.BorderBrush = Brushes.Transparent;
        if (busy) return;
        var paths = e.DataTransfer.TryGetFiles()?.Select(x => x.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        e.Handled = true;
        var all = new List<string>();
        var errors = new List<string>();
        busy = true; importing = new CancellationTokenSource();
        try
        {
            var token = importing.Token;
            var report = new Progress<int>(count => status.Text = preferences.Language == "en" ? $"Searching added folders: {count}" : $"Поиск файлов в добавленных папках: {count}");
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(path))
                {
                    var scan = await Task.Run(() => new FolderScanner().Scan(path, report, token), token);
                    all.AddRange(scan.Files); errors.AddRange(scan.Errors);
                }
                else all.Add(path);
            }
        }
        catch (OperationCanceledException) { status.Text = preferences.Language == "en" ? "Adding files cancelled." : "Добавление отменено."; return; }
        finally { importing.Dispose(); importing = null; busy = false; }
        await ImportAsync(all);
        if (errors.Count > 0)
        {
            var dialog = new Window { Title = Tr("Ошибки при чтении папок"), Width = 620, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, errors), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
            await dialog.ShowDialog(this);
        }
    }
    private void OnGalleryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(gallery).Properties.IsLeftButtonPressed) return;
        var card = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (card?.Tag is not GalleryEntry) return;
        dragPress = e; dragStart = e.GetPosition(gallery);
        pressedSelection = card.IsSelected ? project.Stickers.Where(item => gallery.SelectedItems?.OfType<ListBoxItem>().Any(row => row.Tag is GalleryEntry entry && entry.Item == item) == true).ToArray() : null;
    }
    private void ClearInsertion() { if (insertionCard is not null) insertionCard.BorderThickness = new Thickness(0); insertionCard = null; }
    private async void StartGalleryDrag(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(gallery);
        if (busy || dragPress is null || !e.GetCurrentPoint(gallery).Properties.IsLeftButtonPressed || Math.Abs(position.X - dragStart.X) + Math.Abs(position.Y - dragStart.Y) < 8) return;
        var pressed = dragPress; dragPress = null;
        dragItems = pressedSelection ?? project.Stickers.Where(item => gallery.SelectedItems?.OfType<ListBoxItem>().Any(card => card.Tag is GalleryEntry entry && entry.Item == item) == true).ToArray();
        if (dragItems.Length == 0) { dragItems = null; return; }
        try { dragScroll.Start(); using var data = new DataTransfer(); data.Add(DataTransferItem.CreateText("StickerForge gallery items")); await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move); }
        finally { dragScroll.Stop(); scrollDirection = 0; ClearInsertion(); dragItems = null; pressedSelection = null; }
    }
    private async Task SelectAsync()
    {
        if (status.Text?.StartsWith("Предпросмотр GIF:", StringComparison.Ordinal) == true || status.Text?.StartsWith("GIF preview:", StringComparison.Ordinal) == true) status.Text = Tr("Готово");
        ClearVideo();
        StopGif();
        animationBar.IsVisible = false;
        gifPath = null; gifDurations = null; gifPlay.IsVisible = false;
        var generation = ++previewGeneration;
        if (gallery.SelectedItem is not ListBoxItem { Tag: GalleryEntry entry })
        {
            details.Text = Tr("Выберите стикер"); preview.Source = null; previewBitmap?.Dispose(); previewBitmap = null;
            return;
        }
        var item = entry.Item;
        LoadTransform(item.Transform);
        details.Text = preferences.Language == "en" ? $"Loading: {Path.GetFileName(item.SourcePath)}…" : $"Загрузка: {Path.GetFileName(item.SourcePath)}…";
        try
        {
            if (!cache.TryGetValue(item.Id, out var info))
            {
                var result = (await importer.ImportAsync([item.SourcePath]))[0];
                if (generation != previewGeneration) return;
                if (result.Info is null) { details.Text = preferences.Language == "en" ? "Could not read the file." : "Не удалось прочитать файл."; return; }
                info = result.Info; cache[item.Id] = info;
            }
            animationBar.IsVisible = info.Video is not null || info.FrameDurations is { Length: > 1 };
            animationDuration = info.Video?.DurationSeconds ?? (info.FrameDurations is { Length: > 1 } durationFrames ? durationFrames.Sum(x => Math.Max(x, 20)) / 1000d : 0);
            if (animationBar.IsVisible) ConfigureFragment(item.Transform);
            var previewVideo = info.Video;
            if (previewVideo is null && info.FrameDurations is { Length: > 1 } && info.Format.Equals("Gif", StringComparison.OrdinalIgnoreCase))
            {
                try { previewVideo = await ffmpeg.ProbeAsync(item.SourcePath); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.ComponentModel.Win32Exception or TimeoutException or System.Text.Json.JsonException)
                { if (generation == previewGeneration) status.Text = preferences.Language == "en" ? "GIF preview: using fallback playback. Check the file and media components." : "Предпросмотр GIF: используется резервное воспроизведение. Проверьте файл и компоненты обработки."; }
            }
            if (generation != previewGeneration) return;
            if (previewVideo is not null && info.Video is null) { animationDuration = previewVideo.DurationSeconds; ConfigureFragment(item.Transform); }
            var initialStart = animationBar.IsVisible ? (double)(fragmentStart.Value ?? 0) : 0;
            var firstFrame = info.FrameDurations is { Length: > 0 } frameDurations ? FrameAt(frameDurations, initialStart) : 0;
            var bytes = previewVideo is { } initialVideo ? await ffmpeg.ReadTransformedFrameAsync(item.SourcePath, initialVideo, Math.Min(initialStart, Math.Max(0, initialVideo.DurationSeconds - .05)), CopyTransform(item.Transform)) : info.FrameDurations is not null
                ? await Task.Run(() => GifFrameReader.ReadFrame(item.SourcePath, firstFrame))
                : info.FfmpegStill ? await ffmpeg.ReadFrameAsync(item.SourcePath, 0) : await Task.Run(() => File.ReadAllBytes(item.SourcePath));
            if (previewVideo is null) bytes = await Task.Run(() => RenderPreview(bytes, CopyTransform(item.Transform)));
            if (generation != previewGeneration) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
            details.Text = $"{Path.GetFileName(item.SourcePath)}\n{SourceFormat(info, item.SourcePath)} · {info.Width} × {info.Height}\n{(preferences.Language == "en" ? "Ready to export" : "Готов к подготовке")}";
            if (previewVideo is { } video)
            {
                details.Text += $"\n{video.DurationSeconds:0.00} {(preferences.Language == "en" ? "sec" : "с")}";
                videoPath = item.SourcePath; videoMetadata = video;
                updatingSeek = true; videoSeek.Maximum = Math.Max(0, video.DurationSeconds - 0.001); videoSeek.Value = Math.Min(initialStart, videoSeek.Maximum); updatingSeek = false;
                videoPlay.IsVisible = videoSeek.IsVisible = videoTime.IsVisible = true;
                UpdateVideoTime(videoSeek.Value);
            }
            if (previewVideo is null && info.FrameDurations is { Length: > 1 } durations)
            {
                gifPath = item.SourcePath; gifDurations = durations; gifFrame = firstFrame;
                details.Text += $"\n{durations.Sum() / 1000d:0.00} {(preferences.Language == "en" ? "sec" : "с")}";
                gifPlay.IsVisible = videoTime.IsVisible = true; UpdateVideoTime(initialStart);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or TimeoutException or System.Text.Json.JsonException)
        { if (generation == previewGeneration) details.Text = preferences.Language == "en" ? "Could not display the file." : "Не удалось показать файл."; }
    }
    private GalleryEntry? SelectedEntry() => (gallery.SelectedItem as ListBoxItem)?.Tag as GalleryEntry;
    private void ConfigureFragment(TransformSettings transform)
    {
        sourceDurationLabel.Text = $"{(preferences.Language == "en" ? "Source" : "Источник")}: {FormatTime(animationDuration)}";
        SetFragmentFields(transform.StartSeconds, transform.DurationSeconds);
        videoTime.IsVisible = true;
        UpdateVideoTime((double)(fragmentStart.Value ?? 0));
    }
    private void SetFragmentFields(double start, double duration)
    {
        if (animationDuration <= 0 || !double.IsFinite(animationDuration)) return;
        var minimum = Math.Min(.05, animationDuration);
        var maxStart = Math.Min(36000, Math.Max(0, animationDuration - minimum));
        start = Math.Clamp(double.IsFinite(start) ? start : 0, 0, maxStart);
        duration = Math.Clamp(double.IsFinite(duration) ? duration : minimum, minimum, Math.Min(3, animationDuration - start));
        updatingFragment = true;
        fragmentStart.Maximum = (decimal)maxStart;
        fragmentDuration.Minimum = 0; fragmentDuration.Maximum = (decimal)Math.Min(3, animationDuration); fragmentDuration.Minimum = (decimal)minimum;
        fragmentStart.Value = (decimal)start; fragmentDuration.Value = (decimal)duration;
        updatingFragment = false;
        SyncTimeline(Math.Clamp(timeline.Current, 0, animationDuration));
    }
    private async Task FragmentFieldsChangedAsync()
    {
        if (updatingTransform || updatingFragment || !animationBar.IsVisible) return;
        SetFragmentFields((double)(fragmentStart.Value ?? 0), (double)(fragmentDuration.Value ?? .05m));
        await TransformChangedAsync();
    }
    private void SyncTimeline(double current)
    {
        if (animationDuration <= 0) return;
        timeline.SetState(animationDuration, (double)(fragmentStart.Value ?? 0), (double)(fragmentDuration.Value ?? .05m), current);
    }
    private async Task SeekAnimationAsync(double seconds)
    {
        if (videoPath is not null && videoMetadata is not null)
        {
            var playing = videoPlayback is not null;
            StopVideo(); updatingSeek = true; videoSeek.Value = Math.Clamp(seconds, 0, videoSeek.Maximum); updatingSeek = false;
            UpdateVideoTime(videoSeek.Value);
            if (playing) StartVideo(); else await ShowSeekFrameAsync(videoPath, videoSeek.Value, ++previewGeneration);
        }
        else if (gifPath is { } path && gifDurations is { Length: > 0 } durations && SelectedEntry() is { } entry)
        {
            StopGif(); var index = FrameAt(durations, seconds); var generation = ++previewGeneration; var settings = CopyTransform(entry.Item.Transform);
            try
            {
                var bytes = await Task.Run(() => RenderPreview(GifFrameReader.ReadFrame(path, index), settings));
                if (generation != previewGeneration) return;
                gifFrame = index; SetPreview(bytes); UpdateVideoTime(durations.Take(index).Sum(x => Math.Max(x, 20)) / 1000d);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { if (generation == previewGeneration) status.Text = preferences.Language == "en" ? "Could not seek the animation." : "Не удалось перемотать анимацию."; }
        }
    }
    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss\.ff") : time.ToString(@"mm\:ss\.ff");
    }
    private static TransformSettings CopyTransform(TransformSettings t) => new() { Fit = t.Fit, Scale = t.Scale, OffsetX = t.OffsetX, OffsetY = t.OffsetY, Rotation = t.Rotation, FlipHorizontal = t.FlipHorizontal, FlipVertical = t.FlipVertical, StartSeconds = t.StartSeconds, DurationSeconds = t.DurationSeconds };
    private void LoadTransform(TransformSettings t)
    {
        updatingTransform = true; updatingFragment = true;
        fragmentStart.Maximum = 36000; fragmentDuration.Maximum = 3; fragmentDuration.Minimum = .01m;
        fitMode.SelectedIndex = (int)t.Fit; scale.Value = t.Scale; offsetX.Value = t.OffsetX; offsetY.Value = t.OffsetY; rotation.Value = t.Rotation; flipHorizontal.IsChecked = t.FlipHorizontal; flipVertical.IsChecked = t.FlipVertical; fragmentStart.Value = (decimal)Math.Clamp(t.StartSeconds, 0, 36000); fragmentDuration.Value = (decimal)Math.Clamp(t.DurationSeconds, .01, 3);
        updatingFragment = false;
        updatingTransform = false;
        UpdateTransformLabels();
    }
    private void UpdateTransformLabels()
    {
        scaleLabel.Text = Tr("Масштаб, %");
        rotationLabel.Text = Tr("Поворот, °");
        offsetXLabel.Text = Tr("Положение X");
        offsetYLabel.Text = Tr("Положение Y");
    }
    private void RotateBy(double degrees) => rotation.Value = ((rotation.Value + degrees + 180) % 360 + 360) % 360 - 180;
    private void ResetGeometry(bool resetFlips = false)
    {
        updatingTransform = true;
        scale.Value = 1; offsetX.Value = 0; offsetY.Value = 0; rotation.Value = 0;
        if (resetFlips) { flipHorizontal.IsChecked = false; flipVertical.IsChecked = false; }
        updatingTransform = false;
        UpdateTransformLabels();
    }
    private async Task FitModeChangedAsync()
    {
        if (updatingTransform || refreshing || busy || SelectedEntry() is null) return;
        ResetGeometry();
        await TransformChangedAsync();
    }
    private async Task TransformChangedAsync()
    {
        if (updatingTransform || refreshing || busy || SelectedEntry() is not { } entry) return;
        var t = entry.Item.Transform; t.Fit = fitMode.SelectedIndex is >= 0 and <= 3 ? (FitMode)fitMode.SelectedIndex : FitMode.Fit;
        t.Scale = scale.Value; t.OffsetX = offsetX.Value; t.OffsetY = offsetY.Value; t.Rotation = rotation.Value;
        t.FlipHorizontal = flipHorizontal.IsChecked == true; t.FlipVertical = flipVertical.IsChecked == true;
        t.StartSeconds = (double)(fragmentStart.Value ?? 0); t.DurationSeconds = (double)(fragmentDuration.Value ?? 3);
        SyncTimeline(t.StartSeconds);
        MarkDirty();
        if (!cache.TryGetValue(entry.Item.Id, out var info)) return;
        if (videoMetadata is not null && videoPath == entry.Item.SourcePath)
        {
            if (videoPlayback is null)
            {
                updatingSeek = true; videoSeek.Value = Math.Min(t.StartSeconds, videoSeek.Maximum); updatingSeek = false;
                UpdateVideoTime(videoSeek.Value); await ShowSeekFrameAsync(entry.Item.SourcePath, videoSeek.Value, ++previewGeneration);
            }
            else { StopVideo(); StartVideo(); }
            return;
        }
        if (info.FrameDurations is not null)
        {
            if (gifPlayback is null)
            {
                var frameGeneration = ++previewGeneration; var frameSettings = CopyTransform(t); var index = FrameAt(info.FrameDurations, frameSettings.StartSeconds);
                try { var frame = await Task.Run(() => RenderPreview(GifFrameReader.ReadFrame(entry.Item.SourcePath, index), frameSettings)); if (frameGeneration == previewGeneration) { gifFrame = index; SetPreview(frame); UpdateVideoTime(info.FrameDurations.Take(index).Sum(x => Math.Max(x, 20)) / 1000d); } }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { if (frameGeneration == previewGeneration) details.Text = preferences.Language == "en" ? "Could not display the file." : "Не удалось показать файл."; }
            }
            return;
        }
        var generation = ++previewGeneration; var snapshot = CopyTransform(t);
        try
        {
            var input = info.FfmpegStill ? await ffmpeg.ReadFrameAsync(entry.Item.SourcePath, 0) : await Task.Run(() => File.ReadAllBytes(entry.Item.SourcePath));
            var bytes = await Task.Run(() => RenderPreview(input, snapshot));
            if (generation != previewGeneration) return;
            using var stream = new MemoryStream(bytes); var bitmap = new Bitmap(stream);
            preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { status.Text = preferences.Language == "en" ? "Could not display the preview." : "Не удалось показать предпросмотр."; }
    }
    private static byte[] RenderPreview(byte[] bytes, TransformSettings transform)
    {
        using var original = SKImage.FromEncodedData(bytes) ?? throw new InvalidDataException("Image decoding failed.");
        using var rendered = StickerPackExporter.Render(original, transform);
        using var encoded = rendered.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
    private static int FrameAt(int[] durations, double seconds)
    {
        var ms = Math.Max(0, seconds * 1000); var elapsed = 0d;
        for (var i = 0; i < durations.Length - 1; i++) { elapsed += Math.Max(durations[i], 20); if (ms < elapsed) return i; }
        return durations.Length - 1;
    }
    private void SetPreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); var bitmap = new Bitmap(stream);
        preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
    }
    private void UpdateVideoTime(double seconds)
    {
        videoTime.Text = $"{FormatTime(seconds)} / {FormatTime(animationDuration)}";
        SyncTimeline(seconds);
    }
    private void StopVideo()
    {
        videoPlayback?.Cancel(); videoPlayback = null; videoPlay.Content = Tr("Воспроизвести");
    }
    private void ClearVideo()
    {
        videoSeekRequest?.Cancel(); videoSeekRequest = null;
        StopVideo(); videoPath = null; videoMetadata = null;
        videoPlay.IsVisible = videoSeek.IsVisible = videoTime.IsVisible = false;
        previewGeneration++;
    }
    private void StartVideo()
    {
        if (videoPath is null || videoMetadata is null) return;
        videoSeekRequest?.Cancel(); videoSeekRequest = null;
        var source = new CancellationTokenSource(); videoPlayback = source; videoPlay.Content = Tr("Пауза");
        _ = PlayVideoAsync(videoPath, videoMetadata, videoSeek.Value, source);
    }
    private async Task PlayVideoAsync(string path, VideoMetadata metadata, double start, CancellationTokenSource source)
    {
        try
        {
            while (true)
            {
                var settings = SelectedEntry()?.Item.Transform;
                var loopStart = Math.Clamp(settings?.StartSeconds ?? 0, 0, Math.Max(0, metadata.DurationSeconds - .05));
                var loopEnd = Math.Min(metadata.DurationSeconds, loopStart + (settings?.DurationSeconds ?? 3));
                if (start < loopStart || start >= loopEnd) start = loopStart;
                var clock = Stopwatch.StartNew();
                var received = false;
                var transform = CopyTransform(settings ?? new());
                await foreach (var frame in ffmpeg.StreamFramesAsync(path, metadata, start, source.Token, transform))
                {
                    if (frame.Seconds >= loopEnd) break;
                    received = true;
                    var delay = frame.Seconds - start - clock.Elapsed.TotalSeconds;
                    if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), source.Token);
                    source.Token.ThrowIfCancellationRequested();
                    var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
                    using (var locked = bitmap.Lock()) for (var y = 0; y < frame.Height; y++) Marshal.Copy(frame.Rgba, y * frame.Width * 4, IntPtr.Add(locked.Address, y * locked.RowBytes), frame.Width * 4);
                    preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
                    updatingSeek = true; videoSeek.Value = Math.Min(frame.Seconds, videoSeek.Maximum); updatingSeek = false;
                    UpdateVideoTime(frame.Seconds);
                }
                source.Token.ThrowIfCancellationRequested();
                if (!received) throw new InvalidDataException("Video decoder returned no frames.");
                start = loopStart;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or System.ComponentModel.Win32Exception)
        { status.Text = preferences.Language == "en" ? "Could not play the video." : "Не удалось воспроизвести видео."; }
        finally
        {
            if (ReferenceEquals(videoPlayback, source)) { videoPlayback = null; videoPlay.Content = Tr("Воспроизвести"); }
            source.Dispose();
        }
    }
    private async Task ShowSeekFrameAsync(string path, double seconds, long generation)
    {
        videoSeekRequest?.Cancel();
        var source = new CancellationTokenSource();
        videoSeekRequest = source;
        try
        {
            var transform = CopyTransform(SelectedEntry()?.Item.Transform ?? new());
            if (videoMetadata is null) return;
            var bytes = await ffmpeg.ReadTransformedFrameAsync(path, videoMetadata, seconds, transform, source.Token);
            if (generation != previewGeneration || videoPlayback is not null) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or System.ComponentModel.Win32Exception)
        { if (generation == previewGeneration) status.Text = preferences.Language == "en" ? "Could not seek the animation." : "Не удалось перемотать анимацию."; }
        finally { if (ReferenceEquals(videoSeekRequest, source)) videoSeekRequest = null; source.Dispose(); }
    }
    private void StopGif()
    {
        gifPlayback?.Cancel();
        gifPlayback = null;
        gifPlay.Content = Tr("Воспроизвести");
    }
    private void ToggleGif()
    {
        if (gifPlayback is not null) { StopGif(); return; }
        if (gifPath is null || gifDurations is null) return;
        var source = new CancellationTokenSource();
        gifPlayback = source;
        gifPlay.Content = Tr("Пауза");
        _ = PlayGifAsync(gifPath, gifDurations, source);
    }
    private async Task PlayGifAsync(string path, int[] durations, CancellationTokenSource source)
    {
        try
        {
            while (true)
            {
                await Task.Delay(Math.Max(durations[gifFrame], 20), source.Token);
                var transformSettings = SelectedEntry()?.Item.Transform;
                var begin = (transformSettings?.StartSeconds ?? 0) * 1000;
                var end = begin + (transformSettings?.DurationSeconds ?? 3) * 1000;
                var elapsed = 0d; var first = 0;
                while (first < durations.Length - 1 && elapsed + Math.Max(durations[first], 20) <= begin) elapsed += Math.Max(durations[first++], 20);
                var next = gifFrame + 1;
                var nextTime = durations.Take(Math.Min(next, durations.Length)).Sum(x => Math.Max(x, 20));
                if (next >= durations.Length || nextTime >= end || next < first) next = first;
                var bytes = await Task.Run(() => GifFrameReader.ReadFrame(path, next, source.Token), source.Token);
                source.Token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(bytes);
                var transform = CopyTransform(SelectedEntry()?.Item.Transform ?? new());
                var rendered = await Task.Run(() => RenderPreview(bytes, transform), source.Token);
                source.Token.ThrowIfCancellationRequested(); SetPreview(rendered); gifFrame = next;
                UpdateVideoTime(durations.Take(next).Sum(x => Math.Max(x, 20)) / 1000d);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { status.Text = preferences.Language == "en" ? "Could not play the animation." : "Не удалось воспроизвести анимацию."; }
        finally
        {
            if (ReferenceEquals(gifPlayback, source)) { gifPlayback = null; gifPlay.Content = Tr("Воспроизвести"); }
            source.Dispose();
        }
    }
    private static byte[] RenderFrame(VideoFrame frame, TransformSettings transform)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(frame.Rgba, 0, bitmap.GetPixels(), frame.Rgba.Length);
        using var source = SKImage.FromBitmap(bitmap); using var result = StickerPackExporter.Render(source, transform);
        using var encoded = result.Encode(SKEncodedImageFormat.Png, 100); return encoded.ToArray();
    }
    private Task RemoveSelected()
    {
        if (busy) return Task.CompletedTask;
        var selected = gallery.SelectedItems?.OfType<ListBoxItem>().Select(x => ((GalleryEntry)x.Tag!).Item).ToArray() ?? [];
        if (selected.Length == 0) return Task.CompletedTask;
        foreach (var item in selected) { project.Stickers.Remove(item); cache.Remove(item.Id); }
        MarkDirty(); RefreshGallery(); return Task.CompletedTask;
    }
    private Task MoveSelected(int delta)
    {
        if (busy || gallery.SelectedItems?.Count != 1 || gallery.SelectedItem is not ListBoxItem { Tag: GalleryEntry entry }) return Task.CompletedTask;
        var item = entry.Item;
        var index = project.Stickers.IndexOf(item); var target = index + delta;
        if (target < 0 || target >= project.Stickers.Count) return Task.CompletedTask;
        project.Stickers.RemoveAt(index); project.Stickers.Insert(target, item); MarkDirty(); RefreshGallery(); gallery.SelectedIndex = target;
        return Task.CompletedTask;
    }
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.N) await NewAsync();
            else if (e.Key == Key.O) await OpenAsync();
            else if (e.Key == Key.S) await SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            else if (e.Key == Key.I) await PickFilesAsync();
            else return;
            e.Handled = true;
        }
    }
    private sealed record GalleryEntry(StickerItem Item, Bitmap? Thumbnail, Border Frame, Border ActiveBadge);
}
