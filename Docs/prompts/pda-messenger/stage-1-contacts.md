# PDA-мессенджер — Этап 1: Каталог контактов из записей станции

Ты — Senior DevOps/C# разработчик с 10-летним стажем, эксперт в архитектуре ECS (RobustToolbox / Space Station 14)
и автоматизации CI/CD. Реализуешь этапы механики мессенджера для КПК в репозитории
https://github.com/Selbster/malinov-station.

🌿 ВЕТКА: работай в ветке `pda-messenger`. НЕ переключайся на `alpha-master`, НЕ мержи.
Один этап = один атомарный коммит: `feat(pda-messenger): этап 1 — каталог контактов`.

## 🛠️ DevOps Gate (из корня репозитория; копия CI проекта .github/workflows/build-test-debug.yml)

1. `dotnet restore`
2. `dotnet build --configuration DebugOpt --no-restore /m`
3. `dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj --no-build`
4. `dotnet test --no-build --configuration DebugOpt Content.Tests/Content.Tests.csproj`
5. `dotnet test --no-build --configuration DebugOpt Content.IntegrationTests/Content.IntegrationTests.csproj`

## ⚠️ Жесткие правила (DevOps Gate)

1. «ЗЕЛЕНЫЙ СВЕТ»: перед коммитом и отчётом ВСЕ 5 команд обязаны пройти (exit code 0).
   Во время итераций внутри этапа допускаются шаги 2–3 + точечный целевой тест (шаг 5 тяжёлый).
2. ОШИБКИ: чинить причину, не симптом. После 3 неудачных попыток подряд на одном шаге — СТОП:
   опиши упавшую команду, точный текст ошибки и список попыток. Не продолжай вслепую.
3. ЗАПРЕТ КОСТЫЛЕЙ: запрещено ослаблять assert'ы, добавлять [Ignore]/[Explicit], менять логику чужих тестов.
4. ИЗОЛЯЦИЯ И КОНВЕНЦИИ ФОРКА: обязательно прочитай `.agents/skills/ss14-upstream-maintenance/SKILL.md`.
   Всё новое — только в `_MalinovStation`. Vanilla-файлы на этом этапе не менять НИ ОДНОГО.
5. ОТЧЁТНОСТЬ: обнови `Docs/messenger-rollout.md`:
   `- [x] Этап 1 — каталог контактов (commit: <hash>, CI: green)`.

## 🧠 Тестирование

Content.IntegrationTests — реальный headless сервер+клиент (не моки). Образцы:
`Content.IntegrationTests/Fixtures/GameTest.cs`, `Tests/DeviceNetwork/DeviceNetworkTest.cs`,
`Tests/Station/StationJobsTest.cs` (создание тестовой станции через prototype gameMap).

## 📐 Сквозные соглашения мессенджера (соблюдай строго)

- Папки кода: `Content.{Shared|Server|Client}/_MalinovStation/Messenger/`.
  Namespace: `Content.<Проект>._MalinovStation.Messenger`.
- Прототипы: `MalinovMessengerCartridge`, сервер `MalinovMessengerServer` (этап 4, пока не существует).
- Частота DeviceNetwork: прототип `MalinovMessengerFrequency`, значение **2210**
  (`Resources/Prototypes/_MalinovStation/Device/malinov_devicenet_frequencies.yml`) — на этом этапе не используется.
- Константы протокола: класс `MalinovMessengerConstants` (Shared); команды: `announce`, `msg`, `ack`,
  `directory_req`, `directory` — заполняются по этапам.
- CVar'ы: файл `CCVars.Messenger.cs`, имена `malinov.messenger.*`.
- Локализация: ключи `malinov-messenger-*`; файлы
  `Resources/Locale/{en-US,ru-RU}/_MalinovStation/malinov-cartridges.ftl` — ОБА языка синхронно.

---

## 📦 КОНТЕКСТ: что уже сделано (Этап 0)

