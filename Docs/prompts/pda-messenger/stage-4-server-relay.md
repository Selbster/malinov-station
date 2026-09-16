# PDA-мессенджер — Этап 4: Сервер-ретранслятор станции (passthrough)

Ты — Senior DevOps/C# разработчик с 10-летним стажем, эксперт в архитектуре ECS (RobustToolbox / Space Station 14)
и автоматизации CI/CD. Реализуешь этапы механики мессенджера для КПК в репозитории
https://github.com/Selbster/malinov-station.

🌿 ВЕТКА: работай в ветке `pda-messenger`. НЕ переключайся на `alpha-master`, НЕ мержи.
Один этап = один атомарный коммит: `feat: сервер-ретранслятор`.

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
   `- [x] Этап 4 — сервер-ретранслятор (commit: <hash>, CI: green)`.

## 🧠 Тестирование

Content.IntegrationTests — реальный headless сервер+клиент (не моки). Образцы:
`Fixtures/GameTest.cs`, `Tests/DeviceNetwork/DeviceNetworkTest.cs`.

## 📐 Сквозные соглашения мессенджера (соблюдай строго)

- Папки кода: `Content.{Shared|Server|Client}/_MalinovStation/Messenger/`.
  Namespace: `Content.<Проект>._MalinovStation.Messenger`.
- Прототипы: внутренняя программная сущность `MalinovMessengerCartridge`; НОВЫЙ сервер `MalinovMessengerServer`;
  частота `MalinovMessengerFrequency` = **2210**.
- Константы протокола (`MalinovMessengerConstants`): команды `announce`, `msg` (уже есть),
  добавляются `directory_req`, `directory`; ключи payload: `command`, `sender_name`, `text`,
  `target`, `entries`.
- CVar'ы: файл `CCVars.Messenger.cs`, имена `malinov.messenger.*`.
- Локализация: ключи `malinov-messenger-*`; en-US/ru-RU синхронно.

---

## 📦 КОНТЕКСТ: что уже сделано

Этапы 0–3: скелет; каталог из записей станции; доставка сообщений через сервер-ретранслятор
с анонсами присутствия; история переписки, привязанная к ID-карте. Если чего-то нет — СТОП.

## 🎯 ЦЕЛЬ ЭТАПА 4

Вся маршрутизация идёт через существующий станционный `TelecomServer`, дополненный компонентом
`MalinovMessengerServer`: анонсы присутствия и сообщения направляются на активный `TelecomServer`,
он пересылает пакеты адресатам (passthrough, ничего не хранит) и ведёт директорию «имя → адрес».
Клиент, у которого сервер недоступен, показывает ошибку «сервер недоступен».

## 🚫 ЯВНО НЕ ДЕЛАТЬ

Хранение сообщений на сервере, ack/таймауты, уведомления, антиспам, админ-логи. Размещение сервера на картах
не требуется — `TelecomServer` уже существует на картах, а для тестов спавнится как `TelecomServer`.

⚠️ **Разрешённое vanilla-изменение**: в `Resources/Prototypes/Entities/Structures/Machines/telecomms.yml`
к прототипу `TelecomServer` добавляются:
- `receiveFrequencyId: MalinovMessengerFrequency` и `transmitFrequencyId: MalinovMessengerFrequency`
  в компоненте `DeviceNetwork`;
- компонент `MalinovMessengerServer`.
Это единственное изменение vanilla на этапе 4.

## 📚 ОБЯЗАТЕЛЬНОЕ ЧТЕНИЕ

1. `Resources/Prototypes/Entities/Structures/Machines/telecomms.yml` — прототип `TelecomServer`,
   к которому добавляется компонент `MalinovMessengerServer` и частота мессенджера.
2. `Content.Server/Radio/EntitySystems/RadioSystem.cs` — пример поиска активного `TelecomServerComponent`
   по питанию и трансформу.
3. `Content.Server/Medical/CrewMonitoring/CrewMonitoringServerSystem.cs` — живой образец сервера на DeviceNetwork.
4. `Content.Server/DeviceNetwork/System(s)/DeviceNetworkSystem.cs` — QueuePacket/SetAddress API.
5. Своё: `_MalinovStation/Messenger/*` (точки интеграции клиента).

## 📝 ЧТО СОЗДАТЬ/ИЗМЕНИТЬ

1. **Vanilla-изменение (разрешённое)**: в `Resources/Prototypes/Entities/Structures/Machines/telecomms.yml`
   к прототипу `TelecomServer` добавить:
   - в `DeviceNetwork`: `receiveFrequencyId: MalinovMessengerFrequency` и
     `transmitFrequencyId: MalinovMessengerFrequency`;
   - компонент `MalinovMessengerServer`.
   Это позволяет использовать существующие на станции `TelecomServer` как ретранслятор мессенджера.
2. Shared: компонент `MalinovMessengerServerComponent`; расширение констант командами `directory_req`/
   `directory` и ключами `target`, `entries`.
3. Server: система `MalinovMessengerServerSystem`:
   - Регистрация: пакет `announce` от клиента → записывает «имя → адрес отправителя» в директорию (память раунда).
   - Директория: `directory_req` → ответ `directory` {entries} запросившему.
   - Ретрансляция: `msg` {target(имя), sender_name, text} → резолвинг по директории → `QueuePacket` адресату;
     нет адресата → ответ об ошибке отправителю.
4. Server (клиентская логика программы):
   - Адрес активного сервера ищется по `TelecomServerComponent` + `MalinovMessengerServerComponent`
     + питание на той же карте/станции.
   - Анонсы и сообщения шлются НА СЕРВЕР; директория подтягивается по `directory_req`
     и мерджится с каталогом записей станции (этап 1).
   - Нет активного сервера → состояние «сервер недоступен» (отправка блокируется).
5. Client (`MalinovMessengerUiFragment`): индикатор режима («через сервер» / «недоступен»),
   сообщение об ошибке доставки.
6. Локализация en+ru: статусы сервера. Тесты (см. DoD). Обнови `Docs/messenger-rollout.md`.

## ✅ DEFINITION OF DONE (Этап 4)

Все 5 команд гейта зелёные; изменён ровно один vanilla-файл (`telecomms.yml`) с добавлением
компонента `MalinovMessengerServer` и частоты. Интеграционный тест:

1. Спавнить на гриде два PassengerPDA с активными программами и один `TelecomServer`
   (он уже содержит `MalinovMessengerServerComponent` после изменения прототипа).
   Убедиться, что сервер стал активным (питание + компонент на той же карте).
2. Оба клиента зарегистрировались (директория сервера содержит обе записи).
3. A отправляет B через сервер → B получил сообщение; ассерт у B.
4. Отправка при отсутствии сервера (удалить сущность) → клиент получает состояние «сервер недоступен»,
   исключений нет.

## 📤 ФОРМАТ ОТЧЁТА

1. Список файлов (+ git status; vanilla-изменение — только `telecomms.yml`).
2. Подтверждение зелёного гейта.
3. Обновлённый `Docs/messenger-rollout.md`.
4. Полный `git diff Resources/Prototypes/Entities/Structures/Machines/telecomms.yml`.

Приступай. Первым делом сообщи, какие файлы читаешь, затем выполняй.
