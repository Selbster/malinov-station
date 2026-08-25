# PDA-мессенджер — Этап 2: Прямая отправка (P2P через DeviceNetwork)

Ты — Senior DevOps/C# разработчик с 10-летним стажем, эксперт в архитектуре ECS (RobustToolbox / Space Station 14)
и автоматизации CI/CD. Реализуешь этапы механики мессенджера для КПК в репозитории
https://github.com/Selbster/malinov-station.

🌿 ВЕТКА: работай в ветке `pda-messenger`. НЕ переключайся на `alpha-master`, НЕ мержи.
Один этап = один атомарный коммит: `feat: прямая отправка P2P`.

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
   `- [x] Этап 2 — прямая отправка P2P (commit: <hash>, CI: green)`.

## 🧠 Тестирование

Content.IntegrationTests — реальный headless сервер+клиент (не моки). Образцы:
`Fixtures/GameTest.cs`, `Tests/DeviceNetwork/DeviceNetworkTest.cs`. Для ожидания асинхронных событий
используй примитивы pair (`pair.RunTicks`/условия) по образцам соседних тестов.

## 📐 Сквозные соглашения мессенджера (соблюдай строго)

- Папки кода: `Content.{Shared|Server|Client}/_MalinovStation/Messenger/`.
  Namespace: `Content.<Проект>._MalinovStation.Messenger`.
- Прототипы: `MalinovMessengerCartridge`; сервер `MalinovMessengerServer` появится на этапе 4.
- Частота DeviceNetwork: прототип `MalinovMessengerFrequency`, значение **2210**,
  файл `Resources/Prototypes/_MalinovStation/Device/malinov_devicenet_frequencies.yml`
  (создать на этом этапе).
- Константы протокола: класс `MalinovMessengerConstants` (Shared): частота, ключи payload
  (`command`, `sender_name`, `text`, `target`) и команды `announce`, `msg`.
  (`directory_*` — с этапа 4.)
- CVar'ы: файл `CCVars.Messenger.cs`, имена `malinov.messenger.*`.
- Локализация: ключи `malinov-messenger-*`; файлы
  `Resources/Locale/{en-US,ru-RU}/_MalinovStation/malinov-cartridges.ftl` — ОБА языка синхронно.
- Транспорт: у `BasePDA` уже есть `DeviceNetwork` (Wireless), `WirelessNetworkConnection range: 500`,
  `CartridgeLoader`.

---

## 📦 КОНТЕКСТ: что уже сделано

Этап 0: скелет программы (внутренний прототип, компонент, система, UIFragment, локаль en/ru, система автоустановки, тест).
Этап 1: каталог экипажа из записей станции (`MalinovMessengerContact`, состояние со списком и статусом),
тест создания станции. Если чего-то нет в ветке — СТОП, сообщи человеку.

## 🎯 ЦЕЛЬ ЭТАПА 2

Выбранный контакт получает сообщение напрямую через wireless DeviceNetwork (без сервера-посредника):
отправитель пишет текст в открытом приложении → адресату доставляется пакет → у адресата в открытом
приложении появляется входящая строка. Каталог из этапа 1 обогащается статусом «в сети»
(по анонсам программ).

## 🚫 ЯВНО НЕ ДЕЛАТЬ

Историю переписки между сессиями просмотра (этап 3), сервер-ретранслятор и директорию (4),
звуковые уведомления (5), rate-limit (6), админ-логи (7). Vanilla не трогать.
Известное упрощение: сопоставление «запись станции ↔ анонс» выполняется по имени владельца;
задокументируй его TODO-комментарием в коде.

## 📚 ОБЯЗАТЕЛЬНОЕ ЧТЕНИЕ

1. `Content.Server/DeviceNetwork/System(s)/DeviceNetworkSystem.cs` — публичный API отправки
   `QueuePacket(uid, address|null, payload, frequency?, device?)`, `SetReceiveFrequency`,
   `SetTransmitFrequency`, `SetAddress`, `RandomizeAddress`.
2. `Content.Shared/CartridgeLoader/CartridgeLoaderSystem.Relay.cs` — пакеты доезжают до программ как
   `CartridgeRelayedEvent<DeviceNetworkPacketEvent>`; подписывайся именно на него.
3. `Content.Shared/DeviceNetwork/DeviceNetworkConstants.cs` — стиль ключей payload.
4. Примеры отправщиков/получателей: `Content.Server/Fax/FaxSystem.cs`,
   `Content.Server/Medical/CrewMonitoring/CrewMonitoringServerSystem.cs`.
5. `Content.Shared/PDA/PdaComponent.cs` — `OwnerName` для подписи анонса.

## 📝 ЧТО СОЗДАТЬ/ИЗМЕНИТЬ (только _MalinovStation)

1. YAML: файл `malinov_devicenet_frequencies.yml` с прототипом `MalinovMessengerFrequency` (2210)
   и локализованным именем (ключ в malinov-cartridges.ftl).
2. Shared:
   - `MalinovMessengerUiMessageEvent : CartridgeMessageEvent` (action Send / RefreshContacts,
     targetAddress: string?, text: string?).
   - Расширить `MalinovMessengerUiState`: выбранное имя контакта, строки текущей сессии
     (List строк вида «Имя: текст»), набор имён «в сети».
   - В `MalinovMessengerConstants` — ключи и команды `announce`/`msg`.
3. Server (`MalinovMessengerCartridgeSystem`):
   - Активация программы (`CartridgeActivatedEvent`): выставить частоту loader'у
     (`SetReceiveFrequency`/`SetTransmitFrequency` = MalinovMessengerFrequency); деактивация — вернуть null.
   - Анонс присутствия: broadcast `announce` {sender_name = PdaComponent.OwnerName} при активации;
     приём анонсов → кэш «имя → адрес» в компоненте программы (TTL ~30 сек, обновляется свежими анонсами).
   - Отправка: обработка UI-сообщения Send → `QueuePacket(адрес получателя, {command: msg, sender_name, text})`;
     ограничение длины текста константой (напр. 512 символов), обрезка или отказ.
   - Приём: подписка на `CartridgeRelayedEvent<DeviceNetworkPacketEvent>`; cmd=msg → добавить строку
     во «временную сессию» компонента и обновить UiState.
4. Client (`MalinovMessengerUiFragment`): выбор контакта из списка, TextEdit + кнопка отправки,
   лента строк текущей сессии, индикатор «в сети/не в сети» у имени.
5. Локализация en+ru: подсказка ввода, кнопка отправки, статусы онлайна.
6. Тесты (см. DoD). Обнови `Docs/messenger-rollout.md`.

## ✅ DEFINITION OF DONE (Этап 2)

Все 5 команд гейта зелёные; vanilla не изменён. Интеграционный тест:

1. Спавнить два PassengerPDA рядом (range 500 покрывает), активировать программу на обоих
   (активация должна выставить частоту и разослать announce).
2. Прогнать тики так, чтобы оба кэша получили чужой анонс; ассерт: каждый видит другого «в сети».
3. Через систему отправителя отправить сообщение адресату (по адресу из кэша).
4. Прогнать тики до обработки пакета; ассерт: у получателя в компоненте есть строка от имени отправителя
   с исходным текстом; UiState обновлён.
5. Отправка неизвестному адресу не падает и возвращает ошибку состояния (не exception).

## 📤 ФОРМАТ ОТЧЁТА

1. Список изменённых/созданных файлов (+ git status без vanilla).
2. Подтверждение зелёного гейта.
3. Обновлённый `Docs/messenger-rollout.md`.

Приступай. Первым делом сообщи, какие файлы читаешь, затем выполняй.
