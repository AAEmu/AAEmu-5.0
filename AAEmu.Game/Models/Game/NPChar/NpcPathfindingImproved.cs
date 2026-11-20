// ============================================================
// ФАЙЛ: NpcPathfindingImproved.cs
// Локальный обход doodad и forbidden-area препятствий для NPC.
//
// ТЕКУЩИЕ ПРИНЦИПЫ:
// - doodad и другие NPC оцениваются единым local planner'ом через sampling/scoring;
// - для doodad используется более мягкая avoidance-модель, чем для живых целей;
// - forbidden area и world mesh проверяются через GeoData helper'ы;
// - stuck detection и turn-back остаются fallback-механизмами.
// ============================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.CryEngine.Mission;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.NPChar;

#region Новые типы данных

/// <summary>
/// Результат корректировки пути движения.
/// </summary>
public enum PathAdjustmentResult
{
    /// <summary>Путь свободен, коррекция не требуется</summary>
    PathClear,

    /// <summary>Путь успешно скорректирован для обхода препятствия</summary>
    Adjusted,

    /// <summary>Обход невозможен, NPC остановится перед препятствием</summary>
    Stopped,

    /// <summary>Ошибка контекста (ParentWorld или Region отсутствуют)</summary>
    InvalidContext,

    /// <summary>Обход невозможен, NPC разворачивается на 180°</summary>
    TurningBack
}

/// <summary>
/// Подробный результат одного шага MoveTowards.
/// </summary>
public enum MoveResult
{
    /// <summary>NPC не может двигаться или шаг слишком мал для движения.</summary>
    CannotMove,

    /// <summary>NPC уже находится в пределах допустимого расстояния до цели.</summary>
    ReachedDestination,

    /// <summary>NPC успешно сделал очередной шаг к цели.</summary>
    Moving,

    /// <summary>NPC не смог безопасно продвинуться вперёд и считается застрявшим.</summary>
    Stuck
}

public enum LocalObstacleKind
{
    Doodad,
    Npc,
    WorldObject
}

/// <summary>
/// Унифицированное представление локального препятствия для steering-слоя.
/// </summary>
public class LocalAvoidanceObstacle
{
    public uint SourceId { get; init; }
    public LocalObstacleKind Kind { get; init; }
    public Vector3 Position { get; init; }
    public float Radius { get; init; }
    public float HardRadius { get; init; }
    public float SoftRadius { get; init; }
    public float Weight { get; init; }
    public float AllowedOverlap { get; init; }
}

/// <summary>
/// Кандидат шага локального avoidance после быстрой obstacle-оценки.
/// Дорогая GeoData-проверка выполняется только для лучших кандидатов.
/// </summary>
public readonly struct AvoidanceCandidate
{
    public AvoidanceCandidate(Vector3 position, Vector3 lookAheadPoint, float score, int direction)
    {
        Position = position;
        LookAheadPoint = lookAheadPoint;
        Score = score;
        Direction = direction;
    }

    public Vector3 Position { get; }
    public Vector3 LookAheadPoint { get; }
    public float Score { get; }
    public int Direction { get; }
}

/// <summary>
/// Короткоживущий кэш nearby doodad для локального avoidance.
/// </summary>
public class DoodadQueryCacheState
{
    public Vector3 Origin { get; set; } = Vector3.Zero;
    public float SearchRadius { get; set; }
    public uint WorldId { get; set; }
    public uint ZoneId { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.MinValue;
    public List<Doodad> Doodads { get; set; } = new();
}

/// <summary>
/// Короткоживущий кэш nearby obstacle descriptors из vertsmission.
/// </summary>
public class WorldObstacleQueryCacheState
{
    public Vector3 Origin { get; set; } = Vector3.Zero;
    public float SearchRadius { get; set; }
    public uint WorldId { get; set; }
    public uint ZoneId { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.MinValue;
    public List<ObstacleDataDescriptor> Obstacles { get; set; } = new();
}

/// <summary>
/// Короткоживущий кэш nearby NPC для локального avoidance.
/// </summary>
public class NpcQueryCacheState
{
    public Vector3 Origin { get; set; } = Vector3.Zero;
    public float SearchRadius { get; set; }
    public uint WorldId { get; set; }
    public uint ZoneId { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.MinValue;
    public List<Npc> Npcs { get; set; } = new();
}

/// <summary>
/// Короткоживущий кэш финальной высоты шага движения.
/// </summary>
public class MovementHeightCacheState
{
    public Vector3 Origin { get; set; } = Vector3.Zero;
    public float Height { get; set; }
    public uint WorldId { get; set; }
    public uint ZoneId { get; set; }
    public bool UseReferenceHeight { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.MinValue;
}

/// <summary>
/// Конфигурация параметров для системы обхода препятствий.
/// </summary>
public class NpcPathfindingConfig
{
    /// <summary>Радиус поиска doodad вокруг NPC</summary>
    public float DoodadSearchRadius { get; set; } = 5f;

    /// <summary>Радиус поиска world obstacle descriptors из vertsmission вокруг NPC</summary>
    public float WorldObstacleSearchRadius { get; set; } = 5f;

    /// <summary>Включить короткий кэш выборки nearby doodad для pathfinding</summary>
    public bool EnableDoodadQueryCache { get; set; } = true;

    /// <summary>Включить короткий кэш nearby world obstacle descriptors для pathfinding</summary>
    public bool EnableWorldObstacleQueryCache { get; set; } = true;

    /// <summary>Включить короткий кэш nearby NPC для pathfinding</summary>
    public bool EnableNpcQueryCache { get; set; } = true;

    /// <summary>Срок жизни кэша nearby doodad в миллисекундах</summary>
    public int DoodadQueryCacheTtlMs { get; set; } = 300;

    /// <summary>Насколько NPC может сместиться, прежде чем nearby doodad query надо пересчитать</summary>
    public float DoodadQueryCacheReuseDistance { get; set; } = 1.25f;

    /// <summary>Запас по радиусу, при котором ещё можно переиспользовать предыдущую выборку nearby doodad</summary>
    public float DoodadQueryCacheRadiusMargin { get; set; } = 0.5f;

    /// <summary>Срок жизни кэша nearby world obstacle descriptors в миллисекундах</summary>
    public int WorldObstacleQueryCacheTtlMs { get; set; } = 400;

    /// <summary>Насколько NPC может сместиться, прежде чем nearby world obstacle query надо пересчитать</summary>
    public float WorldObstacleQueryCacheReuseDistance { get; set; } = 1.25f;

    /// <summary>Запас по радиусу, при котором ещё можно переиспользовать предыдущую выборку nearby world obstacle descriptors</summary>
    public float WorldObstacleQueryCacheRadiusMargin { get; set; } = 0.5f;

    /// <summary>Срок жизни кэша nearby NPC в миллисекундах</summary>
    public int NpcQueryCacheTtlMs { get; set; } = 200;

    /// <summary>Насколько NPC может сместиться, прежде чем nearby NPC query надо пересчитать</summary>
    public float NpcQueryCacheReuseDistance { get; set; } = 1.0f;

    /// <summary>Запас по радиусу, при котором ещё можно переиспользовать предыдущую выборку nearby NPC</summary>
    public float NpcQueryCacheRadiusMargin { get; set; } = 0.25f;

    /// <summary>Включить короткий кэш финальной высоты шага движения</summary>
    public bool EnableMovementHeightCache { get; set; } = true;

    /// <summary>Срок жизни кэша финальной высоты шага движения в миллисекундах</summary>
    public int MovementHeightCacheTtlMs { get; set; } = 300;

    /// <summary>Насколько NPC может сместиться, прежде чем финальную высоту нужно пересчитать</summary>
    public float MovementHeightCacheReuseDistance { get; set; } = 1.5f;

    /// <summary>Размер doodad по умолчанию, если ModelSize не задан</summary>
    public float DefaultDoodadSize { get; set; } = 1f;

    /// <summary>Радиус obstacle для doodad без внятного размера модели или simRadius</summary>
    public float DefaultDoodadAvoidanceRadius { get; set; } = 0.5f;

    /// <summary>Множитель перевода sim_radius doodad из клиентских сантиметров в метры</summary>
    public float DoodadSimRadiusScale { get; set; } = 0.01f;

    /// <summary>Минимальный итоговый радиус, при котором doodad считается препятствием для NPC</summary>
    public float MinDoodadBlockingRadius { get; set; } = 0.45f;

    /// <summary>Игнорировать doodad без sim_radius/ModelSize вместо fallback-радиуса</summary>
    public bool IgnoreDoodadsWithoutCollisionGeometry { get; set; } = false;

    /// <summary>Учитывать в NPC avoidance только doodad, помеченные как сталкивающиеся с vehicle</summary>
    public bool RequireDoodadVehicleCollision { get; set; } = true;

    /// <summary>Подстроки model path для визуально низких doodad, которые не должны менять путь NPC</summary>
    public string[] IgnoredDoodadModelKeywords { get; set; } = ["grass", "weed", "waterweed"];

    /// <summary>Минимальный размер модели NPC</summary>
    public float MinModelSize { get; set; } = 0.75f;

    /// <summary>Дополнительный отступ для избежания столкновений.</summary>
    public float CollisionPadding { get; set; } = 0.05f;

    /// <summary>Максимальное расстояние отклонения от исходного пути.</summary>
    public float MaxDetourDistance { get; set; } = 10f;

    /// <summary>Минимальное расстояние между точками пути</summary>
    public float MinWaypointDistance { get; set; } = 0.5f;

    /// <summary>Разрешить разворот на 180° при невозможности двигаться дальше</summary>
    public bool AllowTurnBack { get; set; } = true;

    /// <summary>Включить детальное логирование</summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>Включить узкие debug-логи только по world obstacle proxy из vertsmission</summary>
    public bool EnableWorldObstacleDebugLogging { get; set; } = false;

    /// <summary>Включить фазовый performance-тайминг внутри MoveTowards/pathfinding</summary>
    public bool EnablePerformanceLogging { get; set; } = false;

    /// <summary>Минимальная длительность шага pathfinding, после которой пишется performance-разбивка</summary>
    public int PerformanceLogThresholdMs { get; set; } = 25;

    /// <summary>Включить узкий debug-режим только для выбранного NPC/template, даже если общий debug выключен.</summary>
    public bool EnableTargetedDebugLogging { get; set; } = false;

    /// <summary>Если > 0, писать targeted debug только для этого objId.</summary>
    public uint DebugTargetObjId { get; set; } = 0;

    /// <summary>Если > 0, писать targeted debug только для этого templateId.</summary>
    public uint DebugTargetTemplateId { get; set; } = 0;

    /// <summary>Порог детекции застревания (сколько раз NPC должен оказаться в той же позиции)</summary>
    public int StuckDetectionThreshold { get; set; } = 3;

    /// <summary>Расстояние, при котором позиции считаются одинаковыми</summary>
    public float StuckPositionThreshold { get; set; } = 0.3f;

    /// <summary>Таймаут в секундах перед эвейдом, когда цель находится на недостижимой местности.</summary>
    public float UnreachableTargetEvadeTimeoutSeconds { get; set; } = 5.0f;

    /// <summary>Максимальная разница по высоте, при которой цель считается находящейся на недостижимой для наземного NPC местности.</summary>
    public float UnreachableTargetMaxZDelta { get; set; } = 3.0f;

    /// <summary>Насколько сильно NPC может заходить в коллизионный радиус doodad, если это помогает пройти узкий проход</summary>
    public float DoodadAllowedPassageOverlap { get; set; } = 0.25f;

    /// <summary>Максимальная длина локального recovery-шага при застревании</summary>
    public float MaxRecoveryStepDistance { get; set; } = 4f;

    /// <summary>Дополнительный радиус личного пространства вокруг NPC для локального avoidance</summary>
    public float AvoidanceRadius { get; set; } = 1.25f;

    /// <summary>Базовый радиус прохождения NPC, аналог passRadius из клиента</summary>
    public float PassRadius { get; set; } = 0f;

    /// <summary>Радиус коридора движения, аналог pathRadius из клиента</summary>
    public float PathRadius { get; set; } = 0.25f;

    /// <summary>Дополнительный мягкий отступ именно для doodad, меньше чем для живых целей</summary>
    public float DoodadAvoidanceRadius { get; set; } = 0.24f;

