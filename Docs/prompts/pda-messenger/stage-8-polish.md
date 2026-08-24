# PDA-мессенджер — Этап 8: Полировка и подготовка к мержу 🚧 (плейтест человеком)

Ты — Senior DevOps/C# разработчик с 10-летним стажем, эксперт в архитектуре ECS (RobustToolbox / Space Station 14)
и автоматизации CI/CD. Финальный этап механики мессенджера для КПК в репозитории
https://github.com/Selbster/malinov-station.

🌿 ВЕТКА: работай в ветке `pda-messenger`. НЕ переключайся на `alpha-master`, НЕ мержи.
Один этап = один атомарный коммит: `feat(pda-messenger): этап 8 — полировка и подготовка к мержу`.

## 🛠️ DevOps Gate (из корня репозитория; копия CI проекта .github/workflows/build-test-debug.yml)

1. `dotnet restore`
2. `dotnet build --configuration DebugOpt --no-restore /m`
3. `dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj --no-build`
4. `dotnet test --no-build --configuration DebugOpt Content.Tests/Content.Tests.csproj`
5. `dotnet test --no-build --configuration DebugOpt Content.IntegrationTests/Content.IntegrationTests.csproj`

## ⚠️ Жесткие правила (DevOps Gate)

1. «ЗЕЛЕНЫЙ СВЕТ»: перед коммитом и отчётом ВСЕ 5 команд обязаны пройти (exit code 0).
2. ОШИБКИ: чинить причину, не симптом. После 3 неудачных попыток подряд — СТОП с описанием.
3. ЗАПРЕТ КОСТЫЛЕЙ: запрещено ослаблять assert'ы, добавлять [Ignore]/[Explicit], менять чужие тесты.
4. ИЗОЛЯЦИЯ И КОНВЕНЦИИ ФОРКА: `.agents/skills/ss14-upstream-maintenance/SKILL.md`.
   Всё новое в `_MalinovStation`; vanilla не менять (LogType.cs уже правился на этапе 7 — больше НИЧЕГО).
5. ОТЧЁТНОСТЬ: финально приведи `Docs/messenger-rollout.md` в порядок (все этапы, хэши, статус CI).

## 🧠 Тестирование

Полный гейт обязателен дважды: до коммита и после (контрольная проверка чистой ветки).

## 📐 Сквозные соглашения мессенджера

- Namespace `Content.<Проект>._MalinovStation.Messenger`; префиксы `Malinov*`/`malinov-*`.
- CVar'ы проекта: `.history_per_contact`, `.relay_timeout_sec`, `.rate_window_sec`,
  `.rate_max_msgs`, `.sound_enabled`.

---

## 📦 КОНТЕКСТ: что уже сделано

Этапы 0–7 завершены и закоммичены: скелет; каталог из записей станции; P2P; история;
сервер-релей; failover; уведомления; антиспам/mute; админ-логи. Проверь `Docs/messenger-rollout.md`
и фактическое состояние ветки; чего-то нет — СТОП.

## 🎯 ЦЕЛЬ ЭТАПА 8

Финализация: аудит автоустановки программы и исключений, чистка долгов, аудит локализации,
подготовка материалов для ручного плейтеста и мержа.

## 📝 ЧТО СДЕЛАТЬ

1. **Аудит автоустановки**: проверить, что `MalinovMessengerInstallerSystem` устанавливает программу на все
   station-side PDA, кроме прописанных в исключениях (CentCom, ядерные оперативники, ERT/CBRN).
   При необходимости обновить список `ExcludedPdaPrototypes`.
2. **Чистка**: убрать все временные TODO этапов 1–6, кроме осознанно оставленных решений
   (упрощение сопоставления по имени — оставить с пометкой «known simplification»).
3. **Аудит локализации en-US/ru-RU**: каждая строка UI имеет ключ в ОБЕИХ локалях, нет забытых
   сырых строк; прогнать YAMLLinter.
4. **Аудит диффа**: `git diff alpha-master...HEAD --stat` — убедиться, что вне `_MalinovStation`,
   `Docs/` и тестов изменён только `Content.Shared.Database/LogType.cs` (этап 7). Отчёт приложить.
5. **Документация для человека** (в `Docs/messenger-rollout.md`, раздел «Плейтест»):
   чек-лист ручной проверки в игре: проверка наличия программы в КПК, каталог, переписка двух игроков,
   отказ сервера + failover, уведомления, rate-limit, mute, админ-логи.
   Плюс итоговое summary изменений для PR.

## ✅ DEFINITION OF DONE (Этап 8)

- Двойной зелёный гейт (до и после коммита); дифф соответствует п.4.
- Автоустановка программы на обычные PDA и отсутствие на исключённых покрыты тестами.
- Локализация полная (en+ru), TODO почищены.
- `Docs/messenger-rollout.md`: все 9 строк этапов отмечены, есть раздел «Плейтест» и summary.

## 🛑 ГЕЙТ ЧЕЛОВЕКА (обязательно!)

После коммита и отчёта ОСТАНОВИСЬ. Мерж в `alpha-master` выполняет человек ПОСЛЕ плейтеста.
Тебе запрещено выполнять merge, rebase поверх alpha-master или push без явного указания.

## 📤 ФОРМАТ ОТЧЁТА

1. `git diff alpha-master...HEAD --stat` (полный).
2. Двойное подтверждение зелёного гейта.
3. Финальный `Docs/messenger-rollout.md`.
4. Список известных упрощений/долгов (если остались).

Приступай. Первым делом сообщи, какие файлы читаешь, затем выполняй.
