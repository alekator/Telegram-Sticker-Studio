# Форматы Telegram

Исследовано 2026-11-22. Это исследование, не реализованный валидатор. Правила зависят от канала публикации и могут измениться; перед релизом перепроверить.

| Формат | Требования и источники |
| --- | --- |
| Static | Для [импорта](https://core.telegram.org/import-stickers): PNG/WEBP, прозрачный слой, максимум 512 KB; одна сторона 512 px, другая не больше 512. Белая обводка и тень описаны как рекомендации оформления. |
| Video | [Требования](https://core.telegram.org/stickers#video-requirements): WEBM/VP9, без аудио, до 30 FPS, до 3 секунд, до 256 KB, одна сторона 512 px, другая не больше. Зацикливание рекомендуется. [Импорт](https://core.telegram.org/import-stickers) дополнительно требует alpha; [API](https://core.telegram.org/api/stickers) говорит, что прозрачность поддерживается. Не считать эти документы взаимозаменяемыми. |
| TGS | [Руководство](https://core.telegram.org/stickers#animation-requirements): 512×512, до 3 секунд и 64 KB, 60 FPS, цикл, объекты внутри холста и ограничения Lottie. [Импорт](https://core.telegram.org/import-stickers) указывает 30–60 FPS; для консервативной совместимости нужен отдельный профиль. GIF/WEBM не являются TGS. |

[Bot API](https://core.telegram.org/bots/api#inputsticker) задаёт 1–20 emoji на стикер; [addStickerToSet](https://core.telegram.org/bots/api#addstickertoset) ограничивает обычные наборы 120, emoji-наборы 200. [MTProto API](https://core.telegram.org/api/stickers) допускает смешанные форматы, но это не доказывает одинаковую поддержку каждым способом создания. Требования к [обложке Bot API](https://core.telegram.org/bots/api#setstickersetthumbnail): 100×100 PNG/WEBP до 128 KB, либо TGS/WEBM до 32 KB; формат должен соответствовать набору. Проверять актуальную документацию выбранного API.

Экспорт ZIP/папки — локальная упаковка для последующей публикации, не заявленный формат прямого импорта Telegram. Возможные способы публикации: ручная загрузка через Telegram или отдельная будущая интеграция с [Bot API](https://core.telegram.org/bots/api#createnewstickerset); последняя требует сеть и не входит в автономный M1.
