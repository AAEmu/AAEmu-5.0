using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

// Server-driven continuous auto-attack. Started by Character.StartAutoSkill (wired from
// CSStartSkillPacket.HandleCommonSkill when a basic attack 2/3/4 is used). Re-fires the
// basic attack at weapon speed until the target dies / leaves range / combat ends, so the
// player does not have to re-click each swing. Ported from the 1.2 implementation.
public class UseAutoAttackSkillTask : SkillTask
{
    private readonly Character _caster;
    private readonly Skill _mainhandSkill;

    // Offhand auto-attack skill (id 3), cached lazily; whether it fires each tick is
    // decided fresh in Execute() based on current offhand equipment.
    private Skill _offhandSkill;
    private SkillTemplate _offhandSkillTemplate;

    public UseAutoAttackSkillTask(Skill skill, Character caster) : base(skill)
    {
        _mainhandSkill = skill;
        _caster = caster;
        Cancelled = false;
    }

    public override void Execute()
    {
        var target = _caster.CurrentTarget as Unit;

        // Stop conditions: dead, no target, target dead, self-target, cancelled
        if (_caster.Hp <= 0 || target == null || target.Hp <= 0 || target.ObjId == _caster.ObjId || Cancelled)
        {
            StopAutoAttack();
            return;
        }

        // Skill-pause: while another skill is casting or during GCD, skip this tick
        // (don't cancel — auto-attack resumes after the skill ends).
        if (_caster.SkillTask != null)
            return;
        if (_caster.GlobalCooldown >= DateTime.UtcNow)
            return;

        if (!_caster.CanAttack(target))
            return;

        // Range check — pause (don't cancel) if the target moved out of range
        var maxRange = GetEffectiveMaxRange();
        var distance = _caster.GetDistanceTo(target);
        if (distance > maxRange)
            return;

        var casterCaster = new SkillCasterUnit(_caster.ObjId);
        var targetCaster = new SkillCastUnitTarget(target.ObjId);
        var skillObject = SkillObject.GetByType(SkillObjectType.None);

        // Fire mainhand attack
        _mainhandSkill.Use(_caster, casterCaster, targetCaster, skillObject, true, out _);

        // Dual-wield: fire the offhand swing too when an offhand weapon is equipped.
        var offhandSkill = ResolveOffhandSkill();
        if (offhandSkill != null)
        {
            var offCaster = new SkillCasterUnit(_caster.ObjId);
            var offTarget = new SkillCastUnitTarget(target.ObjId);
            var offSkillObject = SkillObject.GetByType(SkillObjectType.None);
            offhandSkill.Use(_caster, offCaster, offTarget, offSkillObject, true, out _);
        }

        // Re-pace if attack speed changed (buff/debuff/weapon swap)
        var newDelay = TimeSpan.FromMilliseconds(SkillManager.GetAttackDelay(_mainhandSkill.Template, _caster));
        if (newDelay != RepeatInterval)
            RepeatInterval = newDelay;
    }

    // Offhand auto-attack fires only when mainhand is melee (id 2) and the offhand slot
    // currently holds a weapon (not a shield / empty). Skill instance cached lazily.
    private Skill ResolveOffhandSkill()
    {
        if (_mainhandSkill.Template.Id != 2)
            return null;

        var offhandItem = _caster.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Offhand);
        if (offhandItem?.Template is not WeaponTemplate offWeapon)
            return null;

        // Only dual-wield real offhand WEAPONS. Shields are also a Holdable/WeaponTemplate here,
        // so exclude them by holdable slot type — otherwise the player "swings" their shield.
        if (offWeapon.HoldableTemplate?.SlotTypeId == (uint)EquipmentItemSlotType.Shield)
            return null;

        if (_offhandSkill != null)
            return _offhandSkill;

        _offhandSkillTemplate ??= SkillManager.Instance.GetSkillTemplate(3);
        if (_offhandSkillTemplate == null)
            return null;

        _offhandSkill = new Skill(_offhandSkillTemplate);
        return _offhandSkill;
    }

    private void StopAutoAttack()
    {
        Cancelled = true;
        _caster.IsAutoAttack = false;
        _caster.AutoAttackTask = null;
        Cancel();
    }

    // Max attack range from the equipped weapon, falling back to the skill template.
    private float GetEffectiveMaxRange()
    {
        var slot = _mainhandSkill.Template.Id == 4
            ? EquipmentItemSlot.Ranged
            : EquipmentItemSlot.Mainhand;

        var weapon = _caster.Equipment?.GetItemBySlot((int)slot);
        if (weapon?.Template is WeaponTemplate wt && wt.HoldableTemplate != null && wt.HoldableTemplate.MaxRange > 0)
            return wt.HoldableTemplate.MaxRange;

        return _mainhandSkill.Template.MaxRange > 0 ? _mainhandSkill.Template.MaxRange : 3f;
    }
}
