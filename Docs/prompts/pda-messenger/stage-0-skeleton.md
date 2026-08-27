# PDA-мессенджер — Этап 0: Скелет программы «Мессенджер»

Ты — Senior DevOps/C# разработчик с 10-летним стажем, эксперт в архитектуре ECS (RobustToolbox / Space Station 14)
и автоматизации CI/CD. Реализуешь этапы механики мессенджера для КПК в репозитории
https://github.com/Selbster/malinov-station.

🌿 ВЕТКА: работай в ветке `pda-messenger`. НЕ переключайся на `alpha-master`, НЕ мержи.
Один этап = один атомарный коммит (делает пользователь после review): `feat(pda-messenger): этап 0 — скелет программы мессенджера`.

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
5. ОТЧЁТНОСТЬ: создай и обнови `Docs/messenger-rollout.md`:
   `- [ ] Этап 0 — скелет программы (commit: TBD, CI: green)`.
   Отмечать `[x]` и вписывать хэш коммита будет пользователь после ручного коммита.
6. **КОММИТ ДЕЛАЕТ ПОЛЬЗОВАТЕЛЬ.** Агент не выполняет `git commit`, `git push`, merge, rebase
   и любые другие git-мутации без явного указания.

## 🧠 Тестирование

Content.IntegrationTests — реальный headless сервер+клиент (не моки). Образцы:
`Content.IntegrationTests/Fixtures/GameTest.cs` и `Tests/DeviceNetwork/DeviceNetworkTest.cs`
(`Pair`, `pair.Server`, `pair.Client`, `[TestPrototypes]`). Проверяй фактическим прогоном, не догадкой.

## 📐 Сквозные соглашения мессенджера (соблюдай строго)

- Папки кода: `Content.Shared/_MalinovStation/Messenger/`, `Content.Server/_MalinovStation/Messenger/`,
  `Content.Client/_MalinovStation/Messenger/`.
  Namespace: `Content.<Проект>._MalinovStation.Messenger`.
- Прототипы: внутренняя программная сущность `MalinovMessengerCartridge` (parent: BasePDACartridge),
  сервер `MalinovMessengerServer` (этап 4).
- Частота DeviceNetwork: прототип `- type: deviceFrequency`, id `MalinovMessengerFrequency`, значение **2210**,
  файл `Resources/Prototypes/_MalinovStation/Device/malinov_devicenet_frequencies.yml`.
- Константы протокола: статический класс `MalinovMessengerConstants` в Shared
  (команды payload: `announce`, `msg`, `directory_req`, `directory` — заполняются по этапам).
- Локализация: ключи с префиксом `malinov-messenger-*`; файлы
  `Resources/Locale/en-US/_MalinovStation/malinov-cartridges.ftl` и ru-RU аналог — ОБА языка всегда синхронно.
- Транспорт уже есть: у `BasePDA` (`pda.yml`) есть `DeviceNetwork` (Wireless), `WirelessNetworkConnection range: 500`,
  `CartridgeLoader`, `Ringer`.

---

## 🎯 ЦЕЛЬ ЭТАПА 0

Пустой, но архитектурно правильный каркас приложения КПК «Мессенджер»: программа существует как внутренний
прототип-картридж и устанавливается в PDA автоматически при спавне через серверную систему, открывается и
показывает заглушку без ошибок в логах. Никакой логики обмена сообщениями.

## 🚫 ЯВНО НЕ ДЕЛАТЬ

Каталог контактов, отправку сообщений, серверную логику, изменение preinstalled-списков у PDA (pda.yml),
правки vanilla-файлов (cartridges.yml, CCVars.cs, vanilla *.ftl). Если захочется сделать «заодно» — TODO-комментарий.

## 📚 ОБЯЗАТЕЛЬНОЕ ЧТЕНИЕ (изучи структуру 1:1)

1. `Content.Shared/CartridgeLoader/Cartridges/NotekeeperCartridgeComponent.cs` и `NotekeeperCartridgeSystem.cs`
   — паттерн Component+System для программ КПК (живут в Shared, не в Server!).
2. `Content.Shared/CartridgeLoader/Cartridges/NotekeeperUiState.cs` — паттерн BUI-состояния.
3. `Content.Client/CartridgeLoader/Cartridges/NotekeeperUi.cs` + `NotekeeperUiFragment.xaml(.cs)`
   — паттерн клиентского рендера: класс-наследник `UIFragment` + XAML-фрагмент.
