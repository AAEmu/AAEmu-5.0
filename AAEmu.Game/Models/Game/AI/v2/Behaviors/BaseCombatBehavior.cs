using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.AI.v2.Params.Almighty;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using Point = AAEmu.Game.Models.Game.AI.AStar.Point;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors;

/// <summary>
/// Base class for combat-related AI behaviors. Provides common functionality for combat movement,
/// target selection, and skill usage.
/// </summary>
public abstract class BaseCombatBehavior : Behavior
{
    // Distance and range constants
    private const float DefaultReturnDistance = 50f;
    private const float DefaultAbsoluteReturnDistance = 200f;
    private const float DefaultMeleeAttackRangeAdjustment = 1f;
    private const float DefaultFlyingHeightAdjustment = 15f;

    /// <summary>
    /// Returns true when this NPC's world has geodata available for navigation pathing.
    /// Replaces the old AppConfiguration.Instance.World.GeoDataMode check, which was a global flag
    /// that did not account for worlds where geodata is simply not loaded yet.
    /// </summary>
    protected bool CanUseNavigationPathing()
    {
        if (Ai?.Owner == null)
            return false;

        if (Ai.Owner.Transform.WorldId <= 0)
            return false;

        return Ai.Owner.ParentWorld?.Template?.GeoData != null;
    }

    // Internal state
    internal bool _strafeDuringDelay;
    internal DateTime _combatStartTime;
    internal Queue<AiSkill> _skillQueue = new();
    internal string _pipeName;

    private uint _phaseType;
    private bool _startingSkillAlreadyUsed;

    protected BaseCombatBehavior()
    {
        _combatStartTime = DateTime.UtcNow;
        _delayEnd = DateTime.MinValue;
    }

    /// <summary>
    /// Moves the AI owner towards the target while respecting combat conditions and range requirements
    /// </summary>
    /// <param name="target">The target unit to move towards</param>
    /// <param name="delta">Time elapsed since last update</param>
    protected void MoveInRange(BaseUnit target, TimeSpan delta)
    {
        if (!ValidateMoveConditions(target))
            return;

        var range = CalculateAttackRange();
        var currentPosition = Ai.Owner.Transform.Local.ClonePosition();
        var targetPosition = target.Transform.Local.ClonePosition();
        var (speed, moveFlags) = CalculateMovementParameters(delta);
        var distanceToTarget = Ai.Owner.GetDistanceTo(target, true);

        // Ensure the distance to the target is not less than 4 units before moving
        if (distanceToTarget < 1.5f)
            return;

        HandleMovement(currentPosition, targetPosition, speed, moveFlags, range, distanceToTarget);
    }

    private bool ValidateMoveConditions(BaseUnit target)
    {
        if (Ai?.Owner == null || target == null)
            return false;

        if (IsUnitImmobilized())
            return false;

        if (IsUnitCasting())
            return false;

        return true;
    }

