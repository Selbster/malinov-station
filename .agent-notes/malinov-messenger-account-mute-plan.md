# Malinov Messenger — мьют по аккаунт-имени + одноимённые карты. План (Фаза 2)

Дата: 2026-09-05. Автор: opencode, на основе Фазы 1 (read-only).

## Текущая архитектура (кратко)

Мессенджер — трёхсубъектная схема через device-network (частота `MalinovMessengerConstants.Frequency = 2210`):

- **Картридж** (PDA, `MalinovMessengerCartridgeSystem`) — «аккаунт» = вставленная ID-карта.
  `session.IdentityName = idCard.FullName`, `session.LinkedAccount = EntityUid карты`.
  История в `MalinovMessengerAccountComponent.Sessions` живёт на сущности ID-карты.
- **Релей** (`MalinovMessengerServerSystem`, на TelecomServer) — `Directory: name → address`,
  `Muted: HashSet<string>` (строки имени из карты), rate-limit по имени, MutedNames в рассылке.
- **Клиент** (`Content.Client`) — читает только `MalinovMessengerUiState` (в т.ч. `IsMuted`), пакеты не
  шлёт и `MutedNamesKey` сам не парсит (это делает server-картридж).

Путь пакета: картридж `SendMessage` → `{cmd=msg, sender_name=IdentityName, target, text, msg_id}` →
релей (`Directory[target]` → `QueuePacket`) → картридж получателя (`AddSessionMessage`).
Мьют: релей шлёт `{cmd=directory, entries, muted_names}`; картридж ставит
`session.IsMuted = mutedNames.Contains(IdentityName)`.

Ключевая проблема: **весь мьют и адресация завязаны на строку `idCard.FullName`**, которая:
- меняется (*смена ID-карты меняет «аккаунт»*) и может быть пустой;
- не уникальна (две карты с одинаковым FullName → `Directory[name] = address` перезатирает,
  `Muted.Contains(name)` мьют обоих).

Аккаунт игрока (SS14 launcher username) в мессенджере не используется вообще.
Эталонный паттерн vanilla — `KickCommand`/`BanCommand`: `IPlayerManager.TryGetSessionByUsername(name, ...)`
→ `session.Name`; `IPlayerManager.TryGetSessionByEntity(uid, out session)` для сущностей.

---

## Фаза 3 — мьют по аккаунт-имени (предлагаемый объём)

### Новый сетевой ключ
В `MalinovMessengerConstants` (Shared) добавить `SenderAccountKey = "sender_account"` — строка
аккаунт-имени (launcher username). Это расширение `NetworkPayload` словарём: старые клиенты не
ломаются (неизвестный ключ игнорируется), декодеры девайс-сети терпимы.

### Получение аккаунт-имени отправителя (server, картридж)
- Новый `IPlayerManager` dependency в `MalinovMessengerCartridgeSystem`.
- Помощник `TryGetSenderAccount(loaderUid)`: обход контейнерной цепочки вверх (паттерн
  `TryGetPdaHolder`, CartridgeSystem:638) до сущности с `ActorComponent` →
  `_playerManager.TryGetSessionByEntity(holder, out var session)` → `session.Name`.
- Кэш в `MalinovMessengerCartridgeSessionComponent.AccountName: string?`. Обновляется в `Update()`
  (пересчёт, когда PDA в руках; null когда на полу — announce не несёт account).
  Смена карты меняет `IdentityName`, но НЕ `AccountName` → мьют переживает смену карты.

### Что несёт relay
- `EnsureComp<MalinovMessengerServerComponent>`:
  - `Muted` хранит **аккаунт-имена** (переименовать поле не обязательно — комментарием пометить).
  - `Directory` оставить как есть (ключ — display-name) для совместимости адресации. 
- `AnnouncePresence` и `SendMessage` добавляют `SenderAccountKey = session.AccountName`.
- `HandleMessage` (релей): блокирует если `Muted.Contains(senderAccount)`; если account отсутствует
  в пакете (PDA на полу / провал резолва) — fallback на проверку по `senderName` (прежнее поведение),
  чтобы не потерять мьют на пограничных состояниях.
- `HandleAnnounce` (релей): Directory пополняется, как раньше (ключ — display-name). Аккаунт хранить
  в отдельном `Dictionary<string /*account*/, string /*address*/> AccountAddresses` (опционально,
  только если понадобится решить адресацию по аккаунту — в этой фазе не требуется).

### Команды
- `MessengerMuteCommand` / `MessengerUnmuteCommand`:
  - вместо «любая строка» → `_playerManager.TryGetSessionByUsername(args[0], out var target)`;
    не найден (нет на сервере) → ошибка «игрок не онлайн» (мьют раунд-локальный, как сейчас);
  - `system.Mute(relay, target.Name, adminName)` — в Muted попадает аккаунт-имя;
  - `GetCompletion` по `_playerManager.Sessions` (как `KickCommand`), чтобы админ видел аккаунт-имена.
