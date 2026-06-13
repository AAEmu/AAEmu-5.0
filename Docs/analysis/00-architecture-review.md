# Архитектурный анализ AAEmu — Code Review от Senior-разработчика

**Дата:** 2026-06-13  
**Версия проекта:** AAEmu 5.0.7.0 private  
**Аналитик:** Senior Developer Code Review

---

## 1. Общая архитектура

AAEmu — это серверная часть MMORPG ArcheAge, написанная на C# (.NET 10.0). Проект использует модульную архитектуру с разделением на три основных проекта:

```
AAEmu.sln
├── AAEmu.Login      — Сервер авторизации
├── AAEmu.Game       — Игровой сервер (основная логика)
└── AAEmu.Commons    — Общие утилиты и сетевой слой
```

---

## 2. Ключевые компоненты и их ответственность

### 2.1. AAEmu.Commons — Базовый слой

| Компонент | Ответственность |
|-----------|-----------------|
| `Server.cs` | TCP-сервер на базе NetCoreServer, управление подключениями |
| `Session.cs` | Сессия клиента, отправка/приём пакетов, атрибуты |
| `Client.cs` | TCP-клиент для межсерверного взаимодействия |
| `PacketStream.cs` | Сериализация/десериализация пакетов |
| `MySQL.cs` | Подключение к MySQL |
| `Singleton.cs` | Базовый класс синглтона |

### 2.2. AAEmu.Login — Сервер авторизации

| Компонент | Ответственность |
|-----------|-----------------|
| `LoginService.cs` | Инициализация сервера логина |
| `Program.cs` | Точка входа, конфигурация, DI |

### 2.3. AAEmu.Game — Игровой сервер (ядро)

#### Менеджеры (Core/Managers)

| Менеджер | Ответственность | Размер |
|----------|-----------------|--------|
| `WorldManager.cs` | Управление мирами, объектами, регионами, видимостью | ~1785 строк |
| `SkillManager.cs` | Загрузка и управление скиллами, баффами, эффектами | ~2334 строки |
| `ItemManager.cs` | Шаблоны предметов, инвентарь, контейнеры, таймеры | ~2503 строки |
| `QuestManager.cs` | Квесты, компоненты, действия, таймеры | ~2136 строк |
| `HousingManager.cs` | Жильё, налоги, мебель, строительство | ~2947 строк |
| `CharacterManager.cs` | Персонажи, онлайн-трекинг | — |
| `AuctionManager.cs` | Аукцион | — |
| `MailManager.cs` | Почта | — |
| `SpawnManager.cs` | Спавн юнитов | — |
| `TaskManager.cs` | Планировщик задач | — |

#### Сетевой слой

| Компонент | Ответственность |
|-----------|-----------------|
| `GameNetwork` | Клиентские подключения |
| `StreamNetwork` | Внутриигровые события |
| `LoginNetwork` | Связь с сервером логина |

---

## 3. Паттерны взаимодействия

### 3.1. Инициализация (GameService.StartAsync)

```
GameService.StartAsync
├── MySQL Database Update
├── ClientFileManager.Initialize
├── TickManager.Initialize
├── WorldManager.Load
├── Load GameData
├── Load Managers
├── Spawn All
└── Start Networks
```

### 3.2. Паттерн Singleton

Большинство менеджеров используют паттерн Singleton через базовый класс:

```csharp
public class WorldManager : Singleton<WorldManager>, IWorldManager
```

### 3.3. Паттерн Observer (события)

```csharp
// SkillManager подписывается на загрузку предметов
SkillManager.Instance.OnSkillsLoaded += OnSkillsLoaded;

// WorldManager подписывается на тики
TickManager.Instance.OnTick.Subscribe(ActiveRegionTick, TimeSpan.FromSeconds(1));
```

### 3.4. Паттерн Region-based Visibility