    private bool IsUnitImmobilized()
    {
        return Ai.Owner.Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened)
            || Ai.Owner.IsDead
            || Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle))
            || Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare));
    }

    private bool IsUnitCasting()
    {
        return (Ai.Owner.ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running;
    }

    private float CalculateAttackRange()
    {
        var range = Ai.Owner.Template.AttackStartRangeScale;
        if (Ai.Owner.Template.UseRangeMod && _maxWeaponRange != 0)
        {
            range *= _maxWeaponRange;
        }

        // Special case adjustment for melee attacks
        if (Ai.Owner.Template.BaseSkillId == 2 && Ai.Owner.Template.Skills.Count == 0 && range <= 4)
        {
            range -= DefaultMeleeAttackRangeAdjustment;
        }

        return range;
    }

    private (float speed, byte flags) CalculateMovementParameters(TimeSpan delta)
    {
        var speed = Ai.GetRealMovementSpeed(Ai.Owner.BaseMoveSpeed);
        var flags = Ai.GetRealMovementFlags(speed);
        speed *= delta.Milliseconds / 1000.0;
        return ((float)speed, flags);
    }

    private void HandleMovement(Vector3 currentPosition, Vector3 targetPosition, float speed, byte moveFlags, float range, float distanceToTarget)
    {
        if (CanUseNavigationPathing())
        {
            HandleGeoDataMovement(currentPosition, targetPosition, speed, moveFlags, range, distanceToTarget);
        }
        else
        {
            HandleDirectMovement(targetPosition, speed, moveFlags, range, distanceToTarget);
        }
    }

    private void HandleGeoDataMovement(Vector3 currentPosition, Vector3 targetPosition, float speed, byte moveFlags, float range, float distanceToTarget)
    {
        // ----------------------------------------------------------------
        // PATHFINDING FIX (v4): repath cadence and waypoint consumption.
        //
        // Legacy bug (1) - inverted repath condition:
        //   The old code was `if (|EndPointPos - target.pos| <= ModelSize) FindPath()`,
        //   i.e. "repath when the cached endpoint is *close to* the target". The intent
        //   per the comment was the opposite ("if not at target position, recompute").
        //   The flipped condition meant NPCs kept following stale routes whenever the
        //   player kited away, producing the "monster takes detours" symptom.
        //
        // Legacy bug (2) - AlreadyTargeted as a global pathfinding lock:
        //   Once Npc.SetTarget(other) is called, Ai.AlreadyTargeted=true and UpdateTarget()
        //   stops calling FindPath even when the target moves. Repath now goes through
        //   this MoveInRange instead, gated on a real movement threshold + a cooldown.
        //
        // Legacy bug (3) - waypoint reach tolerance == attack range:
        //   The old code used `range` (attack start range, scaled) as both "should I move
        //   closer" and "have I reached this waypoint". For a melee NPC with range ~3m,
        //   that consumed waypoints far too eagerly. We now use a dedicated waypoint
        //   tolerance (default 0.9 m) for waypoint dequeue, and keep `range` only for the
        //   final-target stop decision.
        // ----------------------------------------------------------------

        var pathCfg = AppConfiguration.Instance.World?.Pathing;
        var pathingEnabled = pathCfg?.Enabled ?? true;
        var repathDist = pathCfg?.RepathTargetMovedDistance ?? 2.5f;
        var repathCooldownMs = pathCfg?.RepathCooldownMs ?? 350;
        var waypointTol = pathCfg?.WaypointReachTolerance ?? 0.9f;
        var directFallback = pathCfg?.DirectFallbackWhenVisible ?? true;
        var rejectDetours = pathCfg?.RejectDetourPaths ?? false;
        var detourThreshold = pathCfg?.DetourRejectThreshold ?? 5.0f;

        if (Ai.PathNode != null && pathingEnabled)
        {
            // Decide whether we need to recompute the path.
            var pathEmpty = Ai.PathNode.FoundPath == null || Ai.PathNode.FoundPath.Count == 0;
            var targetChanged = Ai.PathNode.LastTargetObjId != (Ai.Owner.CurrentTarget?.ObjId ?? 0);
            var targetMoved = !targetChanged
                              && Vector3.Distance(Ai.PathNode.LastTargetPosAtRepath, targetPosition) > repathDist;
            var cooldownReady = (DateTime.UtcNow - Ai.PathNode.LastRepathTime).TotalMilliseconds >= repathCooldownMs;

            // PATHFINDING v2 fix: do NOT repath when the NPC is already in attack range.
            //
            // Live data showed NPCs entering an infinite repath loop the moment they
            // arrived at the target: every tick consumed the (now-empty) path queue,
            // every tick saw pathEmpty=true, every tick re-ran FindPath. The NPC was
            // already in range and should have been attacking — but BaseCombatBehavior
            // kept calling MoveInRange and we kept repathing pointlessly. The CSV showed
            // this as 6+ consecutive "PathEmpty" log lines on a stationary NPC standing
            // 0.4m from the target.
            //
            // Skip repath when distanceToTarget <= range. The outer movement code will
            // see the empty path and call StopMovement() — which is exactly what we want
            // when the NPC has reached its attack position.
            var inAttackRange = distanceToTarget <= range;

            if ((pathEmpty || targetChanged || targetMoved) && cooldownReady && !inAttackRange)
            {
                // Tag the reason for diagnostics.
                Ai.PathNode.LastRepathReason = pathEmpty ? "PathEmpty"
                    : targetChanged ? "NewTarget"
                    : "TargetMoved";

                // Only measure time if the Warn log will actually be emitted.
                if (Logger.IsWarnEnabled)
                {
                    var sw = Stopwatch.StartNew();
                    if (Ai.Owner.CurrentTarget is Unit targetUnit)
                        Ai.Owner.FindPath(targetUnit);
                    sw.Stop();
                    if (sw.ElapsedMilliseconds >= 1)
                        Logger.Warn($"FindPath took {sw.Elapsed} for Ai.Owner.ObjId:{Ai.Owner.ObjId}, Owner.TemplateId {Ai.Owner.TemplateId} @ {Ai.Owner.Transform}");
                }
                else
                {
                    if (Ai.Owner.CurrentTarget is Unit targetUnit)
                        Ai.Owner.FindPath(targetUnit);
                }

                Ai.PathNode.EndPointPos = new Vector3(targetPosition.X, targetPosition.Y, targetPosition.Z);
            }
        }

        // ----------------------------------------------------------------
        // Movement consumption.
        //
        // PATHFINDING v3 fix: detour-detection guard.
        //
        // Even with the path-rejection in Npc.FindPath, a stale path computed before
        // the patch upgrade or in a previous repath can sit in FoundPath and lead the
        // NPC the wrong way. Before consuming the next waypoint, double-check it is
        // not a flagrant detour vs. the current target position. If it is, drop the
        // entire path and let the DirectFallback / range logic take over.
        //
        // v6: this guard is disabled by default (RejectDetourPaths=false) because the
        // optimized A* no longer produces backward paths. Kept as an emergency net,
        // gated on the same config flag + threshold as Npc.FindPath.
        // ----------------------------------------------------------------
        if (pathingEnabled
            && rejectDetours
            && Ai.PathNode != null
            && Ai.PathNode.FoundPath != null
            && Ai.PathNode.FoundPath.Count > 0
            && Ai.Owner.CurrentTarget != null)
        {
            var npcWorldPos = Ai.Owner.Transform.World.Position;
            var tgtWorldPos = Ai.Owner.CurrentTarget.Transform.World.Position;
            var nextWp = Ai.PathNode.FoundPath.Peek();
            if (!nextWp.Equals(Vector3.Zero))
            {
                var dNpcTgt = Vector3.Distance(npcWorldPos, tgtWorldPos);
                var dWpTgt = Vector3.Distance(nextWp, tgtWorldPos);
                var dNpcWp = Vector3.Distance(npcWorldPos, nextWp);
                if (dWpTgt > dNpcTgt + detourThreshold && dNpcWp > detourThreshold)
                {
                    Ai.PathNode.FoundPath.Clear();
                    Ai.PathNode.LastStatus = PathStatus.DirectFallback;
                }
            }
        }

        if (Ai.PathNode != null)
        {
            if (Ai.PathNode.FoundPath != null && Ai.PathNode.FoundPath.Count > 0 && !Ai.PathNode.FoundPath.Peek().Equals(Vector3.Zero))
            {
                var nextPathPoint = Ai.PathNode.FoundPath.Peek();
                var distanceToWaypoint = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, nextPathPoint, true);

                // Waypoint dequeue uses dedicated tolerance, not attack range.
                if (pathingEnabled && distanceToWaypoint <= waypointTol)
                {
                    Ai.PathNode.CurrentTargetPos = Ai.PathNode.FoundPath.Dequeue();

                    // After consuming a waypoint, if there are still more, head to the next
                    // one this same tick to avoid a 1-tick stall. Otherwise check distance
                    // to the actual target to decide whether to keep moving or stop.
                    if (Ai.PathNode.FoundPath.Count > 0)
                    {
                        nextPathPoint = Ai.PathNode.FoundPath.Peek();
                        Ai.Owner.MoveTowards(nextPathPoint, (float)speed, moveFlags, waypointTol);
                    }
                    else
                    {
                        // Path consumed entirely. Use real target distance + attack range
                        // for the final approach.
                        distanceToTarget = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget, true);
                        if (distanceToTarget > range)
                            Ai.Owner.MoveTowards(targetPosition, (float)speed, moveFlags, range);
                        else
                            Ai.Owner.StopMovement();
                    }
                }
                else
                {
                    // Still en route to the waypoint.
                    // Legacy code used `range` (attack range) as MoveTowards's tolerance,
                    // which would trip MoveTowards's "I'm close enough" early-return for
                    // ranged NPCs. Keep using waypointTol for movement-side as well so
                    // the NPC actually walks up to the waypoint before dequeuing.
                    var moveTol = pathingEnabled ? waypointTol : range;
                    Ai.Owner.MoveTowards(nextPathPoint, (float)speed, moveFlags, moveTol);
                }
            }
            else
            {
                // No path available. Direct chase if visible, else stand still.
                if (distanceToTarget > range)
                {
                    if (pathingEnabled && directFallback && Ai.Owner.UnitIsVisible(Ai.Owner.CurrentTarget))
                    {
                        // Mark so /pathmob can see we are not on a navmesh path.
                        Ai.PathNode.LastStatus = PathStatus.DirectFallback;
                        Ai.Owner.MoveTowards(targetPosition, (float)speed, moveFlags, range);
                    }
                    else if (!pathingEnabled)
                    {
                        // Legacy behaviour.
                        Ai.Owner.MoveTowards(targetPosition, (float)speed, moveFlags, range);
                    }
                    else
                    {
                        Ai.Owner.StopMovement();
                    }
                }
                else
                {
                    Ai.Owner.StopMovement();
                }
            }
        }
        else
        {
            HandleDirectMovement(targetPosition, speed, moveFlags, range, distanceToTarget);
        }
    }

    private void HandleDirectMovement(Vector3 targetPosition, float speed, byte moveFlags, float range, float distanceToTarget)
    {
        if (distanceToTarget > range)
        {
            Ai.Owner.MoveTowards(targetPosition, speed, moveFlags);
        }
        else
        {
            Ai.Owner.StopMovement();
        }
    }

    #region State Properties

    /// <summary>
    /// Gets whether the unit can strafe based on delay and configuration
    /// </summary>
    protected bool CanStrafe => DateTime.UtcNow > _delayEnd || _strafeDuringDelay;

    /// <summary>
    /// Gets whether the unit is currently using a skill
    /// </summary>
    protected bool IsUsingSkill => Ai.Owner.SkillTask != null || Ai.Owner.ActivePlotState != null;

    /// <summary>
    /// Gets whether the unit can use skills based on current state and conditions
    /// </summary>
    protected bool CanUseSkill
    {
        get
        {
            if (Ai?.Owner == null)
                return false;

            if (IsUsingSkill)
                return false;

            if ((Ai.Owner.ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
                return false;

            if (Ai.Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep || e.Template.Silence))
                return false;

            return DateTime.UtcNow >= _delayEnd && !Ai.Owner.IsGlobalCooldowned;
        }
    }

    /// <summary>
    /// Determines if the unit should return to its idle position based on distance conditions
    /// </summary>
    protected bool ShouldReturn
    {
        get
        {
            if (Ai?.Owner == null)
                return true;

            var returnDistance = Ai.Owner.Template.ReturnDistance > 0 ?
                Ai.Owner.Template.ReturnDistance : DefaultReturnDistance;

            var absoluteReturnDistance = Ai.Owner.Template.AbsoluteReturnDistance > 0 ?
                Ai.Owner.Template.AbsoluteReturnDistance : DefaultAbsoluteReturnDistance;

            if (Ai.Owner.CurrentTarget == null)
                return true;

            // Horizontal (2D) leash distance. Cave/indoor NPCs can have an IdlePosition Z
            // resolved to the surface terrain above the cave (no cave-floor geodata), so a 3D
            // distance reads tens of metres "from home" while the NPC stands on the cave floor —
            // instantly leashing it out of combat the moment it is attacked (exposed once
            // npc-cave-float-clamp stopped them floating up toward that surface home Z). The leash
            // cares about horizontal wander, so ignore Z.
            var ownerPos = Ai.Owner.Transform.World.Position;
            var targetPos = Ai.Owner.CurrentTarget.Transform.World.Position;
            var idlePos = Ai.IdlePosition;
            var distanceToTarget = Vector2.Distance(new Vector2(ownerPos.X, ownerPos.Y), new Vector2(targetPos.X, targetPos.Y));
            var distanceToIdlePosition = Vector2.Distance(new Vector2(ownerPos.X, ownerPos.Y), new Vector2(idlePos.X, idlePos.Y));

            return (distanceToTarget > returnDistance || distanceToIdlePosition > returnDistance)
                && distanceToIdlePosition <= absoluteReturnDistance;
        }
    }

    #endregion

    #region Target Management

    /// <summary>
    /// Updates the current target based on aggro table and visibility
    /// </summary>
    /// <returns>True if a valid target was found and set, false otherwise</returns>
    public bool UpdateTarget()
    {
        if (Ai?.Owner == null) return false;

        foreach (var aggro in Ai.Owner.AggroTable.Values.OrderByDescending(a => a.TotalAggro))
        {
            if (aggro.Owner is Unit u && IsValidTarget(u))
            {
                ProcessTargetSelection(u);
                return true;
            }
        }

        ClearInvalidTarget();
        return false;
    }

    private bool ProcessTargetSelection(Unit target)
    {
        if (!IsValidTarget(target))
            return false;

        UpdateTargetAndAggro(target);
        return true;
    }

    private bool IsValidTarget(Unit target)
    {
        if (!Ai.Owner.UnitIsVisible(target) || target.Hp <= 0)
            return false;

        if (CanUseNavigationPathing())
            return true;

        return WorldManager.Instance.GetUnit(target.ObjId) != null;
    }

    private void UpdateTargetAndAggro(Unit target)
    {
        // PATHFINDING FIX (v4): do NOT gate FindPath on Ai.AlreadyTargeted.
        //
        // The legacy logic was:
        //   if (CurrentAggroTarget != abuser && !AlreadyTargeted) FindPath(abuser);
        //
        // SetTarget() always sets AlreadyTargeted=true, so after the very first
        // SetTarget call the FindPath path was permanently blocked here, and any
        // subsequent target switch (taunt, multi-attacker tables, threat changes)
        // never produced a new path. Combined with the inverted repath condition
        // in MoveInRange, this is what makes NPCs "stick" to their first route.
        //
        // We now re-pathfind whenever the *aggro target* changes (legitimate target
        // switch). The throttle / movement-based repath cadence in MoveInRange
        // takes care of follow-up updates while pursuing the same target — that is
        // a movement-frequency concern, not a target-change concern.
        var pathingEnabled = AppConfiguration.Instance.World?.Pathing?.Enabled ?? true;
        var aggroTargetChanged = Ai.Owner.CurrentAggroTarget != target;

        if (pathingEnabled)
        {
            if (aggroTargetChanged)
            {
                if (Ai.PathNode != null)
                    Ai.PathNode.LastRepathReason = "AggroTargetChanged";
                Ai.Owner.FindPath(target);
            }
        }
        else
        {
            // Legacy gate, kept for A/B testing.
            if (Ai.Owner.CurrentAggroTarget != target && !Ai.AlreadyTargeted)
            {
                Ai.Owner.FindPath(target);
            }
        }

        Ai.Owner.CurrentAggroTarget = target;
        Ai.Owner.SetTarget(target);
        UpdateAggroHelp(target);
    }

    private void ClearInvalidTarget()
    {
        if (Ai.Owner.CurrentTarget is not Unit currentTarget)
        {
            Ai.Owner.CurrentAggroTarget = null;
            Ai.Owner.SetTarget(null);
            return;
        }

        if (currentTarget.Hp <= 0)
        {
            Ai.Owner.CurrentAggroTarget = null;
            Ai.Owner.SetTarget(null);
        }
    }

    #endregion

    #region Combat State Management

    /// <summary>
    /// Updates phase-specific behavior based on pipe name
    /// </summary>
    protected void CheckPipeName()
    {
        switch (_pipeName)
        {
            case "phase_dragon_ground" when _phaseType == 1:
                var groundHeight = WorldManager.Instance.GetHeight(Ai.Owner.Transform.ZoneId,
                    Ai.Owner.Transform.Local.Position.X,
                    Ai.Owner.Transform.Local.Position.Y);
                Ai.Owner.Transform.Local.SetHeight(groundHeight);
                break;

            case "phase_dragon_fly_hovering" when _phaseType == 2:
                Ai.Owner.Transform.Local.SetHeight(Ai.Owner.Transform.Local.Position.Z + DefaultFlyingHeightAdjustment);
                Ai.Owner.StopMovement();
                break;

            case "phase_dragon_fly_path":
                Ai.GoToFollowPath();
                break;
        }
    }

    /// <summary>
    /// Refreshes the skill queue based on available skill lists and parameters
    /// </summary>
    protected bool RefreshSkillQueue(List<AiSkillList> skillLists, AiParams aiParams)
    {
        if (!ValidateSkillQueueParameters(skillLists, aiParams))
            return false;

        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);

        if (skillLists.Count == 0)
            return TryUseBaseSkill();

        return ProcessSkillLists(skillLists, aiParams, targetDist);
    }

    private bool ValidateSkillQueueParameters(List<AiSkillList> skillLists, AiParams aiParams)
    {
        return skillLists != null && aiParams != null && Ai?.Owner?.CurrentTarget != null;
    }

    private bool ProcessSkillLists(List<AiSkillList> skillLists, AiParams aiParams, float targetDist)
    {
        var selectedSkillList = skillLists.RandomElementByWeight(s => s.Dice);
        if (selectedSkillList == null)
            return false;

        UpdateAiParameters(selectedSkillList, aiParams);
        LogSkillListSelection(selectedSkillList);

        if (ProcessStartingSkills(selectedSkillList))
            return true;

        return ProcessAvailableSkills(selectedSkillList.SkillLists, targetDist);
    }

    private void UpdateAiParameters(AiSkillList skillList, AiParams aiParams)
    {
        _pipeName = skillList.PipeName;
        _phaseType = skillList.PhaseType;
        aiParams.RestorationOnReturn = skillList.Restoration;
        aiParams.GoReturnState = skillList.GoReturn;
    }

    private void LogSkillListSelection(AiSkillList skillList)
    {
        Logger.Debug($"RefreshSkillQueue: Unit {Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, " +
                    $"HP Range=[{skillList.HealthRangeMin}-{skillList.HealthRangeMax}], " +
                    $"Time Range=[{skillList.TimeRangeStart}-{skillList.TimeRangeEnd}], " +
                    $"Skills={skillList.SkillLists.Count}, Dice={skillList.Dice}");
    }

    private bool ProcessStartingSkills(AiSkillList skillList)
    {
        if (skillList.StartAiSkills.Count == 0 || _startingSkillAlreadyUsed)
            return false;

        foreach (var skill in skillList.StartAiSkills)
        {
            if (Ai.Owner.Cooldowns.CheckCooldown(skill.SkillId))
                continue;

            Logger.Debug($"Unit {Ai.Owner.ObjId} using start skill {skill.SkillId}");
            _skillQueue.Enqueue(skill);
            _startingSkillAlreadyUsed = true;
        }

        return _skillQueue.Count > 0;
    }

    private bool ProcessAvailableSkills(List<SkillList> skillLists, float targetDist)
    {
        var skillList = skillLists.RandomElementByWeight(s => s.Dice);
        if (skillList == null)
            return false;

        foreach (var skill in skillList.Skills.Where(skill => IsSkillUsable(skill, targetDist)))
        {
            _skillQueue.Enqueue(skill);
            Logger.Debug($"Unit {Ai.Owner.ObjId} adding skill {skill.SkillId} to queue");
        }

        return _skillQueue.Count > 0;
    }

    private bool IsSkillUsable(AiSkill skill, float targetDist)
    {
        if (Ai.Owner.Cooldowns.CheckCooldown(skill.SkillId))
            return false;

        var template = SkillManager.Instance.GetSkillTemplate(skill.SkillId);
        if (template == null)
            return false;

        return targetDist >= template.MinRange && targetDist <= template.MaxRange ||
               template.TargetType == SkillTargetType.Self;
    }

    private bool TryUseBaseSkill()
    {
        if (Ai.Owner.Template.BaseSkillId == 0)
            return false;

        var baseSkill = new AiSkill
        {
            SkillId = (uint)Ai.Owner.Template.BaseSkillId,
            Strafe = Ai.Owner.Template.BaseSkillStrafe,
            Delay = Ai.Owner.Template.BaseSkillDelay
        };

        Logger.Debug($"Unit {Ai.Owner.ObjId} using base skill {baseSkill.SkillId}");
        _skillQueue.Enqueue(baseSkill);

        return true;
    }

    #endregion

    /// <summary>
    /// Returns nearby players within range, in front, and not greeted recently.
    /// </summary>
    public static List<Character> GetPlayersInRange(
        Unit owner,
        float range,
        double fovScale,
        Dictionary<uint, DateTime> greeted,
        TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;

        return WorldManager
            .GetAround<Character>(owner, range, true)
            .Where(p => MathUtil.IsFront(owner, p, fovScale))
            .Where(p => !greeted.TryGetValue(p.ObjId, out var last) || now - last >= cooldown)
            .ToList();
    }
}
