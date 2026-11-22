# ImageSpace 0.3.2-alpha.1: проверка возможностей для M1.3

Исследованы исходники [тега v0.3.2-alpha.1, коммит 123d26e318f26f613a68cb1dcd89199619670106][tree], и текущее использование в StickerForge. Выводы о библиотеке сделаны по реализации, не по интерфейсу или README. Наличие API не означает, что функция уже подключена к StickerForge или проверена на всех ОС.

В `StickerForge.Rendering` напрямую подключены `ImageSpace.Editing`, `ImageSpace.Documents`, `ImageSpace.Skia` 0.3.2-alpha.1 и SkiaSharp 3.119.4. По проектным ссылкам исходников Editing использует Imaging и Filters, а те — Core; Documents и Skia используют Core, Skia также использует SkiaSharp. Эти проекты на `net10.0` не ссылаются на Uno или ONNX: [Editing][editing-project], [Imaging][imaging-project], [Filters][filters-project], [Documents][documents-project], [Skia][skia-project], [Core][core-project]. Это анализ зафиксированного графа; состав дистрибутива и нативные библиотеки проверяются отдельно при публикации.

Обозначения: **M1.2** — подключено в текущем редакторе; **API** — реализация есть, но необходимы адаптер, UI и тесты; **ограничено** — реализация уже накладывает существенные границы; **нет** — необходимая возможность не подтверждена этим API.

| Возможность | Подтверждённые API / библиотека | Готовность и ограничения |
|---|---|---|
| Цвет, размер, жёсткость, непрозрачность, flow и spacing кисти | `BrushSettings`, `BrushEngine.Begin/Paint/End`, `Rgba32` — [Imaging][brush], [Core][layer] | **M1.2**: кисть работает; настройки в `GraphicsSession` зафиксированы. **API**: значения можно передавать в `Paint`, нужна панель, валидация и тесты. Давление передаётся параметром `pressure`; получение давления от пера в текущем Avalonia-холсте не подтверждено. |
| Типы мазков | `PaintMode.Brush`, `Pencil`, `Eraser`, `Clone`, `Dodge`, `Burn`, `Smudge` — [BrushEngine][brush] | **API**: алгоритмы присутствуют; это не библиотека импортируемых Photoshop-кистей или подтверждение сложных текстурных наконечников. Clone использует смещение и снимок источника, поэтому для него нужен интерфейс выбора источника. |
| Ластик | `PaintMode.Eraser` — [BrushEngine][brush] | **M1.2**: работает на выбранном слое. Изменяет альфу растрового слоя; для обратимого сокрытия содержимого нужна маска. |
| Слои: видимость, порядок, непрозрачность, наложение | `Layer.Visible/Opacity/Blend`; `EditorSession.AddLayer/DuplicateLayer/DeleteLayer/MoveLayer/Execute`; [Core][layer], [Editing][session], [ImageRenderer.DrawLayer][renderer] | **M1.2**: добавление, выбор, порядок и видимость. **API**: непрозрачность и перечисленные `LayerBlend` рендерятся, но UI и roundtrip-тесты каждого режима нужны. Список слоёв плоский; группа слоёв в `LayerKind` не определена. |
| Выделение | `Selections.Rectangle` (включая эллипс), `Polygon`, `Contiguous`, `Combine`, `Invert`, `Bounds` — [Imaging][selections]; `SelectionMorphology.Apply`, `EditorSession.ModifySelection` — [morphology][morphology], [Editing][selection-session] | **API**: маска покрытия, геометрия, цветовая смежная область и морфология; контуры и жесты нужны в Avalonia. Автоматическое AI-выделение не входит в изученный набор. Текущее выделение не сохраняется в архиве. |
| Маски | `Layer.Mask/MaskEnabled/MaskDensity/MaskFeather/MaskLinked/MaskPlacement`; `EditorSession.AddMask/SelectMask/SetMaskProperties/SetMaskEnabled/DeleteMask/InvertMask/LoadMaskSelection/SetMaskLinked` — [Core][layer], [команды][masks], [позиционирование][mask-placement] | **API**: присутствуют пиксельные маски и параметры. `ApplyLayerMask` через `ImageRenderer.BakeLayerMask` запекает маску в пиксели и является изменением содержимого, хотя операция может быть отменена — [Editing][mask-apply], [Skia][mask-render]. Маски групп в этой плоской модели не подтверждены. |
| Перемещение, масштаб и вращение слоя | `Layer.X/Y/ScaleX/ScaleY/Rotation`, `Layer.Transform/ToLocal/ToDocument`, `EditorSession.Execute` — [Core][layer], [Editing][session] | **API**: параметры трансформации и рендер присутствуют; нужны выбор объекта, рамка, ручки и транзакции жеста. Параметры слоя не равны масштабированию всего холста. В M1.2 реализован только zoom/pan вида. |
| Текст и шрифты | `Layer.Kind=Text`, `Text`, `FontFamily`, `FontSize`, `Bold`, цвет; `ImageRenderer.DrawLayer/SetTypeface` — [Core][layer], [Skia][renderer] | **Ограничено**: данные текста и простой рендер строк существуют, но редактор текста StickerForge отсутствует. В исследованном `DrawLayer` используется один `_typeface` рендерера или `SKTypeface.Default`: сам `Layer.FontFamily` не выбирает шрифт на каждый слой. Нельзя обещать независимые шрифты, shaping/IME или одинаковый вид после переноса без отдельного адаптера и проверок. |
| Фигуры | `LayerKind.Rectangle/Ellipse`, `Color`, `StrokeColor`, `StrokeWidth`, `CornerRadius`; отрисовка — [Core][layer], [Skia][renderer] | **API**: редактируемые параметры двух примитивов; произвольный векторный путь, узлы и полноценный векторный редактор не подтверждены. |
| Фильтры | `FilterEngine.Apply/Blur`, `FilterKind`, `EditorSession.ApplyFilter`, `PixelFilterPipeline` — [Filters][filter], [Editing][session] | **API**: реализованы цветовые и пространственные фильтры. Применение через `ApplyFilter` заменяет редактируемую поверхность, это не произвольно переупорядочиваемый недеструктивный стек. UI, отмена и корректность альфы должны проверяться для конкретного фильтра. |
| Цветокоррекция | `AdjustmentKind`, `CurvesAdjustment`, `LevelsAdjustment`, `RgbLookupTables`, `ImageRenderer` / `AdjustmentFilterCache` — [Core][layer], [lookup][lookup], [Skia][adjustments] | **API**: есть корректирующие слои и значения кривых/уровней, отображаемые рендерером; нужен собственный UI и проверка сохранения конкретных настроек. Не утверждаем наличие произвольного стека эффектов или равенство всем фильтрам. |
| Undo/Redo | `EditorSession.Begin/Commit/Cancel/Execute`, `Undo/Redo`, `HistoryBudget` — [Editing][session] | **M1.2**: история сессии используется. Снимки ограничены по памяти (по умолчанию 192 МиБ) и числу записей (64); после открытия архива история не восстанавливается. |
| Рендер и PNG | `ImageRenderer.Draw`, `Export`, `Decode`, `Prune/Dispose` — [Skia][renderer] | **M1.2**: Skia-холст и PNG применяются. Альфа и соответствие проверяются отдельно для новых функций; экранный DPI и потоковое владение ресурсами требуют UI-тестов. |