```
WorldManager
├── Regions (64x64 units)
├── Sectors (16x16 per cell)
└── Neighborhood (REGION_NEIGHBORHOOD_SIZE = 2)
```

Объекты добавляются/удаляются из регионов при перемещении, что позволяет эффективно определять видимость.

---

## 4. Потенциальные узкие места и зоны риска

### 4.1. 🔴 Критические проблемы

#### A. Блокировки на коллекциях

```csharp
// WorldManager.cs — потенциальная блокировка
private readonly ConcurrentDictionary<uint, Character> _characters = new();

// Но используется lock в других местах
lock (_allItems) { ... }
lock (_removedItems) { ... }
```

**Риск:** Смешение `ConcurrentDictionary` и явных блокировок может привести к deadlock.

#### B. Долгие операции в тике

```csharp
// WorldManager.ActiveRegionTick — вызывается каждую секунду
private void ActiveRegionTick(TimeSpan delta)
{
    foreach (var character in GetAllCharacters())  // O(n)
    {
        CombatTick(character);
        RegenTick(character);
        BreathTick(character);
    }
    SpawnManager.Instance.Update();  // Может быть тяжёлым
}
```

**Риск:** При большом количестве игроков (>1000) это может вызвать задержки.

#### C. Отсутствие кэширования запросов к БД

```csharp
// HousingManager — запрос к MySQL в горячем пути
using (var connection = MySQL.CreateConnection())
{
    // ...
}
```

**Риск:** Создание нового подключения для каждой операции.

### 4.2. 🟡 Проблемы средней приоритетности

#### A. Большие менеджеры (God Objects)

| Файл | Строки | Проблема |
|------|--------|----------|
| HousingManager.cs | ~2947 | Слишком много ответственности |
| ItemManager.cs | ~2503 | Смешение шаблонов и runtime-логики |
| SkillManager.cs | ~2334 | Загрузка и выполнение в одном классе |

**Рекомендация:** Разделить на:
- `HousingTemplateManager` — загрузка шаблонов
- `HousingInstanceManager` — управление экземплярами
- `HousingTaxManager` — налоговая логика

#### B. Неэффективные поиски

```csharp
// WorldManager — линейный поиск
public Npc GetNpcByTemplateId(uint templateId)
{
    return _npcs.Values.FirstOrDefault(x => x.TemplateId == templateId);
}

// ItemManager — линейный поиск
public ItemTemplate GetItemTemplateFromItemId(uint itemId)
{
    foreach (var item in _templates)
    {
        if (item.Value.Id == itemId) return item.Value;
    }
    return null;
}
```

**Рекомендация:** Использовать `Dictionary<uint, T>` для O(1) поиска.

#### C. Отсутствие пула объектов

```csharp
// Создание новых объектов в горячем пути
var item = new Item();
var house = new House();
```

**Риск:** Давление на GC, особенно при массовых операциях.

### 4.3. 🟢 Проблемы низкой приоритетности

#### A. Дублирование кода

```csharp
// HousingManager — два почти идентичных метода
public void SetUntouchable0(House house, bool isUntouchable) { ... }
public static void SetUntouchable(House house, bool isUntouchable) { ... }
```

#### B. Неиспользуемые поля

```csharp
// Session.cs — не используется
protected override void OnSent(long sent, long pending) { }
protected override void OnError(SocketError error) { }
```

#### C. Комментарии вместо реализации

```csharp
// TODO: port full implementation from nikes WorldManager.AutoWaterProbeEnable
// TODO: proper mail handler
// TODO: better house killing handling
```

---

## 5. Проблемы масштабируемости

### 5.1. Вертикальное масштабирование

| Проблема | Влияние | Решение |
|----------|---------|---------|
| Один поток тиков | Бутылочное горлышко | Разделить на несколько потоков по мирам |
| Глобальные блокировки | Конкуренция | Шардинг по регионам |
| Отсутствие кэша | Нагрузка на БД | Добавить Redis/MemoryCache |