В `_MalinovStation` создан скелет программы: внутренний прототип `MalinovMessengerCartridge`,
`MalinovMessengerCartridgeComponent`, `MalinovMessengerCartridgeSystem` (отдаёт пустое состояние),
`MalinovMessengerUi` + `MalinovMessengerUiFragment` («Нет контактов»),
локаль `malinov-cartridges.ftl` (en/ru), серверная система автоустановки `MalinovMessengerInstallerSystem`,
тест `Content.IntegrationTests/Tests/_MalinovStation/Messenger/MalinovMessengerTest.cs`,
файл `Docs/messenger-rollout.md`. Если чего-то из этого нет в ветке — СТОП, сообщи человеку.

## 🎯 ЦЕЛЬ ЭТАПА 1

Программа «Мессенджер» показывает каталог экипажа станции (имя + должность), построенный из записей
станции (StationRecords/CrewManifest) той станции, к которой привязан КПК. Сетевого обмена ещё нет.

## 🚫 ЯВНО НЕ ДЕЛАТЬ

Отправку сообщений, анонсы присутствия, онлайн-статусы, резолвинг DeviceNetwork-адресов,
серверную сущность, правки vanilla. Если захочется «заодно» — TODO-комментарий.

## 📚 ОБЯЗАТЕЛЬНОЕ ЧТЕНИЕ

1. `Content.Server/CartridgeLoader/Cartridges/CrewManifestCartridgeSystem.cs` — образец получения манифеста:
   `StationSystem.GetOwningStation(uid)` → `CrewManifestSystem.GetCrewManifest(station)` → `UpdateCartridgeUiState`;
   а также образец отмены установки через `ProgramInstallationAttempt` (пригодится на этапе 8).
2. Типы манифеста в `Content.Shared/CrewManifest/` (`CrewManifestEntries`, записи имя/должность).
3. `Content.Shared/CartridgeLoader/CartridgeLoaderSystem.UserInterface.cs` — как состояние доезжает до UI.
4. `Content.IntegrationTests/Tests/Station/StationJobsTest.cs` — как создать тестовую станцию
   (prototype `gameMap` + `/Maps/Test/empty.yml`) в интеграционном тесте.
5. `Content.Shared/PDA/PdaComponent.cs` — поля `OwnerName`, `PdaOwner` (пригодится позже).

## 📝 ЧТО СОЗДАТЬ/ИЗМЕНИТЬ (только _MalinovStation)

1. Shared:
   - Новый сериализуемый тип `MalinovMessengerContact` (Name: string, JobTitle: string).
   - Расширить `MalinovMessengerUiState`: список контактов + строковый статус
     («недоступно», если станции нет). Локализуй статус ключом, не сырой строкой.
2. Server (`MalinovMessengerCartridgeSystem`):
   - При `CartridgeUiReadyEvent`: определить станцию КПК; получить манифест;
     спроецировать в `List<MalinovMessengerContact>`; отправить состояние через
     `UpdateCartridgeUiState`. Нет станции → состояние «манифест недоступен» (как в CrewManifestCartridgeSystem).
3. Client (`MalinovMessengerUiFragment`): рендер списка строк «Имя — должность»
   (BoxContainer с лейблами или ItemList), пустое состояние сохраняется.
4. Локализация en+ru: заголовок списка, текст «манифест недоступен».
5. Тесты `MalinovMessengerTest.cs` дополнить (см. DoD).
6. `Docs/messenger-rollout.md` — отметить этап 1.

## ✅ DEFINITION OF DONE (Этап 1)

Все 5 команд гейта зелёные; vanilla не изменён. Интеграционные тесты:

1. **Без станции**: спавн PassengerPDA вне станции (обычный пустой грид теста),
   активация программы → UiState содержит статус «недоступен» и пустой список контактов.
2. **Со станцией**: по образцу `StationJobsTest` создать тестовую станцию (gameMap на `/Maps/Test/empty.yml`),
   добавить в StationRecords станции одну тестовую запись (имя+должность — найди публичный API
   `StationRecordsSystem` для добавления записи), спавнить PassengerPDA на гриде этой станции,
   активировать программу → UiState содержит ровно одну запись с ожидаемыми Name и JobTitle.

## 📤 ФОРМАТ ОТЧЁТА

1. Список изменённых/созданных файлов (+ git status без vanilla).
2. Подтверждение зелёного гейта.
3. Обновлённый `Docs/messenger-rollout.md`.

Приступай. Первым делом сообщи, какие файлы читаешь, затем выполняй.
