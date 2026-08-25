# Промпты: PDA-мессенджер (Malinov Station)

Пакет самодостаточных промптов для пошаговой реализации программы-мессенджера для КПК.
Каждый файл — полный промпт для **отдельной сессии агента**: скопируй содержимое файла целиком.

## Порядок этапов

| Файл | Этап | Что делает | Гейт человека |
|---|---|---|---|
| [stage-0-skeleton.md](stage-0-skeleton.md) | 0 | Скелет программы: прототип, пустой UI, тест | — |
| [stage-1-contacts.md](stage-1-contacts.md) | 1 | Каталог контактов из записей станции | — |
| [stage-2-p2p.md](stage-2-p2p.md) | 2 | Прямая отправка P2P через DeviceNetwork | — |
| [stage-3-history.md](stage-3-history.md) | 3 | История переписки (в рамках раунда) | — |
| [stage-4-server-relay.md](stage-4-server-relay.md) | 4 | Сервер-ретранслятор станции (passthrough) | — |
| [stage-5-notifications.md](stage-5-notifications.md) | 5 | Popup + звуковые уведомления | ✅ звук проверяется на слух |
| [stage-6-antispam.md](stage-6-antispam.md) | 6 | Rate-limit и админ-глушение | ✅ утверждение цифр баланса |
| [stage-7-admin-logs.md](stage-7-admin-logs.md) | 7 | Админ-логи (LogType) | — |
| [stage-8-polish.md](stage-8-polish.md) | 8 | CVar-гейт включения, локализация, плейтест | ✅ плейтест |

Правила запуска:
1. Этапы выполняются строго по порядку, каждый — в новой сессии агента на ветке `pda-messenger`.
2. После каждого этапа: review диффа + зелёный CI → только потом следующий файл.
3. Этапы с гейтом человека останавливаются после зелёных тестов и требуют вашего явного решения.
4. **Коммит и мерж выполняет только пользователь.** Агент готовит изменения, запускает тесты,
   предлагает сообщение коммита, но сам не делает `git commit`, `git push` и не мержит ветки.
5. Прогресс фиксируется в `Docs/messenger-rollout.md` (создаётся на этапе 0).

## Сквозные соглашения (зашиты в каждый промпт)

- Изоляция форка: всё новое в `_MalinovStation`, vanilla-файлы не трогаются
  (единственное исключение — помеченная правка `LogType.cs` на этапе 7).
- Имена: префикс `Malinov*` / `malinov-*`. Namespace `Content.{Shared|Server|Client}._MalinovStation.Messenger`.
- Прототипы: `MalinovMessengerCartridge` (внутренняя программная сущность), `MalinovMessengerServer`;
  частота DeviceNetwork `MalinovMessengerFrequency` = **2210**.
- Команды протокола (`MalinovMessengerConstants`): `announce`, `msg`, `directory_req`, `directory`.
- CVar'ы: `.history_per_contact`,
  `.rate_window_sec`, `.rate_max_msgs`, `.sound_enabled`.
- Локализация: ключи `malinov-messenger-*`, файлы `Resources/Locale/{en-US,ru-RU}/_MalinovStation/malinov-cartridges.ftl`.
- Транспорт уже есть: у `BasePDA` есть `DeviceNetwork` (Wireless), `WirelessNetworkConnection range: 500`,
  `CartridgeLoader`, `Ringer`.

## Принятые архитектурные решения

- Каталог контактов — из записей станции (StationRecords/CrewManifest), онлайн-статус — по анонсам программ.
- История переписки — только в рамках раунда, без БД (решение зафиксировано, не пересматривать молча).
- Этап 4 — сервер по паттерну `SingletonDeviceNetServerSystem`.