### 5.2. Горизонтальное масштабирование

**Текущее состояние:** Архитектура не поддерживает горизонтальное масштабирование.

**Рекомендации:**
1. Вынести состояние в распределённое хранилище (Redis)
2. Реализовать межсерверную коммуникацию (gRPC/RabbitMQ)
3. Разделить на микросервисы:
   - Auth Service
   - World Service
   - Chat Service
   - Trade Service

---

## 6. Проблемы поддерживаемости

### 6.1. Отсутствие тестов

```
AAEmu.UnitTests/         → Минимальное покрытие
AAEmu.IntegrationTests/  → Интеграционные тесты
```

**Рекомендация:** Добавить unit-тесты для:
- Менеджеров (Mock БД)
- Формул расчёта
- Сетевой сериализации

### 6.2. Слабая типизация

```csharp
// Использование object для атрибутов
public object GetAttribute(string name);
void AddAttribute(string name, object attribute);
```

**Рекомендация:** Использовать generic-типы или конкретные классы.

### 6.3. Отсутствие интерфейсов для менеджеров

```csharp
// Прямая зависимость от синглтона
WorldManager.Instance.GetAllCharacters();
```

**Рекомендация:** Внедрять зависимости через DI:

```csharp
public class SomeService
{
    private readonly IWorldManager _worldManager;
    
    public SomeService(IWorldManager worldManager)
    {
        _worldManager = worldManager;
    }
}
```

---

## 7. Рекомендации по улучшению

### Приоритет 1 (Критический)

1. **Разделить большие менеджеры** на специализированные компоненты
2. **Добавить кэширование** для частых запросов к БД
3. **Оптимизировать поиски** — заменить линейные на хэш-таблицы

### Приоритет 2 (Высокий)

4. **Реализовать пул объектов** для Item, House и т.д.
5. **Добавить unit-тесты** для критической логики
6. **Внедрить DI** вместо синглтонов

### Приоритет 3 (Средний)

7. **Рефакторинг дублирующегося кода**
8. **Добавить метрики** (Prometheus/Grafana)
9. **Документировать архитектуру** (ADR)

---

## 8. Заключение

AAEmu представляет собой функциональный сервер MMORPG с чётким разделением на компоненты. Основные проблемы:

- **Производительность:** Большие менеджеры, неэффективные поиски, отсутствие кэширования
- **Масштабируемость:** Архитектура заточена под один сервер
- **Поддерживаемость:** Отсутствие тестов, слабая типизация, God Objects

Архитектура требует постепенного рефакторинга с фокусом на разделение ответственности и внедрение современных практик разработки.

---

## Приложение A: Карта зависимостей

```
AAEmu.Game
├── Core/
│   ├── Managers/
│   │   ├── World/
│   │   │   ├── WorldManager ←→ PhysicsManager
│   │   │   ├── ZoneManager
│   │   │   └── SpawnManager
│   │   ├── SkillManager ←→ ItemManager
│   │   ├── ItemManager ←→ QuestManager
│   │   ├── HousingManager ←→ MailManager
│   │   └── ...
│   ├── Network/
│   │   ├── Game/
│   │   ├── Login/
│   │   └── Stream/
│   └── Packets/
│       ├── C2G/ (Client → Game)
│       └── G2C/ (Game → Client)
├── Models/
│   ├── Game/
│   │   ├── Char/
│   │   ├── Items/
│   │   ├── Skills/
│   │   └── ...
│   └── ...
└── Utils/
    ├── Scripts/
    └── DB/
```

## Приложение B: Метрики кода

| Метрика | Значение |
|---------|----------|
| Общий размер решения | ~500+ файлов |
| Крупнейший файл | HousingManager.cs (~2947 строк) |
| Средний размер менеджера | ~1500-2500 строк |
| Количество менеджеров | ~40+ |
| Покрытие тестами | Низкое |
