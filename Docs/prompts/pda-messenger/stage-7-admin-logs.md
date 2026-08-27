# PDA-мессенджер — Этап 7: Админ-логи

Ты — Senior DevOps/C# разработчик с 10-летним стажем, эксперт в архитектуре ECS (RobustToolbox / Space Station 14)
и автоматизации CI/CD. Реализуешь этапы механики мессенджера для КПК в репозитории
https://github.com/Selbster/malinov-station.

🌿 ВЕТКА: работай в ветке `pda-messenger`. НЕ переключайся на `alpha-master`, НЕ мержи.
Один этап = один атомарный коммит: `feat: админ-логи`.

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
   Всё новое — только в `_MalinovStation`.

   ⛔ ЕДИНСТВЕННОЕ РАЗРЕШЁННОЕ ИСКЛЮЧЕНИЕ НА ВЕСЬ ПРОЕКТ: vanilla-файл
   `Content.Shared.Database/LogType.cs`, в который ДОБАВЛЯЮТСЯ новые значения enum.
   Правила правки:
   - только добавление новых членов enum, ничего существующего не менять и не переупорядочивать;
   - значения начинать с **1200** (вверх от vanilla-диапазона, чтобы исключить коллизии
     с будущими upstream-значениями после текущего максимума 105);
   - обернуть блок маркерами:
     `// Malinov edit start - messenger admin logs`
     ... новые члены ...
     `// Malinov edit end`;
   - XML-doc комментарий на каждого члена (по стилю файла).
5. ОТЧЁТНОСТЬ: обнови `Docs/messenger-rollout.md`:
   `- [x] Этап 7 — админ-логи (commit: <hash>, CI: green)`.

## 🧠 Тестирование

Content.IntegrationTests — реальный headless сервер+клиент (не моки). Образцы:
`Fixtures/GameTest.cs`, `Tests/DeviceNetwork/DeviceNetworkTest.cs`.
Если в репо существует инфраструктура перехвата админ-логов в тестах (поищи LogRecorder/аналог) —
используй её; если нет — ограничься smoke-проверками отсутствия исключений.

## 📐 Сквозные соглашения мессенджера (соблюдай строго)

- Папки кода: `Content.{Shared|Server|Client}/_MalinovStation/Messenger/`.
  Namespace: `Content.<Проект>._MalinovStation.Messenger`.
- Прототипы: `MalinovMessengerCartridge`, `MalinovMessengerServer`; частота **2210**.
- CVar'ы: файл `CCVars.Messenger.cs` (новых на этом этапе нет).
- Локализация: en-US/ru-RU синхронно (тексты логов НЕ локализуются — они для админов на английском).

---

## 📦 КОНТЕКСТ: что уже сделано

Этапы 0–6: полная цепочка доставки через сервер-ретранслятор, уведомления, rate-limit и mute/unmute команды.
Если чего-то нет — СТОП.

## 🎯 ЦЕЛЬ ЭТАПА 7

Ключевые события мессенджера видны в панели админ-логов: отправка, отказ доставки, срабатывание
rate-limit, глушение/разглушение.

## 🚫 ЯВНО НЕ ДЕЛАТЬ

Логирование содержимого входящих у получателя (приватность читателя), отдельное UI для просмотра
логов, персистентность сверх стандартной системы админ-логов.

## 📚 ОБЯЗАТЕЛЬНОЕ ЧТЕНИЕ

1. `Content.Shared.Database/LogType.cs` — стиль enum, текущий максимум значения (105).
2. Использование админ-логов: поиск по репо `Add(LogType.` — образцы вызова
   (`IAdminLogManager.Add(LogType, LogImpact, $"...")`), выбор LogImpact по смыслу.
3. Своё: `_MalinovStation/Messenger/*` — точки: отправка (клиент), ретрансляция/отказ (сервер),
   rate-limit, mute/unmute команды.

## 📝 ЧТО СОЗДАТЬ/ИЗМЕНИТЬ

1. `Content.Shared.Database/LogType.cs` (разрешённая помеченная правка, см. правило 4):
   добавить члены `MalinovMessengerSend = 1200`, `MalinovMessengerDeliveryFail = 1201`,
   `MalinovMessengerRateLimited = 1202`, `MalinovMessengerMute = 1203` с XML-doc.
2. `_MalinovStation/Messenger`: инжект `IAdminLogManager`; логировать:
   - Send (Low): отправитель → получатель (имена/адреса, БЕЗ текста сообщения);
   - DeliveryFail (Low): причина (нет адресата / сервер недоступен);
   - RateLimited (Medium): кто, окно/лимит;
   - Mute (Medium): админ, цель, действие (mute/unmute).
3. Тесты (см. DoD). Обнови `Docs/messenger-rollout.md`.

## ✅ DEFINITION OF DONE (Этап 7)

Все 5 команд гейта зелёные; `git status` показывает ровно ОДИН изменённый vanilla-файл
(`LogType.cs`) с маркерами Malinov edit. Интеграционный тест:

1. Сценарий отправки A→B и mute A выполняется без исключений; при наличии инфраструктуры
   перехвата логов — ассерт на появление записей соответствующих LogType.
2. Дифф `LogType.cs` минимален (только добавленные члены + маркеры).

## 📤 ФОРМАТ ОТЧЁТА

1. Список файлов (+ полный вывод `git diff Content.Shared.Database/LogType.cs`).
2. Подтверждение зелёного гейта.
3. Обновлённый `Docs/messenger-rollout.md`.

Приступай. Первым делом сообщи, какие файлы читаешь, затем выполняй.
