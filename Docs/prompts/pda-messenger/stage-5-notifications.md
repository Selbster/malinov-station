# PDA-мессенджер — Этап 5: Уведомления 🔊 (звук проверяется человеком)

Ты — Senior DevOps/C# разработчик с 10-летним стажем, эксперт в архитектуре ECS (RobustToolbox / Space Station 14)
и автоматизации CI/CD. Реализуешь этапы механики мессенджера для КПК в репозитории
https://github.com/Selbster/malinov-station.

🌿 ВЕТКА: работай в ветке `pda-messenger`. НЕ переключайся на `alpha-master`, НЕ мержи.
Один этап = один атомарный коммит: `feat: уведомления о входящих`.

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
   Всё новое — только в `_MalinovStation`. Vanilla-файлы не менять.
5. ОТЧЁТНОСТЬ: обнови `Docs/messenger-rollout.md`:
   `- [x] Этап 5 — уведомления (commit: <hash>, CI: green)`.

## 🧠 Тестирование

Content.IntegrationTests — реальный headless сервер+клиент (не моки). Образцы:
`Fixtures/GameTest.cs`, `Tests/DeviceNetwork/DeviceNetworkTest.cs`.
Звук в интеграционных тестах НЕ ассертится — его проверяет человек (гейт ниже).

## 📐 Сквозные соглашения мессенджера (соблюдай строго)

- Папки кода: `Content.{Shared|Server|Client}/_MalinovStation/Messenger/`.
  Namespace: `Content.<Проект>._MalinovStation.Messenger`.
- Прототипы: `MalinovMessengerCartridge`, `MalinovMessengerServer`; частота **2210**.
- CVar'ы: файл `CCVars.Messenger.cs`. Новый:
  `malinov.messenger.sound_enabled` (bool, default true).
- Локализация: ключи `malinov-messenger-*`; en-US/ru-RU синхронно.
- ВАЖНО: новые бинарные ассеты (.ogg/.wav) НЕ добавлять — только существующие ресурсы репозитория.

---

## 📦 КОНТЕКСТ: что уже сделано

Этапы 0–4: скелет; каталог; прямая доставка; история; сервер-релей.
Если чего-то нет — СТОП.

## 🎯 ЦЕЛЬ ЭТАПА 5

Когда сообщение приходит получателю, у которого приложение «Мессенджер» сейчас не активно,
КПК получателя уведомляет: popup над КПК + короткий звуковой сигнал. Если приложение активно —
уведомления нет (сообщение просто появляется в диалоге).

## 🚫 ЯВНО НЕ ДЕЛАТЬ

Мигание спрайта PDA, всплывающие окна BUI поверх PDA, вибро/рингтон-редактор, антиспам (6), логи (7).
Vanilla не трогать. Новые аудиофайлы не добавлять.

## 📚 ОБЯЗАТЕЛЬНОЕ ЧТЕНИЕ

1. `Content.Server/PDA/Ringer/RingerSystem.cs` — как устроен звук на PDA (образец, API напрямую не трогаем).
2. `Robust.Shared.Audio` / использование `AudioSystem.PlayPvs`/`PlayGlobal` в системах контента
   (поиск по репо) + паттерн `SoundCollectionSpecifier`.
3. Пример sound collection: `Resources/Prototypes/_MalinovStation/SoundCollections/` (если есть)
   или vanilla `Resources/Prototypes/SoundCollections/*.yml` как образец формата.
4. Popup: использования `PopupSystem.PopupEntity(...)` с фильтром по игроку в системах контента.
5. Своё: `_MalinovStation/Messenger/*` — точка обработки входящего сообщения.

## 📝 ЧТО СОЗДАТЬ/ИЗМЕНИТЬ (только _MalinovStation)

1. YAML: sound collection `malinov_messenger_notification`
   (`Resources/Prototypes/_MalinovStation/SoundCollections/malinov_messenger.yml`) со ссылкой
   на ОДИН существующий короткий звук из `Resources/Audio/` (подбери нейтральный «блип»;
   укажи выбранный файл в отчёте для ручной проверки).
2. Shared: константа звука; флаг/логика «нужно ли уведомление» (программа активна?).
3. Server: при входящем сообщении, если программа получателя НЕ активна:
   - `PopupEntity` над КПК получателя с локализованным текстом «Новое сообщение от {sender}»
     (видит только владелец КПК — по `PdaComponent.PdaOwner`, если игрок онлайн);
   - проиграть звук `PlayPvs` на сущности КПК при включённом `malinov.messenger.sound_enabled`.
4. Client: без изменений UI (уведомление вне приложения).
5. Новый CVar. Локализация en+ru. Тесты (см. DoD). Обнови `Docs/messenger-rollout.md`.

## ✅ DEFINITION OF DONE (Этап 5)

Все 5 команд гейта зелёные; vanilla не изменён; новых бинарников нет. Интеграционный тест:

1. A→B при АКТИВНОЙ программе B → уведомления не возникает (ассерт логики условия через
   наблюдаемое состояние/флаг системы или отсутствие вызова — оформи проверяемо).
2. A→B при НЕактивной программе B → система фиксирует уведомление (проверяемое следствие:
   например, тестовая подписка/флаг в системе; звук и popup фактом прогона без исключений).
3. Отправка при выключенном `malinov.messenger.sound_enabled` проходит, исключений нет.

## 🛑 ГЕЙТ ЧЕЛОВЕКА (обязательно!)

После зелёного гейта ОСТАНОВИСЬ и запроси у человека:
1. Плейтест звука: выдать чек-лист (спавн двух PDA с программами, отправка при свёрнутом приложении,
   оценка громкости/уместности выбранного звука; если звук не подходит — человек назовёт замену).
2. Явное подтверждение до перехода к Этапу 6.

## 📤 ФОРМАТ ОТЧЁТА

1. Список файлов (+ git status без vanilla; путь выбранного аудиофайла).
2. Подтверждение зелёного гейта.
3. Обновлённый `Docs/messenger-rollout.md` + чек-лист проверки звука.

Приступай. Первым делом сообщи, какие файлы читаешь, затем выполняй.
