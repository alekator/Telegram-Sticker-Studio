using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace StickerForge.Desktop;

internal static class StudioLocalization
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["Файл"] = "File", ["Правка"] = "Edit", ["Вид"] = "View", ["Стикеры"] = "Stickers", ["Помощь"] = "Help",
        ["Новый проект"] = "New project", ["Открыть проект…"] = "Open project…", ["Сохранить"] = "Save", ["Сохранить как…"] = "Save as…", ["Экспортировать стикеры…"] = "Export stickers…",
        ["Удалить выделенные"] = "Remove selected", ["Сбросить подгонку"] = "Reset adjustments", ["Применить настройки к выделенным"] = "Apply settings to selection",
        ["Показать или скрыть галерею"] = "Show or hide gallery", ["Показать или скрыть свойства"] = "Show or hide properties", ["Уместить в окно"] = "Fit to window", ["Показать 1:1"] = "Show 1:1",
        ["Добавить файлы…"] = "Add files…", ["Добавить папку…"] = "Add folder…", ["Переместить выше"] = "Move up", ["Переместить ниже"] = "Move down", ["О программе"] = "About",
        ["Файлы"] = "Files", ["Папка"] = "Folder", ["Галерея"] = "Gallery", ["Свойства"] = "Properties", ["Отменить"] = "Cancel",
        ["Добавить файлы"] = "Add files", ["Удалить"] = "Remove", ["СТИКЕРЫ"] = "STICKERS", ["ВЫБРАННЫЙ СТИКЕР"] = "SELECTED STICKER",
        ["РЕЖИМ ПОДГОНКИ"] = "PLACEMENT", ["ТОЧНАЯ НАСТРОЙКА"] = "ADJUSTMENTS", ["ОТРАЖЕНИЕ"] = "FLIP", ["ГРУППОВАЯ ПОДГОНКА"] = "BATCH ADJUSTMENTS", ["МАСШТАБ ПРОСМОТРА"] = "VIEW ZOOM",
        ["Масштаб, %"] = "Scale, %", ["Поворот, °"] = "Rotation, °", ["Положение X"] = "Position X", ["Положение Y"] = "Position Y",
        ["По горизонтали"] = "Horizontal", ["По вертикали"] = "Vertical", ["Сбросить поворот"] = "Reset rotation", ["Фрагмент анимации"] = "Animation segment", ["Начало, сек."] = "Start, sec.", ["Длительность, сек."] = "Duration, sec.",
        ["Воспроизвести"] = "Play", ["Пауза"] = "Pause", ["Применить настройки ко всем выделенным"] = "Apply settings to all selected",
        ["Перетащите файлы сюда или нажмите «Добавить файлы»"] = "Drag files here or choose Add files", ["Выберите стикер для редактирования"] = "Select a sticker to edit",
        ["Перетащите файлы или папки сюда либо нажмите «Добавить файлы»"] = "Drag files or folders here, or choose Add files", ["Название набора"] = "Pack name",
        ["Вместить целиком"] = "Fit entire image", ["Заполнить с обрезкой"] = "Fill and crop", ["Растянуть (искажает пропорции)"] = "Stretch (distorts proportions)", ["Вручную"] = "Manual",
        ["Настройки"] = "Settings", ["Язык"] = "Language", ["Оформление"] = "Appearance", ["Тёмная"] = "Dark", ["Светлая"] = "Light", ["Как в системе"] = "System", ["Закрыть"] = "Close",
        ["Перетащите файлы в окно"] = "Drag files into the window", ["PNG · JPG · GIF · MP4"] = "PNG · JPG · GIF · MP4", ["Готово"] = "Ready"
        , ["Выберите стикер"] = "Select a sticker", ["Проект открыт"] = "Project opened", ["Проект сохранён"] = "Project saved",
        ["Экспорт не завершён. Готовый набор не создан."] = "Export incomplete. No sticker pack was created.", ["Экспорт отменён."] = "Export cancelled.",
        ["Добавьте файлы перед экспортом."] = "Add files before exporting.", ["Импорт отменён."] = "Import cancelled.",
        ["Результат экспорта"] = "Export result", ["Результат импорта"] = "Import result", ["Открыть папку"] = "Open folder",
        ["Не удалось подготовить:"] = "Could not prepare:", ["Подготовлены:"] = "Prepared:", ["Неизвестная ошибка."] = "Unknown error.",
        ["Несохранённые изменения"] = "Unsaved changes", ["Отменить несохранённые изменения?"] = "Discard unsaved changes?", ["Не сохранять"] = "Discard", ["Отмена"] = "Cancel",
        ["Открыть проект"] = "Open project", ["Сохранить проект"] = "Save project", ["Добавить папку"] = "Add folder",
        ["Ошибки при чтении папки"] = "Folder reading errors", ["Ошибки при чтении папок"] = "Folder reading errors",
        ["Локальная подготовка изображений и видео для стикеров Telegram."] = "Prepare images and videos locally for Telegram stickers.",
        ["Размер 1:1 не помещается; полотно показано целиком."] = "Actual size does not fit; showing the entire canvas.",
        ["Перед закрытием отмените текущую операцию."] = "Cancel the current operation before closing.",
        ["Экспорт не завершён. Проверьте исходные файлы, FFmpeg и доступ к папке; подробности в журнале экспорта."] = "Export incomplete. Check the source files, media components, and folder permissions; see the export log for details.",
        ["Добавить файлы · Ctrl+I"] = "Add files · Ctrl+I", ["Сохранить проект · Ctrl+S"] = "Save project · Ctrl+S",
        ["Подготовить файлы для Telegram"] = "Prepare files for Telegram", ["Отменить текущую операцию"] = "Cancel current operation",
        ["Изменить ширину галереи"] = "Resize gallery", ["Изменить ширину панели свойств"] = "Resize properties panel",
        ["Повернуть на 90° влево"] = "Rotate 90° left", ["Повернуть на 90° вправо"] = "Rotate 90° right"
        , ["Редактирование"] = "Editing", ["Режим подгонки"] = "Placement", ["Точная настройка"] = "Adjustments", ["Отражение"] = "Flip", ["Масштаб просмотра"] = "View zoom", ["＋  Добавить файлы"] = "＋  Add files", ["Команды проекта"] = "Project commands", ["Создавайте стикеры для Telegram"] = "Create stickers for Telegram"
        , ["Подготовка файлов для Telegram"] = "Preparing files for Telegram", ["Экспорт стикеров…"] = "Exporting stickers…"
        , ["Добавить"] = "Add", ["Открыть"] = "Open", ["Другие действия"] = "More actions"
        , ["Свернуть галерею"] = "Hide gallery", ["Показать галерею"] = "Show gallery", ["Свернуть свойства"] = "Hide properties", ["Показать свойства"] = "Show properties"
        , ["Экспорт завершён!"] = "Export complete!", ["Продолжить работу"] = "Back to editing", ["НЕТ ФАЙЛА"] = "FILE MISSING", ["ТЕКУЩИЙ"] = "CURRENT", ["Лицензии"] = "Licenses"
    };
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, LocalizedKey> TooltipKeys = new();
    public static string Text(string text, string language) => language == "en" && English.TryGetValue(text, out var value) ? value : text;
    public static void Apply(Control root, string language)
    {
        foreach (var control in root.GetSelfAndLogicalDescendants().OfType<Control>())
        {
            if (control is TextBlock block && block.Tag is LocalizedKey label) block.Text = Text(label.Value, language);
            if (control is Button button && button.Tag is LocalizedKey buttonKey) button.Content = Text(buttonKey.Value, language);
            if (control is MenuItem item && item.Tag is LocalizedKey menuKey) item.Header = Text(menuKey.Value, language);
            if (TooltipKeys.TryGetValue(control, out var tooltip)) ToolTip.SetTip(control, Text(tooltip.Value, language));
        }
    }
    public static void Capture(Control root)
    {
        foreach (var control in root.GetSelfAndLogicalDescendants().OfType<Control>())
        {
            if (ToolTip.GetTip(control) is string tooltip && English.ContainsKey(tooltip)) TooltipKeys.GetValue(control, _ => new LocalizedKey(tooltip));
            if (control.Tag is not null) continue;
            var text = control switch { TextBlock block => block.Text, Button button => button.Content as string, MenuItem menu => menu.Header as string, _ => null };
            if (text is not null && English.ContainsKey(text)) control.Tag = new LocalizedKey(text);
        }
    }
    private sealed record LocalizedKey(string Value);
}