    /// <summary>Радиус corridor-учёта именно для doodad, чтобы не держать лишнюю дистанцию</summary>
    public float DoodadPathRadius { get; set; } = 0.12f;

    /// <summary>Какая доля passRadius NPC участвует в жёстком запрете на проход рядом с doodad</summary>
    public float DoodadPassRadiusFactor { get; set; } = 0.33f;

    /// <summary>Дополнительный мягкий отступ для world obstacle proxy из vertsmission</summary>
    public float WorldObstacleAvoidanceRadius { get; set; } = 0.2f;

    /// <summary>Радиус corridor-учёта для world obstacle proxy</summary>
    public float WorldObstaclePathRadius { get; set; } = 0.18f;

    /// <summary>Какая доля passRadius NPC участвует в жёстком запрете на проход рядом с world obstacle proxy</summary>
    public float WorldObstaclePassRadiusFactor { get; set; } = 0.4f;

    /// <summary>Максимальная разница по высоте, при которой world obstacle proxy считается релевантным для NPC</summary>
    public float WorldObstacleMaxHeightOffset { get; set; } = 2.5f;

    /// <summary>Сила реакции на близкие препятствия, аналог collisionReactionScale</summary>
    public float CollisionReactionScale { get; set; } = 1.5f;

    /// <summary>Насколько далеко вперёд смотреть при оценке кандидата шага</summary>
    public float LookAheadDistance { get; set; } = 2f;

    /// <summary>Радиус поиска других NPC для локального avoidance</summary>
    public float NpcSearchRadius { get; set; } = 4f;

    /// <summary>Максимально допустимое изменение Z за один тик для наземного NPC</summary>
    public float MaxVerticalStepDelta { get; set; } = 0.75f;

    /// <summary>Шаг углового семплирования кандидатов движения</summary>
    public float SamplingAngleStepDegrees { get; set; } = 15f;

    /// <summary>Максимальное отклонение от желаемого направления при семплировании</summary>
    public float SamplingMaxAngleDegrees { get; set; } = 90f;

    /// <summary>Максимальное количество ближайших NPC-препятствий. При превышении — NPC-NPC avoidance пропускается.</summary>
    public int MaxNearbyNpcObstacles { get; set; } = 20;

    /// <summary>Если в регионе объектов больше этого порога, поиск ближайших NPC для avoidance пропускается полностью.</summary>
    public int SkipNpcAvoidanceIfRegionObjectCountExceeds { get; set; } = 500;

    public static NpcPathfindingConfig Default => new NpcPathfindingConfig();
}

/// <summary>
/// Информация о текущем состоянии обхода препятствий.
/// </summary>
public class DetourState
{
    public int PreferredDirection { get; set; } = 0;
    public int FailedAttempts { get; set; } = 0;
    public Vector3 LastBlockedPosition { get; set; } = Vector3.Zero;
    public DateTime LastTurnBackTime { get; set; } = DateTime.MinValue;
    public float TurnBackCooldown { get; set; } = 2.0f;

    /// <summary>История последних позиций для детекции застревания</summary>
    public Queue<Vector3> RecentPositions { get; } = new Queue<Vector3>();

    /// <summary>Счётчик застреваний</summary>
    public int StuckCount { get; set; } = 0;

    public bool CanTurnBack()
    {
        return (DateTime.UtcNow - LastTurnBackTime).TotalSeconds >= TurnBackCooldown;
    }

    public void MarkTurnBack()
    {
        LastTurnBackTime = DateTime.UtcNow;
        FailedAttempts = 0;
        StuckCount = 0;
        RecentPositions.Clear();
    }

    public bool CheckAndUpdateStuck(Vector3 currentPosition, float threshold, int maxStuckCount)
    {
        RecentPositions.Enqueue(currentPosition);

        while (RecentPositions.Count > 10)
            RecentPositions.Dequeue();

        if (RecentPositions.Count >= 3)
        {
            var positions = RecentPositions.ToArray();
            var stuck = true;

            for (int i = 1; i < positions.Length; i++)
            {
                if (Vector3.Distance(positions[i], positions[i - 1]) > threshold)
                {
                    stuck = false;
                    break;
                }
            }

            if (stuck)
            {
                StuckCount++;
                return StuckCount >= maxStuckCount;
            }
            else
            {
                StuckCount = Math.Max(0, StuckCount - 1);
            }
        }

        return false;
    }

    public void ResetStuckState()
    {
        StuckCount = 0;
        RecentPositions.Clear();
    }
}

/// <summary>
/// Агрегирует фазовые тайминги одного вызова NPC MoveTowards/pathfinding.
/// </summary>
public sealed class PathfindingPerformanceTimings
{
    public PathfindingPerformanceTimings(long startTicks)
    {
        StartTicks = startTicks;
    }

    public long StartTicks { get; }
    public long LocalAvoidanceTicks { get; set; }
    public long LocalObstacleCollectionTicks { get; set; }
    public long DoodadObstacleTicks { get; set; }
    public long WorldObstacleTicks { get; set; }
    public long NpcObstacleTicks { get; set; }
    public long CandidateScoringTicks { get; set; }
    public long CandidateWalkableTicks { get; set; }
    public long PostAvoidanceSegmentCheckTicks { get; set; }
    public long WorldMeshFallbackTicks { get; set; }
    public long HeightProbeTicks { get; set; }
    public long MoveApplyTicks { get; set; }
    public int DoodadObstacleCount { get; set; }
    public int WorldObstacleCount { get; set; }
    public int NpcObstacleCount { get; set; }
    public int TotalObstacleCount { get; set; }
    public int CandidateAngleCount { get; set; }
    public int CandidateScoredCount { get; set; }
    public int CandidateWalkableChecks { get; set; }
}

#endregion

// ============================================================
// ЧАСТИЧНОЕ ОПРЕДЕЛЕНИЕ КЛАССА NPC
// ============================================================

public partial class Npc
{
    #region CanMove helper

    /// <summary>
    /// Returns true when the NPC is allowed to move (not stunned/sleeping/rooted/etc).
    /// Encapsulates the buff checks previously done inline in the legacy MoveTowards.
    /// </summary>
    private bool CanMove()
    {
        if (IsDead)
            return false;

        if (Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened))
            return false;

        if (Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle)) ||
            Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
            return false;