4. `Resources/Prototypes/Entities/Objects/Devices/cartridges.yml` — блоки `BasePDACartridge` и `NotekeeperCartridge`
   КАК ОБРАЗЕЦ (файл не редактировать!).
5. `Resources/Locale/en-US/cartridge-loader/cartridges.ftl` и ru-RU аналог — образец ключа programName.
6. `Content.Shared/CCVar/CCVars.Misc.cs` (пример партиала CCVars) +
   `Content.Shared/_MalinovStation/Audio/MalinovSoundCollections.cs` (образец кода и именования форка).
7. `Content.Shared/CartridgeLoader/CartridgeLoaderSystem.Programs.cs` — API: `InstallProgram`,
   `TryGetProgram<T>`, `HasProgram<T>`.
8. `Content.IntegrationTests/Tests/DeviceNetwork/DeviceNetworkTest.cs` — шаблон теста.

## 📝 ЧТО СОЗДАТЬ (вся структура — в _MalinovStation)

1. `Resources/Prototypes/_MalinovStation/Devices/malinov-cartridges.yml` — сущность `MalinovMessengerCartridge`
   (parent: BasePDACartridge): Sprite (свободный state из cartridge.rsi), `UIFragment ui: !type:MalinovMessengerUi`,
   `Cartridge` c `programName: malinov-messenger-program-name` и иконкой из существующих ресурсов,
   компонент `MalinovMessengerCartridge`. Vanilla cartridges.yml НЕ трогать.
2. `Content.Shared/_MalinovStation/Messenger/MalinovMessengerCartridgeComponent.cs` — пустой компонент
   `MalinovMessengerCartridgeComponent`.
3. `Content.Shared/_MalinovStation/Messenger/MalinovMessengerCartridgeSystem.cs` — подписка на
   `CartridgeUiReadyEvent`, отдача пустого состояния (как NotekeeperCartridgeSystem).
4. `Content.Shared/_MalinovStation/Messenger/MalinovMessengerUiState.cs` — минимальный класс состояния.
5. `Content.Client/_MalinovStation/Messenger/MalinovMessengerUi.cs` +
   `MalinovMessengerUiFragment.xaml(.cs)` — UIFragment-заглушка «Нет контактов».
   Отдельная регистрация на клиенте не нужна — тип резолвится сериализацией из YAML.
 6. Локализация: ключ `malinov-messenger-program-name` («Messenger» / «Мессенджер») + строки заглушки,
    файлы `Resources/Locale/{en-US,ru-RU}/_MalinovStation/malinov-cartridges.ftl`.
 7. `Content.IntegrationTests/Tests/_MalinovStation/Messenger/MalinovMessengerTest.cs` — GameTest.
    Проверяет автоустановку программы на обычный PDA и отсутствие программы на исключённых PDA (CentCom и др.).
 8. `Docs/messenger-rollout.md` — создать.

## ✅ DEFINITION OF DONE (Этап 0)

- Все 5 команд гейта зелёные; `git status` подтверждает: vanilla-файлы не изменены.
- Тест `MalinovMessengerTest.cs`:
  1. Спавнит КОНКРЕТНЫЙ PDA (например PassengerPDA) на тестовой карте. ВНИМАНИЕ: `BasePDA` — abstract,
     спавнить его нельзя.
  2. Проверяет, что программа установилась автоматически:
     `HasProgram<MalinovMessengerCartridgeComponent>() == true`.
  3. Проверяет, что программа НЕ установилась на исключённый PDA (например `CentcomPDA`):
     `HasProgram<MalinovMessengerCartridgeComponent>() == false`.
- Программа видна в списке программ PDA; прогон тестов без ошибок/exceptions в логах.

## 📤 ФОРМАТ ОТЧЁТА

1. Список созданных файлов (+ подтверждение отсутствия изменений в vanilla).
2. Подтверждение успеха всех 5 команд гейта.
3. Содержимое `Docs/messenger-rollout.md`.
4. Если застрял: точный текст ошибки и предпринятые попытки. Не переходи к Этапу 1 до завершения Этапа 0.

Приступай. Первым делом сообщи, какие файлы читаешь, затем выполняй.
