# Исправление: buff_id званий становится серверно-авторитетным (2026-06-14)

## Проблема

При смене активного звания (титула) персонажем ассоциированный бафф никогда не
применялся. Метод `addBuff` ожидал **ID титула** для поиска
`AppellationTemplate` и извлечения `BuffId`, но методы `Change()` и `Load()`
передавали **сырой ID баффа** из клиентского пакета / БД. Поскольку ни один
шаблон не имеет ID, совпадающего с ID баффа, `GetAppellationsTemplate`
всегда возвращал `null` и бафф молча пропускался.

Дополнительно: клиентский `buff_id` сохранялся и рассылался без серверной
валидации, что позволяло потенциально применять произвольные баффы.

## Причина

`CharacterAppellations.addBuff(uint titleId)` использовал параметр как ключ
шаблона:
```csharp
var buffId = CharacterManager.Instance.GetAppellationsTemplate(titleId)?.BuffId ?? 0;
```

Но `Change()` вызывал `addBuff(appellationBuffId)`, где `appellationBuffId` —
это сырой ID баффа из клиентского пакета, а не ID титула. То же несовпадение
было в `Load()` со значением `buff_id` из БД.

## Исправление

### 1. `addBuff` — рефакторинг, принимает сырой ID баффа

```csharp
// Было
private void addBuff(uint titleId)
{
    var buffId = CharacterManager.Instance.GetAppellationsTemplate(titleId)?.BuffId ?? 0;
    if (buffId != 0) Owner.Buffs.AddBuff(buffId, Owner);
}

// Стало
private void addBuff(uint buffId)
{
    if (buffId != 0) Owner.Buffs.AddBuff(buffId, Owner);
}
```

### 2. `Change()` — серверно-авторитетный ID баффа из шаблона

```csharp
// Было
ActiveAppellationBuffId = appellationBuffId;
addBuff(appellationBuffId);

// Стало
var template = CharacterManager.Instance.GetAppellationsTemplate(appellationId);
var buffId = template?.BuffId ?? 0;
ActiveAppellationBuffId = buffId;
addBuff(buffId);

// Предупреждение при несовпадении (защита от подмены)
if (appellationBuffId != 0 && appellationBuffId != buffId)
{
    Logger.Warn("Клиентский бафф {0} отличается от серверного {1} ...", ...);
}
```

### 3. `Load()` — аналогичный подход

```csharp
// Было
ActiveAppellationBuffId = activeBuffId;
addBuff(activeBuffId);

// Стало
var template = CharacterManager.Instance.GetAppellationsTemplate(id);
var buffId = template?.BuffId ?? 0;
ActiveAppellationBuffId = buffId;
addBuff(buffId);
```

### 4. SQL-миграция

Добавлена колонка `buff_id` в таблицу `appellations`:
```sql
ALTER TABLE `appellations` ADD COLUMN `buff_id` INT UNSIGNED NOT NULL DEFAULT 0 AFTER `owner`;
```

## Изменённые файлы

| Файл | Изменение |
|------|-----------|
| `AAEmu.Game/Models/Game/Char/CharacterAppellations.cs` | `addBuff` принимает сырой ID баффа; `Change()` и `Load()` определяют BuffId из серверного шаблона |
| `AAEmu.Game/SQL/updates/2026-06-14_aaemu_game_appellations_add_buff_id.sql` | Добавлена колонка `buff_id` |
| `AAEmu.UnitTests/Game/Models/Game/Char/CharacterAppellationsTests.cs` | Тесты обновлены с Moq, проверяют вызов `AddBuff` с корректным ID |

## Тесты

**`AAEmu.UnitTests/Game/Models/Game/Char/CharacterAppellationsTests.cs`**

| Тест | Что проверяет |
|------|---------------|
| `Change_ToKnownAppellation_BroadcastsPacketWithCorrectBuffId` | Известное звание → пакет содержит `BuffId` из шаблона И `AddBuff` вызывается с правильным ID |
| `Change_ToZero_BroadcastsPacketWithZeroBuffId` | Сброс звания → `buffId = 0` в пакете |
| `Change_ToUnknownId_LeavesActiveAppellationUnchanged` | Неизвестный ID → `ActiveAppellation` не изменяется |
