# Компоненты Windows x64

Пакет включает FFmpeg и FFprobe версии `n9.0.2-62-gac1c6e003f-20261123`, вариант Windows x64 LGPL, из [закреплённого релиза FFmpeg-Builds](https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/tag/autobuild-2026-11-23-12-43). Архив: `ffmpeg-n9.0.2-62-gac1c6e003f-win64-lgpl-9.0.zip`; SHA-256 `E174AF1BC75771DB087A1E9AB789D1F004215CA5B545D5CE826E2C8EEC873EFB`.

- `ffmpeg.exe`: SHA-256 `ACE36305D537A20868A776819BE530D3CE042CF93F70ED07A9CBDBBE7B03579C`.
- `ffprobe.exe`: SHA-256 `F7D94376A390D38BAAEB151D29E37E94E9E374B0664E0436C8C3BA69A7A95AAE`.
- FFmpeg: [исходный код commit ac1c6e003f](https://github.com/FFmpeg/FFmpeg/tree/ac1c6e003f), LGPL версии 3 или позднее; текст поставляется как `licenses/FFmpeg-LGPL-3.0.txt`. Конфигурация сборки доступна через `ffmpeg.exe -version` и включает `--enable-version3`, `--enable-libvpx`, без `--enable-gpl` и `--enable-nonfree`.
- [Рецепт сборки и закреплённые зависимости](https://github.com/FFmpeg-Builds/FFmpeg-Builds/tree/b9ebe81995b58cd4e3177a6c0ad5dc2392f7f7fd). В частности, libvpx построен из [7e000a41a7727d5b9477dbbe5727431560d6a16c](https://chromium.googlesource.com/webm/libvpx/+/7e000a41a7727d5b9477dbbe5727431560d6a16c); его лицензионное и патентное уведомления включены в папку `licenses`.
- .NET, Avalonia и SkiaSharp поставляются с приложением; сведения о лицензиях проекта приведены в `licenses/PROJECT-LICENSING.md`.

FFmpeg-Builds включает множество дополнительных статически связанных библиотек. Полный инвентарь их лицензий, соответствующих исходных текстов и материалов для перелинковки этой конкретной сборки ещё не подтверждён. Поэтому этот пакет предназначен для проверки, а не для объявления готовым публичным релизом до завершения проверки условий распространения.