- FTL: description/help/success обновить: «<аккаунт-имя лаунчера>», не «имя из ID-карты».
- Админ-лог `Mute/Unmute`: писать `account='...' card='...'` (карт-имя берём из Directory/сессии,
  если доступно; иначе только account). Расширить подпись: `Mute(uid, string accountName, string? cardName, string? admin)`.

### Сторона получателя (server-картридж)
- `HandleDirectory`: `session.IsMuted = accountName != null && mutedNames.Contains(accountName)`
  (или fallback `IdentityName` при null account). Клиентский `UiState.IsMuted` не трогаем —
  контракт клиента не ломается (fragment просто рисует оверлей/лок).
- Мьют-ошибка уже умеет ставить `IsMuted = true` при `malinov-messenger-error-muted` на error-reply.

### Совместимость / стоп-проверки
- Новый ключ пакета — согласованное изменение Shared+Server; клиент не читает пакеты → не ломается.
- Изменения только в `_MalinovStation` (Scope), vanilla не трогаем.

### Tradeoffs минимального варианта
- Мьют наказывает аккаунт строго. Смена карты не снимает, однофамильцы по карте не задеваются.
- НО адресация (`Directory` по display-name) и коллизия одноимённых карт остаются — см. Фазу 4.

---

## Фаза 4 — одноимённые карты (варианты, нужен выбор человека)

### Контекст
Источники имён: (1) crew-manifest (источник контактов для UI), (2) `idCard.FullName` (источник
`IdentityName`/`SenderNameKey`). Получатель выбирается по имени-строке, поэтому два члена экипажа
одинакового имени неразличимы в UI уже на уровне crew-manifest.

### Вариант A — минимальный (рекомендуемый)
- `Muted` → аккаунт-имена (Фаза 3). Это снимает главную несправедливость: мьют однофамильца по карте
  не задевает второго, смена карты не снимает мьют.
- `Directory`/`Peers`/target остаются строкой display-name. Коллизия доставки при ДВУХ ОДИНАКОВЫХ
  именах остаётся как сейчас (затирание), фиксировать считаем не нашим (это ограничение crew-manifest/XAML-строки).
- Ребёностка `HandleDirectory` comment «identical names» — заменить на «Mute by account; routing by display-name».
- **Плюсы**: ноль изменений клиентского UI-контракта (Contacts, SelectedContact, TargetName),
  ноль изменений пакетов кроме аккаунт-поля, короткий дифф.
- **Минусы**: две карты с одинаковым FullName всё ещё неразличимы для реальной отправки сообщения
  (кто-то один «выигрывает» в Directory). Это принятое ограничение.

### Вариант B — полный
- Вводится стабильный ключ аккаунта для адресации:
  - релей: `Directory: Dictionary<string /*account*/, string /*address*/>` + отдельная карта
    `display-name → account` (или entry `{Name, Account, Address}`);
  - `TargetKey` в сообщении → аккаунт получателя; картридж хранит
    `Peers: Dictionary<string /*account*/, PeerCache>` и на клиента отдаёт контакты с полем id
    (`MalinovMessengerContact.Id`, `SelectedContact`/`TargetName` → id);
- **Минусы**: ломает клиентский контракт (`MalinovMessengerContact`, `SelectedContact`, `TargetName`,
  `MalinovMessengerUiMessageEvent`), требует согласования сети, больше тестов. Это отдельное крупное
  изменение, НЕ входит в объём текущей задачи без отдельного решения.

### Рекомендация
Фаза 3 (мьют по аккаунту) — сразу, вариант A. Фаза 4 — вариант A (минимальный) или B (полный) на
выбор человека; A закрывает критерии приёмки «одинаковые карт-имена не задевают мьют друг друга».

---

## Тесты (Фаза 3)
- `MalinovMessengerTest`:
  - `Mute_BlocksDeliveryUntilUnmute`: PDA удерживаются через `HoldPdaWithSession` (хелпер уже есть,
    line 2850); мьют по `session.Name`; смена карты (смена FullName) НЕ снимает мьют;
  - новый `Mute_ByAccountName_SurvivesCardSwap`: два игрока, обе карты названы одинаково:
    мьют первого — второй продолжает слать;
  - `MuteCommand_RequiresAdminRights` — без изменений (атрибут).
- `MalinovMessengerAdminLogTest`: Mute-лог содержит account-имя + контекст (карта/display-name).