        if ((ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return false;

        return true;
    }

    #endregion

    #region Конфигурация и состояние

    private NpcPathfindingConfig _pathfindingConfig;
    protected NpcPathfindingConfig PathfindingConfig
    {
        get => _pathfindingConfig ??= AppConfiguration.Instance?.NpcPathfinding ?? NpcPathfindingConfig.Default;
        set => _pathfindingConfig = value;
    }

    private DateTime _lastWorldMeshAttempt = DateTime.MinValue;
    private static readonly TimeSpan WorldMeshAttemptCooldown = TimeSpan.FromSeconds(1);

    private DetourState _detourState;
    protected DetourState DetourState => _detourState ??= new DetourState();

    private Dictionary<uint, DateTime> _obstacleDebugLogTimes = new();
    protected Dictionary<uint, DateTime> ObstacleDebugLogTimes => _obstacleDebugLogTimes ??= new Dictionary<uint, DateTime>();

    private DoodadQueryCacheState _doodadQueryCache;
    private WorldObstacleQueryCacheState _worldObstacleQueryCache;
    private NpcQueryCacheState _npcQueryCache;
    private MovementHeightCacheState _movementHeightCache;
    private readonly List<LocalAvoidanceObstacle> _localObstacleBuffer = new(16);
    private readonly List<Doodad> _cachedDoodadScratch = new(16);
    private readonly List<ObstacleDataDescriptor> _cachedWorldObstacleScratch = new(16);
    private readonly List<Npc> _cachedNpcScratch = new(16);
    private readonly List<float> _candidateAnglesBuffer = new(16);
    private readonly List<AvoidanceCandidate> _avoidanceCandidateBuffer = new(16);
    private static readonly Comparison<AvoidanceCandidate> AvoidanceCandidateScoreComparison = static (left, right) => left.Score.CompareTo(right.Score);

    #endregion

    #region Основные методы

    private PathAdjustmentResult TryAdjustMovementAroundDoodadsEx(
        Vector3 fromPosition,
        Vector3 toPosition,
        out Vector3 adjustedPosition,
        PathfindingPerformanceTimings perfTimings = null)
    {
        adjustedPosition = toPosition;

        if (ParentWorld == null || Region == null)
        {
            LogPathfindingDebug("TryAdjustMovementAroundDoodads: Invalid context");
            return PathAdjustmentResult.InvalidContext;
        }

        if (DetourState.CheckAndUpdateStuck(fromPosition,
            PathfindingConfig.StuckPositionThreshold,
            PathfindingConfig.StuckDetectionThreshold))
        {
            LogPathfindingDebug($"NPC {ObjId}: STUCK DETECTED! Turning back...");
            adjustedPosition = CalculateTurnBackPosition(fromPosition, toPosition);
            DetourState.MarkTurnBack();
            return PathAdjustmentResult.TurningBack;
        }

        var localAvoidanceStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var result = TryPlanLocalAvoidanceStep(fromPosition, toPosition, out adjustedPosition, perfTimings);
        if (perfTimings != null && localAvoidanceStartTicks != 0)
            perfTimings.LocalAvoidanceTicks += System.Diagnostics.Stopwatch.GetTimestamp() - localAvoidanceStartTicks;

        return result;
    }

    private PathAdjustmentResult TryPlanLocalAvoidanceStep(
        Vector3 fromPosition,
        Vector3 toPosition,
        out Vector3 adjustedPosition,
        PathfindingPerformanceTimings perfTimings = null)
    {
        const float MinimumAdjustedProgressDistance = 0.1f;
        const int MaxNoProgressAdjustedAttempts = 2;

        adjustedPosition = toPosition;

        var desired = toPosition - fromPosition;
        desired.Z = 0;
        var stepDistance = desired.Length();
        if (stepDistance < 0.05f)
            return PathAdjustmentResult.PathClear;

        var obstacles = CollectLocalAvoidanceObstacles(fromPosition, toPosition, stepDistance, perfTimings);
        if (obstacles.Count == 0)
        {
            DetourState.FailedAttempts = 0;
            DetourState.ResetStuckState();
            return PathAdjustmentResult.PathClear;
        }

        if (TrySelectBestAvoidanceStep(fromPosition, toPosition, stepDistance, obstacles, out var bestStep, out var bestDirection, perfTimings))
        {
            adjustedPosition = bestStep;
            var remainingDistance = Vector3.Distance(bestStep, toPosition);
            var progressDistance = stepDistance - remainingDistance;
            var isAdjustedStep = remainingDistance > 0.05f;
            var madeMeaningfulProgress = !isAdjustedStep || (progressDistance >= MinimumAdjustedProgressDistance && progressDistance >= -0.05f);

            if (madeMeaningfulProgress)
            {
                DetourState.FailedAttempts = 0;
                DetourState.ResetStuckState();
            }
            else
            {
                DetourState.FailedAttempts++;
                DetourState.LastBlockedPosition = fromPosition;
                LogPathfindingDebug(
                    $"NPC {ObjId}: Local avoidance adjusted without meaningful progress (progress={progressDistance:F3}, remaining={remainingDistance:F3}, attempts={DetourState.FailedAttempts})");

                if (DetourState.FailedAttempts >= MaxNoProgressAdjustedAttempts)
                {
                    if (PathfindingConfig.AllowTurnBack && DetourState.CanTurnBack())
                    {
                        adjustedPosition = CalculateTurnBackPosition(fromPosition, toPosition);
                        DetourState.MarkTurnBack();
                        LogPathfindingDebug($"NPC {ObjId}: Repeated no-progress avoidance, turning back");
                        return PathAdjustmentResult.TurningBack;
                    }

                    adjustedPosition = LimitMovementDistance(fromPosition, CalculateSoftStopPosition(fromPosition, obstacles), stepDistance);
                    LogPathfindingDebug($"NPC {ObjId}: Repeated no-progress avoidance, stopping");
                    return PathAdjustmentResult.Stopped;
                }

                adjustedPosition = fromPosition;
                if (bestDirection != 0)
                    DetourState.PreferredDirection = bestDirection;
                LogPathfindingDebug($"NPC {ObjId}: Local avoidance rejected step {bestStep} due to negative progress, standing still");
                return PathAdjustmentResult.Stopped;
            }

            if (bestDirection != 0)
                DetourState.PreferredDirection = bestDirection;
            LogPathfindingDebug($"NPC {ObjId}: Local avoidance selected step {bestStep}");
            return remainingDistance <= 0.05f
                ? PathAdjustmentResult.PathClear
                : PathAdjustmentResult.Adjusted;
        }

        DetourState.FailedAttempts++;
        DetourState.LastBlockedPosition = fromPosition;

        if (PathfindingConfig.AllowTurnBack && DetourState.CanTurnBack())
        {
            adjustedPosition = CalculateTurnBackPosition(fromPosition, toPosition);
            DetourState.MarkTurnBack();
            LogPathfindingDebug($"NPC {ObjId}: Local planner failed, turning back");
            return PathAdjustmentResult.TurningBack;
        }

        adjustedPosition = LimitMovementDistance(fromPosition, CalculateSoftStopPosition(fromPosition, obstacles), stepDistance);
        LogPathfindingDebug($"NPC {ObjId}: Local planner failed, stopping");
        return PathAdjustmentResult.Stopped;
    }

    private List<LocalAvoidanceObstacle> CollectLocalAvoidanceObstacles(
        Vector3 fromPosition,
        Vector3 toPosition,
        float stepDistance,
        PathfindingPerformanceTimings perfTimings = null)
    {
        var collectionStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var result = _localObstacleBuffer;
        result.Clear();
        var searchRadius = Math.Max(PathfindingConfig.DoodadSearchRadius, stepDistance + PathfindingConfig.LookAheadDistance + GetEffectivePassRadius());
        var lookAheadTarget = LimitMovementDistance(fromPosition, toPosition, stepDistance + PathfindingConfig.LookAheadDistance);
        var passRadius = GetEffectivePassRadius();
        var doodadObstacleCount = 0;
        var worldObstacleCount = 0;
        var npcObstacleCount = 0;

        var doodadStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        foreach (var doodad in GetNearbyDoodadsForPathfinding(fromPosition, searchRadius))
        {
            if (doodad?.Transform == null)
                continue;

            if (!NpcDoodadObstacleClassifier.TryGetNpcAvoidanceRadius(doodad, PathfindingConfig, out var doodadRadius))
                continue;

            var obstacle = new LocalAvoidanceObstacle
            {
                SourceId = doodad.ObjId,
                Kind = LocalObstacleKind.Doodad,
                Position = doodad.Transform.World.Position,
                Radius = doodadRadius,
                HardRadius = doodadRadius + Math.Max(0.1f, passRadius * PathfindingConfig.DoodadPassRadiusFactor) - PathfindingConfig.DoodadAllowedPassageOverlap,
                SoftRadius = doodadRadius + PathfindingConfig.DoodadPathRadius + PathfindingConfig.DoodadAvoidanceRadius,
                Weight = 0.9f,
                AllowedOverlap = PathfindingConfig.DoodadAllowedPassageOverlap
            };

            if (!IsObstacleRelevantForLocalAvoidance(fromPosition, lookAheadTarget, obstacle))
                continue;

            LogRelevantDoodadObstacle(doodad, obstacle);
            result.Add(obstacle);
            doodadObstacleCount++;
        }
        if (perfTimings != null && doodadStartTicks != 0)
        {
            perfTimings.DoodadObstacleTicks += System.Diagnostics.Stopwatch.GetTimestamp() - doodadStartTicks;
            perfTimings.DoodadObstacleCount += doodadObstacleCount;
        }

        var worldObstacleSearchRadius = Math.Max(PathfindingConfig.WorldObstacleSearchRadius, stepDistance + PathfindingConfig.LookAheadDistance + GetEffectivePassRadius());
        var worldObstacleStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        foreach (var worldObstacle in GetNearbyWorldObstaclesForPathfinding(fromPosition, worldObstacleSearchRadius))
        {
            var obstacle = new LocalAvoidanceObstacle
            {
                SourceId = 0,
                Kind = LocalObstacleKind.WorldObject,
                Position = worldObstacle.Pos,
                Radius = Math.Max(0.2f, (float)worldObstacle.ApproxRadius + PathfindingConfig.CollisionPadding),
                HardRadius = Math.Max(0.2f, (float)worldObstacle.ApproxRadius + PathfindingConfig.CollisionPadding) + Math.Max(0.1f, passRadius * PathfindingConfig.WorldObstaclePassRadiusFactor),
                SoftRadius = Math.Max(0.2f, (float)worldObstacle.ApproxRadius + PathfindingConfig.CollisionPadding) + PathfindingConfig.WorldObstaclePathRadius + PathfindingConfig.WorldObstacleAvoidanceRadius,
                Weight = 1.1f,
                AllowedOverlap = 0.05f
            };

            if (!IsObstacleRelevantForLocalAvoidance(fromPosition, lookAheadTarget, obstacle))
                continue;

            LogRelevantWorldObstacle(worldObstacle, obstacle);
            result.Add(obstacle);
            worldObstacleCount++;
        }
        if (perfTimings != null && worldObstacleStartTicks != 0)
        {
            perfTimings.WorldObstacleTicks += System.Diagnostics.Stopwatch.GetTimestamp() - worldObstacleStartTicks;
            perfTimings.WorldObstacleCount += worldObstacleCount;
        }

        var npcSearchRadius = Math.Max(PathfindingConfig.NpcSearchRadius, stepDistance + PathfindingConfig.LookAheadDistance);
        var npcObstacleStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var regionObjectCount = 0; // Region.ObjectCount not available in this codebase
        var skipNpcAvoidance = regionObjectCount > PathfindingConfig.SkipNpcAvoidanceIfRegionObjectCountExceeds;
        if (skipNpcAvoidance)
        {
            LogPathfindingDebug($"NPC {ObjId}: Skipping NPC avoidance due to high region object count ({regionObjectCount})");
        }
        else
        {
            foreach (var npc in GetNearbyNpcsForPathfinding(fromPosition, npcSearchRadius))
            {
                if (npc == null || npc.ObjId == ObjId || npc.IsDead || npc.Transform == null)
                    continue;

                var npcRadius = npc.ModelSize > 0 ? npc.ModelSize : 1.5f;
                var obstacle = new LocalAvoidanceObstacle
                {
                    SourceId = npc.ObjId,
                    Kind = LocalObstacleKind.Npc,
                    Position = npc.Transform.World.Position,
                    Radius = npcRadius,
                    HardRadius = npcRadius + passRadius,
                    SoftRadius = npcRadius + Math.Max(PathfindingConfig.PathRadius, passRadius) + PathfindingConfig.AvoidanceRadius,
                    Weight = 1.5f,
                    AllowedOverlap = 0.05f
                };

                if (!IsObstacleRelevantForLocalAvoidance(fromPosition, lookAheadTarget, obstacle))
                    continue;

                result.Add(obstacle);
                npcObstacleCount++;

                if (npcObstacleCount >= PathfindingConfig.MaxNearbyNpcObstacles)
                    break;
            }
        }
        if (perfTimings != null && npcObstacleStartTicks != 0)
        {
            perfTimings.NpcObstacleTicks += System.Diagnostics.Stopwatch.GetTimestamp() - npcObstacleStartTicks;
            perfTimings.NpcObstacleCount += npcObstacleCount;
        }

        if (perfTimings != null && collectionStartTicks != 0)
        {
            perfTimings.LocalObstacleCollectionTicks += System.Diagnostics.Stopwatch.GetTimestamp() - collectionStartTicks;
            perfTimings.TotalObstacleCount = result.Count;
        }

        return result;
    }

    private List<Doodad> GetNearbyDoodadsForPathfinding(Vector3 origin, float searchRadius)
    {
        if (!PathfindingConfig.EnableDoodadQueryCache)
            return WorldManager.GetAround<Doodad>(this, searchRadius, useModelSize: true) ?? [];

        if (TryGetCachedNearbyDoodads(origin, searchRadius, out var cachedDoodads))
            return cachedDoodads;

        var freshDoodads = WorldManager.GetAround<Doodad>(this, searchRadius, useModelSize: true) ?? [];
        UpdateDoodadQueryCache(origin, searchRadius, freshDoodads);
        return freshDoodads;
    }

    private bool TryGetCachedNearbyDoodads(Vector3 origin, float searchRadius, out List<Doodad> doodads)
    {
        doodads = null;

        if (_doodadQueryCache == null || _doodadQueryCache.ExpiresAt <= DateTime.UtcNow)
            return false;

        if (Transform.WorldId != _doodadQueryCache.WorldId || Transform.ZoneId != _doodadQueryCache.ZoneId)
            return false;

        if (DistanceSquared2D(origin, _doodadQueryCache.Origin) > PathfindingConfig.DoodadQueryCacheReuseDistance * PathfindingConfig.DoodadQueryCacheReuseDistance)
            return false;

        if (searchRadius > _doodadQueryCache.SearchRadius + PathfindingConfig.DoodadQueryCacheRadiusMargin)
            return false;

        var allowedDistance = searchRadius +
                              PathfindingConfig.DoodadQueryCacheReuseDistance +
                              PathfindingConfig.DoodadQueryCacheRadiusMargin;

        var allowedDistanceSquared = allowedDistance * allowedDistance;
        _cachedDoodadScratch.Clear();
        foreach (var doodad in _doodadQueryCache.Doodads)
        {
            if (doodad?.Transform == null)
                continue;

            if (DistanceSquared2D(origin, doodad.Transform.World.Position) > allowedDistanceSquared)
                continue;

            _cachedDoodadScratch.Add(doodad);
        }

        doodads = _cachedDoodadScratch;
        return true;
    }

    private void UpdateDoodadQueryCache(Vector3 origin, float searchRadius, List<Doodad> doodads)
    {
        var filteredDoodads = new List<Doodad>(doodads.Count);
        foreach (var doodad in doodads)
        {
            if (doodad != null)
                filteredDoodads.Add(doodad);
        }

        _doodadQueryCache = new DoodadQueryCacheState
        {
            Origin = origin,
            SearchRadius = searchRadius,
            WorldId = Transform.WorldId,
            ZoneId = Transform.ZoneId,
            ExpiresAt = DateTime.UtcNow.AddMilliseconds(Math.Max(50, PathfindingConfig.DoodadQueryCacheTtlMs)),
            Doodads = filteredDoodads
        };
    }

    private List<ObstacleDataDescriptor> GetNearbyWorldObstaclesForPathfinding(Vector3 origin, float searchRadius)
    {
        if (ParentWorld?.Template == null)
            return [];

        if (!PathfindingConfig.EnableWorldObstacleQueryCache)
        {
            var directBai = ParentWorld.Template.GetBaiByPos(origin);
            var directObstacles = directBai?.GetNearbyObstacleDescriptors(origin, searchRadius, PathfindingConfig.WorldObstacleMaxHeightOffset) ?? [];
            LogWorldObstacleQuery(origin, searchRadius, directObstacles.Count, cacheHit: false);
            return directObstacles;
        }

        if (TryGetCachedNearbyWorldObstacles(origin, searchRadius, out var cachedObstacles))
        {
            LogWorldObstacleQuery(origin, searchRadius, cachedObstacles.Count, cacheHit: true);
            return cachedObstacles;
        }

        var bai = ParentWorld.Template.GetBaiByPos(origin);
        var freshObstacles = bai?.GetNearbyObstacleDescriptors(origin, searchRadius, PathfindingConfig.WorldObstacleMaxHeightOffset) ?? [];
        UpdateWorldObstacleQueryCache(origin, searchRadius, freshObstacles);
        LogWorldObstacleQuery(origin, searchRadius, freshObstacles.Count, cacheHit: false);
        return freshObstacles;
    }

    private bool TryGetCachedNearbyWorldObstacles(Vector3 origin, float searchRadius, out List<ObstacleDataDescriptor> obstacles)
    {
        obstacles = null;

        if (_worldObstacleQueryCache == null || _worldObstacleQueryCache.ExpiresAt <= DateTime.UtcNow)
            return false;

        if (Transform.WorldId != _worldObstacleQueryCache.WorldId || Transform.ZoneId != _worldObstacleQueryCache.ZoneId)
            return false;

        if (DistanceSquared2D(origin, _worldObstacleQueryCache.Origin) > PathfindingConfig.WorldObstacleQueryCacheReuseDistance * PathfindingConfig.WorldObstacleQueryCacheReuseDistance)
            return false;

        if (searchRadius > _worldObstacleQueryCache.SearchRadius + PathfindingConfig.WorldObstacleQueryCacheRadiusMargin)
            return false;

        var allowedDistance = searchRadius +
                              PathfindingConfig.WorldObstacleQueryCacheReuseDistance +
                              PathfindingConfig.WorldObstacleQueryCacheRadiusMargin;

        _cachedWorldObstacleScratch.Clear();
        foreach (var obstacle in _worldObstacleQueryCache.Obstacles)
        {
            if (obstacle == null)
                continue;

            if (MathF.Abs(obstacle.Pos.Z - origin.Z) > PathfindingConfig.WorldObstacleMaxHeightOffset + Math.Max(0.5f, obstacle.ApproxHeight))
                continue;

            var obstacleAllowedDistance = allowedDistance + Math.Max(0.1f, (float)obstacle.ApproxRadius);
            if (DistanceSquared2D(origin, obstacle.Pos) > obstacleAllowedDistance * obstacleAllowedDistance)
                continue;

            _cachedWorldObstacleScratch.Add(obstacle);
        }

        obstacles = _cachedWorldObstacleScratch;
        return true;
    }

    private void UpdateWorldObstacleQueryCache(Vector3 origin, float searchRadius, List<ObstacleDataDescriptor> obstacles)
    {
        var filteredObstacles = new List<ObstacleDataDescriptor>(obstacles.Count);
        foreach (var obstacle in obstacles)
        {
            if (obstacle != null)
                filteredObstacles.Add(obstacle);
        }

        _worldObstacleQueryCache = new WorldObstacleQueryCacheState
        {
            Origin = origin,
            SearchRadius = searchRadius,
            WorldId = Transform.WorldId,
            ZoneId = Transform.ZoneId,
            ExpiresAt = DateTime.UtcNow.AddMilliseconds(Math.Max(50, PathfindingConfig.WorldObstacleQueryCacheTtlMs)),
            Obstacles = filteredObstacles
        };
    }

    private List<Npc> GetNearbyNpcsForPathfinding(Vector3 origin, float searchRadius)
    {
        if (!PathfindingConfig.EnableNpcQueryCache)
            return WorldManager.GetAround<Npc>(this, searchRadius, useModelSize: true) ?? [];

        if (TryGetCachedNearbyNpcs(origin, searchRadius, out var cachedNpcs))
            return cachedNpcs;

        var freshNpcs = WorldManager.GetAround<Npc>(this, searchRadius, useModelSize: true) ?? [];
        UpdateNpcQueryCache(origin, searchRadius, freshNpcs);
        return freshNpcs;
    }

    private bool TryGetCachedNearbyNpcs(Vector3 origin, float searchRadius, out List<Npc> npcs)
    {
        npcs = null;

        if (_npcQueryCache == null || _npcQueryCache.ExpiresAt <= DateTime.UtcNow)
            return false;

        if (Transform.WorldId != _npcQueryCache.WorldId || Transform.ZoneId != _npcQueryCache.ZoneId)
            return false;

        if (DistanceSquared2D(origin, _npcQueryCache.Origin) > PathfindingConfig.NpcQueryCacheReuseDistance * PathfindingConfig.NpcQueryCacheReuseDistance)
            return false;

        if (searchRadius > _npcQueryCache.SearchRadius + PathfindingConfig.NpcQueryCacheRadiusMargin)
            return false;

        var allowedDistance = searchRadius +
                              PathfindingConfig.NpcQueryCacheReuseDistance +
                              PathfindingConfig.NpcQueryCacheRadiusMargin;
        var allowedDistanceSquared = allowedDistance * allowedDistance;

        _cachedNpcScratch.Clear();
        foreach (var npc in _npcQueryCache.Npcs)
        {
            if (npc?.Transform == null || npc.IsDead)
                continue;

            if (DistanceSquared2D(origin, npc.Transform.World.Position) > allowedDistanceSquared)
                continue;

            _cachedNpcScratch.Add(npc);
        }

        npcs = _cachedNpcScratch;
        return true;
    }

    private void UpdateNpcQueryCache(Vector3 origin, float searchRadius, List<Npc> npcs)
    {
        var filteredNpcs = new List<Npc>(npcs.Count);
        foreach (var npc in npcs)
        {
            if (npc != null && !npc.IsDead)
                filteredNpcs.Add(npc);
        }

        _npcQueryCache = new NpcQueryCacheState
        {
            Origin = origin,
            SearchRadius = searchRadius,
            WorldId = Transform.WorldId,
            ZoneId = Transform.ZoneId,
            ExpiresAt = DateTime.UtcNow.AddMilliseconds(Math.Max(25, PathfindingConfig.NpcQueryCacheTtlMs)),
            Npcs = filteredNpcs
        };
    }

    private void LogRelevantDoodadObstacle(Doodad doodad, LocalAvoidanceObstacle obstacle)
    {
        if (!PathfindingConfig.EnableDebugLogging || doodad == null)
            return;

        var now = DateTime.UtcNow;
        if (ObstacleDebugLogTimes.TryGetValue(doodad.ObjId, out var lastLoggedAt) &&
            (now - lastLoggedAt).TotalSeconds < 2.0)
        {
            return;
        }

        ObstacleDebugLogTimes[doodad.ObjId] = now;

        var template = doodad.Template;
        var templateId = doodad.TemplateId;
        var model = $"<modelKindId={template?.ModelKindId ?? 0}>";
        var simRadius = template?.SimRadius ?? 0;
        var noCollision = template?.NoCollision ?? false;
        var modelSize = doodad.ModelSize;
        var calculatedRadius = obstacle.Radius;

        LogPathfindingDebug(
            $"NPC {ObjId}: Relevant doodad obstacle objId={doodad.ObjId}, templateId={templateId}, model={model}, simRadius={simRadius}, modelSize={modelSize:F2}, noCollision={noCollision}, avoidanceRadius={calculatedRadius:F2}, position={doodad.Transform.World.Position}");
    }

    private void LogWorldObstacleQuery(Vector3 origin, float searchRadius, int count, bool cacheHit)
    {
        if (!PathfindingConfig.EnableWorldObstacleDebugLogging)
            return;

        var debugKey = unchecked((uint)HashCode.Combine("world_query", (int)MathF.Round(origin.X), (int)MathF.Round(origin.Y)));
        var now = DateTime.UtcNow;
        if (ObstacleDebugLogTimes.TryGetValue(debugKey, out var lastLoggedAt) &&
            (now - lastLoggedAt).TotalSeconds < 1.5)
        {
            return;
        }

        ObstacleDebugLogTimes[debugKey] = now;
        Logger.Debug(
            $"NPC {ObjId}: World obstacle query {origin} radius={searchRadius:F2} count={count} cacheHit={cacheHit}");
    }

    private void LogRelevantWorldObstacle(ObstacleDataDescriptor worldObstacle, LocalAvoidanceObstacle obstacle)
    {
        if (!PathfindingConfig.EnableWorldObstacleDebugLogging || worldObstacle == null)
            return;

        var debugKey = GetWorldObstacleDebugKey(worldObstacle);
        var now = DateTime.UtcNow;
        if (ObstacleDebugLogTimes.TryGetValue(debugKey, out var lastLoggedAt) &&
            (now - lastLoggedAt).TotalSeconds < 2.0)
        {
            return;
        }

        ObstacleDebugLogTimes[debugKey] = now;
        Logger.Debug(
            $"NPC {ObjId}: Relevant world obstacle pos={worldObstacle.Pos}, approxRadius={(float)worldObstacle.ApproxRadius:F2}, approxHeight={worldObstacle.ApproxHeight}, flags={worldObstacle.Flags}, avoidanceRadius={obstacle.Radius:F2}");
    }

    private static uint GetWorldObstacleDebugKey(ObstacleDataDescriptor worldObstacle)
    {
        return unchecked((uint)HashCode.Combine(
            (int)MathF.Round(worldObstacle.Pos.X * 2f),
            (int)MathF.Round(worldObstacle.Pos.Y * 2f),
            (int)MathF.Round(worldObstacle.Pos.Z),
            (int)MathF.Round((float)worldObstacle.ApproxRadius * 10f),
            worldObstacle.ApproxHeight,
            worldObstacle.Flags));
    }

    private bool IsObstacleRelevantForLocalAvoidance(Vector3 fromPosition, Vector3 lookAheadTarget, LocalAvoidanceObstacle obstacle)
    {
        var influenceRadius = obstacle.SoftRadius;
        var influenceRadiusSquared = influenceRadius * influenceRadius;
        var currentDistanceSquared = DistanceSquared2D(fromPosition, obstacle.Position);
        if (currentDistanceSquared <= influenceRadiusSquared)
            return true;

        var corridorLimit = influenceRadius + 0.25f;
        var corridorDistanceSquared = DistancePointToSegmentSquared2D(obstacle.Position, fromPosition, lookAheadTarget);
        if (corridorDistanceSquared > corridorLimit * corridorLimit)
            return false;

        var movementDir = lookAheadTarget - fromPosition;
        movementDir.Z = 0;
        if (movementDir.LengthSquared() < 0.001f)
            return false;

        var toObstacle = obstacle.Position - fromPosition;
        toObstacle.Z = 0;
        return Vector3.Dot(Vector3.Normalize(movementDir), Vector3.Normalize(toObstacle)) >= -0.15f;
    }

    private bool TrySelectBestAvoidanceStep(
        Vector3 fromPosition,
        Vector3 toPosition,
        float stepDistance,
        List<LocalAvoidanceObstacle> obstacles,
        out Vector3 bestStep,
        out int bestDirection,
        PathfindingPerformanceTimings perfTimings = null)
    {
        bestStep = toPosition;
        bestDirection = 0;

        var desired = toPosition - fromPosition;
        desired.Z = 0;
        if (desired.LengthSquared() < 0.001f)
            return false;

        var desiredDir = Vector3.Normalize(desired);
        var candidateAngles = _candidateAnglesBuffer;
        FillCandidateAngles(candidateAngles);
        var candidates = _avoidanceCandidateBuffer;
        candidates.Clear();
        if (perfTimings != null)
            perfTimings.CandidateAngleCount = candidateAngles.Count;

        var candidateScoringStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        foreach (var angleDegrees in candidateAngles)
        {
            var candidateDir = RotateDirection(desiredDir, angleDegrees);
            var candidate = fromPosition + candidateDir * stepDistance;
            candidate.Z = fromPosition.Z;

            var lookAheadDistance = stepDistance + PathfindingConfig.LookAheadDistance;
            var lookAheadPoint = fromPosition + candidateDir * lookAheadDistance;
            lookAheadPoint.Z = fromPosition.Z;

            var score = EvaluateAvoidanceCandidate(fromPosition, toPosition, candidate, lookAheadPoint, candidateDir, angleDegrees, obstacles, out var hardBlocked);
            if (hardBlocked)
                continue;

            candidates.Add(new AvoidanceCandidate(candidate, lookAheadPoint, score, Math.Sign(angleDegrees)));
        }
        if (perfTimings != null && candidateScoringStartTicks != 0)
        {
            perfTimings.CandidateScoringTicks += System.Diagnostics.Stopwatch.GetTimestamp() - candidateScoringStartTicks;
            perfTimings.CandidateScoredCount = candidates.Count;
        }

        var candidateWalkableStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        candidates.Sort(AvoidanceCandidateScoreComparison);
        var topCandidatesToCheck = Math.Min(candidates.Count, 3);
        for (var i = 0; i < topCandidatesToCheck; i++)
        {
            var candidate = candidates[i];
            if (perfTimings != null)
                perfTimings.CandidateWalkableChecks++;

            if (!IsAvoidanceCandidateWalkable(fromPosition, candidate))
                continue;

            if (perfTimings != null && candidateWalkableStartTicks != 0)
                perfTimings.CandidateWalkableTicks += System.Diagnostics.Stopwatch.GetTimestamp() - candidateWalkableStartTicks;

            bestStep = candidate.Position;
            bestDirection = candidate.Direction;
            return true;
        }

        if (perfTimings != null && candidateWalkableStartTicks != 0)
            perfTimings.CandidateWalkableTicks += System.Diagnostics.Stopwatch.GetTimestamp() - candidateWalkableStartTicks;

        return false;
    }

    private float EvaluateAvoidanceCandidate(
        Vector3 fromPosition,
        Vector3 toPosition,
        Vector3 candidate,
        Vector3 lookAheadPoint,
        Vector3 candidateDir,
        float angleDegrees,
        List<LocalAvoidanceObstacle> obstacles,
        out bool hardBlocked)
    {
        hardBlocked = false;

        var score = Vector3.Distance(candidate, toPosition) + MathF.Abs(angleDegrees) * 0.03f;

        foreach (var obstacle in obstacles)
        {
            var hardRadiusSquared = obstacle.HardRadius * obstacle.HardRadius;
            var endpointDistanceSquared = DistanceSquared2D(candidate, obstacle.Position);
            var segmentDistanceSquared = DistancePointToSegmentSquared2D(obstacle.Position, fromPosition, candidate);
            var lookAheadDistanceSquared = DistancePointToSegmentSquared2D(obstacle.Position, fromPosition, lookAheadPoint);

            if (segmentDistanceSquared < hardRadiusSquared ||
                endpointDistanceSquared < hardRadiusSquared ||
                lookAheadDistanceSquared < hardRadiusSquared)
            {
                hardBlocked = true;
                return float.PositiveInfinity;
            }

            var softRadiusSquared = obstacle.SoftRadius * obstacle.SoftRadius;
            var effectiveDistanceSquared = Math.Min(endpointDistanceSquared, lookAheadDistanceSquared);
            if (effectiveDistanceSquared < softRadiusSquared)
            {
                var effectiveDistance = MathF.Sqrt(effectiveDistanceSquared);
                var toObstacle = obstacle.Position - fromPosition;
                toObstacle.Z = 0f;
                var toObstacleLengthSquared = toObstacle.LengthSquared();
                var aheadFactor = 0f;
                if (toObstacleLengthSquared > 0.0001f)
                    aheadFactor = Math.Max(0f, Vector3.Dot(toObstacle / MathF.Sqrt(toObstacleLengthSquared), candidateDir));

                var proximityPenalty = (obstacle.SoftRadius - effectiveDistance) * obstacle.Weight * PathfindingConfig.CollisionReactionScale;
                score += proximityPenalty * (1f + aheadFactor);
            }
        }

        if (DetourState.PreferredDirection != 0 && Math.Sign(angleDegrees) != 0)
        {
            if (Math.Sign(angleDegrees) != DetourState.PreferredDirection)
                score += 1.5f;
            else
                score -= 0.1f;
        }

        return score;
    }

    private bool IsAvoidanceCandidateWalkable(Vector3 fromPosition, AvoidanceCandidate candidate)
    {
        return IsMovementPointWalkable(candidate.LookAheadPoint) &&
               IsMovementSegmentWalkable(fromPosition, candidate.LookAheadPoint);
    }

    private float GetObstacleHardRadius(LocalAvoidanceObstacle obstacle, float passRadius)
    {
        if (obstacle.Kind == LocalObstacleKind.Doodad)
        {
            var doodadPassRadius = Math.Max(0.1f, passRadius * PathfindingConfig.DoodadPassRadiusFactor);
            return obstacle.Radius + doodadPassRadius;
        }

        if (obstacle.Kind == LocalObstacleKind.WorldObject)
        {
            var worldObjectPassRadius = Math.Max(0.1f, passRadius * PathfindingConfig.WorldObstaclePassRadiusFactor);
            return obstacle.Radius + worldObjectPassRadius;
        }

        return obstacle.Radius + passRadius;
    }

    private float GetObstacleSoftRadius(LocalAvoidanceObstacle obstacle, float passRadius)
    {
        if (obstacle.Kind == LocalObstacleKind.Doodad)
            return obstacle.Radius + PathfindingConfig.DoodadPathRadius + PathfindingConfig.DoodadAvoidanceRadius;

        if (obstacle.Kind == LocalObstacleKind.WorldObject)
            return obstacle.Radius + PathfindingConfig.WorldObstaclePathRadius + PathfindingConfig.WorldObstacleAvoidanceRadius;

        var pathRadius = Math.Max(PathfindingConfig.PathRadius, passRadius);
        return obstacle.Radius + pathRadius + PathfindingConfig.AvoidanceRadius;
    }

    private void FillCandidateAngles(List<float> angles)
    {
        angles.Clear();
        angles.Add(0f);
        var step = Math.Max(5f, PathfindingConfig.SamplingAngleStepDegrees);
        var maxAngle = Math.Max(step, PathfindingConfig.SamplingMaxAngleDegrees);
        var preferred = DetourState.PreferredDirection == 0 ? 1 : DetourState.PreferredDirection;

        for (var angle = step; angle <= maxAngle; angle += step)
        {
            angles.Add(preferred * angle);
            angles.Add(-preferred * angle);
        }
    }

    private Vector3 CalculateTurnBackPosition(Vector3 currentPosition, Vector3 targetPosition)
    {
        var toTarget = targetPosition - currentPosition;
        toTarget.Z = 0;

        if (toTarget.LengthSquared() < 0.001f)
        {
            var randomAngle = Random.Shared.Next(0, 360) * MathF.PI / 180f;
            var fallbackDir = new Vector3(MathF.Cos(randomAngle), MathF.Sin(randomAngle), 0);
            return currentPosition + fallbackDir * 3f;
        }

        var awayFromTarget = Vector3.Normalize(-toTarget);
        var retreatDistance = Math.Max(2.5f, Math.Min(PathfindingConfig.MaxDetourDistance * 0.5f, PathfindingConfig.MaxRecoveryStepDistance));

        var turnBackPosition = currentPosition + awayFromTarget * retreatDistance;
        turnBackPosition.Z = currentPosition.Z;

        LogPathfindingDebug($"NPC {ObjId} turning back: from {currentPosition} to {turnBackPosition}");

        return turnBackPosition;
    }

    #endregion

    #region Вспомогательные методы геометрии

    private static Vector3 RotateDirection(Vector3 direction, float angleDegrees)
    {
        if (MathF.Abs(angleDegrees) < 0.001f)
            return direction;

        var radians = angleDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new Vector3(
            direction.X * cos - direction.Y * sin,
            direction.X * sin + direction.Y * cos,
            0f);
    }

    private float GetEffectivePassRadius()
    {
        if (PathfindingConfig.PassRadius > 0f)
            return PathfindingConfig.PassRadius;

        return Math.Max(ModelSize, PathfindingConfig.MinModelSize);
    }

    private static float DistancePointToSegmentSquared2D(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
    {
        var segmentX = segmentEnd.X - segmentStart.X;
        var segmentY = segmentEnd.Y - segmentStart.Y;
        var lengthSquared = segmentX * segmentX + segmentY * segmentY;

        if (lengthSquared < 0.0001f)
        {
            var dx = point.X - segmentStart.X;
            var dy = point.Y - segmentStart.Y;
            return dx * dx + dy * dy;
        }

        var targetX = point.X - segmentStart.X;
        var targetY = point.Y - segmentStart.Y;
        var projection = (targetX * segmentX + targetY * segmentY) / lengthSquared;
        projection = Math.Clamp(projection, 0f, 1f);
        var closestX = segmentStart.X + segmentX * projection;
        var closestY = segmentStart.Y + segmentY * projection;
        var deltaX = point.X - closestX;
        var deltaY = point.Y - closestY;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private static float DistanceSquared2D(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private Vector3 CalculateSoftStopPosition(Vector3 fromPosition, List<LocalAvoidanceObstacle> obstacles)
    {
        if (obstacles.Count == 0)
            return fromPosition;

        var closest = obstacles[0];
        var closestDistanceSquared = DistanceSquared2D(fromPosition, closest.Position);
        for (var i = 1; i < obstacles.Count; i++)
        {
            var obstacle = obstacles[i];
            var distanceSquared = DistanceSquared2D(fromPosition, obstacle.Position);
            if (distanceSquared >= closestDistanceSquared)
                continue;

            closest = obstacle;
            closestDistanceSquared = distanceSquared;
        }

        var away = fromPosition - closest.Position;
        away.Z = 0;
        if (away.LengthSquared() < 0.001f)
            away = Vector3.UnitX;
        else
            away = Vector3.Normalize(away);

        return fromPosition + away * Math.Max(0.5f, GetEffectivePassRadius());
    }

    /// <summary>
    /// Проверяет, можно ли пройти отрезок с точки зрения GeoData.
    /// Адаптация: использует AiGeoDataManager (Singleton) вместо AiGeoDataManager(worldTemplate).
    /// </summary>
    private bool IsMovementSegmentWalkable(Vector3 from, Vector3 to)
    {
        var geoData = AiGeoDataManager.Instance;

        var start = from;
        start.Z = GetFastGeoValidationHeight(from);

        var end = to;
        end.Z = GetFastGeoValidationHeight(to);

        var extraRadius = Math.Max(ModelSize, PathfindingConfig.MinModelSize) * 0.5f;
        var pointBlocked = IsMovementPointBlocked(end, out _);
        var geodataBlocked = pointBlocked ||
                             geoData.IsSegmentBlocked(start, end, extraRadius);
        if (geodataBlocked)
        {
            if (CanAllowRaisedShortMoveThroughGeodata(start, end, extraRadius))
            {
                LogTargetedPathfindingDebug(
                    $"allowing raised-layer short move despite geodata block: start={FormatVector3(start)}, end={FormatVector3(end)}, extraRadius={extraRadius:F2}");
            }
            else
            {
                LogTargetedPathfindingDebug($"segment blocked by geodata: start={FormatVector3(start)}, end={FormatVector3(end)}, extraRadius={extraRadius:F2}");
                return false;
            }
        }

        if (ShouldUseStaticWorldCollisionValidation(start, end) &&
            ParentWorld.IsStaticCollisionSegmentBlocked(start, end, MathF.Max(0.75f, extraRadius + 0.5f), out _))
        {
            LogTargetedPathfindingDebug($"segment blocked by static collision: start={FormatVector3(start)}, end={FormatVector3(end)}, extraRadius={extraRadius:F2}");
            return false;
        }

        return true;
    }

    private bool IsMovementPointWalkable(Vector3 point)
    {
        var blocked = IsMovementPointBlocked(point, out _);
        if (!blocked)
            return true;

        var validationPoint = point;
        validationPoint.Z = GetFastGeoValidationHeight(point);
        LogTargetedPathfindingDebug($"point blocked by geodata: point={FormatVector3(validationPoint)}");
        return !blocked;
    }

    private bool IsMovementPointBlocked(Vector3 point, out bool allowedByRaisedIndoorLayer)
    {
        allowedByRaisedIndoorLayer = false;

        var validationPoint = point;
        validationPoint.Z = GetFastGeoValidationHeight(point);
        return AiGeoDataManager.Instance.CheckImpossibleWalk(
            validationPoint,
            GetRaisedIndoorValidationAnchorHeight(validationPoint),
            out allowedByRaisedIndoorLayer);
    }

    /// <summary>
    /// Для geodata-проверок держимся текущего слоя движения NPC.
    /// Адаптация: использует Template.GetHeight вместо WorldInstance.GetHeightUsingHeightMapDat.
    /// </summary>
    private float GetFastGeoValidationHeight(Vector3 position)
    {
        if (ParentWorld?.Template == null)
            return position.Z;

        var heightState = Ai?.HeightState;
        if (heightState is { IsInitialized: true })
        {
            if (heightState.LayerLocked)
            {
                if (heightState.AnchorKind == NpcHeightAnchorKind.InteriorFloor)
                    return heightState.LastResolvedZ > 0f ? heightState.LastResolvedZ : heightState.AnchorZ;

                return heightState.LastResolvedZ > 0f ? heightState.LastResolvedZ : position.Z;
            }
        }

        var terrainHeight = ParentWorld.Template.GetHeight(position.X, position.Y);
        return position.Z < terrainHeight ? terrainHeight : position.Z;
    }

    private float? GetRaisedIndoorValidationAnchorHeight(Vector3 validationPoint)
    {
        if (CanFly || ParentWorld?.Template == null)
            return null;

        const float ElevatedLayerThreshold = 0.5f;
        const float SubterraneanLayerThreshold = 1.25f;

        var terrainHeight = ParentWorld.Template.GetHeight(validationPoint.X, validationPoint.Y);
        var deltaToTerrain = validationPoint.Z - terrainHeight;
        var looksElevated = deltaToTerrain > ElevatedLayerThreshold;
        var looksSubterranean = deltaToTerrain < -SubterraneanLayerThreshold;
        if (!looksElevated && !looksSubterranean)
            return null;

        if (Ai?.HeightState is { IsInitialized: true } heightState &&
            heightState.AnchorKind == NpcHeightAnchorKind.InteriorFloor)
        {
            return heightState.AnchorZ;
        }

        var currentPosition = Transform.World.Position;
        var currentTerrainHeight = ParentWorld.Template.GetHeight(currentPosition.X, currentPosition.Y);
        var currentDeltaToTerrain = currentPosition.Z - currentTerrainHeight;
        if (currentDeltaToTerrain > ElevatedLayerThreshold || currentDeltaToTerrain < -SubterraneanLayerThreshold)
            return currentPosition.Z;

        return validationPoint.Z;
    }

    private bool ShouldUseStaticWorldCollisionValidation(Vector3 from, Vector3 to)
    {
        if (ParentWorld?.Template == null || ParentWorld.Physics?.StaticGeometryIndex == null)
            return false;

        const float ElevatedLayerThreshold = 0.5f;
        const float TerrainStaticValidationDistance = 4.0f;

        var fromTerrain = ParentWorld.Template.GetHeight(from.X, from.Y);
        if (from.Z - fromTerrain > ElevatedLayerThreshold)
            return true;

        var toTerrain = ParentWorld.Template.GetHeight(to.X, to.Y);
        if (to.Z - toTerrain > ElevatedLayerThreshold)
            return true;

        if (DistanceSquared2D(from, to) > TerrainStaticValidationDistance * TerrainStaticValidationDistance)
            return false;

        var staticGeometryIndex = ParentWorld.Physics.StaticGeometryIndex;
        if (staticGeometryIndex.GetEntryCountAt(from.X, from.Y) > 0 ||
            staticGeometryIndex.GetEntryCountAt(to.X, to.Y) > 0)
        {
            return true;
        }

        var midX = (from.X + to.X) * 0.5f;
        var midY = (from.Y + to.Y) * 0.5f;
        return staticGeometryIndex.GetEntryCountAt(midX, midY) > 0;
    }

    private bool CanAllowRaisedShortMoveThroughGeodata(Vector3 start, Vector3 end, float extraRadius)
    {
        if (CanFly || ParentWorld?.Template == null)
            return false;

        if (Ai?.HeightState is { IsInitialized: true } heightState &&
            heightState.AnchorKind == NpcHeightAnchorKind.InteriorFloor)
        {
            return false;
        }

        const float ElevatedLayerThreshold = 0.5f;
        const float ShortMoveDistance = 2.25f;
        const float LayerTolerance = 0.6f;

        if (DistanceSquared2D(start, end) > ShortMoveDistance * ShortMoveDistance)
            return false;

        var startTerrain = ParentWorld.Template.GetHeight(start.X, start.Y);
        var endTerrain = ParentWorld.Template.GetHeight(end.X, end.Y);
        if (start.Z - startTerrain <= ElevatedLayerThreshold ||
            end.Z - endTerrain <= ElevatedLayerThreshold)
        {
            return false;
        }

        if (MathF.Abs(start.Z - end.Z) > MathF.Max(PathfindingConfig.MaxVerticalStepDelta, LayerTolerance))
            return false;

        return !ParentWorld.IsStaticCollisionSegmentBlocked(start, end, MathF.Max(0.75f, extraRadius + 0.5f), out _);
    }

    /// <summary>
    /// Resolves the movement height for a NPC at the given probe position.
    /// Adaptation: uses World.Template.GetHeight instead of WorldInstance.GetHeightUsingHeightMapDat.
    /// </summary>
    private float ResolveMovementHeight(Vector3 heightProbe)
    {
        var npc = Ai?.Owner;

        // Recorded navmesh floor is the ground truth for cave/indoor movement.
        if (npc != null && !npc.CanFly)
        {
            var navFloorZ = WorldManager.Instance.GetNearestNavFloor(npc.Transform.ZoneId, heightProbe.X, heightProbe.Y);
            if (!float.IsNaN(navFloorZ))
            {
                var terrainZ = ParentWorld?.Template?.GetHeight(heightProbe.X, heightProbe.Y) ?? 0f;
                if (terrainZ == 0f || navFloorZ <= terrainZ + 0.5f)
                    return navFloorZ;
            }
        }

        var isFlying = npc != null && npc.CanFly;
        var hasAggro = npc?.CurrentAggroTarget != null;
        var heightState = npc?.Ai?.HeightState;

        bool useReferenceHeight;
        if (npc == null)
        {
            useReferenceHeight = false;
        }
        else if (isFlying)
        {
            useReferenceHeight = !hasAggro;
        }
        else
        {
            useReferenceHeight = true;
        }

        var allowMovementHeightCache = true;
        if (useReferenceHeight &&
            heightState != null &&
            heightState.LayerLocked &&
            heightState.AnchorKind == NpcHeightAnchorKind.InteriorFloor)
        {
            allowMovementHeightCache = false;
        }

        if (useReferenceHeight &&
            hasAggro &&
            heightState != null &&
            heightState.AnchorKind == NpcHeightAnchorKind.Terrain)
        {
            allowMovementHeightCache = false;
        }

        if (allowMovementHeightCache && TryGetCachedMovementHeight(heightProbe, useReferenceHeight, out var cachedHeight))
        {
            if (useReferenceHeight &&
                heightState != null &&
                heightState.AnchorKind == NpcHeightAnchorKind.Terrain &&
                ParentWorld?.Template != null)
            {
                var terrain = ParentWorld.Template.GetHeight(heightProbe.X, heightProbe.Y);
                if (cachedHeight < terrain - 0.20f)
                {
                    return terrain;
                }
            }

            return cachedHeight;
        }

        float resolvedHeight;
        if (useReferenceHeight)
        {
            // Fast path: terrain-ground NPCs without aggro can use cheap heightmap
            if (heightState != null && heightState.AnchorKind == NpcHeightAnchorKind.Terrain && !hasAggro)
            {
                resolvedHeight = ParentWorld?.Template?.GetHeight(heightProbe.X, heightProbe.Y) ?? heightProbe.Z;
                heightState.UpdateLastResolved(resolvedHeight);
            }
            else
            {
                resolvedHeight = NpcHeightResolver.ResolveHeight(Ai, heightProbe, ParentWorld, NpcHeightResolutionContext.MovementStep);
            }
        }
        else
        {
            resolvedHeight = (isFlying && hasAggro) ? heightProbe.Z : (ParentWorld?.Template?.GetHeight(heightProbe.X, heightProbe.Y) ?? heightProbe.Z);
        }

        if (allowMovementHeightCache)
            UpdateMovementHeightCache(heightProbe, resolvedHeight, useReferenceHeight);
        return resolvedHeight;
    }

    private bool TryGetCachedMovementHeight(Vector3 probe, bool useReferenceHeight, out float height)
    {
        height = 0f;

        if (!PathfindingConfig.EnableMovementHeightCache ||
            _movementHeightCache == null ||
            _movementHeightCache.ExpiresAt <= DateTime.UtcNow)
        {
            return false;
        }

        if (_movementHeightCache.UseReferenceHeight != useReferenceHeight)
            return false;

        if (Transform.WorldId != _movementHeightCache.WorldId || Transform.ZoneId != _movementHeightCache.ZoneId)
            return false;

        if (DistanceSquared2D(probe, _movementHeightCache.Origin) > PathfindingConfig.MovementHeightCacheReuseDistance * PathfindingConfig.MovementHeightCacheReuseDistance)
            return false;

        height = _movementHeightCache.Height;
        return true;
    }

    private void UpdateMovementHeightCache(Vector3 probe, float height, bool useReferenceHeight)
    {
        if (!PathfindingConfig.EnableMovementHeightCache)
            return;

        _movementHeightCache = new MovementHeightCacheState
        {
            Origin = probe,
            Height = height,
            WorldId = Transform.WorldId,
            ZoneId = Transform.ZoneId,
            UseReferenceHeight = useReferenceHeight,
            ExpiresAt = DateTime.UtcNow.AddMilliseconds(Math.Max(25, PathfindingConfig.MovementHeightCacheTtlMs))
        };
    }

    private bool TryPreserveInteriorDetourHeight(Vector3 oldPosition, Vector3 detourPosition, float resolvedHeight, out float preservedHeight)
    {
        preservedHeight = resolvedHeight;

        if (CanFly || ParentWorld?.Template == null)
            return false;

        const float ElevatedLayerThreshold = 0.5f;
        const float TerrainSnapThreshold = 0.75f;
        const float DetourHeightTolerance = 0.5f;

        var terrainHeight = ParentWorld.Template.GetHeight(detourPosition.X, detourPosition.Y);
        var detourLooksElevated = detourPosition.Z - terrainHeight > ElevatedLayerThreshold ||
                                  oldPosition.Z - terrainHeight > ElevatedLayerThreshold;
        if (!detourLooksElevated)
        {
            LogTargetedPathfindingDebug(
                $"detour preserve skipped: not elevated enough, detourZ={detourPosition.Z:F2}, currentZ={oldPosition.Z:F2}, terrainZ={terrainHeight:F2}");
            return false;
        }

        var detourStaysOnCurrentLayer = MathF.Abs(detourPosition.Z - oldPosition.Z) <= MathF.Max(PathfindingConfig.MaxVerticalStepDelta, DetourHeightTolerance);
        if (!detourStaysOnCurrentLayer)
        {
            LogTargetedPathfindingDebug(
                $"detour preserve skipped: layer mismatch, detourZ={detourPosition.Z:F2}, currentZ={oldPosition.Z:F2}");
            return false;
        }

        var resolvedLooksLikeTerrain = resolvedHeight <= terrainHeight + TerrainSnapThreshold;
        var resolvedAlreadyNearDetour = MathF.Abs(resolvedHeight - detourPosition.Z) <= DetourHeightTolerance;
        if (!resolvedLooksLikeTerrain || resolvedAlreadyNearDetour)
        {
            LogTargetedPathfindingDebug(
                $"detour preserve skipped: resolvedHeight={resolvedHeight:F2}, detourZ={detourPosition.Z:F2}, terrainZ={terrainHeight:F2}, resolvedLooksLikeTerrain={resolvedLooksLikeTerrain}, resolvedAlreadyNearDetour={resolvedAlreadyNearDetour}");
            return false;
        }

        preservedHeight = detourPosition.Z;
        return true;
    }

    private bool TryPreserveRaisedShortMoveHeight(Vector3 oldPosition, Vector3 targetPosition, float resolvedHeight, out float preservedHeight)
    {
        preservedHeight = resolvedHeight;

        if (CanFly || ParentWorld?.Template == null)
            return false;

        const float ElevatedLayerThreshold = 0.5f;
        const float TerrainSnapThreshold = 0.75f;
        const float HeightTolerance = 0.5f;
        const float ShortMoveDistance = 2.25f;

        if (DistanceSquared2D(oldPosition, targetPosition) > ShortMoveDistance * ShortMoveDistance)
            return false;

        var terrainHeight = ParentWorld.Template.GetHeight(targetPosition.X, targetPosition.Y);
        var looksElevated = targetPosition.Z - terrainHeight > ElevatedLayerThreshold ||
                            oldPosition.Z - terrainHeight > ElevatedLayerThreshold;
        if (!looksElevated)
            return false;

        if (MathF.Abs(targetPosition.Z - oldPosition.Z) > MathF.Max(PathfindingConfig.MaxVerticalStepDelta, HeightTolerance))
            return false;

        var resolvedLooksLikeTerrain = resolvedHeight <= terrainHeight + TerrainSnapThreshold;
        var resolvedDroppedBelowTarget = targetPosition.Z - resolvedHeight > HeightTolerance;
        if (!resolvedLooksLikeTerrain && !resolvedDroppedBelowTarget)
            return false;

        preservedHeight = targetPosition.Z;
        return true;
    }

    private static Vector3 LimitMovementDistance(Vector3 fromPosition, Vector3 targetPosition, float maxDistance)
    {
        var delta = targetPosition - fromPosition;
        delta.Z = 0;

        var distance = delta.Length();
        if (distance <= 0.001f || distance <= maxDistance)
            return targetPosition;

        var direction = delta / distance;
        return new Vector3(
            fromPosition.X + direction.X * maxDistance,
            fromPosition.Y + direction.Y * maxDistance,
            targetPosition.Z);
    }

    private static Vector3 CalculateStopBeforeObstacle(Vector3 fromPosition, Vector3 obstaclePosition, float obstacleRadius)
    {
        var awayFromObstacle = fromPosition - obstaclePosition;
        awayFromObstacle.Z = 0;

        if (awayFromObstacle.LengthSquared() < 0.001f)
            awayFromObstacle = Vector3.UnitX;
        else
            awayFromObstacle = Vector3.Normalize(awayFromObstacle);

        var stopPoint = obstaclePosition + awayFromObstacle * (obstacleRadius + 0.3f);
        stopPoint.Z = fromPosition.Z;
        return stopPoint;
    }

    #endregion

    #region Логирование

    private void LogPathfindingDebug(string message)
    {
        if (PathfindingConfig.EnableDebugLogging || ShouldLogTargetedPathfindingDebug())
        {
            Logger.Debug(message);
        }
    }

    internal bool ShouldLogTargetedPathfindingDebug()
    {
        if (!PathfindingConfig.EnableTargetedDebugLogging)
            return false;

        if (PathfindingConfig.DebugTargetObjId > 0 && ObjId == PathfindingConfig.DebugTargetObjId)
            return true;

        if (PathfindingConfig.DebugTargetTemplateId > 0 && TemplateId == PathfindingConfig.DebugTargetTemplateId)
            return true;

        return false;
    }

    internal void LogTargetedPathfindingDebug(string message)
    {
        if (!ShouldLogTargetedPathfindingDebug())
            return;

        Logger.Debug("NPC {0}:{1} targeted-debug: {2}", ObjId, TemplateId, message);
    }

    private bool ShouldLogPathfindingPerformance()
    {
        return PathfindingConfig.EnablePerformanceLogging && Logger.IsDebugEnabled;
    }

    private void LogPathfindingPerformance(
        PathfindingPerformanceTimings perfTimings,
        Vector3 fromPosition,
        Vector3 requestedTarget,
        Vector3 finalTarget,
        PathAdjustmentResult doodadResult,
        MoveResult moveResult)
    {
        if (perfTimings == null || !ShouldLogPathfindingPerformance())
            return;

        var totalTicks = System.Diagnostics.Stopwatch.GetTimestamp() - perfTimings.StartTicks;
        var thresholdMs = Math.Max(1, PathfindingConfig.PerformanceLogThresholdMs);
        var thresholdTicks = System.Diagnostics.Stopwatch.Frequency * thresholdMs / 1000;
        if (totalTicks < thresholdTicks)
            return;

        Logger.Debug(
            "NPC {0}:{1} MoveTowards timing: total={2:F2}ms, behavior={3}, result={4}, doodadResult={5}, from={6}, requestedTarget={7}, finalTarget={8}, obstacleCollect={9:F2}ms (doodads={10}:{11:F2}ms, world={12}:{13:F2}ms, npcs={14}:{15:F2}ms, total={16}), candidates angles={17}, scored={18}, score={19:F2}ms, walkableChecks={20}, walkable={21:F2}ms, localAvoidance={22:F2}ms, postSegment={23:F2}ms, worldMesh={24:F2}ms, height={25:F2}ms, apply={26:F2}ms",
            ObjId,
            TemplateId,
            ToMs(totalTicks),
            Ai?.GetCurrentBehavior()?.GetType().Name ?? "None",
            moveResult,
            doodadResult,
            FormatVector3(fromPosition),
            FormatVector3(requestedTarget),
            FormatVector3(finalTarget),
            ToMs(perfTimings.LocalObstacleCollectionTicks),
            perfTimings.DoodadObstacleCount,
            ToMs(perfTimings.DoodadObstacleTicks),
            perfTimings.WorldObstacleCount,
            ToMs(perfTimings.WorldObstacleTicks),
            perfTimings.NpcObstacleCount,
            ToMs(perfTimings.NpcObstacleTicks),
            perfTimings.TotalObstacleCount,
            perfTimings.CandidateAngleCount,
            perfTimings.CandidateScoredCount,
            ToMs(perfTimings.CandidateScoringTicks),
            perfTimings.CandidateWalkableChecks,
            ToMs(perfTimings.CandidateWalkableTicks),
            ToMs(perfTimings.LocalAvoidanceTicks),
            ToMs(perfTimings.PostAvoidanceSegmentCheckTicks),
            ToMs(perfTimings.WorldMeshFallbackTicks),
            ToMs(perfTimings.HeightProbeTicks),
            ToMs(perfTimings.MoveApplyTicks));
    }

    private static double ToMs(long ticks)
    {
        return ticks <= 0 ? 0 : ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"<{value.X:F2}, {value.Y:F2}, {value.Z:F2}>";
    }

    #endregion

    #region Обновлённый метод MoveTowards

    /// <summary>
    /// Перемещается к целевой позиции с обходом препятствий.
    /// </summary>
    public MoveResult MoveTowards(Vector3 other, float distance, byte actorFlags = 4, float rangeTolerance = 1f)
    {
        distance *= Ai?.Owner?.MoveSpeedMul ?? 1f;

        if (distance < 0.01f)
            return MoveResult.CannotMove;

        if (!CanMove())
            return MoveResult.CannotMove;

        var oldPosition = Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Transform.Local.Position, other, true);

        if (targetDist <= rangeTolerance)
            return MoveResult.ReachedDestination;

        var perfTimings = ShouldLogPathfindingPerformance()
            ? new PathfindingPerformanceTimings(System.Diagnostics.Stopwatch.GetTimestamp())
            : null;
        var warnEnabled = Logger.IsWarnEnabled;
        var warnStartTicks = warnEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

        long doodadAdjTicks = 0, postSegTicks = 0, worldMeshTicks = 0, heightTicks = 0, applyTicks = 0;

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        var travelDist = Math.Min(targetDist, distance);

        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, Transform.Local.Position, other);
        var targetPosition = new Vector3(newX, newY, newZ);
        var doodadResult = PathAdjustmentResult.PathClear;

        MoveResult FinalizeMoveResult(MoveResult result)
        {
            if (ShouldLogTargetedPathfindingDebug())
            {
                LogTargetedPathfindingDebug(
                    $"MoveTowards result={result}, doodadResult={doodadResult}, from={FormatVector3(oldPosition)}, requested={FormatVector3(other)}, final={FormatVector3(targetPosition)}");
            }
            LogPathfindingPerformance(perfTimings, oldPosition, other, targetPosition, doodadResult, result);

            if (warnStartTicks != 0)
            {
                var totalMs = ToMs(System.Diagnostics.Stopwatch.GetTimestamp() - warnStartTicks);
                if (totalMs >= 150.0)
                {
                    if (perfTimings != null)
                    {
                        Logger.Debug("NPC {0}:{1} MoveTowards SLOW: total={2:F2}ms, result={3}, doodadResult={4}, from={5}, target={6}, obstacleCollect={7:F2}ms (doodads={8}:{9:F2}ms, world={10}:{11:F2}ms, npcs={12}:{13:F2}ms, total={14}), candidates={15}, scored={16}, score={17:F2}ms, walkableChecks={18}, walkable={19:F2}ms, localAvoidance={20:F2}ms, postSegment={21:F2}ms, worldMesh={22:F2}ms, height={23:F2}ms, apply={24:F2}ms",
                            ObjId, TemplateId, totalMs, result, doodadResult,
                            FormatVector3(oldPosition), FormatVector3(other),
                            ToMs(perfTimings.LocalObstacleCollectionTicks),
                            perfTimings.DoodadObstacleCount, ToMs(perfTimings.DoodadObstacleTicks),
                            perfTimings.WorldObstacleCount, ToMs(perfTimings.WorldObstacleTicks),
                            perfTimings.NpcObstacleCount, ToMs(perfTimings.NpcObstacleTicks),
                            perfTimings.TotalObstacleCount,
                            perfTimings.CandidateAngleCount, perfTimings.CandidateScoredCount,
                            ToMs(perfTimings.CandidateScoringTicks),
                            perfTimings.CandidateWalkableChecks, ToMs(perfTimings.CandidateWalkableTicks),
                            ToMs(perfTimings.LocalAvoidanceTicks),
                            ToMs(perfTimings.PostAvoidanceSegmentCheckTicks),
                            ToMs(perfTimings.WorldMeshFallbackTicks),
                            ToMs(perfTimings.HeightProbeTicks),
                            ToMs(perfTimings.MoveApplyTicks));
                    }
                    else
                    {
                        Logger.Debug(
                            "NPC {0}:{1} MoveTowards SLOW: total={2:F2}ms, result={3}, doodadResult={4}, from={5}, target={6}, doodadAdj={7:F2}ms, postSeg={8:F2}ms, worldMesh={9:F2}ms, height={10:F2}ms, apply={11:F2}ms",
                            ObjId, TemplateId, totalMs, result, doodadResult,
                            FormatVector3(oldPosition), FormatVector3(other),
                            ToMs(doodadAdjTicks), ToMs(postSegTicks), ToMs(worldMeshTicks), ToMs(heightTicks), ToMs(applyTicks));
                    }
                }
            }
            return result;
        }

        var doodadAdjStart = warnEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        doodadResult = TryAdjustMovementAroundDoodadsEx(oldPosition, targetPosition, out var doodadAdjusted, perfTimings);
        if (warnEnabled) doodadAdjTicks = System.Diagnostics.Stopwatch.GetTimestamp() - doodadAdjStart;

        switch (doodadResult)
        {
            case PathAdjustmentResult.Adjusted:
                targetPosition = doodadAdjusted;
                newX = targetPosition.X;
                newY = targetPosition.Y;
                LogPathfindingDebug($"NPC {ObjId}: Path adjusted around doodad");
                break;

            case PathAdjustmentResult.Stopped:
                targetPosition = doodadAdjusted;
                newX = targetPosition.X;
                newY = targetPosition.Y;
                LogPathfindingDebug($"NPC {ObjId}: Stopped before obstacle");
                break;

            case PathAdjustmentResult.TurningBack:
                targetPosition = doodadAdjusted;
                newX = targetPosition.X;
                newY = targetPosition.Y;
                LogPathfindingDebug($"NPC {ObjId}: Retreating from obstacle");
                break;
        }

        var postAvoidanceSegmentCheckStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var postSegStart = warnEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var shouldCheckPostAvoidanceSegment = doodadResult != PathAdjustmentResult.Adjusted;
        var segmentBlockedAfterAvoidance = shouldCheckPostAvoidanceSegment && !IsMovementSegmentWalkable(oldPosition, targetPosition);
        if (perfTimings != null && postAvoidanceSegmentCheckStartTicks != 0)
            perfTimings.PostAvoidanceSegmentCheckTicks += System.Diagnostics.Stopwatch.GetTimestamp() - postAvoidanceSegmentCheckStartTicks;
        if (warnEnabled) postSegTicks = System.Diagnostics.Stopwatch.GetTimestamp() - postSegStart;

        var worldMeshFallbackStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var worldMeshStart = warnEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var usedWorldMeshDetour = false;
        var now = DateTime.UtcNow;
        if (segmentBlockedAfterAvoidance &&
            now - _lastWorldMeshAttempt >= WorldMeshAttemptCooldown &&
            TryAdjustMovementAroundWorldMesh(oldPosition, other, out var meshAdjusted))
        {
            _lastWorldMeshAttempt = now;
            targetPosition = meshAdjusted;
            newX = targetPosition.X;
            newY = targetPosition.Y;
            usedWorldMeshDetour = true;
            LogTargetedPathfindingDebug(
                $"world-mesh detour selected from full target: requested={FormatVector3(other)}, detour={FormatVector3(meshAdjusted)}");
        }
        else if (segmentBlockedAfterAvoidance)
        {
            LogPathfindingDebug($"NPC {ObjId}: Blocking movement through world geometry");
            targetPosition = oldPosition;
            newX = oldPosition.X;
            newY = oldPosition.Y;
        }
        if (perfTimings != null && worldMeshFallbackStartTicks != 0)
            perfTimings.WorldMeshFallbackTicks += System.Diagnostics.Stopwatch.GetTimestamp() - worldMeshFallbackStartTicks;
        if (warnEnabled) worldMeshTicks = System.Diagnostics.Stopwatch.GetTimestamp() - worldMeshStart;

        targetPosition = LimitMovementDistance(oldPosition, targetPosition, travelDist);
        newX = targetPosition.X;
        newY = targetPosition.Y;

        var isWaterMovementNpc = NpcWaterSurfaceOverrides.ShouldUseDirectMovement(this);
        if (isWaterMovementNpc &&
            !NpcWaterSurfaceOverrides.HasWaterColumnAt(ParentWorld, targetPosition, out _))
        {
            LogPathfindingDebug(
                $"NPC {ObjId}: Blocking water movement onto land at <{newX:F3}, {newY:F3}, {targetPosition.Z:F3}>");
            StopMovement();
            return FinalizeMoveResult(MoveResult.Stuck);
        }

        var heightProbe = new Vector3(newX, newY, targetPosition.Z);
        var heightProbeStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var heightStart = warnEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var worldZ = ResolveMovementHeight(heightProbe);
        if (usedWorldMeshDetour && TryPreserveInteriorDetourHeight(oldPosition, targetPosition, worldZ, out var preservedDetourHeight))
        {
            LogTargetedPathfindingDebug(
                $"preserving interior detour height: detourZ={targetPosition.Z:F2}, resolvedZ={worldZ:F2}, preservedZ={preservedDetourHeight:F2}, terrainZ={ParentWorld.Template.GetHeight(targetPosition.X, targetPosition.Y):F2}");
            worldZ = preservedDetourHeight;
        }
        else if (TryPreserveRaisedShortMoveHeight(oldPosition, targetPosition, worldZ, out var preservedRaisedHeight))
        {
            LogTargetedPathfindingDebug(
                $"preserving raised short-move height: targetZ={targetPosition.Z:F2}, resolvedZ={worldZ:F2}, preservedZ={preservedRaisedHeight:F2}, terrainZ={ParentWorld.Template.GetHeight(targetPosition.X, targetPosition.Y):F2}");
            worldZ = preservedRaisedHeight;
        }
        if (perfTimings != null && heightProbeStartTicks != 0)
            perfTimings.HeightProbeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - heightProbeStartTicks;
        if (warnEnabled) heightTicks = System.Diagnostics.Stopwatch.GetTimestamp() - heightStart;

        if (!CanFly &&
            !isWaterMovementNpc &&
            Ai?.HeightState is { IsInitialized: true, AnchorKind: NpcHeightAnchorKind.Terrain } &&
            ParentWorld != null)
        {
            var dzUp = worldZ - oldPosition.Z;
            if (dzUp > PathfindingConfig.MaxVerticalStepDelta)
            {
                var currentTerrain = ParentWorld.Template?.GetHeight(oldPosition.X, oldPosition.Y) ?? 0f;
                if (currentTerrain > 0f && oldPosition.Z < currentTerrain - 0.10f)
                {
                    worldZ = oldPosition.Z + PathfindingConfig.MaxVerticalStepDelta;
                }
            }
        }

        if (!CanFly && !isWaterMovementNpc && MathF.Abs(worldZ - oldPosition.Z) > PathfindingConfig.MaxVerticalStepDelta)
        {
            LogPathfindingDebug(
                $"NPC {ObjId}: Blocking unsafe vertical step from Z={oldPosition.Z:F2} to Z={worldZ:F2} at <{newX:F3}, {newY:F3}>");
            StopMovement();
            return FinalizeMoveResult(MoveResult.Stuck);
        }

        var moveApplyStartTicks = perfTimings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var applyStart = warnEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var movementTarget = new Vector3(newX, newY, worldZ);
        var angle = MathUtil.CalculateAngleFrom(oldPosition, movementTarget);
        var movementIntensity = actorFlags == 5 ? 63 : 127;
        var velocityMagnitude = actorFlags == 5 ? 2500f : 4000f;
        var (velX, velY) = MathUtil.AddDistanceToFront(velocityMagnitude, 0, 0, (float)angle.DegToRad());

        Transform.Local.SetPosition(newX, newY, worldZ);
        Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
        var (rx, ry, rz) = Transform.Local.ToRollPitchYawSBytesMovement();

        moveType.X = Transform.Local.Position.X;
        moveType.Y = Transform.Local.Position.Y;
        moveType.Z = Transform.Local.Position.Z;
        moveType.VelX = (short)velX;
        moveType.VelY = (short)velY;
        moveType.VelZ = 0;
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = actorFlags;
        moveType.Flags = MoveTypeFlags.Moving | (IsInBattle ? MoveTypeFlags.InCombat : 0);
        moveType.DeltaMovement = [0, (sbyte)movementIntensity, 0];
        moveType.Stance = CurrentGameStance;
        moveType.Alertness = CurrentAlertness;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
        var moved = CheckMovedPosition(oldPosition);

        BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveType), true);
        if (perfTimings != null && moveApplyStartTicks != 0)
            perfTimings.MoveApplyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - moveApplyStartTicks;
        if (warnEnabled) applyTicks = System.Diagnostics.Stopwatch.GetTimestamp() - applyStart;

        if (Vector3.Distance(Transform.Local.Position, other) <= rangeTolerance)
            return FinalizeMoveResult(MoveResult.ReachedDestination);

        if (!moved ||
            doodadResult is PathAdjustmentResult.Stopped
            or PathAdjustmentResult.TurningBack)
        {
            return FinalizeMoveResult(MoveResult.Stuck);
        }

        return FinalizeMoveResult(MoveResult.Moving);
    }

