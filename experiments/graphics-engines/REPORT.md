# StickerForge: аудит графических движков

Дата: 23 ноября 2026. Ветка: `experiment/graphics-engine-comparison`.
Прототипы не подключены к solution StickerForge и не меняют его формат проектов.

## Зафиксированные источники

- ImageSpace: NuGet `0.3.2-alpha.1`, исходный [тег и коммит 123d26e318f26f613a68cb1dcd89199619670106](https://github.com/wieslawsoltes/ImageSpace/tree/123d26e318f26f613a68cb1dcd89199619670106). MIT: [LICENSE](https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/LICENSE), [сторонние компоненты](https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/THIRD-PARTY-NOTICES.md). Пакеты 0.5.1-alpha.1, упомянутые в текущем README upstream, не нашлись при восстановлении из настроенного nuget.org; использована реально доступная версия.
- Composa: версия 1.4.0, [коммит 86529d3b5355d28ddc28ec3db2e4de1d4d00f63b](https://github.com/dvdstelt/Composa/tree/86529d3b5355d28ddc28ec3db2e4de1d4d00f63b), исходный `Composa.Core` через ProjectReference. MIT: [LICENSE](https://github.com/dvdstelt/Composa/blob/86529d3b5355d28ddc28ec3db2e4de1d4d00f63b/LICENSE), [сторонние компоненты](https://github.com/dvdstelt/Composa/blob/86529d3b5355d28ddc28ec3db2e4de1d4d00f63b/packaging/THIRD-PARTY-NOTICES.txt).

Для распространения необходимо сохранить тексты лицензий и уведомления для реально поставляемых компонентов и отдельно проверить транзитивные нативные библиотеки. Здесь исходный код upstream в Git StickerForge не добавляется.

## Фактические проверки

Windows, .NET SDK 10.0.110, конфигурация Release, SkiaSharp 3.119.4. Оба прототипа — консольные автоматические проверки: ненулевой код выхода при ошибке. Это не тесты UI.

| Сценарий | ImageSpace | Composa.Core |
|---|---|---|
| Restore и сборка | прошли | прошли; upstream выдал предупреждение CS8602 в EditorSession.Pixels.cs:687 |
| Прозрачный документ, два слоя | прошли | прошли |
| Настоящий мазок кистью | проверен по альфе пикселя | проверен по альфе композиции |
| Undo и Redo | проверены по пикселю | проверены по композиции |
| Сохранение на диск и открытие | нативный архив `.imagespace`; восстановлены слои и мазок | `ProjectFile.Save/Load`; восстановлены слои, мазок проверен итоговым PNG |
| Прозрачный PNG | после восстановления: прозрачный угол и непрозрачный пиксель мазка | после восстановления: прозрачный угол и непрозрачный пиксель мазка |
| Avalonia без Uno | зависимости движка восстановлены без Uno; реальный control не проверен | Core не требует Avalonia; реальный control не проверен |

Не проверены частичная прозрачность и все режимы наложения, сложные мазки, большой документ, стабильность памяти, интеграция с UI, GIF/видео, Linux и macOS. Эти результаты не являются проверкой редактора StickerForge. Модели AI не скачивались и не использовались.

Дополнительно `dotnet build StickerForge.sln -c Release --no-restore` прошёл без ошибок и предупреждений; `dotnet test StickerForge.sln -c Release --no-build --no-restore` — 15 пройдено, 1 пропущен (`RealMp4AndWebmDecodeProbeSeekAndCancel`), ошибок нет. Причина пропуска в выводе не указана; считать эту интеграционную видеопроверку выполненной нельзя.

## Запуск

Из корня репозитория, установлен .NET 10 и доступен NuGet:

```powershell
dotnet run --project experiments/graphics-engines/ImageSpace/Experiment.csproj -c Release
powershell -ExecutionPolicy Bypass -File experiments/graphics-engines/Composa/setup.ps1
dotnet run --project experiments/graphics-engines/Composa/Experiment.csproj -c Release
```

`setup.ps1` выполняется один раз: частичный checkout в игнорируемую `.external/composa`, с исключением `Models`. Исходники не попадают в коммит StickerForge. Скрипт использует Git и сеть. Восстановление Composa скачивает пакет ONNX Runtime, хотя модели не используются. Для Linux/macOS понадобится выполнить аналогичные команды Git из setup.ps1 и проверить их на целевой машине; эти шаги здесь не выполнялись.

Диагностика удаления AI намеренно **не собирается**:

```powershell
dotnet build experiments/graphics-engines/Composa/NoAIProbe/NoAIProbe.csproj -c Release
```

Это самостоятельный исследовательский проект, не зависимость рабочего прототипа. Он исключает Vision, Subject и Upscale и не подключает ONNX. Ожидаемые ошибки отсутствующих `Vision`, `ResampleMode` и `EnhancedLayers` демонстрируют связи оставшегося кода. Из этого не следует, что полное отделение невозможно; лишь простое исключение файлов оказалось недостаточным.

## Зависимости и размер

Фактический `dotnet list package --include-transitive` для ImageSpace: `ImageSpace.Documents`, `Editing`, `Skia`, `Core`, `Filters`, `Imaging` 0.3.2-alpha.1; `SkiaSharp` и NativeAssets Win32/macOS 3.119.4. Uno и ONNX в этом графе не обнаружены. Для Linux потребуется проверить подключение и загрузку нативных assets отдельно.

Composa: `SkiaSharp` 3.119.4, NativeAssets Windows/macOS/Linux 3.119.4, `Microsoft.ML.OnnxRuntime` и `Microsoft.ML.OnnxRuntime.Managed` 1.30.0, `System.Numerics.Tensors` 9.0.0. ONNX — обязательная ссылка исходного [Composa.Core.csproj](https://github.com/dvdstelt/Composa/blob/86529d3b5355d28ddc28ec3db2e4de1d4d00f63b/src/Composa.Core/Composa.Core.csproj). Его наличие нарушает требование StickerForge не включать нейросетевые зависимости при прямой интеграции.

Размеры каталогов `bin/Release/net10.0`, рекурсивно, после обычной сборки на Windows: ImageSpace 304 004 002 байта, Composa 708 924 603 байта. Это не размер дистрибутива: каталоги содержат нативные файлы для нескольких платформ, относятся к framework-dependent сборкам и зависят от пакетов. Самостоятельный publish для конкретного RID не измерялся, сравнение влияния на существующее приложение не проведено.

## Интеграция и рекомендация

ImageSpace предоставляет раздельные библиотеки модели, кистей, транзакций, файлов и Skia. Прототип действительно использовал `ImageDocument`, `BrushEngine`, `EditorSession`, `DocumentArchive` и `ImageRenderer` без Uno. У upstream интерактивные control и workspace относятся к Uno, поэтому в Avalonia нужно реализовать или адаптировать обработку ввода, отображение холста, выделения и управляющие панели. Само наличие библиотек не означает готового Avalonia-редактора. Пакеты — alpha, возможны изменения API.

Composa предоставляет работающие команды `EditorSession`, `ProjectFile` и `DocumentRenderer` без `Composa.App`. Однако его ядро целиком зависит от ONNX; удаление AI потребует выделения связанных типов и доработки кода, а также регрессионных тестов обычных инструментов. Существующий Avalonia `CanvasView` находится в приложении Composa и не проверялся как независимый control.

Оба движка имеют собственные модели документов и форматы. Для StickerForge нужен адаптер сессии, хранение ссылок и правок в совместимом с проектом виде, управление ресурсами и отдельно архитектура применения правок к кадрам GIF/видео. Прямой замены `StickerProject` или его формата эксперименты не требуют. Оба эксперимента проверяют растровый документ, а не единый движок редактирования движущегося медиа.

**Рекомендация: ImageSpace для следующей проверки интеграции.** На проверенной версии он уже реализует требуемый срез без Uno и нейросетевых зависимостей, а Composa работает только с ONNX в графе и не отделяется простым исключением AI-файлов. Перед выбором для продукта необходимо проверить пригодность alpha API, Avalonia-ввод и рендеринг, лицензии поставки, производительность, память и реальные платформы. В StickerForge движок в рамках этой задачи не интегрирован.