## Изменяемые файлы (Фаза 3)
- `Content.Shared\_MalinovStation\Messenger\MalinovMessengerConstants.cs` (+`SenderAccountKey`).
- `Content.Server\_MalinovStation\Messenger\MalinovMessengerServerSystem.cs`.
- `Content.Server\_MalinovStation\Messenger\MalinovMessengerCartridgeSystem.cs`.
- `Content.Server\_MalinovStation\Messenger\MalinovMessengerCartridgeSessionComponent.cs` (+`AccountName`).
- `Content.Server\_MalinovStation\Messenger\MessengerMuteCommand.cs` / `MessengerUnmuteCommand.cs`.
- `Resources\Locale\en-US\_MalinovStation\malinov-cartridges.ftl` / `ru-RU\...` (description/help/success).
- `Content.IntegrationTests\Tests\_MalinovStation\Messenger\MalinovMessengerTest.cs` / `...AdminLogTest.cs`.

## Открытые вопросы к человеку перед реализацией
1. Мьютять ТОЛЬКО онлайн-игрока (lookup по сессии) — ок? Офлайн-аккаунт мьютить не сможем (раунд-локально отсутствует ID). Предлагаю: онлайн-проверка + понятная ошибка.
2. Вариант Фазы 4: A (минимальный) или B (полный).
3. Новый ключ пакета `sender_account` — подтверждаешь (стоп-условие требует согласования).

---

## Решение (согласовано 2026-09-06)
1. **Да, Фаза 3** — реализовано.
2. **Только онлайн** — `messengermute/unmute` резолвят аккаунт через `IPlayerManager.TryGetSessionByUsername`; офлайн → `malinov-messenger-command-player-not-found`.
3. **Вариант B** — реализовано:
   - `Muted` = аккаунт-имена; `Directory`/`Peers` завязаны на аккаунт (display-name — метка, fallback только при отсутствии аккаунта: PDA не в руках).
   - Новый сетевой ключ `SenderAccountKey` + `DisplayNamesKey` (Shared+Server, клиент читает только `UiState`).
   - `MalinovMessengerContact.Id` = стабильный аккаунт; клиент выбирает/шлёт по Id, шапка рисует display-name.
   - История ключуется по аккаунту отправителя.

## Доводка 2026-09-06: присутствие = КАРТА, мьют = АККАУНТ (баг «украденный КПК дублирует имя»)
**Баг**: при смене владельца КПК релей не чистил `Directory`; под аккаунт-ключом оставался
призрачный ключ старого владельца на том же адресе + само-эхо на украденном КПК → контакт
«имя карты» дублировался.

**Решения** (подтверждено человеком):
- Новый сетевой ключ `SenderCardKey = "sender_card"` (server-side `GetNetEntity(карты).ToString()`).
- Присутствие/адресация/история завязаны на **стабильный id карты**: `Directory`/`Names`/
  `Peers`/сессии/unread/`SelectedContact`/`TargetKey` ключуются cardId. Карта сохраняет identity
  при переезде между КПК и смене держателя → украденная карта никогда не дублируется
  (ключ карты единственный, старый адрес перезатирается ре-анонсом).
- Мьют/`Muted` — **по аккаунту держателя** (`SenderAccountKey`), переживает смену карты;
  проверка в релее только при наличии аккаунта. Rate-limit: `account ?? card ?? name`.
- Релей: `AccountCards: announce-key → cardId` для админ-лога «card: 'имя'»; TTL-прунинг
  `Directory`/`Names`/`_lastSeen` по `MalinovMessengerPeerTtlSeconds` в `HandleAnnounce`.
- Клиент: `MalinovMessengerContact.Id` = cardId; `ContactKey = Id ?? Name`.
- Self = своя карта (`session.CardId`): отправка себе — по cardId; история-и ключи контактов — cardId.
- Отправка в админ-лог логирует display-name цели (`Peers[cardId].Name`).

**Файлы**: `Constants` (+`SenderCardKey`), `Contact` (Id=card), `ServerComponent` (+`AccountCards`),
`ServerSystem` (rekey, TTL, mute-by-account, log context), `CartridgeSystem`
(LinkAccount→`CardId`, announce/send/peers/history/self по cardId, `IsMuted` по account),
`CartridgeSessionComponent` (+`CardId`), тесты → cardId-ключи.

**Проверка**: `dotnet build` 0 ошибок; `FullyQualifiedName~MalinovMessenger` — 32 passed, 0 skipped.
Новый регресс-тест `StolenCard_MovesPresenceWithoutDuplication` (карта переезжает в КПК вора:
один каталог-ключ, один peer на имя, маршрут к КПК вора; падает на аккаунт-модели).
Ни один upstream/vanilla файл не изменён.