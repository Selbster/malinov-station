# PDA-мессенджер — Этап 3: История переписки (в рамках раунда)

Ты — Senior DevOps/C# разработчик с 10-летним стажем, эксперт в архитектуре ECS (RobustToolbox / Space Station 14)
и автоматизации CI/CD. Реализуешь этапы механики мессенджера для КПК в репозитории
https://github.com/Selbster/malinov-station.

🌿 ВЕТКА: работай в ветке `pda-messenger`. НЕ переключайся на `alpha-master`, НЕ мержи.
Один этап = один атомарный коммит: `feat(pda-messenger): этап 3 — история переписки`.

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
   `- [x] Этап 3 — история переписки (commit: <hash>, CI: green)`.

## 🧠 Тестирование

Content.IntegrationTests — реальный headless сервер+клиент (не моки). Образцы:
`Fixtures/GameTest.cs`, `Tests/DeviceNetwork/DeviceNetworkTest.cs`. CVar в тесте переопределяй через
`IConfigurationManager` сервера (`server.ResolveDependency<IConfigurationManager>()`,
`CVarDef.FromString`-стиль по образцам существующих тестов).

## 📐 Сквозные соглашения мессенджера (соблюдай строго)

- Папки кода: `Content.{Shared|Server|Client}/_MalinovStation/Messenger/`.
  Namespace: `Content.<Проект>._MalinovStation.Messenger`.
- Прототипы: `MalinovMessengerCartridge`; частота `MalinovMessengerFrequency` = **2210**.
- Константы протокола: класс `MalinovMessengerConstants`; команды `announce`, `msg` (этап 2);
  `ack`, `directory_req`, `directory` — позже.
- CVar'ы: файл `CCVars.Messenger.cs`. На этом этапе добавляется
  `malinov.messenger.history_per_contact` (int, default 50, CVar.SERVER | CVar.REPLICATED? — реши по образцам).
- Локализация: ключи `malinov-messenger-*`; файлы en-US и ru-RU синхронно.

---

## 📦 КОНТЕКСТ: что уже сделано

Этапы 0–2: скелет картриджа; каталог экипажа из записей станции; прямая отправка P2P через
DeviceNetwork с анонсами присутствия и временной лентой строк текущей сессии. Если чего-то нет — СТОП.

## 🎯 ЦЕЛЬ ЭТАПА 3

Структурированная история переписки: по каждому контакту хранится упорядоченный список сообщений
(входящие/исходящие, время), переживает деактивацию/активацию программы и закрытие КПК,
но живёт только до конца раунда (без БД — решение зафиксировано в README пакета).

## 🚫 ЯВНО НЕ ДЕЛАТЬ

Персистентность между раундами (осознанно отклонена), вложения/стикеры, сервер-релей (4a),
ack/failover (4b), уведомления (5), антиспам (6), админ-логи (7). Vanilla не трогать.

## 📚 ОБЯЗАТЕЛЬНОЕ ЧТЕНИЕ

1. Свой код этапов 0–2 в `_MalinovStation/Messenger` (точки расширения состояния).
2. `Robust.Shared.Timing` / использование `Timing.CurTime` в системах контента (образец поиска по репо) —
   для меток времени сообщений.
3. `Content.Shared/CartridgeLoader/CartridgeLoaderSystem.UserInterface.cs` — механика UpdateCartridgeUiState.

## 📝 ЧТО СОЗДАТЬ/ИЗМЕНИТЬ (только _MalinovStation)

1. Shared:
   - Сериализуемый тип `MalinovMessengerMessage`: SenderName, Text, Timestamp (TimeSpan), Outgoing (bool).
   - Заменить временную ленту этапа 2 на структуру: словарь «имя контакта → List<MalinovMessengerMessage>»
     в компоненте картриджа (серверная память раунда).
   - Расширить `MalinovMessengerUiState`: сообщения выбранного диалога + признак исходящего у каждой строки.
2. Server (`MalinovMessengerCartridgeSystem`):
   - Входящее/исходящее сообщение записывается в историю соответствующего контакта;
     при превышении лимита (`malinov.messenger.history_per_contact`) самые старые вытесняются.
   - При выборе контакта в UI — состояние отдаёт полную историю этого диалога.
3. Client (`MalinovMessengerUiFragment`): экран диалога со скроллом истории (ScrollContainer),
   разделение входящих/исходящих визуально (выравнивание/цвет по стилям проекта), время в формате HH:mm.
4. Новый CVar в `CCVars.Messenger.cs`.
5. Локализация en+ru: подписи времени/пустого диалога («Нет сообщений»).
6. Тесты (см. DoD). Обнови `Docs/messenger-rollout.md`.

## ✅ DEFINITION OF DONE (Этап 3)

Все 5 команд гейта зелёные; vanilla не изменён. Интеграционный тест:

1. Два PassengerPDA с активными программами обмениваются тремя сообщениями (A→B, B→A, A→B).
2. Ассерт: история диалога A(с B) содержит 3 записи в правильном порядке с корректными флагами Outgoing.
3. Деактивировать и снова активировать программу у A, открыть диалог с B → история та же (не потерялась).
4. Лимит: установить `malinov.messenger.history_per_contact=2` в тестовом окружении → после четвёртого
   сообщения в истории остаются только последние 2.

## 📤 ФОРМАТ ОТЧЁТА

1. Список изменённых/созданных файлов (+ git status без vanilla).
2. Подтверждение зелёного гейта.
3. Обновлённый `Docs/messenger-rollout.md`.

Приступай. Первым делом сообщи, какие файлы читаешь, затем выполняй.