    private bool TryAdjustMovementAroundWorldMesh(Vector3 fromPosition, Vector3 toPosition, out Vector3 adjustedPosition)
    {
        adjustedPosition = toPosition;

        var geoData = AiGeoDataManager.Instance;

        var start = fromPosition;
        start.Z = GetFastGeoValidationHeight(fromPosition);

        var end = toPosition;
        end.Z = GetFastGeoValidationHeight(toPosition);

        if (!geoData.LinePassesThroughForbiddenArea(
                start,
                end,
                [],
                false,
                out AiShape obstacleShape,
                out var intersectionPoint,
                out _,
                8f))
        {
            return TryResolveStaticCollisionFallbackForWorldMesh(fromPosition, toPosition, out adjustedPosition);
        }

        var stepDistance = Math.Max(Vector3.Distance(fromPosition, toPosition), PathfindingConfig.MinWaypointDistance);
        if (obstacleShape != null &&
            geoData.TryFindLocalDetourWaypoint(
                start,
                end,
                intersectionPoint,
                obstacleShape,
                out var detourWaypoint,
                PathfindingConfig.MaxDetourDistance))
        {
            var detourCandidate = LimitMovementDistance(fromPosition, detourWaypoint, stepDistance);
            if (ShouldUseStaticWorldCollisionValidation(fromPosition, detourCandidate) &&
                ParentWorld.IsStaticCollisionSegmentBlocked(
                    fromPosition,
                    detourCandidate,
                    MathF.Max(0.75f, GetEffectivePassRadius() + 0.5f),
                    out _))
            {
                LogTargetedPathfindingDebug(
                    $"rejecting world-mesh detour due to static collision: from={FormatVector3(fromPosition)}, detour={FormatVector3(detourCandidate)}");
            }
            else
            {
                adjustedPosition = detourCandidate;
                LogPathfindingDebug($"NPC {ObjId}: Adjusting path around world mesh using geodata detour");
                return true;
            }
        }

        if (TryResolveStaticCollisionFallbackForWorldMesh(fromPosition, toPosition, out adjustedPosition))
            return true;

        var direction = toPosition - fromPosition;
        var distance = direction.Length();
        if (distance < 0.1f)
            return false;

        direction = Vector3.Normalize(direction);

        var stepSize = 1f;
        var steps = (int)(distance / stepSize);

        for (var i = 1; i <= steps; i++)
        {
            var checkPos = fromPosition + direction * (i * stepSize);
            checkPos.Z = GetFastGeoValidationHeight(checkPos);

            if (IsMovementPointBlocked(checkPos, out _))
            {
                if (i > 1)
                {
                    adjustedPosition = fromPosition + direction * ((i - 1) * stepSize);
                    adjustedPosition.Z = fromPosition.Z;
                    LogPathfindingDebug($"NPC {ObjId}: Stopping before forbidden world mesh");
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveStaticCollisionFallbackForWorldMesh(Vector3 fromPosition, Vector3 toPosition, out Vector3 adjustedPosition)
    {
        adjustedPosition = toPosition;

        var start = fromPosition;
        start.Z = GetFastGeoValidationHeight(fromPosition);

        var end = toPosition;
        end.Z = GetFastGeoValidationHeight(toPosition);

        var staticCollisionTolerance = MathF.Max(0.75f, GetEffectivePassRadius() + 0.5f);
        if (!ShouldUseStaticWorldCollisionValidation(start, end) ||
            !ParentWorld.IsStaticCollisionSegmentBlocked(start, end, staticCollisionTolerance, out _))
        {
            return false;
        }

        if (TryAdjustMovementAroundStaticCollision(fromPosition, toPosition, out adjustedPosition))
            return true;

        return TryClampMovementBeforeStaticCollision(fromPosition, toPosition, out adjustedPosition);
    }

    private bool TryAdjustMovementAroundStaticCollision(Vector3 fromPosition, Vector3 toPosition, out Vector3 adjustedPosition)
    {
        adjustedPosition = toPosition;

        var desired = toPosition - fromPosition;
        desired.Z = 0f;
        var desiredLength = desired.Length();
        if (desiredLength < 0.1f)
            return false;

        var desiredDir = Vector3.Normalize(desired);
        var stepDistance = Math.Min(desiredLength, Math.Max(PathfindingConfig.MinWaypointDistance, PathfindingConfig.MaxDetourDistance));
        var angleCandidates = new[] { 30f, -30f, 55f, -55f, 80f, -80f };
        var bestDistanceToTarget = float.PositiveInfinity;
        var found = false;

        foreach (var angle in angleCandidates)
        {
            var candidateDir = RotateDirection(desiredDir, angle);
            var candidate = fromPosition + candidateDir * stepDistance;
            candidate.Z = ResolveMovementHeight(candidate with { Z = fromPosition.Z });

            if (!IsMovementPointWalkable(candidate))
                continue;

            if (!IsMovementSegmentWalkable(fromPosition, candidate))
                continue;

            var distanceToTarget = Vector3.DistanceSquared(candidate, toPosition);
            if (distanceToTarget >= bestDistanceToTarget)
                continue;

            adjustedPosition = candidate;
            bestDistanceToTarget = distanceToTarget;
            found = true;
        }

        if (found)
            LogPathfindingDebug($"NPC {ObjId}: Adjusting path around static collision");

        return found;
    }

    private bool TryClampMovementBeforeStaticCollision(Vector3 fromPosition, Vector3 toPosition, out Vector3 adjustedPosition)
    {
        adjustedPosition = fromPosition;

        const int BinarySearchIterations = 6;
        const float MinAdvanceDistance = 0.35f;

        if (DistanceSquared2D(fromPosition, toPosition) < MinAdvanceDistance * MinAdvanceDistance)
            return false;

        var best = fromPosition;
        var low = 0f;
        var high = 1f;
        var found = false;

        for (var i = 0; i < BinarySearchIterations; i++)
        {
            var mid = (low + high) * 0.5f;
            var candidate = Vector3.Lerp(fromPosition, toPosition, mid);
            candidate.Z = ResolveMovementHeight(candidate with { Z = fromPosition.Z });

            if (IsMovementPointWalkable(candidate) && IsMovementSegmentWalkable(fromPosition, candidate))
            {
                best = candidate;
                low = mid;
                found = true;
            }
            else
            {
                high = mid;
            }
        }

        if (!found || DistanceSquared2D(fromPosition, best) < MinAdvanceDistance * MinAdvanceDistance)
            return false;

        adjustedPosition = best;
        LogPathfindingDebug($"NPC {ObjId}: Clamping movement before static collision");
        return true;
    }

    #endregion
}