## Сохранение и его пределы

[`DocumentArchive.Save/Load`][archive] записывает manifest, метаданные слоёв, RGBA пиксели и маски; версии архива 1–4 выбираются по используемым свойствам. Это позволяет сохранять параметры текста, фигур, трансформаций, наложения и корректирующих слоёв как метаданные, а результат растровой кисти/ластика/применённого фильтра — как изменённые пиксели. Для каждой новой функции нужен реальный тест «редактирование → сохранение → открытие → редактирование и рендер», а не предположение, что все режимы сериализуются без потерь.

`ImageDocument.Selection` и `EditMask` присутствуют в модели, но поля соответствующего manifest и запись selection в реализации отсутствуют. История `EditorSession` также отсутствует в архиве: выделение, активный режим редактирования маски и undo/redo после закрытия документа не восстанавливаются. Источник кисти не хранится как список мазков; изменение пикселей отменяемо в текущей сессии, но не является общим неразрушающим графом операций.

Лимиты реализации: [`PixelSurface`][surface] ограничивает сторону 8192 и площадь 16 777 216 пикселями; [`ImageDocument.Validate`][document] — 128 слоями; архив ограничен 128 МиБ сжатых и 384 МиБ распакованных данных. Это лимиты конкретной версии, а не рекомендации по размеру интерфейса. Формат StickerForge версии 2 хранит ссылки на отдельные `.imagespace` в `.assets`, см. [действующее описание](GRAPHICS_EDITOR.md).

## Следствия для будущей анимации

В изученной модели документ содержит слои статического изображения, а не таймлайн, кадровые дорожки или ключевые кадры. Нельзя обещать, что сохранение ImageSpace автоматически сохранит редактирование GIF/видео или обеспечит одинаковые эффекты на каждом кадре. Для этого требуется отдельная модель времени и контракт представления кадра; GIF/видео M1.1 должны продолжать работать без миграции и редактирования до утверждения отдельного этапа.

[tree]: https://github.com/wieslawsoltes/ImageSpace/tree/123d26e318f26f613a68cb1dcd89199619670106
[core-project]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Core/ImageSpace.Core.csproj
[imaging-project]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Imaging/ImageSpace.Imaging.csproj
[filters-project]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Filters/ImageSpace.Filters.csproj
[editing-project]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Editing/ImageSpace.Editing.csproj
[documents-project]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Documents/ImageSpace.Documents.csproj
[skia-project]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Skia/ImageSpace.Skia.csproj
[brush]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Imaging/BrushEngine.cs
[layer]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Core/Layer.cs
[document]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Core/ImageDocument.cs
[surface]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Core/PixelSurface.cs
[renderer]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Skia/ImageRenderer.cs
[session]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Editing/EditorSession.cs
[selections]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Imaging/Selections.cs
[morphology]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Imaging/SelectionMorphology.cs
[selection-session]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Editing/EditorSession.Selections.cs
[masks]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Editing/EditorSession.Masks.cs
[mask-placement]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Editing/EditorSession.MaskPlacement.cs
[mask-apply]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Editing/EditorSession.MaskApplication.cs
[mask-render]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Skia/ImageRenderer.MaskApplication.cs
[filter]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Filters/FilterEngine.cs
[lookup]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Core/RgbLookupTables.cs
[adjustments]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Skia/AdjustmentFilterCache.cs
[archive]: https://github.com/wieslawsoltes/ImageSpace/blob/123d26e318f26f613a68cb1dcd89199619670106/src/ImageSpace.Documents/DocumentArchive.cs
