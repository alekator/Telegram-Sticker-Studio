# Telegram integration: work in progress

The current branch is not a functional Telegram publisher and is not ready for a real-account test.

Implemented building blocks:

- WTelegramClient 4.4.11 is referenced only by the isolated Telegram project. `TelegramLogin` exposes WTelegramClient's stepwise `Login`: phone first, then the requested verification code or 2FA password; a session path and independently protected session key are required from the caller. It is not wired into the app, and no platform key store exists yet. WTelegramClient logging and logout/session-file deletion must be reviewed before exposing it.
- `StaticStickerPreparer` decodes static PNG/WebP, checks dimensions, resizes proportionally to a 512-pixel long edge and writes a separate PNG preserving alpha, rejecting output above 512 KiB. No source file is changed. It does not yet guarantee the encoded result will be accepted by Telegram; the API documentation describes sticker documents as WebP, so the final upload conversion and media attributes need confirmation against the server.
- `TelegramPublication` exercises the upload/create/confirmation flow against a replaceable API. An exception after the create request is treated as an uncertain server result; it is never retried automatically. This is a preliminary in-memory coordinator, not durable recovery or a production MTProto implementation. Emoji validation currently only checks for non-empty text.

Missing before a manual end-to-end test: OS-backed session key protection; user-facing credential and login flows; a concrete MTProto adapter with the actual upload/check/create/add/get calls; durable reconciliation and FloodWait handling; UI for emoji, progress, cancellation and links; verified PNG/WebP compatibility; GIF/video to VP9 conversion and alpha validation. Import and preview are unchanged. Real credentials and session files must not be checked into Git.

Obtain your own API ID and hash using [Telegram's instructions](https://core.telegram.org/api/obtaining_api_id). Do not enter them into the current prototype: there is no user-facing secure configuration yet. A manual publication checklist will be meaningful only after the missing production components are in place.
